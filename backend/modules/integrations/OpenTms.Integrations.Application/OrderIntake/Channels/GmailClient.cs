using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Volo.Abp.Timing;

namespace OpenTms.Integrations.OrderIntake.Channels;

/// <summary>
/// Gmail through its REST API (design 72 "Channels"): scope <c>gmail.readonly</c> with offline access and
/// <c>prompt=consent</c> (so a refresh token always comes back); the label is the folder; the cursor is the mailbox's
/// historyId. New mail = the history records <c>messageAdded</c> and <c>labelAdded</c> for the label; a cursor Gmail no
/// longer knows (404) falls back to listing the label's messages after the last sync minus a day. The external id is
/// Gmail's message id.
/// </summary>
public class GmailClient : MailProviderClientBase
{
    public const string Name = "OrderIntake.Gmail";

    public const string AuthorizeEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";

    public const string GoogleTokenEndpoint = "https://oauth2.googleapis.com/token";

    public const string ApiBase = "https://gmail.googleapis.com/gmail/v1/users/me/";

    public const string Scope = "https://www.googleapis.com/auth/gmail.readonly";

    public const string InboxLabelId = "INBOX";

    private const int PageSize = 500;

    public GmailClient(IHttpClientFactory httpClientFactory, IOptions<MailOAuthOptions> options, IClock clock)
        : base(httpClientFactory, options, clock)
    {
    }

    public override MailProvider Provider => MailProvider.Gmail;

    protected override string HttpClientName => Name;

    protected override string ErrorPrefix => "gmail";

    protected override string TokenEndpoint => GoogleTokenEndpoint;

    public override string BuildAuthorizeUrl(string state, string redirectUri)
    {
        var client = RequireClient();
        return Query(AuthorizeEndpoint,
        [
            new("client_id", client.ClientId!),
            new("redirect_uri", redirectUri),
            new("response_type", "code"),
            new("scope", Scope),
            new("access_type", "offline"),
            new("prompt", "consent"),
            new("state", state)
        ]);
    }

