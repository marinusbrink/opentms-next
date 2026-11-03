using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using OpenTms.Integrations.OrderIntake.Channels;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Timing;
using Xunit;

namespace OpenTms.Integrations.OrderIntake;

public sealed class MutableClock(DateTime now) : IClock
{
    public DateTime Now { get; set; } = now;

    public DateTimeKind Kind => DateTimeKind.Utc;

    public bool SupportsMultipleTimezone => true;

    public DateTime Normalize(DateTime dateTime) => dateTime;

    public DateTime ConvertToUserTime(DateTime dateTime) => dateTime;

    public DateTimeOffset ConvertToUserTime(DateTimeOffset dateTime) => dateTime;

    public DateTime ConvertToUtc(DateTime dateTime) => dateTime;
}

/// <summary>
/// The OAuth state of a mailbox connection (design 72 "Connect", "Tenant dimension"): data-protected with its own purpose,
/// 15 minutes, carrying tenant, connection and user. The anonymous callback takes the tenant only from here. Risk class:
/// Critical — a forged or replayed state would attach a mailbox (and its mail) to another tenant.
/// </summary>
public class MailboxOAuthStateProtectorTests
{
    private static readonly DateTime Now = new(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);
    private readonly MutableClock _clock = new(Now);
    private readonly EphemeralDataProtectionProvider _keys = new();

    private MailboxOAuthStateProtector Protector(IDataProtectionProvider? keys = null) => new(keys ?? _keys, _clock);

    [Fact]
    public void A_state_round_trips_tenant_connection_and_user_and_is_encrypted()
    {
        var tenant = Guid.NewGuid();
        var connection = Guid.NewGuid();
        var user = Guid.NewGuid();

        var state = Protector().Protect(tenant, connection, user);

        state.ShouldNotContain(connection.ToString(), Case.Insensitive);
        state.ShouldNotContain(tenant.ToString(), Case.Insensitive);
        var read = Protector().Unprotect(state).ShouldNotBeNull();
        (read.TenantId, read.ConnectionId, read.UserId).ShouldBe((tenant, connection, user));
        read.ExpiresAt.ShouldBe(Now.AddMinutes(15));
        read.Nonce.Length.ShouldBe(32);

        Protector().Unprotect(Protector().Protect(null, connection, null))!.TenantId.ShouldBeNull("a host-side state keeps the host");
        Protector().Protect(tenant, connection, user).ShouldNotBe(state, "every state carries its own nonce");
    }

    [Fact]
    public void A_state_expires_after_fifteen_minutes()
    {
        var state = Protector().Protect(Guid.NewGuid(), Guid.NewGuid(), null);

        _clock.Now = Now.AddMinutes(14).AddSeconds(59);
        Protector().Unprotect(state).ShouldNotBeNull();
        _clock.Now = Now.AddMinutes(15);
        Protector().Unprotect(state).ShouldBeNull();
        _clock.Now = Now.AddDays(1);
        Protector().Unprotect(state).ShouldBeNull();
    }

    [Fact]
    public void A_tampered_or_truncated_state_is_null()
    {
        var state = Protector().Protect(Guid.NewGuid(), Guid.NewGuid(), null);
        var chars = state.ToCharArray();
        chars[chars.Length / 2] = chars[chars.Length / 2] == 'A' ? 'B' : 'A';

        Protector().Unprotect(new string(chars)).ShouldBeNull();
        Protector().Unprotect(state[..^3]).ShouldBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("garbage")]
    [InlineData("%%%")]
    public void Garbage_is_null(string? state)
    {
        Protector().Unprotect(state).ShouldBeNull();
    }

    [Fact]
    public void An_oversized_state_is_null_without_unprotecting()
    {
        Protector().Unprotect(new string('A', 5000)).ShouldBeNull();
    }

