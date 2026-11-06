using System;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;
using OpenTms.Integrations.AiConnector;
using OpenTms.Integrations.Permissions;
using OpenTms.Platform.Features;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Features;
using Volo.Abp.Identity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Security.Claims;

namespace OpenTms.AiConnector;

/// <summary>Why the connector refuses a user (design 74, "Checks on every /mcp request"), in the order of those checks.</summary>
public enum AiConnectorRefusal
{
    HostUser = 1,
    Impersonation = 2,
    FeatureOff = 3,
    NotAccepted = 4,
    PermissionMissing = 5,
    UserInactive = 6,
    ConnectionRevoked = 7,
}

/// <summary>
/// Checks 1 to 5 of design 74, in that order, shared by <c>/mcp</c> and the connect screen so neither can drift from the
/// other. Runs in the tenant the request resolved to from the principal; needs a unit of work around it.
/// </summary>
public class AiConnectorAccessChecker : ITransientDependency
{
    private readonly ICurrentTenant _currentTenant;
    private readonly IFeatureChecker _features;
    private readonly IPermissionChecker _permissions;
    private readonly IAiConnectionChecker _connections;
    private readonly IdentityUserManager _users;

    public AiConnectorAccessChecker(
        ICurrentTenant currentTenant,
        IFeatureChecker features,
        IPermissionChecker permissions,
        IAiConnectionChecker connections,
        IdentityUserManager users)
    {
        _currentTenant = currentTenant;
        _features = features;
        _permissions = permissions;
        _connections = connections;
        _users = users;
    }

    /// <summary>Null when every check passes, else the first one that failed.</summary>
    public async Task<AiConnectorRefusal?> CheckAsync(ClaimsPrincipal principal)
    {
        /* Check 1: the tenant comes from the token or session, never from anything else in the request. */
        var tenantId = principal.FindTenantId();
        if (tenantId == null || _currentTenant.Id != tenantId)
        {
            return AiConnectorRefusal.HostUser;
        }

        /* Check 2. */
        if (principal.FindImpersonatorUserId() != null)
        {
            return AiConnectorRefusal.Impersonation;
        }

        /* Check 3: the tree rule, a node needs its ancestors. */
        if (!await _features.IsEnabledAsync(AppFeatures.Integrations) || !await _features.IsEnabledAsync(AppFeatures.IntegrationsApp.AiConnector))
        {
            return AiConnectorRefusal.FeatureOff;
        }

        if (!await _connections.IsAcceptedAsync())
        {
            return AiConnectorRefusal.NotAccepted;
        }

        /* Check 4. */
        if (!await _permissions.IsGrantedAsync(principal, IntegrationsPermissions.AiConnector.Use))
        {
            return AiConnectorRefusal.PermissionMissing;
        }

        /* Check 5: read every time, so deactivating a user stops the connector at once and reactivating resumes it. */
        var userId = principal.FindUserId();
        var user = userId == null ? null : await _users.FindByIdAsync(userId.Value.ToString());
        return user is { IsActive: true } ? null : AiConnectorRefusal.UserInactive;
    }
}
