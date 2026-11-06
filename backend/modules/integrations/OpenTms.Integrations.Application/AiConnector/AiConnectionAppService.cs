using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;
using OpenTms.Integrations.Permissions;
using OpenTms.Platform.Features;
using OpenTms.Platform.Grid;
using OpenTms.Platform.ReferenceData;
using Volo.Abp.Authorization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Features;
using Volo.Abp.Identity;
using Volo.Abp.Security.Claims;
using Volo.Abp.Uow;
using Volo.Abp.Users;

namespace OpenTms.Integrations.AiConnector;

/// <summary>
/// The connections of the AI connector (design 74 "Connector administration"): every connected tool shows here with whether it
/// is live, and can be switched off from here (PO decision 6). <c>Integrations.AiConnector.Use</c> reaches one's own,
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
    private readonly IPermissionChecker _permissions;

    public AiConnectionAppService(
        IAiConnectionRepository connections,
        IIdentityUserRepository users,
        IOpenIddictApplicationManager applications,
        AiConnectionAuthorizationRevoker revoker,
        IPermissionChecker permissions)
    {
        _connections = connections;
        _users = users;
        _applications = applications;
        _revoker = revoker;
        _permissions = permissions;
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
        var lapsedBefore = Clock.Now - AiConnectorConsts.RefreshTokenLifetime;
        var page = await AsyncExecuter.ToListAsync(Sort(query, input, lapsedBefore).Skip(input.StartRow).Take(input.EndRow - input.StartRow));

        var users = (await _users.GetListByIdsAsync(page.Select(c => c.UserId).Distinct().ToList())).ToDictionary(u => u.Id);
        var isLapsed = IsLapsed(lapsedBefore).Compile();

        /* Only a row that could still be live needs the user: a switched-off or expired one stays that way whatever the user holds. */
        var usersToCheck = page
            .Where(c => c.RevokedAt == null && !isLapsed(c))
            .Select(c => c.UserId)
            .Distinct()
            .Where(id => users.GetValueOrDefault(id)?.IsActive == true)
            .ToList();
        var usersServed = await GetUsersHoldingUseAsync(usersToCheck);

        var tools = await ToolNamesAsync(page.Select(c => c.ClientId).Distinct());

        return new GridResponse<AiConnectionRowDto>
        {
            Rows = page.Select(c => new AiConnectionRowDto
            {
                Id = c.Id,
                UserId = c.UserId,
                UserName = users.GetValueOrDefault(c.UserId)?.UserName,
                ToolName = tools.GetValueOrDefault(c.ClientId, c.ClientId),
                ConnectedAt = c.ConnectedAt,
                LastUsedAt = c.LastUsedAt,
                RevokedAt = c.RevokedAt,
                Status = c.RevokedAt != null ? AiConnectionStatus.SwitchedOff
                    : isLapsed(c) ? AiConnectionStatus.Expired
                    : usersServed.Contains(c.UserId) ? AiConnectionStatus.Live
                    : AiConnectionStatus.NotWorking,
            }).OrderBy(row => row.Status).ToList(),
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

        /* RevokedAt blocks /mcp, this blocks renewal, after this tenant's transaction so a failure there has revoked nothing.
         * Idempotent: revoking again retries a failure of the host step. */
        _revoker.RevokeAfterCommit([connection.AuthorizationId]);
        Logger.LogInformation("AI connection {ConnectionId} revoked in tenant {TenantId} by user {UserId}", connection.Id, CurrentTenant.Id, CurrentUser.Id);
    }

    /// <summary>
    /// Of these users, those who hold <c>Integrations.AiConnector.Use</c>, decided by the same <see cref="IPermissionChecker"/> call
    /// <c>/mcp</c> makes (check 4), so this screen and <c>/mcp</c> cannot disagree. ABP has no permission API across users: its batch
    /// overloads take many permissions for one user. So the roles come in one batched read, and each user of the page gets one check,
    /// which reads ABP's permission cache and reaches the database only on a cold cache.
    /// </summary>
    private async Task<HashSet<Guid>> GetUsersHoldingUseAsync(List<Guid> userIds)
    {
        var holding = new HashSet<Guid>();
        if (userIds.Count == 0)
        {
            return holding;
        }

        var roles = (await _users.GetRoleNamesAsync(userIds)).ToDictionary(u => u.Id, u => u.RoleNames);
        foreach (var userId in userIds)
        {
            var claims = new List<Claim> { new(AbpClaimTypes.UserId, userId.ToString()) };
            if (CurrentTenant.Id != null)
            {
                claims.Add(new Claim(AbpClaimTypes.TenantId, CurrentTenant.Id.Value.ToString()));
            }

            claims.AddRange(roles.GetValueOrDefault(userId, []).Select(role => new Claim(AbpClaimTypes.Role, role)));
            if (await _permissions.IsGrantedAsync(new ClaimsPrincipal(new ClaimsIdentity(claims)), IntegrationsPermissions.AiConnector.Use))
            {
                holding.Add(userId);
            }
        }

        return holding;
    }

    /// <summary>The refresh token has lapsed: neither the last use nor the connecting is within its lifetime. One rule for the sort and the status.</summary>
    private static Expression<Func<AiConnection, bool>> IsLapsed(DateTime lapsedBefore) =>
        c => (c.LastUsedAt == null || c.LastUsedAt < lapsedBefore) && c.ConnectedAt < lapsedBefore;

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

    /// <summary>
    /// Switched-off rows last and expired ones before them, then the newest (design 74); a column the grid asks for sorts within
    /// that. Whether a user may still connect lives in another module's tables, so SQL cannot order on it: the page itself puts
    /// Live before NotWorking.
    /// </summary>
    private static IQueryable<AiConnection> Sort(IQueryable<AiConnection> query, ReferenceDataListRequestDto input, DateTime lapsedBefore)
    {
        var live = query.OrderBy(c => c.RevokedAt != null).ThenBy(IsLapsed(lapsedBefore));
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