    [Fact]
    public void A_state_of_another_key_ring_or_another_purpose_is_null()
    {
        Protector().Unprotect(Protector(new EphemeralDataProtectionProvider()).Protect(Guid.NewGuid(), Guid.NewGuid(), null)).ShouldBeNull();

        var payload = JsonSerializer.Serialize(new MailboxOAuthState(Guid.NewGuid(), Guid.NewGuid(), null, "abc", Now.AddMinutes(10)));
        var otherPurpose = _keys.CreateProtector("OpenTms.Impersonation.Ticket.v1").Protect(payload);
        Protector().Unprotect(otherPurpose).ShouldBeNull();

        /* The right purpose with a payload that is no state, or one without connection or nonce. */
        var purpose = _keys.CreateProtector(OrderIntakeConsts.OAuthStatePurpose);
        Protector().Unprotect(purpose.Protect("not json")).ShouldBeNull();
        Protector().Unprotect(purpose.Protect(JsonSerializer.Serialize(new MailboxOAuthState(Guid.NewGuid(), Guid.Empty, null, "abc", Now.AddMinutes(10))))).ShouldBeNull();
        Protector().Unprotect(purpose.Protect(JsonSerializer.Serialize(new MailboxOAuthState(Guid.NewGuid(), Guid.NewGuid(), null, "", Now.AddMinutes(10))))).ShouldBeNull();
    }
}

/// <summary>A scripted provider API: answers by method and path, records every request with its form body.</summary>
public sealed class ScriptedProviderHandler : HttpMessageHandler
{
    public sealed record Seen(HttpMethod Method, Uri Uri, string? Authorization, Dictionary<string, string> Form);

    private readonly List<(Func<HttpRequestMessage, bool> When, Func<HttpRequestMessage, HttpResponseMessage> Answer)> _routes = new();

    public List<Seen> Requests { get; } = new();

    public ScriptedProviderHandler On(Func<HttpRequestMessage, bool> when, Func<HttpRequestMessage, HttpResponseMessage> answer)
    {
        _routes.Add((when, answer));
        return this;
    }

    public ScriptedProviderHandler OnGet(string pathAndQueryStart, string json, HttpStatusCode status = HttpStatusCode.OK)
        => On(r => r.Method == HttpMethod.Get && r.RequestUri!.AbsoluteUri.StartsWith(pathAndQueryStart, StringComparison.Ordinal), _ => Json(json, status));

    public ScriptedProviderHandler OnToken(string tokenUrl, string json, HttpStatusCode status = HttpStatusCode.OK)
        => On(r => r.Method == HttpMethod.Post && r.RequestUri!.AbsoluteUri == tokenUrl, _ => Json(json, status));

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string>();
        if (request.Content is FormUrlEncodedContent)
        {
            var parsed = HttpUtility.ParseQueryString(await request.Content.ReadAsStringAsync(cancellationToken));
            foreach (var key in parsed.AllKeys.OfType<string>())
            {
                form[key] = parsed[key]!;
            }
        }

        Requests.Add(new Seen(request.Method, request.RequestUri!, request.Headers.Authorization?.ToString(), form));
        /* The last matching route wins, so a test can override a default. */
        for (var i = _routes.Count - 1; i >= 0; i--)
        {
            if (_routes[i].When(request))
            {
                return _routes[i].Answer(request);
            }
        }

        throw new InvalidOperationException("Unscripted provider call: " + request.Method + " " + request.RequestUri);
    }
}

public sealed class ScriptedHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public List<string> Names { get; } = new();

    public HttpClient CreateClient(string name)
    {
        Names.Add(name);
        return new HttpClient(handler, disposeHandler: false);
    }
}

/// <summary>
/// The Gmail and Microsoft Graph adapters (design 72 "Channels", "Sync") against a scripted provider API: the authorize URL
/// (scopes, offline access, state), the code exchange, refresh with Microsoft's rotation, <c>invalid_grant</c> →
/// reconnect, folders (Gmail user labels and INBOX; Graph paths to depth 3), the cursor without importing, paging of
/// changes with a checkpoint per record/page, Gmail's 404 fallback, Graph's 410 restart and the Message-ID as external id,
/// raw message fetch, and that a link outside Graph never receives the token. Risk class: High — external integrations.
/// </summary>
public class MailProviderClientTests
{
    private static readonly DateTime Now = new(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);
    private readonly ScriptedProviderHandler _http = new();
    private readonly MutableClock _clock = new(Now);

