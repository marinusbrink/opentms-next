using System;
using System.Threading;
using System.Threading.Tasks;

namespace OpenTms.Integrations.OrderIntake;

/// <summary>
/// The provider's OAuth callback of a mailbox connection (design 72 "Connect"), for the host's anonymous endpoint
/// <c>GET /api/integrations/mailbox-oauth/callback</c>: the tenant and connection come only from the data-protected
/// <c>state</c>. A valid state ends in a redirect to the SPA's mailboxes page (<c>?connected={id}</c> or
/// <c>?connectError={code}</c>); an invalid or expired state, or a connection that awaits no consent, ends in a plain 400
/// without a redirect. The consent completes only in the browser of the user who started it (design 72 build note 17):
/// <paramref name="session"/> must name the state's user and tenant, else <c>?connectError=session_required</c>.
/// </summary>
public interface IMailboxOAuthCallbackProcessor
{
    /// <param name="session">The OpenTMS sign-in the request carries (the auth server's cookie); null when it carries none.</param>
    Task<MailboxOAuthCallbackResult> ProcessAsync(string? code, string? state, string? error, MailboxOAuthCallbackSession? session = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// Who the browser on the callback is signed in to OpenTMS as: the effective user (the impersonated one during an
/// impersonation, which is the user the state recorded) and that user's tenant (null for the host).
/// </summary>
public record MailboxOAuthCallbackSession(Guid UserId, Guid? TenantId);

/// <param name="RedirectUrl">The SPA page to send the browser to; null when the state was refused (answer 400).</param>
public record MailboxOAuthCallbackResult(string? RedirectUrl)
{
    public static readonly MailboxOAuthCallbackResult Refused = new((string?)null);

    public bool IsRefused => RedirectUrl == null;
}
