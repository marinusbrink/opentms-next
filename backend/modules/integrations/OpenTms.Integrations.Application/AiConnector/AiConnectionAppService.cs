using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;
using OpenTms.Integrations.Permissions;
using OpenTms.Platform.Features;
using OpenTms.Platform.Grid;
using OpenTms.Platform.ReferenceData;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Features;
using Volo.Abp.Identity;
using Volo.Abp.Uow;
using Volo.Abp.Users;

namespace OpenTms.Integrations.AiConnector;

/// <summary>
/// The connections of the AI connector (design 74 "Connector administration"): every connected tool shows here as live, and
/// can be switched off from here (PO decision 6). <c>Integrations.AiConnector.Use</c> reaches one's own,
/// <c>Integrations.AiConnector.Manage</c> everyone's, so those checks depend on the row and are made in the method. Every query
/// is tenant-filtered, so a row of another tenant is not found.
/// </summary>
[RequiresFeature(AppFeatures.Integrations, AppFeatures.IntegrationsApp.AiConnector, RequiresAll = true)]
[Authorize]
public class AiConnectionAppService : IntegrationsAppServiceBase, IAiConnectionAppService
{
    private readonly IAiConnectionRepository _connections;
    private readonly IIdentityUserRepository _users;
    private readonly IOpenIddictApplicationManager _applications;
    private readonly AiConnectionAuthorizationRevoker _revoker;

    public AiConnectionAppService(
        IAiConnectionRepository connections,
        IIdentityUserRepository users,
        IOpenIddictApplicationManager applications,
        AiConnectionAuthorizationRevoker revoker)
    {
        _connections = connections;
        _users = users;
        _applications = applications;
        _revoker = revoker;
    }

    public virtual async Task<GridResponse<AiConnectionRowDto>> GetListAsync(ReferenceDataListRequestDto input)
    {
        var all = await AuthorizationService.IsGrantedAsync(IntegrationsPermissions.AiConnector.Manage);
        if (!all && !await AuthorizationService.IsGrantedAsync(IntegrationsPermissions.AiConnector.Use))
        {
            throw new AbpAuthorizationException(code: AbpAuthorizationErrorCodes.GivenPolicyHasNotGranted);
        }

        var userId = CurrentUser.GetId();
        var query = await _connections.GetQueryableAsync();
        if (!all)
        {
            query = query.Where(c => c.UserId == userId);
        }

        var count = await AsyncExecuter.LongCountAsync(query);
        var page = await AsyncExecuter.ToListAsync(Sort(query, input).Skip(input.StartRow).Take(input.EndRow - input.StartRow));

        var users = (await _users.GetListByIdsAsync(page.Select(c => c.UserId).Distinct().ToList())).ToDictionary(u => u.Id, u => u.UserName);
        var tools = await ToolNamesAsync(page.Select(c => c.ClientId).Distinct());

        return new GridResponse<AiConnectionRowDto>
        {
            Rows = page.Select(c => new AiConnectionRowDto
            {
                Id = c.Id,
                UserId = c.UserId,
                UserName = users.GetValueOrDefault(c.UserId),
                ToolName = tools.GetValueOrDefault(c.ClientId, c.ClientId),
                ConnectedAt = c.ConnectedAt,
                LastUsedAt = c.LastUsedAt,
                RevokedAt = c.RevokedAt,
            }).ToList(),
            TotalCount = count,
            FilteredCount = count,
        };
    }

    public virtual async Task RevokeAsync(Guid id)
    {
        var connection = await _connections.FindAsync(id) ?? throw new EntityNotFoundException(typeof(AiConnection), id);
        var permission = connection.UserId == CurrentUser.GetId() ? IntegrationsPermissions.AiConnector.Use : IntegrationsPermissions.AiConnector.Manage;
        if (!await AuthorizationService.IsGrantedAsync(permission) && !await AuthorizationService.IsGrantedAsync(IntegrationsPermissions.AiConnector.Manage))
        {
            throw new AbpAuthorizationException(code: AbpAuthorizationErrorCodes.GivenPolicyHasNotGranted);
        }

        connection.Revoke(Clock.Now, CurrentUser.Id);
        await _connections.UpdateAsync(connection, autoSave: true);

        /* RevokedAt blocks /mcp, this blocks renewal. Idempotent: revoking again retries a failure here. */
        await _revoker.RevokeAsync(connection.AuthorizationId);
        Logger.LogInformation("AI connection {ConnectionId} revoked in tenant {TenantId} by user {UserId}", connection.Id, CurrentTenant.Id, CurrentUser.Id);
    }

    /// <summary>The name of a tool comes from its OpenIddict application in the host database (design 74: never from a caller).</summary>
    private async Task<Dictionary<string, string>> ToolNamesAsync(IEnumerable<string> clientIds)
    {
        var names = new Dictionary<string, string>();
        using (CurrentTenant.Change(null))
        {
            using var unitOfWork = UnitOfWorkManager.Begin(new AbpUnitOfWorkOptions(isTransactional: false), requiresNew: true);
            foreach (var clientId in clientIds)
            {
                var application = await _applications.FindByClientIdAsync(clientId);
                names[clientId] = (application == null ? null : await _applications.GetDisplayNameAsync(application)) ?? clientId;
            }

            await unitOfWork.CompleteAsync();
        }

        return names;
    }

    /// <summary>Live rows first, then the newest (design 74); a column the grid asks for sorts within that.</summary>
    private static IQueryable<AiConnection> Sort(IQueryable<AiConnection> query, ReferenceDataListRequestDto input)
    {
        var live = query.OrderBy(c => c.RevokedAt != null);
        return (input.SortBy?.ToLowerInvariant(), input.Descending) switch
        {
            ("connectedat", false) => live.ThenBy(c => c.ConnectedAt).ThenBy(c => c.Id),
            ("lastusedat", false) => live.ThenBy(c => c.LastUsedAt).ThenBy(c => c.Id),
            ("lastusedat", true) => live.ThenByDescending(c => c.LastUsedAt).ThenBy(c => c.Id),
            ("revokedat", false) => live.ThenBy(c => c.RevokedAt).ThenBy(c => c.Id),
            ("revokedat", true) => live.ThenByDescending(c => c.RevokedAt).ThenBy(c => c.Id),
            _ => live.ThenByDescending(c => c.ConnectedAt).ThenBy(c => c.Id),
        };
    }
}