    private static IOptions<MailOAuthOptions> Configured(bool google = true, bool microsoft = true) => Options.Create(new MailOAuthOptions
    {
        Google = google ? new MailOAuthClientOptions { ClientId = "google-id", ClientSecret = "google-secret" } : new MailOAuthClientOptions(),
        Microsoft = microsoft ? new MailOAuthClientOptions { ClientId = "ms-id", ClientSecret = "ms-secret" } : new MailOAuthClientOptions()
    });

    private GmailClient Gmail(IOptions<MailOAuthOptions>? options = null) => new(new ScriptedHttpClientFactory(_http), options ?? Configured(), _clock);

    private MicrosoftGraphMailClient Graph(IOptions<MailOAuthOptions>? options = null) => new(new ScriptedHttpClientFactory(_http), options ?? Configured(), _clock);

    private static async Task<List<MailChange>> AllAsync(IAsyncEnumerable<MailChange> changes)
    {
        var list = new List<MailChange>();
        await foreach (var change in changes)
        {
            list.Add(change);
        }

        return list;
    }

    /* ---------- OAuth ---------- */

    [Fact]
    public void The_gmail_authorize_url_asks_read_only_offline_consent_and_carries_the_state()
    {
        var url = new Uri(Gmail().BuildAuthorizeUrl("STATE+/=", "https://api.example.test/api/integrations/mailbox-oauth/callback"));
        var query = HttpUtility.ParseQueryString(url.Query);

        url.GetLeftPart(UriPartial.Path).ShouldBe(GmailClient.AuthorizeEndpoint);
        query["scope"].ShouldBe("https://www.googleapis.com/auth/gmail.readonly");
        query["access_type"].ShouldBe("offline");
        query["prompt"].ShouldBe("consent");
        query["state"].ShouldBe("STATE+/=");
        query["client_id"].ShouldBe("google-id");
        query["redirect_uri"].ShouldBe("https://api.example.test/api/integrations/mailbox-oauth/callback");
        url.Query.ShouldNotContain("google-secret");
    }

    [Fact]
    public void The_microsoft_authorize_url_uses_the_common_authority_and_read_only_scopes()
    {
        var url = new Uri(Graph().BuildAuthorizeUrl("S", "https://api.example.test/cb"));
        var query = HttpUtility.ParseQueryString(url.Query);

        url.GetLeftPart(UriPartial.Path).ShouldBe("https://login.microsoftonline.com/common/oauth2/v2.0/authorize");
        query["scope"].ShouldBe("offline_access https://graph.microsoft.com/Mail.Read https://graph.microsoft.com/User.Read");
        query["state"].ShouldBe("S");
    }

    [Fact]
    public void An_unconfigured_provider_refuses_with_ProviderNotConfigured()
    {
        Should.Throw<BusinessException>(() => Gmail(Configured(google: false)).BuildAuthorizeUrl("s", "r")).Code.ShouldBe(IntegrationsErrorCodes.ProviderNotConfigured);
        Should.Throw<BusinessException>(() => Graph(Configured(microsoft: false)).BuildAuthorizeUrl("s", "r")).Code.ShouldBe(IntegrationsErrorCodes.ProviderNotConfigured);
    }

    [Fact]
    public async Task The_code_is_exchanged_for_tokens()
    {
        _http.OnToken(GmailClient.GoogleTokenEndpoint, """{"access_token":"at-1","refresh_token":"rt-1","expires_in":3599}""");

        var tokens = await Gmail().ExchangeCodeAsync("the-code", "https://api.example.test/cb");

        tokens.ShouldBe(new MailTokenResult("at-1", "rt-1"));
        var form = _http.Requests.Single().Form;
        form["grant_type"].ShouldBe("authorization_code");
        form["code"].ShouldBe("the-code");
        form["client_secret"].ShouldBe("google-secret");
        form["redirect_uri"].ShouldBe("https://api.example.test/cb");
    }

