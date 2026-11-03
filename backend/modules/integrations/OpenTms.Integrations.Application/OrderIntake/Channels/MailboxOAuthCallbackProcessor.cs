using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTms.Integrations.Features;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Features;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace OpenTms.Integrations.OrderIntake.Channels;

/// <summary>
/// The OAuth callback of a mailbox connection (design 72 "Connect"). The tenant comes only from the data-protected state
/// ("Tenant dimension"): no header, no cookie, no query parameter decides it. A state that does not unprotect, has
/// expired, or names a connection that is not waiting for consent is refused without a redirect. Otherwise the code is
/// exchanged, the account's address read, the refresh token stored encrypted, and the browser goes back to the SPA with
/// <c>?connected={id}</c> or <c>?connectError={code}</c> (one of <see cref="ConnectErrors"/>; never provider text). The
/// request must carry the OpenTMS sign-in of the state's user and tenant, else <see cref="ConnectErrors.SessionRequired"/>.
/// </summary>
public class MailboxOAuthCallbackProcessor : IMailboxOAuthCallbackProcessor, ITransientDependency
{
    /// <summary>The codes of <c>?connectError=</c>, for the SPA's message.</summary>
    public static class ConnectErrors
    {
        public const string ConsentDenied = "consent_denied";
        public const string NotConfigured = "not_configured";
        public const string AccountMismatch = "account_mismatch";
        public const string AlreadyConnected = "already_connected";
        public const string NoRefreshToken = "no_refresh_token";
        public const string FeatureDisabled = "feature_disabled";
        public const string ProviderError = "provider_error";
        public const string SessionRequired = "session_required";
    }

    private readonly MailboxOAuthStateProtector _states;
    private readonly ICurrentTenant _currentTenant;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly IRepository<MailboxConnection, Guid> _connections;
    private readonly MailProviderClientResolver _clients;
    private readonly MailboxTokenService _tokens;
    private readonly MailboxOAuthUrls _urls;
    private readonly IFeatureChecker _features;
    private readonly IClock _clock;

    public ILogger<MailboxOAuthCallbackProcessor> Logger { get; set; } = NullLogger<MailboxOAuthCallbackProcessor>.Instance;

    public MailboxOAuthCallbackProcessor(
        MailboxOAuthStateProtector states,
        ICurrentTenant currentTenant,
        IUnitOfWorkManager unitOfWorkManager,
        IRepository<MailboxConnection, Guid> connections,
        MailProviderClientResolver clients,
        MailboxTokenService tokens,
        MailboxOAuthUrls urls,
        IFeatureChecker features,
        IClock clock)
    {
        _states = states;
        _currentTenant = currentTenant;
        _unitOfWorkManager = unitOfWorkManager;
        _connections = connections;
        _clients = clients;
        _tokens = tokens;
        _urls = urls;
        _features = features;
        _clock = clock;
    }

