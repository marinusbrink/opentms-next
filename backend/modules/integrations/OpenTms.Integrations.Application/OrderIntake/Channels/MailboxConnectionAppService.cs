using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using OpenTms.Integrations.Features;
using OpenTms.Integrations.Permissions;
using OpenTms.Platform.Features;
using OpenTms.Platform.Grid;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Features;
using Volo.Abp.Timing;
using Volo.Abp.Uow;
using Volo.Abp.Validation;

namespace OpenTms.Integrations.OrderIntake.Channels;

/// <summary>
/// Master data → Integrations → Order intake → Mailboxes (design 72 "Channels"). Connect creates the row and returns the
/// provider's authorize URL (the anonymous callback completes it); choosing a folder sets the connection Activating and
/// enqueues <see cref="MailboxActivationJob"/>, which takes the provider's current cursor and activates it, so only mail
/// arriving afterwards is imported. Deleting forgets the refresh token and cursor before the soft delete. Gated by the tenant feature
/// <see cref="IntegrationsFeatures.OrderIntake"/> and the nav node's app features; the refresh token never leaves this
/// module. Default customer and order type are stored as given: their names come from the screen's own lookups (no
/// synchronous call to Master Data or Orders is stated for them in design 72).
/// </summary>
[Authorize(IntegrationsPermissions.Mailboxes.Default)]
[RequiresFeature(
    IntegrationsFeatures.OrderIntake,
    AppFeatures.MasterData.Default,
    AppFeatures.MasterData.Integrations.Default,
    AppFeatures.MasterData.Integrations.Intake.Default,
    AppFeatures.MasterData.Integrations.Intake.Mailboxes,
    RequiresAll = true)]
public class MailboxConnectionAppService : IntegrationsAppServiceBase, IMailboxConnectionAppService
{
    private readonly IRepository<MailboxConnection, Guid> _connections;
    private readonly MailProviderClientResolver _clients;
    private readonly MailboxTokenService _tokens;
    private readonly MailboxOAuthStateProtector _states;
    private readonly MailboxOAuthUrls _urls;
    private readonly IClock _clock;
    private readonly OrderIntakeJobQueue _jobs;

    public MailboxConnectionAppService(
        IRepository<MailboxConnection, Guid> connections,
        MailProviderClientResolver clients,
        MailboxTokenService tokens,
        MailboxOAuthStateProtector states,
        MailboxOAuthUrls urls,
        IClock clock,
        OrderIntakeJobQueue jobs)
    {
        _jobs = jobs;
        _connections = connections;
        _clients = clients;
        _tokens = tokens;
        _states = states;
        _urls = urls;
        _clock = clock;
    }

    public async Task<GridResponse<MailboxConnectionRowDto>> GetListAsync(MailboxConnectionListRequestDto input)
    {
        var query = await _connections.GetQueryableAsync();
        var total = await AsyncExecuter.LongCountAsync(query);

        if (input.Status.Count > 0)
        {
            var statuses = input.Status.Distinct().ToList();
            query = query.Where(c => statuses.Contains(c.Status));
        }

        if (!string.IsNullOrWhiteSpace(input.WildcardSearch))
        {
            var term = input.WildcardSearch.Trim().ToLower();
            query = query.Where(c => c.DisplayName.ToLower().Contains(term) || (c.EmailAddress != null && c.EmailAddress.Contains(term)));
        }

        var filtered = await AsyncExecuter.LongCountAsync(query);
        var rows = await AsyncExecuter.ToListAsync(
            Sort(query, input.SortBy, input.Descending).Skip(input.StartRow).Take(input.EndRow - input.StartRow));

        return new GridResponse<MailboxConnectionRowDto>
        {
            Rows = rows.Select(ToRow).ToList(),
            TotalCount = total,
            FilteredCount = filtered
        };
    }

    public async Task<MailboxConnectionDto> GetAsync(Guid id) => ToDto(await _connections.GetAsync(id));

    [Authorize(IntegrationsPermissions.Mailboxes.Manage)]
    public async Task<MailboxConnectionCreateResultDto> CreateAsync(MailboxConnectionCreateDto input)
    {
        if (!Enum.IsDefined(input.Provider))
        {
            throw new AbpValidationException([new System.ComponentModel.DataAnnotations.ValidationResult("Unknown provider.", [nameof(input.Provider)])]);
        }

        var id = GuidGenerator.Create();
        /* Built before the row is stored: an unconfigured provider (ProviderNotConfigured) leaves nothing behind. */
        var authorizeUrl = await AuthorizeUrlAsync(input.Provider, id);
        await _connections.InsertAsync(new MailboxConnection(id, CurrentTenant.Id, input.Provider, input.DisplayName), autoSave: true);

        Logger.LogInformation("Mailbox connection {ConnectionId} ({Provider}) created by user {UserId}.", id, input.Provider, CurrentUser.Id);
        return new MailboxConnectionCreateResultDto { Id = id, AuthorizeUrl = authorizeUrl };
    }