    public override Task<MailTokenResult> ExchangeCodeAsync(string code, string redirectUri, CancellationToken cancellationToken = default)
    {
        var client = RequireClient();
        return PostTokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["client_id"] = client.ClientId!,
            ["client_secret"] = client.ClientSecret!,
            ["redirect_uri"] = redirectUri
        }, isRefresh: false, cancellationToken);
    }

    public override Task<MailTokenResult> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var client = RequireClient();
        return PostTokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = client.ClientId!,
            ["client_secret"] = client.ClientSecret!
        }, isRefresh: true, cancellationToken);
    }

    public override async Task<string> GetAccountAddressAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        using var profile = (await GetJsonAsync(ApiBase + "profile", accessToken, cancellationToken))!;
        return GetString(profile.RootElement, "emailAddress")
               ?? throw new MailProviderException("gmail_no_address", "Gmail's profile has no address.");
    }

    public override async Task<IReadOnlyList<MailFolder>> ListFoldersAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        using var labels = (await GetJsonAsync(ApiBase + "labels", accessToken, cancellationToken))!;
        return GetArray(labels.RootElement, "labels")
            .Select(l => (Id: GetString(l, "id"), Name: GetString(l, "name"), Type: GetString(l, "type")))
            .Where(l => l.Id != null && l.Name != null && (l.Id == InboxLabelId || string.Equals(l.Type, "user", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(l => l.Id == InboxLabelId ? 0 : 1)
            .ThenBy(l => l.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(l => new MailFolder(l.Id!, l.Name!))
            .ToList();
    }

    public override async Task<string> GetCurrentCursorAsync(string accessToken, string folderId, CancellationToken cancellationToken = default)
        => await GetHistoryIdAsync(accessToken, cancellationToken);

    public override async IAsyncEnumerable<MailChange> GetChangesAsync(
        string accessToken, string folderId, string cursor, DateTime? lastSyncAt, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? pageToken = null;
        do
        {
            var page = await ReadHistoryPageAsync(accessToken, folderId, cursor, pageToken, cancellationToken);
            if (page == null)
            {
                /* The cursor is older than Gmail keeps history: list the label instead; the recorder dedupes. */
                await foreach (var change in FallbackAsync(accessToken, folderId, lastSyncAt, cancellationToken))
                {
                    yield return change;
                }

                yield break;
            }

            foreach (var (recordId, messageIds) in page.Records)
            {
                foreach (var id in messageIds.Where(seen.Add))
                {
                    yield return new MailChange(new MailMessageRef(id, id), null);
                }

                /* "After startHistoryId": once a record's messages are stored, the next sync starts after it. */
                yield return MailChange.Checkpoint(recordId);
            }

            pageToken = page.NextPageToken;
            if (pageToken == null)
            {
                yield return MailChange.Checkpoint(page.HistoryId ?? cursor);
            }
        }
        while (pageToken != null);
    }

    public override async Task<MailRawMessage?> FetchRawAsync(string accessToken, MailMessageRef message, CancellationToken cancellationToken = default)
    {
        using var document = await GetJsonAsync(
            ApiBase + "messages/" + Uri.EscapeDataString(message.ProviderId) + "?format=raw", accessToken, cancellationToken, allowNotFound: true);
        if (document == null)
        {
            return null;
        }

        var raw = GetString(document.RootElement, "raw");
        if (string.IsNullOrEmpty(raw))
        {
            throw new MailProviderException("gmail_no_raw", "Gmail returned a message without its raw content.");
        }

        byte[] content;
        try
        {
            content = Base64Url.DecodeFromChars(raw.TrimEnd('='));
        }
        catch (FormatException e)
        {
            throw new MailProviderException("gmail_invalid_raw", "Gmail returned raw content that is not base64url.", e);
        }

        DateTime? receivedAt = long.TryParse(GetString(document.RootElement, "internalDate"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms)
            ? DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime
            : null;
        return new MailRawMessage(content, receivedAt);
    }

    private async Task<string> GetHistoryIdAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var profile = (await GetJsonAsync(ApiBase + "profile", accessToken, cancellationToken))!;
        return ReadId(profile.RootElement, "historyId")
               ?? throw new MailProviderException("gmail_no_history_id", "Gmail's profile has no historyId.");
    }

    private async Task<HistoryPage?> ReadHistoryPageAsync(string accessToken, string folderId, string cursor, string? pageToken, CancellationToken cancellationToken)
    {
        var parameters = new List<KeyValuePair<string, string>>
        {
            new("startHistoryId", cursor),
            new("labelId", folderId),
            new("historyTypes", "messageAdded"),
            new("historyTypes", "labelAdded"),
            new("maxResults", PageSize.ToString(CultureInfo.InvariantCulture))
        };
        if (pageToken != null)
        {
            parameters.Add(new("pageToken", pageToken));
        }

        using var document = await GetJsonAsync(Query(ApiBase + "history", parameters), accessToken, cancellationToken, allowNotFound: true);
        if (document == null)
        {
            return null;
        }

        var root = document.RootElement;
        var records = new List<(string, List<string>)>();
        foreach (var record in GetArray(root, "history"))
        {
            var recordId = ReadId(record, "id");
            if (recordId == null)
            {
                continue;
            }

            var ids = new List<string>();
            foreach (var added in GetArray(record, "messagesAdded"))
            {
                var message = added.TryGetProperty("message", out var m) ? m : default;
                if (HasLabel(message, folderId) && GetString(message, "id") is { } id)
                {
                    ids.Add(id);
                }
            }

            foreach (var labelled in GetArray(record, "labelsAdded"))
            {
                var message = labelled.TryGetProperty("message", out var m) ? m : default;
                if (GetArray(labelled, "labelIds").Any(l => l.GetString() == folderId) && GetString(message, "id") is { } id)
                {
                    ids.Add(id);
                }
            }

            records.Add((recordId, ids));
        }

        return new HistoryPage(records, GetString(root, "nextPageToken"), ReadId(root, "historyId"));
    }

    private async IAsyncEnumerable<MailChange> FallbackAsync(
        string accessToken, string folderId, DateTime? lastSyncAt, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        /* The new cursor is taken first, so nothing arriving while the label is listed is missed. */
        var historyId = await GetHistoryIdAsync(accessToken, cancellationToken);
        var since = (lastSyncAt ?? Clock.Now).ToUniversalTime().AddDays(-1);
        var after = new DateTimeOffset(DateTime.SpecifyKind(since, DateTimeKind.Utc)).ToUnixTimeSeconds();

        var ids = new List<string>();
        string? pageToken = null;
        do
        {
            var parameters = new List<KeyValuePair<string, string>>
            {
                new("labelIds", folderId),
                new("q", "after:" + after.ToString(CultureInfo.InvariantCulture)),
                new("maxResults", PageSize.ToString(CultureInfo.InvariantCulture))
            };
            if (pageToken != null)
            {
                parameters.Add(new("pageToken", pageToken));
            }

            using var document = (await GetJsonAsync(Query(ApiBase + "messages", parameters), accessToken, cancellationToken))!;
            ids.AddRange(GetArray(document.RootElement, "messages").Select(m => GetString(m, "id")).OfType<string>());
            pageToken = GetString(document.RootElement, "nextPageToken");
        }
        while (pageToken != null);

        /* Gmail lists newest first; record oldest first. */
        foreach (var id in ids.Distinct(StringComparer.Ordinal).Reverse())
        {
            yield return new MailChange(new MailMessageRef(id, id), null);
        }

        yield return MailChange.Checkpoint(historyId);
    }

    private static bool HasLabel(System.Text.Json.JsonElement message, string labelId)
        => GetArray(message, "labelIds").Any(l => l.GetString() == labelId);

    /// <summary>Gmail sends its ids (historyId) as strings; accept a number too.</summary>
    private static string? ReadId(System.Text.Json.JsonElement element, string property)
    {
        if (element.ValueKind != System.Text.Json.JsonValueKind.Object || !element.TryGetProperty(property, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            System.Text.Json.JsonValueKind.String => value.GetString(),
            System.Text.Json.JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

    private record HistoryPage(List<(string RecordId, List<string> MessageIds)> Records, string? NextPageToken, string? HistoryId);
}
