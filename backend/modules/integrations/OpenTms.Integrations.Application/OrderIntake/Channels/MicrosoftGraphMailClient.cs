using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Volo.Abp.Timing;

namespace OpenTms.Integrations.OrderIntake.Channels;

/// <summary>
/// Microsoft 365 through Microsoft Graph (design 72 "Channels"): authority <c>common</c>, scopes <c>offline_access Mail.Read
/// User.Read</c>; every refresh rotates the refresh token. The folder's cursor is a delta link (or, mid-way, a next
/// link, which Graph also resumes from); an expired delta state (410) restarts the delta from the last sync minus a day.
/// The external id is the Message-ID header (Graph ids change on a move), else the Graph id.
/// </summary>
public class MicrosoftGraphMailClient : MailProviderClientBase
{
    public const string Name = "OrderIntake.MicrosoftGraph";

    public const string AuthorizeEndpoint = "https://login.microsoftonline.com/common/oauth2/v2.0/authorize";

    public const string MicrosoftTokenEndpoint = "https://login.microsoftonline.com/common/oauth2/v2.0/token";

    public const string GraphBase = "https://graph.microsoft.com/v1.0/";

    public const string Scope = "offline_access https://graph.microsoft.com/Mail.Read https://graph.microsoft.com/User.Read";

    private const string MessageSelect = "id,internetMessageId,receivedDateTime";

    private static readonly IReadOnlyDictionary<string, string> PageSizeHeader = new Dictionary<string, string> { ["Prefer"] = "odata.maxpagesize=100" };

    public MicrosoftGraphMailClient(IHttpClientFactory httpClientFactory, IOptions<MailOAuthOptions> options, IClock clock)
        : base(httpClientFactory, options, clock)
    {
    }

    public override MailProvider Provider => MailProvider.Microsoft365;

    protected override string HttpClientName => Name;

    protected override string ErrorPrefix => "graph";

    protected override string TokenEndpoint => MicrosoftTokenEndpoint;

    public override string BuildAuthorizeUrl(string state, string redirectUri)
    {
        var client = RequireClient();
        return Query(AuthorizeEndpoint,
        [
            new("client_id", client.ClientId!),
            new("response_type", "code"),
            new("redirect_uri", redirectUri),
            new("response_mode", "query"),
            new("scope", Scope),
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
            ["redirect_uri"] = redirectUri,
            ["scope"] = Scope
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
            ["client_secret"] = client.ClientSecret!,
            ["scope"] = Scope
        }, isRefresh: true, cancellationToken);
    }

    public override async Task<string> GetAccountAddressAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        using var me = (await GetJsonAsync(GraphBase + "me?$select=mail,userPrincipalName", accessToken, cancellationToken))!;
        var address = GetString(me.RootElement, "mail");
        if (string.IsNullOrWhiteSpace(address))
        {
            address = GetString(me.RootElement, "userPrincipalName");
        }