    [Authorize(IntegrationsPermissions.Mailboxes.Manage)]
    public async Task<MailboxAuthorizeUrlDto> ReconnectAsync(Guid id)
    {
        var connection = await _connections.GetAsync(id);
        if (!connection.AwaitsConsent)
        {
            throw new BusinessException(IntegrationsErrorCodes.MailboxStateInvalid).WithData("status", connection.Status);
        }

        return new MailboxAuthorizeUrlDto { AuthorizeUrl = await AuthorizeUrlAsync(connection.Provider, connection.Id) };
    }

    [Authorize(IntegrationsPermissions.Mailboxes.Manage)]
    public async Task<List<MailboxFolderDto>> GetFoldersAsync(Guid id)
    {
        var connection = await _connections.GetAsync(id);
        EnsureConsented(connection);
        var folders = await CallProviderAsync(connection, (client, accessToken) => client.ListFoldersAsync(accessToken));
        return folders.Select(f => new MailboxFolderDto { Id = f.Id, Name = f.Name }).ToList();
    }

    [Authorize(IntegrationsPermissions.Mailboxes.Manage)]
    public async Task<MailboxConnectionDto> UpdateAsync(Guid id, MailboxConnectionUpdateDto input)
    {
        var connection = await _connections.GetAsync(id);
        connection.ConcurrencyStamp = input.ConcurrencyStamp;
        connection.Rename(input.DisplayName);
        connection.SetDefaults(input.DefaultCustomerRelationId, input.DefaultTransportOrderTypeId, input.AutoConfirm);

        var folderId = string.IsNullOrWhiteSpace(input.FolderId) ? null : input.FolderId.Trim();
        var activate = false;
        if (folderId != null && (folderId != connection.FolderId || connection.Status is MailboxStatus.AwaitingFolder or MailboxStatus.Activating))
        {
            EnsureConsented(connection);
            var duplicate = await _connections.AnyAsync(c =>
                c.Id != connection.Id && c.Provider == connection.Provider && c.EmailAddress == connection.EmailAddress && c.FolderId == folderId);
            if (duplicate)
            {
                throw new BusinessException(IntegrationsErrorCodes.MailboxAlreadyConnected).WithData("emailAddress", connection.EmailAddress ?? string.Empty);
            }

            await CallProviderAsync(connection, async (client, accessToken) =>
            {
                var folder = (await client.ListFoldersAsync(accessToken)).FirstOrDefault(f => f.Id == folderId)
                             ?? throw new UserFriendlyException(L["Mailboxes:FolderNotFound"]);
                /* Activating: the provider's cursor is taken by MailboxActivationJob, not in this request (Graph pages the
                 * folder's whole contents; design 72 build note 18). Only mail arriving after activation is imported. */
                connection.ChooseFolder(folder.Id, folder.Name);
                return true;
            });
            activate = true;
        }

        await _connections.UpdateAsync(connection, autoSave: true);
        if (activate)
        {
            /* Enqueued once this unit of work commits, so the job finds the connection Activating. */
            await _jobs.EnqueueActivationAsync(connection.Id);
            Logger.LogInformation("Mailbox connection {ConnectionId} activating on a folder, chosen by user {UserId}.", connection.Id, CurrentUser.Id);
        }

        return ToDto(connection);
    }

    [Authorize(IntegrationsPermissions.Mailboxes.Manage)]
    public async Task<MailboxConnectionDto> PauseAsync(Guid id)
    {
        var connection = await _connections.GetAsync(id);
        connection.Pause();
        await _connections.UpdateAsync(connection, autoSave: true);
        return ToDto(connection);
    }

    [Authorize(IntegrationsPermissions.Mailboxes.Manage)]
    public async Task<MailboxConnectionDto> ResumeAsync(Guid id)
    {
        var connection = await _connections.GetAsync(id);
        connection.Resume();
        await _connections.UpdateAsync(connection, autoSave: true);
        return ToDto(connection);
    }

    [Authorize(IntegrationsPermissions.Mailboxes.Manage)]
    public async Task DeleteAsync(Guid id)
    {
        var connection = await _connections.FindAsync(id);
        if (connection == null)
        {
            return;
        }

        /* The soft-deleted row keeps no lasting access (design 72 build note 21). Saved first: ABP reloads a soft-deleted
         * entry from the database before setting IsDeleted, which would drop unsaved changes. */
        connection.Forget();
        await _connections.UpdateAsync(connection, autoSave: true);
        await _connections.DeleteAsync(connection, autoSave: true);
        Logger.LogInformation("Mailbox connection {ConnectionId} deleted by user {UserId}.", id, CurrentUser.Id);
    }

    private async Task<string> AuthorizeUrlAsync(MailProvider provider, Guid connectionId)
    {
        /* A missing App:SelfUrl is a configuration gap like a provider without client id/secret (BuildAuthorizeUrl throws
         * that one): the same ProviderNotConfigured business error, never a 500 (review minor 6, follow-up). */
        var redirectUri = await _urls.FindRedirectUriAsync();
        if (redirectUri == null)
        {
            Logger.LogWarning("Mailbox connection {ConnectionId}: the OAuth redirect URI is not configured (App:SelfUrl).", connectionId);
            throw new BusinessException(IntegrationsErrorCodes.ProviderNotConfigured).WithData("provider", provider.ToString());
        }

        var state = _states.Protect(CurrentTenant.Id, connectionId, CurrentUser.Id);
        return _clients.Get(provider).BuildAuthorizeUrl(state, redirectUri);
    }

