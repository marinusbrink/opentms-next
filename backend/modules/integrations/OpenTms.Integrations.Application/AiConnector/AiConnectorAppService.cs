using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenTms.Integrations.Permissions;
using OpenTms.Integrations.Settings;
using OpenTms.Platform.Features;
using Volo.Abp.Authorization;
using Volo.Abp.Features;
using Volo.Abp.Identity;
using Volo.Abp.SettingManagement;
using Volo.Abp.Users;

namespace OpenTms.Integrations.AiConnector;

/// <summary>
/// Integrations app → AI connector (design 74 "Connector administration"): the status, and accepting and withdrawing the
/// data sharing. <see cref="AiConnectionAppService"/> serves the connections. Every query is tenant-filtered.
/// </summary>
[RequiresFeature(AppFeatures.Integrations, AppFeatures.IntegrationsApp.AiConnector, RequiresAll = true)]
[Authorize]
public class AiConnectorAppService : IntegrationsAppServiceBase, IAiConnectorAppService
{
    private const int WithdrawBatchSize = 200;

    private readonly IAiConnectionRepository _connections;
    private readonly ISettingManager _settingManager;
    private readonly IIdentityUserRepository _users;
    private readonly AiConnectionAuthorizationRevoker _revoker;
    private readonly IConfiguration _configuration;

    public AiConnectorAppService(
        IAiConnectionRepository connections,
        ISettingManager settingManager,
        IIdentityUserRepository users,
        AiConnectionAuthorizationRevoker revoker,
        IConfiguration configuration)
    {
        _connections = connections;
        _settingManager = settingManager;
        _users = users;
        _revoker = revoker;
        _configuration = configuration;
    }

    public virtual async Task<AiConnectorStatusDto> GetStatusAsync()
    {
        await RequireUseOrManageAsync();
        return await BuildStatusAsync(
            await SettingProvider.GetOrNullAsync(IntegrationsSettings.AiConnectorAcceptedAt),
            await SettingProvider.GetOrNullAsync(IntegrationsSettings.AiConnectorAcceptedByUserId));
    }

    [Authorize(IntegrationsPermissions.AiConnector.Manage)]
    public virtual async Task<AiConnectorStatusDto> AcceptAsync()
    {
        var acceptedAt = await SettingProvider.GetOrNullAsync(IntegrationsSettings.AiConnectorAcceptedAt);
        var acceptedBy = await SettingProvider.GetOrNullAsync(IntegrationsSettings.AiConnectorAcceptedByUserId);

        /* Accepting again keeps who accepted first and when: it is a statement already on record. */
        if (string.IsNullOrEmpty(acceptedAt))
        {
            acceptedAt = Clock.Now.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
            acceptedBy = CurrentUser.GetId().ToString();
            await _settingManager.SetForCurrentTenantAsync(IntegrationsSettings.AiConnectorAcceptedAt, acceptedAt);
            await _settingManager.SetForCurrentTenantAsync(IntegrationsSettings.AiConnectorAcceptedByUserId, acceptedBy);
            Logger.LogInformation("AI connector data sharing accepted in tenant {TenantId} by user {UserId}", CurrentTenant.Id, CurrentUser.Id);
        }

        /* From the values in hand: the setting cache only learns of a change once this unit of work has committed. */
        return await BuildStatusAsync(acceptedAt, acceptedBy);
    }

    [Authorize(IntegrationsPermissions.AiConnector.Manage)]
    public virtual async Task<AiConnectorStatusDto> WithdrawAsync()
    {
        await _settingManager.SetForCurrentTenantAsync(IntegrationsSettings.AiConnectorAcceptedAt, null);
        await _settingManager.SetForCurrentTenantAsync(IntegrationsSettings.AiConnectorAcceptedByUserId, null);

        var now = Clock.Now;
        var revoked = 0;
        var authorizationIds = new List<string>();
        while (true)
        {
            var query = (await _connections.GetQueryableAsync()).Where(c => c.RevokedAt == null).OrderBy(c => c.Id).Take(WithdrawBatchSize);
            var batch = await AsyncExecuter.ToListAsync(query);
            foreach (var connection in batch)
            {
                connection.Revoke(now, CurrentUser.Id);
                authorizationIds.Add(connection.AuthorizationId);
            }

            await _connections.UpdateManyAsync(batch, autoSave: true);
            revoked += batch.Count;
            if (batch.Count < WithdrawBatchSize)
            {
                break;
            }
        }

        /* After this tenant's transaction, so a failure there has revoked nothing; see AiConnectionAuthorizationRevoker. */
        _revoker.RevokeAfterCommit(authorizationIds);

        Logger.LogInformation("AI connector data sharing withdrawn in tenant {TenantId} by user {UserId}; {Count} connections revoked", CurrentTenant.Id, CurrentUser.Id, revoked);
        return await BuildStatusAsync(null, null);
    }

    private async Task RequireUseOrManageAsync()
    {
        if (!await AuthorizationService.IsGrantedAsync(IntegrationsPermissions.AiConnector.Use)
            && !await AuthorizationService.IsGrantedAsync(IntegrationsPermissions.AiConnector.Manage))
        {
            throw new AbpAuthorizationException(code: AbpAuthorizationErrorCodes.GivenPolicyHasNotGranted);
        }
    }

    private async Task<AiConnectorStatusDto> BuildStatusAsync(string? acceptedAt, string? acceptedBy)
    {
        var at = DateTime.TryParse(acceptedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed) ? parsed : (DateTime?)null;
        var byUser = Guid.TryParse(acceptedBy, out var userId) ? await _users.FindAsync(userId, includeDetails: false) : null;

        return new AiConnectorStatusDto
        {
            ConnectorUrl = (_configuration["App:SelfUrl"] ?? string.Empty).TrimEnd('/') + "/mcp",
            IsAccepted = at != null,
            AcceptedAt = at,
            AcceptedByUserName = byUser?.UserName,
        };
    }
}
