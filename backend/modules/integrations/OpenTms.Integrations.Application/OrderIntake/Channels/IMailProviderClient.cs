using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace OpenTms.Integrations.OrderIntake.Channels;

/// <summary>
/// One mail provider's OAuth and read-only mailbox API (design 72 "Channels"): consent, tokens, folders, the cursor of a
/// folder, the new messages since a cursor and the raw RFC 822 content of one. Plain REST over a named
/// <c>HttpClient</c>; nothing is moved, labelled or marked read. A refused refresh token surfaces as
/// <see cref="MailboxReconnectRequiredException"/>, every other provider failure as <see cref="MailProviderException"/>.
/// </summary>
public interface IMailProviderClient
{
    MailProvider Provider { get; }

    /// <summary>Where the browser goes for the user's consent; <paramref name="state"/> comes back on the callback.</summary>
    string BuildAuthorizeUrl(string state, string redirectUri);

    /// <summary>Redeems the authorization code of the callback.</summary>
    Task<MailTokenResult> ExchangeCodeAsync(string code, string redirectUri, CancellationToken cancellationToken = default);

    /// <summary>A new access token; Microsoft also rotates the refresh token (returned in <see cref="MailTokenResult.RefreshToken"/>).</summary>
    Task<MailTokenResult> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default);

    /// <summary>The address of the consented account.</summary>
    Task<string> GetAccountAddressAsync(string accessToken, CancellationToken cancellationToken = default);

    /// <summary>Gmail: the user labels and INBOX. Microsoft: the mail folders to depth 3, named by path ("Inbox/Orders").</summary>
    Task<IReadOnlyList<MailFolder>> ListFoldersAsync(string accessToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// The folder's current position, without importing anything: Gmail's profile historyId; Graph's delta link after paging
    /// the folder's current contents.
    /// </summary>
    Task<string> GetCurrentCursorAsync(string accessToken, string folderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The messages that arrived in the folder since <paramref name="cursor"/>, in order, each with the cursor to store once
    /// it is recorded (null when the provider has none per message), followed by checkpoints that carry the cursor to
    /// store when everything before them is recorded; the last item is always a checkpoint. When the cursor has expired
    /// the client falls back to the messages received since <paramref name="lastSyncAt"/> minus a day (the recorder
    /// dedupes them).
    /// </summary>
    IAsyncEnumerable<MailChange> GetChangesAsync(
        string accessToken, string folderId, string cursor, DateTime? lastSyncAt, CancellationToken cancellationToken = default);

    /// <summary>The raw RFC 822 content of a message, or null when it no longer exists.</summary>
    Task<MailRawMessage?> FetchRawAsync(string accessToken, MailMessageRef message, CancellationToken cancellationToken = default);
}

/// <param name="AccessToken">Short-lived; never stored.</param>
/// <param name="RefreshToken">Null when the provider kept the existing one.</param>
public record MailTokenResult(string AccessToken, string? RefreshToken);

public record MailFolder(string Id, string Name);

/// <param name="ProviderId">The id to fetch the message with.</param>
/// <param name="ExternalId">The dedupe key (design 72 "Dedupe"): Gmail's id, or the Message-ID header, else the Graph id.</param>
/// <param name="ReceivedAt">When the provider received it, when the listing tells.</param>
public record MailMessageRef(string ProviderId, string ExternalId, DateTime? ReceivedAt = null);

/// <summary>A new message (<see cref="Message"/> set) or a checkpoint (only <see cref="Cursor"/>).</summary>
public record MailChange(MailMessageRef? Message, string? Cursor)
{
    public static MailChange Checkpoint(string cursor) => new(null, cursor);
}

public record MailRawMessage(byte[] Content, DateTime? ReceivedAt);

/// <summary>The provider refused the refresh token (<c>invalid_grant</c>): the connection needs a new consent.</summary>
public class MailboxReconnectRequiredException : Exception
{
    public const string ErrorCode = "invalid_grant";

    public MailboxReconnectRequiredException(MailProvider provider)
        : base($"{provider} refused the refresh token (invalid_grant).")
    {
    }
}

/// <summary>A provider call failed; <see cref="ErrorCode"/> is short and carries no personal data (the connection's LastErrorCode).</summary>
public class MailProviderException : Exception
{
    public string ErrorCode { get; }

    /// <summary>The OAuth <c>error</c> of a refused token request (<c>invalid_client</c>, <c>invalid_grant</c>, …), when there was one.</summary>
    public string? OAuthError { get; }

    public MailProviderException(string errorCode, string message, Exception? inner = null, string? oAuthError = null)
        : base(message, inner)
    {
        ErrorCode = errorCode.Length <= OrderIntakeConsts.MaxErrorCodeLength ? errorCode : errorCode[..OrderIntakeConsts.MaxErrorCodeLength];
        OAuthError = oAuthError;
    }
}

/// <summary>Picks the client of a provider.</summary>
public class MailProviderClientResolver : ITransientDependency
{
    private readonly IEnumerable<IMailProviderClient> _clients;

    public MailProviderClientResolver(IEnumerable<IMailProviderClient> clients)
    {
        _clients = clients;
    }

    public IMailProviderClient Get(MailProvider provider)
        => _clients.FirstOrDefault(c => c.Provider == provider)
           ?? throw new AbpException($"No mail provider client is registered for {provider}.");
}