    [Fact]
    public async Task A_refresh_without_a_new_refresh_token_keeps_the_old_one()
    {
        _http.OnToken(GmailClient.GoogleTokenEndpoint, """{"access_token":"at-2","expires_in":3599}""");

        (await Gmail().RefreshAsync("rt-1")).ShouldBe(new MailTokenResult("at-2", null));
        _http.Requests.Single().Form["refresh_token"].ShouldBe("rt-1");
    }

    [Fact]
    public async Task Microsoft_rotates_the_refresh_token_on_every_refresh()
    {
        _http.OnToken(MicrosoftGraphMailClient.MicrosoftTokenEndpoint, """{"access_token":"at-2","refresh_token":"rt-2"}""");

        (await Graph().RefreshAsync("rt-1")).ShouldBe(new MailTokenResult("at-2", "rt-2"));
        _http.Requests.Single().Form["scope"].ShouldContain("Mail.Read");
    }

    [Fact]
    public async Task Invalid_grant_on_a_refresh_means_reconnect_for_both_providers()
    {
        _http.OnToken(GmailClient.GoogleTokenEndpoint, """{"error":"invalid_grant","error_description":"Token has been expired or revoked."}""", HttpStatusCode.BadRequest);
        _http.OnToken(MicrosoftGraphMailClient.MicrosoftTokenEndpoint, """{"error":"invalid_grant"}""", HttpStatusCode.BadRequest);

        await Should.ThrowAsync<MailboxReconnectRequiredException>(() => Gmail().RefreshAsync("revoked"));
        await Should.ThrowAsync<MailboxReconnectRequiredException>(() => Graph().RefreshAsync("revoked"));
    }

    [Fact]
    public async Task Other_token_errors_carry_a_short_code_and_no_provider_text()
    {
        _http.OnToken(GmailClient.GoogleTokenEndpoint, """{"error":"invalid_client","error_description":"The OAuth client was not found for orders@acme.nl"}""", HttpStatusCode.Unauthorized);

        var ex = await Should.ThrowAsync<MailProviderException>(() => Gmail().RefreshAsync("rt"));
        ex.ErrorCode.ShouldBe("gmail_token_invalid_client");
        ex.OAuthError.ShouldBe("invalid_client");
        ex.Message.ShouldNotContain("acme");

        /* invalid_grant on the code exchange is not a reconnect: it is a failed consent. */
        _http.OnToken(GmailClient.GoogleTokenEndpoint, """{"error":"invalid_grant"}""", HttpStatusCode.BadRequest);
        (await Should.ThrowAsync<MailProviderException>(() => Gmail().ExchangeCodeAsync("old", "r"))).ErrorCode.ShouldBe("gmail_token_invalid_grant");
    }

    [Fact]
    public async Task An_unreachable_provider_is_a_provider_error()
    {
        _http.On(_ => true, _ => throw new HttpRequestException("connection refused"));

        (await Should.ThrowAsync<MailProviderException>(() => Gmail().GetAccountAddressAsync("at"))).ErrorCode.ShouldBe("gmail_unreachable");
    }

    /* ---------- Gmail ---------- */

    [Fact]
    public async Task Gmail_lists_inbox_first_then_user_labels_and_sends_the_bearer_token()
    {
        _http.OnGet(GmailClient.ApiBase + "labels", """
            {"labels":[{"id":"Label_2","name":"Zending","type":"user"},{"id":"SENT","name":"SENT","type":"system"},
                       {"id":"INBOX","name":"INBOX","type":"system"},{"id":"Label_1","name":"Orders","type":"user"}]}
            """);

        var folders = await Gmail().ListFoldersAsync("at-1");

        folders.Select(f => f.Id).ShouldBe(["INBOX", "Label_1", "Label_2"]);
        _http.Requests.Single().Authorization.ShouldBe("Bearer at-1");
    }