    private static void EnsureConsented(MailboxConnection connection)
    {
        if (connection.RefreshTokenEncrypted == null || connection.Status is MailboxStatus.AwaitingConsent or MailboxStatus.Reconnect)
        {
            throw new BusinessException(IntegrationsErrorCodes.MailboxStateInvalid).WithData("status", connection.Status);
        }
    }

    /// <summary>
    /// Runs a provider call with a fresh access token (a rotated refresh token is saved with the connection). A refused
    /// refresh token puts the connection in Reconnect at once, in its own unit of work, so the screen shows Reconnect.
    /// </summary>
    private async Task<T> CallProviderAsync<T>(MailboxConnection connection, Func<IMailProviderClient, string, Task<T>> call)
    {
        try
        {
            var accessToken = await _tokens.GetAccessTokenAsync(connection);
            var result = await call(_clients.Get(connection.Provider), accessToken);
            await _connections.UpdateAsync(connection, autoSave: true);
            return result;
        }
        catch (MailboxReconnectRequiredException)
        {
            using (var unitOfWork = UnitOfWorkManager.Begin(new AbpUnitOfWorkOptions(isTransactional: true), requiresNew: true))
            {
                var fresh = await _connections.GetAsync(connection.Id);
                fresh.MarkReconnect(MailboxReconnectRequiredException.ErrorCode, _clock.Now);
                await _connections.UpdateAsync(fresh, autoSave: true);
                await unitOfWork.CompleteAsync();
            }

            Logger.LogInformation("Mailbox connection {ConnectionId} needs a new consent (invalid_grant).", connection.Id);
            throw new BusinessException(IntegrationsErrorCodes.MailboxStateInvalid).WithData("status", MailboxStatus.Reconnect);
        }
        catch (MailProviderException e)
        {
            Logger.LogWarning("Mailbox connection {ConnectionId}: provider call failed ({ErrorCode}).", connection.Id, e.ErrorCode);
            throw new UserFriendlyException(L["Mailboxes:ProviderError"]);
        }
    }

    private static IQueryable<MailboxConnection> Sort(IQueryable<MailboxConnection> query, string? sortBy, bool descending)
        => (sortBy?.ToLowerInvariant()) switch
        {
            null or "" or "displayname" => Order(query, c => c.DisplayName, descending),
            "provider" => Order(query, c => c.Provider, descending),
            "emailaddress" => Order(query, c => c.EmailAddress, descending),
            "foldername" => Order(query, c => c.FolderName, descending),
            "status" => Order(query, c => c.Status, descending),
            "lastsyncat" => Order(query, c => c.LastSyncAt, descending),
            "lasterrorat" => Order(query, c => c.LastErrorAt, descending),
            "creationtime" => Order(query, c => c.CreationTime, descending),
            _ => throw new AbpValidationException([new System.ComponentModel.DataAnnotations.ValidationResult($"Unknown sort column '{sortBy}'.", ["SortBy"])])
        };

    private static IQueryable<MailboxConnection> Order<TKey>(IQueryable<MailboxConnection> query, Expression<Func<MailboxConnection, TKey>> key, bool descending)
        => (descending ? query.OrderByDescending(key) : query.OrderBy(key)).ThenBy(c => c.Id);

    private static MailboxConnectionRowDto ToRow(MailboxConnection c) => new()
    {
        Id = c.Id,
        DisplayName = c.DisplayName,
        Provider = c.Provider,
        EmailAddress = c.EmailAddress,
        FolderName = c.FolderName,
        Status = c.Status,
        LastSyncAt = c.LastSyncAt,
        LastErrorCode = c.LastErrorCode,
        LastErrorAt = c.LastErrorAt,
        CreationTime = c.CreationTime
    };

    private static MailboxConnectionDto ToDto(MailboxConnection c) => new()
    {
        Id = c.Id,
        CreationTime = c.CreationTime,
        CreatorId = c.CreatorId,
        LastModificationTime = c.LastModificationTime,
        LastModifierId = c.LastModifierId,
        ConcurrencyStamp = c.ConcurrencyStamp,
        DisplayName = c.DisplayName,
        Provider = c.Provider,
        EmailAddress = c.EmailAddress,
        FolderId = c.FolderId,
        FolderName = c.FolderName,
        Status = c.Status,
        ConnectedAt = c.ConnectedAt,
        LastSyncAt = c.LastSyncAt,
        LastErrorCode = c.LastErrorCode,
        LastErrorAt = c.LastErrorAt,
        DefaultCustomerRelationId = c.DefaultCustomerRelationId,
        DefaultTransportOrderTypeId = c.DefaultTransportOrderTypeId,
        AutoConfirm = c.AutoConfirm
    };
}