    public async Task<MailboxOAuthCallbackResult> ProcessAsync(
        string? code, string? state, string? error, MailboxOAuthCallbackSession? session = null, CancellationToken cancellationToken = default)
    {
        var payload = _states.Unprotect(state);
        if (payload == null)
        {
            Logger.LogInformation("Mailbox OAuth callback refused: the state is missing, invalid or expired.");
            return MailboxOAuthCallbackResult.Refused;
        }

        using (_currentTenant.Change(payload.TenantId))
        {
            using var unitOfWork = _unitOfWorkManager.Begin(new AbpUnitOfWorkOptions(isTransactional: true), requiresNew: true);
            var connection = await _connections.FindAsync(payload.ConnectionId, cancellationToken: cancellationToken);
            if (connection == null || !connection.AwaitsConsent)
            {
                Logger.LogInformation(
                    "Mailbox OAuth callback refused for connection {ConnectionId} of tenant {TenantId}: unknown or not awaiting consent.",
                    payload.ConnectionId, payload.TenantId);
                return MailboxOAuthCallbackResult.Refused;
            }

            /* Bound to the browser of the user who started the connect (RFC 6749 §10.12, design 72 build note 17): without it, a
             * consent link sent to someone else would connect their mailbox to the sender's tenant. Checked before the
             * code is exchanged; an unknown or not-waiting connection stays a plain 400. */
            if (session == null || payload.UserId == null || session.UserId != payload.UserId || session.TenantId != payload.TenantId)
            {
                Logger.LogWarning(
                    "Mailbox OAuth callback for connection {ConnectionId} of tenant {TenantId} refused: the browser is not signed in as the user who started it (session user {SessionUserId}, tenant {SessionTenantId}).",
                    payload.ConnectionId, payload.TenantId, session?.UserId, session?.TenantId);
                return Error(ConnectErrors.SessionRequired);
            }

            if (!await _features.IsEnabledAsync(IntegrationsFeatures.OrderIntake))
            {
                return Error(ConnectErrors.FeatureDisabled);
            }

            if (!string.IsNullOrEmpty(error) || string.IsNullOrWhiteSpace(code))
            {
                Logger.LogInformation("Mailbox connection {ConnectionId}: the provider returned no consent.", connection.Id);
                return Error(ConnectErrors.ConsentDenied);
            }

            try
            {
                var redirectUri = await _urls.FindRedirectUriAsync();
                if (redirectUri == null)
                {
                    /* App:SelfUrl missing: a configuration gap, answered like an unconfigured provider, never a 500 (review minor 6). */
                    Logger.LogWarning("Mailbox connection {ConnectionId}: the OAuth redirect URI is not configured (App:SelfUrl).", connection.Id);
                    return Error(ConnectErrors.NotConfigured);
                }

                var client = _clients.Get(connection.Provider);
                var tokens = await client.ExchangeCodeAsync(code, redirectUri, cancellationToken);
                if (tokens.RefreshToken == null)
                {
                    return Error(ConnectErrors.NoRefreshToken);
                }

                var address = (await client.GetAccountAddressAsync(tokens.AccessToken, cancellationToken)).Trim().ToLowerInvariant();
                var folderId = connection.FolderId;
                if (await _connections.AnyAsync(
                        c => c.Id != connection.Id && c.Provider == connection.Provider && c.EmailAddress == address && c.FolderId == folderId,
                        cancellationToken))
                {
                    return Error(ConnectErrors.AlreadyConnected);
                }

                connection.CompleteConsent(address, _tokens.Encrypt(tokens.RefreshToken), _clock.Now);
                await _connections.UpdateAsync(connection, autoSave: true, cancellationToken);
                await unitOfWork.CompleteAsync(cancellationToken);

                Logger.LogInformation(
                    "Mailbox connection {ConnectionId} of tenant {TenantId} consented by user {UserId}; status {Status}.",
                    connection.Id, payload.TenantId, payload.UserId, connection.Status);
                return new MailboxOAuthCallbackResult(_urls.MailboxesPage("connected", connection.Id.ToString()));
            }
            catch (BusinessException e) when (e.Code == IntegrationsErrorCodes.MailboxAccountMismatch)
            {
                return Error(ConnectErrors.AccountMismatch);
            }
            catch (BusinessException e) when (e.Code == IntegrationsErrorCodes.ProviderNotConfigured)
            {
                return Error(ConnectErrors.NotConfigured);
            }
            catch (Exception e) when (e is MailProviderException or MailboxReconnectRequiredException)
            {
                Logger.LogWarning(
                    "Mailbox connection {ConnectionId}: the consent could not be completed ({ErrorCode}).",
                    connection.Id, (e as MailProviderException)?.ErrorCode ?? MailboxReconnectRequiredException.ErrorCode);
                return Error(ConnectErrors.ProviderError);
            }
        }
    }

    private MailboxOAuthCallbackResult Error(string connectError) => new(_urls.MailboxesPage("connectError", connectError));
}