    [Fact]
    public async Task Gmail_takes_the_profile_history_id_as_the_cursor_without_listing_messages()
    {
        _http.OnGet(GmailClient.ApiBase + "profile", """{"emailAddress":"orders@acme.nl","historyId":"9001"}""");

        (await Gmail().GetCurrentCursorAsync("at", "Label_1")).ShouldBe("9001");
        (await Gmail().GetAccountAddressAsync("at")).ShouldBe("orders@acme.nl");
        _http.Requests.ShouldAllBe(r => r.Uri.AbsolutePath.EndsWith("/profile"));
    }

    [Fact]
    public async Task Gmail_pages_the_history_of_the_label_with_a_checkpoint_after_every_record()
    {
        _http.On(r => r.RequestUri!.AbsoluteUri.StartsWith(GmailClient.ApiBase + "history") && !r.RequestUri.Query.Contains("pageToken"), _ => ScriptedProviderHandler.Json("""
            {"history":[
               {"id":"101","messagesAdded":[{"message":{"id":"m1","labelIds":["Label_1","INBOX"]}},{"message":{"id":"x9","labelIds":["INBOX"]}}]},
               {"id":"102","labelsAdded":[{"message":{"id":"m2"},"labelIds":["Label_1"]}]}
             ],"nextPageToken":"p2","historyId":"110"}
            """));
        _http.On(r => r.RequestUri!.AbsoluteUri.StartsWith(GmailClient.ApiBase + "history") && r.RequestUri.Query.Contains("pageToken=p2"), _ => ScriptedProviderHandler.Json("""
            {"history":[{"id":"103","messagesAdded":[{"message":{"id":"m1","labelIds":["Label_1"]}},{"message":{"id":"m3","labelIds":["Label_1"]}}]}],"historyId":"120"}
            """));

        var changes = await AllAsync(Gmail().GetChangesAsync("at", "Label_1", "100", Now.AddHours(-1)));

        changes.Select(c => c.Message?.ProviderId ?? "@" + c.Cursor).ShouldBe(["m1", "@101", "m2", "@102", "m3", "@103", "@120"],
            "a message of another label is skipped, a message seen twice is listed once, the page's historyId closes");
        changes.Where(c => c.Message != null).ShouldAllBe(c => c.Message!.ExternalId == c.Message.ProviderId, "Gmail's id is the external id");
        changes.Last().Message.ShouldBeNull("the last item is always a checkpoint");

        var query = HttpUtility.ParseQueryString(_http.Requests[0].Uri.Query);
        query["startHistoryId"].ShouldBe("100");
        query["labelId"].ShouldBe("Label_1");
        query.GetValues("historyTypes").ShouldBe(["messageAdded", "labelAdded"]);
    }

