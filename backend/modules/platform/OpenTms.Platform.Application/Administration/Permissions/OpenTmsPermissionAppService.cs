using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.DependencyInjection;
using Volo.Abp.PermissionManagement;
using Volo.Abp.SimpleStateChecking;

namespace OpenTms.Platform.Administration.Permissions;

/// <summary>
/// ABP's permission-management service with one OpenTMS rule (docs/designs/73-role-permissions.md): the grants of the
/// <c>admin</c> role cannot be changed through the API, so no administrator can remove the administrators' own access.
/// ABP's seed keeps giving <c>admin</c> every permission of its side. Everything else (the provider policies, the side
/// check, a caller granting only what they hold) is ABP's own behavior.
/// </summary>
[Dependency(ReplaceServices = true)]
[ExposeServices(typeof(IPermissionAppService), typeof(PermissionAppService), typeof(OpenTmsPermissionAppService))]
public class OpenTmsPermissionAppService : PermissionAppService
{
    public const string AdminRoleName = "admin";

    public OpenTmsPermissionAppService(
        IPermissionManager permissionManager,
        IPermissionChecker permissionChecker,
        IPermissionDefinitionManager permissionDefinitionManager,
        IResourcePermissionManager resourcePermissionManager,
        IResourcePermissionGrantRepository resourcePermissionGrantRepository,
        IOptions<PermissionManagementOptions> options,
        ISimpleStateCheckerManager<PermissionDefinition> simpleStateCheckerManager)
        : base(
            permissionManager,
            permissionChecker,
            permissionDefinitionManager,
            resourcePermissionManager,
            resourcePermissionGrantRepository,
            options,
            simpleStateCheckerManager)
    {
    }

    public override async Task UpdateAsync(string providerName, string providerKey, UpdatePermissionsDto input)
    {
        if (providerName == RolePermissionValueProvider.ProviderName
            && string.Equals(providerKey, AdminRoleName, StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessException(PlatformErrorCodes.AdminRolePermissionsReadOnly);
        }

        await base.UpdateAsync(providerName, providerKey, input);
    }
}