        return string.IsNullOrWhiteSpace(address)
            ? throw new MailProviderException("graph_no_address", "Graph's /me has no address.")
            : address;
    }

    public override async Task<IReadOnlyList<MailFolder>> ListFoldersAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        var folders = new List<MailFolder>();
        await AddFoldersAsync(accessToken, GraphBase + "me/mailFolders", null, 1, folders, cancellationToken);
        return folders;
    }

    public override async Task<string> GetCurrentCursorAsync(string accessToken, string folderId, CancellationToken cancellationToken = default)
    {
        /* Page through the folder's current contents without returning them: the delta link at the end is "now". */
        var url = InitialDeltaUrl(folderId, null);
        while (true)
        {
            using var page = (await GetJsonAsync(url, accessToken, cancellationToken, headers: PageSizeHeader))!;
            var next = GetString(page.RootElement, "@odata.nextLink");
            if (next != null)
            {
                url = EnsureGraphUrl(next);
                continue;
            }

            return GetString(page.RootElement, "@odata.deltaLink")
                   ?? throw new MailProviderException("graph_no_delta_link", "Graph's delta ended without a delta link.");
        }
    }

    public override async IAsyncEnumerable<MailChange> GetChangesAsync(
        string accessToken, string folderId, string cursor, DateTime? lastSyncAt, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var url = EnsureGraphUrl(cursor);
        var first = true;
        while (true)
        {
            var page = await ReadPageAsync(url, accessToken, cancellationToken);
            if (page == null)
            {
                if (!first)
                {
                    throw new MailProviderException("graph_delta_expired", "Graph's delta state expired while paging.");
                }

                /* The delta state expired (410): start a new delta over what arrived since the last sync minus a day. */
                var since = (lastSyncAt ?? Clock.Now).ToUniversalTime().AddDays(-1);
                url = InitialDeltaUrl(folderId, since);
                first = false;
                continue;
            }

            first = false;
            foreach (var message in page.Messages)
            {
                yield return new MailChange(message, null);
            }

            if (page.NextLink != null)
            {
                yield return MailChange.Checkpoint(page.NextLink);
                url = page.NextLink;
                continue;
            }

            yield return MailChange.Checkpoint(page.DeltaLink
                ?? throw new MailProviderException("graph_no_delta_link", "Graph's delta ended without a delta link."));
            yield break;
        }
    }

    public override async Task<MailRawMessage?> FetchRawAsync(string accessToken, MailMessageRef message, CancellationToken cancellationToken = default)
    {
        var content = await GetBytesAsync(GraphBase + "me/messages/" + Uri.EscapeDataString(message.ProviderId) + "/$value", accessToken, cancellationToken);
        return content == null ? null : new MailRawMessage(content, message.ReceivedAt);
    }

    /// <summary>The external id rule: the Message-ID header when it fits, else the Graph id.</summary>
    public static string ExternalIdOf(string graphId, string? internetMessageId)
    {
        var header = internetMessageId?.Trim();
        return !string.IsNullOrEmpty(header) && header.Length <= OrderIntakeConsts.MaxExternalIdLength ? header : graphId;
    }

    private async Task AddFoldersAsync(string accessToken, string collectionUrl, string? parentPath, int depth, List<MailFolder> folders, CancellationToken cancellationToken)
    {
        var url = collectionUrl + "?$top=100&$select=id,displayName,childFolderCount";
        var children = new List<(string Id, string Path)>();
        while (url != null)
        {
            using var page = (await GetJsonAsync(url, accessToken, cancellationToken))!;
            foreach (var folder in GetArray(page.RootElement, "value"))
            {
                var id = GetString(folder, "id");
                var name = GetString(folder, "displayName");
                if (id == null || string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var path = parentPath == null ? name : parentPath + "/" + name;
                folders.Add(new MailFolder(id, path.Length <= OrderIntakeConsts.MaxFolderNameLength ? path : path[..OrderIntakeConsts.MaxFolderNameLength]));
                if (folder.TryGetProperty("childFolderCount", out var count) && count.ValueKind == JsonValueKind.Number && count.GetInt32() > 0)
                {
                    children.Add((id, path));
                }
            }

            var next = GetString(page.RootElement, "@odata.nextLink");
            url = next == null ? null : EnsureGraphUrl(next);
        }

        if (depth >= OrderIntakeConsts.MaxFolderDepth)
        {
            return;
        }

        foreach (var (id, path) in children)
        {
            await AddFoldersAsync(accessToken, GraphBase + "me/mailFolders/" + Uri.EscapeDataString(id) + "/childFolders", path, depth + 1, folders, cancellationToken);
        }
    }

    private async Task<DeltaPage?> ReadPageAsync(string url, string accessToken, CancellationToken cancellationToken)
    {
        using var page = await GetJsonAsync(url, accessToken, cancellationToken, nullOn: HttpStatusCode.Gone, headers: PageSizeHeader);
        if (page == null)
        {
            return null;
        }

        var messages = new List<MailMessageRef>();
        foreach (var item in GetArray(page.RootElement, "value"))
        {
            if (item.TryGetProperty("@removed", out _) || GetString(item, "id") is not { } id)
            {
                continue;
            }

            DateTime? receivedAt = DateTimeOffset.TryParse(GetString(item, "receivedDateTime"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at)
                ? at.UtcDateTime
                : null;
            messages.Add(new MailMessageRef(id, ExternalIdOf(id, GetString(item, "internetMessageId")), receivedAt));
        }

        var next = GetString(page.RootElement, "@odata.nextLink");
        var delta = GetString(page.RootElement, "@odata.deltaLink");
        return new DeltaPage(messages, next == null ? null : EnsureGraphUrl(next), delta == null ? null : EnsureGraphUrl(delta));
    }

    private static string InitialDeltaUrl(string folderId, DateTime? since)
    {
        var url = GraphBase + "me/mailFolders/" + Uri.EscapeDataString(folderId) + "/messages/delta?$select=" + MessageSelect;
        return since == null
            ? url
            : url + "&$filter=" + Uri.EscapeDataString("receivedDateTime ge " + since.Value.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
    }

    /// <summary>A stored or returned link must point at Graph: the access token is never sent anywhere else.</summary>
    private static string EnsureGraphUrl(string url)
        => url.StartsWith(GraphBase, StringComparison.OrdinalIgnoreCase)
            ? url
            : throw new MailProviderException("graph_cursor_invalid", "A Graph link does not point at Graph.");

    private record DeltaPage(List<MailMessageRef> Messages, string? NextLink, string? DeltaLink);
}