    [Fact]
    public async Task A_quiet_gmail_mailbox_costs_one_call_and_keeps_moving_the_cursor()
    {
        _http.OnGet(GmailClient.ApiBase + "history", """{"historyId":"150"}""");

        var changes = await AllAsync(Gmail().GetChangesAsync("at", "Label_1", "140", null));

        changes.ShouldHaveSingleItem().Cursor.ShouldBe("150");
        _http.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_cursor_gmail_no_longer_knows_falls_back_to_listing_the_label_since_the_last_sync_minus_a_day()
    {
        var lastSync = new DateTime(2026, 11, 2, 8, 0, 0, DateTimeKind.Utc);
        _http.OnGet(GmailClient.ApiBase + "history", """{"error":{"code":404}}""", HttpStatusCode.NotFound);
        _http.OnGet(GmailClient.ApiBase + "profile", """{"historyId":"500"}""");
        _http.On(r => r.RequestUri!.AbsoluteUri.StartsWith(GmailClient.ApiBase + "messages?") && !r.RequestUri.Query.Contains("pageToken"),
            _ => ScriptedProviderHandler.Json("""{"messages":[{"id":"n3"},{"id":"n2"}],"nextPageToken":"q2"}"""));
        _http.On(r => r.RequestUri!.AbsoluteUri.StartsWith(GmailClient.ApiBase + "messages?") && r.RequestUri.Query.Contains("pageToken=q2"),
            _ => ScriptedProviderHandler.Json("""{"messages":[{"id":"n1"}]}"""));

        var changes = await AllAsync(Gmail().GetChangesAsync("at", "Label_1", "1", lastSync));

        changes.Select(c => c.Message?.ProviderId ?? "@" + c.Cursor).ShouldBe(["n1", "n2", "n3", "@500"], "oldest first, then the cursor taken before listing");
        var list = _http.Requests.First(r => r.Uri.AbsolutePath.EndsWith("/messages"));
        var query = HttpUtility.ParseQueryString(list.Uri.Query);
        query["labelIds"].ShouldBe("Label_1");
        query["q"].ShouldBe("after:" + new DateTimeOffset(lastSync.AddDays(-1)).ToUnixTimeSeconds());
        _http.Requests.FindIndex(r => r.Uri.AbsolutePath.EndsWith("/profile")).ShouldBeLessThan(_http.Requests.IndexOf(list), "the new cursor is taken first");
    }

    [Fact]
    public async Task Gmail_fetches_the_raw_message_base64url_decoded_or_null_when_gone()
    {
        var raw = "From: a@b.nl\r\nSubject: Order\r\n\r\nHallo ÿ";
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        _http.OnGet(GmailClient.ApiBase + "messages/m1?format=raw", $$"""{"id":"m1","raw":"{{encoded}}","internalDate":"1762171200000"}""");
        _http.OnGet(GmailClient.ApiBase + "messages/gone?format=raw", "{}", HttpStatusCode.NotFound);

        var message = await Gmail().FetchRawAsync("at", new MailMessageRef("m1", "m1"));
        Encoding.UTF8.GetString(message!.Content).ShouldBe(raw);
        message.ReceivedAt.ShouldBe(DateTimeOffset.FromUnixTimeMilliseconds(1762171200000).UtcDateTime);

        (await Gmail().FetchRawAsync("at", new MailMessageRef("gone", "gone"))).ShouldBeNull();
    }

    /* ---------- Microsoft Graph ---------- */

    [Fact]
    public async Task Graph_lists_folders_as_paths_to_depth_three_following_next_links()
    {
        var b = MicrosoftGraphMailClient.GraphBase;
        _http.OnGet(b + "me/mailFolders?", $$"""{"value":[{"id":"inbox","displayName":"Inbox","childFolderCount":1}],"@odata.nextLink":"{{b}}me/mailFolders?page=2"}""");
        _http.OnGet(b + "me/mailFolders?page=2", """{"value":[{"id":"archive","displayName":"Archive","childFolderCount":0}]}""");
        _http.OnGet(b + "me/mailFolders/inbox/childFolders", """{"value":[{"id":"orders","displayName":"Orders","childFolderCount":1}]}""");
        _http.OnGet(b + "me/mailFolders/orders/childFolders", """{"value":[{"id":"nl","displayName":"NL","childFolderCount":1}]}""");
        _http.OnGet(b + "me/mailFolders/nl/childFolders", """{"value":[{"id":"deep","displayName":"Too deep","childFolderCount":0}]}""");

        var folders = await Graph().ListFoldersAsync("at");

        folders.Select(f => f.Name).ShouldBe(["Inbox", "Archive", "Inbox/Orders", "Inbox/Orders/NL"], ignoreOrder: true);
        _http.Requests.ShouldNotContain(r => r.Uri.AbsolutePath.Contains("/nl/childFolders"), "depth 3 is the limit");
    }

    [Fact]
    public async Task Graph_takes_the_delta_link_after_paging_the_folder_without_returning_messages()
    {
        var b = MicrosoftGraphMailClient.GraphBase;
        _http.OnGet(b + "me/mailFolders/orders/messages/delta?", $$"""{"value":[{"id":"old1"}],"@odata.nextLink":"{{b}}delta-page-2"}""");
        _http.OnGet(b + "delta-page-2", $$"""{"value":[{"id":"old2"}],"@odata.deltaLink":"{{b}}delta-token-now"}""");

        (await Graph().GetCurrentCursorAsync("at", "orders")).ShouldBe(b + "delta-token-now");
    }

    [Fact]
    public async Task Graph_pages_the_delta_with_message_id_as_external_id_and_skips_removals()
    {
        var b = MicrosoftGraphMailClient.GraphBase;
        _http.OnGet(b + "cursor-1", $$$"""
            {"value":[
               {"id":"g1","internetMessageId":"<abc@acme.nl>","receivedDateTime":"2026-11-03T10:00:00Z"},
               {"id":"g2","@removed":{"reason":"deleted"}},
               {"id":"g3"}
             ],"@odata.nextLink":"{{{b}}}cursor-2"}
            """);
        _http.OnGet(b + "cursor-2", $$"""{"value":[{"id":"g4","internetMessageId":"<def@acme.nl>"}],"@odata.deltaLink":"{{b}}cursor-3"}""");

        var changes = await AllAsync(Graph().GetChangesAsync("at", "orders", b + "cursor-1", null));

        changes.Select(c => c.Message?.ExternalId ?? "@" + c.Cursor).ShouldBe(["<abc@acme.nl>", "g3", "@" + b + "cursor-2", "<def@acme.nl>", "@" + b + "cursor-3"]);
        changes[0].Message!.ProviderId.ShouldBe("g1");
        changes[0].Message!.ReceivedAt.ShouldBe(new DateTime(2026, 11, 3, 10, 0, 0, DateTimeKind.Utc));
        _http.Requests.ShouldAllBe(r => r.Uri.AbsoluteUri.StartsWith(b));
    }

    [Fact]
    public async Task An_expired_graph_delta_restarts_from_the_last_sync_minus_a_day()
    {
        var b = MicrosoftGraphMailClient.GraphBase;
        var lastSync = new DateTime(2026, 11, 2, 8, 0, 0, DateTimeKind.Utc);
        _http.OnGet(b + "expired", """{"error":{"code":"SyncStateNotFound"}}""", HttpStatusCode.Gone);
        _http.OnGet(b + "me/mailFolders/orders/messages/delta?", $$"""{"value":[{"id":"g9"}],"@odata.deltaLink":"{{b}}fresh"}""");

        var changes = await AllAsync(Graph().GetChangesAsync("at", "orders", b + "expired", lastSync));

        changes.Select(c => c.Message?.ProviderId ?? "@" + c.Cursor).ShouldBe(["g9", "@" + b + "fresh"]);
        Uri.UnescapeDataString(_http.Requests.Last().Uri.Query).ShouldContain("receivedDateTime ge 2026-11-01T08:00:00Z");
    }

    [Fact]
    public async Task A_cursor_that_does_not_point_at_graph_never_receives_the_token()
    {
        await Should.ThrowAsync<MailProviderException>(() => AllAsync(Graph().GetChangesAsync("at", "orders", "https://evil.example.test/steal", null)));
        _http.Requests.ShouldBeEmpty();

        var b = MicrosoftGraphMailClient.GraphBase;
        _http.OnGet(b + "cursor-1", """{"value":[],"@odata.nextLink":"https://evil.example.test/next"}""");
        await Should.ThrowAsync<MailProviderException>(() => AllAsync(Graph().GetChangesAsync("at", "orders", b + "cursor-1", null)));
        _http.Requests.ShouldAllBe(r => r.Uri.AbsoluteUri.StartsWith(b));
    }

    [Fact]
    public async Task Graph_reads_the_address_from_mail_else_the_user_principal_name()
    {
        var b = MicrosoftGraphMailClient.GraphBase;
        _http.OnGet(b + "me?", """{"mail":null,"userPrincipalName":"planning@acme.onmicrosoft.com"}""");

        (await Graph().GetAccountAddressAsync("at")).ShouldBe("planning@acme.onmicrosoft.com");
    }

    [Fact]
    public void The_external_id_is_the_message_id_header_else_the_graph_id()
    {
        MicrosoftGraphMailClient.ExternalIdOf("g1", " <a@b> ").ShouldBe("<a@b>");
        MicrosoftGraphMailClient.ExternalIdOf("g1", null).ShouldBe("g1");
        MicrosoftGraphMailClient.ExternalIdOf("g1", new string('x', OrderIntakeConsts.MaxExternalIdLength + 1)).ShouldBe("g1");
    }
}
