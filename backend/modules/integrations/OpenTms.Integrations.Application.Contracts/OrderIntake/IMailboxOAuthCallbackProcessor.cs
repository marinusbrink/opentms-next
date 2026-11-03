using System.Threading;
using System.Threading.Tasks;

namespace OpenTms.Integrations.OrderIntake;

/// <summary>
/// The provider's OAuth callback of a mailbox connection (design 72 "Connect"), for the host's anonymous endpoint
/// <c>GET /api/integrations/mailbox-oauth/callback</c>: the tenant and connection come only from the data-protected
/// <c>state</c>. A valid state ends in a redirect to the SPA's mailboxes page (<c>?connected={id}</c> or
/// <c>?connectError={code}</c>); an invalid or expired state, or a connection that awaits no consent, ends in a plain 400
/// without a redirect.
/// </summary>
public interface IMailboxOAuthCallbackProcessor
{
    Task<MailboxOAuthCallbackResult> ProcessAsync(string? code, string? state, string? error, CancellationToken cancellationToken = default);
}

/// <param name="RedirectUrl">The SPA page to send the browser to; null when the state was refused (answer 400).</param>
public record MailboxOAuthCallbackResult(string? RedirectUrl)
{
    public static readonly MailboxOAuthCallbackResult Refused = new((string?)null);

    public bool IsRefused => RedirectUrl == null;
}
