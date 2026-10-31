using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OpenTms.Platform.Emailing;
using Shouldly;
using Xunit;

namespace OpenTms.Integrations.Emailing.Postmark;

/// <summary>
/// The Postmark adapter of docs/designs/64-email-delivery-postmark.md on a scripted transport: no live call (the token is
/// FiLogic's production server). Risk class High: the request contract (endpoint, token header, the configured From on the
/// verified domain, Reply-To, tracking, stream, metadata), the error mapping (422 permanent; 401, 429, 5xx and unreachable
/// transient; a timeout transient with an unknown outcome), the not-configured state, and the token, addresses, subject and
/// body never in a log line.
/// </summary>
public class PostmarkEmailDeliveryProviderTests
{
    private const string Token = "qq-postmark-token-7f3a91";
    private const string BodyMarker = "QQ-BODY-Bakker-Transport";
    private const string Recipient = "planner@klant.example";
    private static readonly Guid MessageId = Guid.Parse("6b0e7e4e-6a49-4f0a-9d3c-1f3b8c1a2d11");
    private static readonly Guid TenantId = Guid.Parse("0c8a5b1e-55d0-4a2c-8a7e-5a6e3c9b8f01");

    private readonly ScriptedPostmarkHandler _handler = new();
    private readonly CapturingLogger<PostmarkEmailDeliveryProvider> _log = new();

    private readonly DictionarySettingProvider _settings = new();

    private PostmarkEmailDeliveryProvider Provider(Action<PostmarkOptions>? configure = null)
    {
        var options = new PostmarkOptions
        {
            BaseUrl = "https://api.postmark.invalid/",
            ServerToken = Token,
            FromAddress = "noreply@ryd-software.com",
            FromName = "OpenTMS"
        };
        configure?.Invoke(options);
        var http = new HttpClient(_handler) { BaseAddress = new Uri(options.BaseUrl) };
        return new PostmarkEmailDeliveryProvider(http, PostmarkTestSettings.Resolver(options, _settings), _log);
    }

    private static OutgoingEmail Mail(string? replyTo = "planning@bakker.example") => new()
    {
        EmailMessageId = MessageId,
        TenantId = TenantId,
        To = Recipient,
        Cc = "cc@klant.example",
        ReplyTo = replyTo,
        Subject = "Orderbevestiging 2026-0001",
        HtmlBody = "<p>" + BodyMarker + "</p>",
        TextBody = BodyMarker,
        Tag = "TransportOrder"
    };

    [Fact]
    public async Task Sends_one_POST_email_with_the_token_the_configured_From_tracking_and_metadata()
    {
        var id = await Provider().SendAsync(Mail(), CancellationToken.None);

        id.ShouldBe("b7bc2f4a-e38e-4336-af7d-e6c392c2f817");
        var (request, body) = _handler.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Post);
        request.RequestUri!.ToString().ShouldBe("https://api.postmark.invalid/email");
        request.Headers.GetValues("X-Postmark-Server-Token").ShouldBe([Token]);
        var json = JsonDocument.Parse(body!).RootElement;
        json.GetProperty("From").GetString().ShouldBe("\"OpenTMS\" <noreply@ryd-software.com>");
        json.GetProperty("To").GetString().ShouldBe(Recipient);
        json.GetProperty("Cc").GetString().ShouldBe("cc@klant.example");
        json.GetProperty("ReplyTo").GetString().ShouldBe("planning@bakker.example", "a sender's own address goes in Reply-To, never in From");
        json.GetProperty("Subject").GetString().ShouldBe("Orderbevestiging 2026-0001");
        json.GetProperty("HtmlBody").GetString().ShouldContain(BodyMarker);
        json.GetProperty("TextBody").GetString().ShouldBe(BodyMarker);
        json.GetProperty("Tag").GetString().ShouldBe("TransportOrder");
        json.GetProperty("MessageStream").GetString().ShouldBe("outbound");
        json.GetProperty("TrackOpens").GetBoolean().ShouldBeTrue();
        json.GetProperty("TrackLinks").GetString().ShouldBe("HtmlAndText");
        json.GetProperty("Metadata").GetProperty("opentms-message-id").GetString().ShouldBe(MessageId.ToString());
        json.GetProperty("Metadata").GetProperty("opentms-tenant-id").GetString().ShouldBe(TenantId.ToString());
        json.TryGetProperty("Bcc", out _).ShouldBeFalse("empty fields are left out");
    }

    [Fact]
    public async Task Host_mail_is_marked_host_in_the_metadata_and_without_ReplyTo_none_is_sent()
    {
        var mail = Mail(replyTo: null);
        mail.TenantId = null;

        await Provider().SendAsync(mail, CancellationToken.None);

        var json = JsonDocument.Parse(_handler.Requests.Single().Body!).RootElement;
        json.GetProperty("Metadata").GetProperty("opentms-tenant-id").GetString().ShouldBe("host");
        json.TryGetProperty("ReplyTo", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task A_422_is_a_permanent_rejection_with_Postmarks_error_code()
    {
        _handler.Respond = _ => ScriptedPostmarkHandler.Json((HttpStatusCode)422, """{"ErrorCode":406,"Message":"You tried to send to recipient(s) that have been marked as inactive."}""");

        var ex = await Should.ThrowAsync<EmailRejectedException>(() => Provider().SendAsync(Mail(), CancellationToken.None));

        ex.ErrorCode.ShouldBe("406");
        ex.Message.ShouldContain("inactive");
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(503)]
    public async Task Other_failures_are_transient_with_a_known_outcome(int status)
    {
        _handler.Respond = _ => ScriptedPostmarkHandler.Json((HttpStatusCode)status, """{"ErrorCode":10,"Message":"no"}""");

        var ex = await Should.ThrowAsync<EmailDeliveryUnavailableException>(() => Provider().SendAsync(Mail(), CancellationToken.None));

        ex.OutcomeUnknown.ShouldBeFalse("Postmark answered: it did not take the mail");
        ex.Message.ShouldContain(status.ToString());
    }

    [Fact]
    public async Task An_unreachable_host_is_transient_with_a_known_outcome()
    {
        _handler.Throw = new HttpRequestException("connection refused");

        var ex = await Should.ThrowAsync<EmailDeliveryUnavailableException>(() => Provider().SendAsync(Mail(), CancellationToken.None));

        ex.OutcomeUnknown.ShouldBeFalse();
    }

    [Fact]
    public async Task A_timeout_is_transient_with_an_unknown_outcome()
    {
        _handler.Throw = new TaskCanceledException("timed out");

        var ex = await Should.ThrowAsync<EmailDeliveryUnavailableException>(() => Provider().SendAsync(Mail(), CancellationToken.None));

        ex.OutcomeUnknown.ShouldBeTrue("the request may have reached Postmark");
    }

    [Fact]
    public async Task A_success_answer_without_a_MessageID_is_not_taken_for_a_sent_mail()
    {
        _handler.Respond = _ => ScriptedPostmarkHandler.Json(HttpStatusCode.OK, """{"ErrorCode":0,"Message":"OK"}""");

        await Should.ThrowAsync<EmailDeliveryUnavailableException>(() => Provider().SendAsync(Mail(), CancellationToken.None));
    }

    [Theory]
    [InlineData(null, "noreply@ryd-software.com")]
    [InlineData(Token, "")]
    public async Task Without_token_or_From_it_is_not_configured_and_calls_nothing(string? token, string from)
    {
        var provider = Provider(o =>
        {
            o.ServerToken = token;
            o.FromAddress = from;
        });

        (await provider.GetStatusAsync()).IsConfigured.ShouldBeFalse();
        await Should.ThrowAsync<EmailDeliveryUnavailableException>(() => provider.SendAsync(Mail(), CancellationToken.None));
        _handler.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_hosts_settings_override_the_configuration_field_by_field_and_empty_ones_keep_it()
    {
        _settings.Values[EmailSettingNames.ServerToken] = "qq-settings-token";
        _settings.Values[EmailSettingNames.FromAddress] = "mail@ryd-software.com";
        _settings.Values[EmailSettingNames.FromName] = "";
        _settings.Values[EmailSettingNames.TrackOpens] = "false";
        _settings.Values[EmailSettingNames.TrackLinks] = "None";

        var provider = Provider();
        await provider.SendAsync(Mail(), CancellationToken.None);

        var (request, body) = _handler.Requests.Single();
        request.Headers.GetValues("X-Postmark-Server-Token").ShouldBe(["qq-settings-token"]);
        var json = JsonDocument.Parse(body!).RootElement;
        json.GetProperty("From").GetString().ShouldBe("\"OpenTMS\" <mail@ryd-software.com>", "an empty From name keeps the configuration's");
        json.GetProperty("TrackOpens").GetBoolean().ShouldBeFalse();
        json.GetProperty("TrackLinks").GetString().ShouldBe("None");
        var status = await provider.GetStatusAsync();
        status.ServerTokenSource.ShouldBe(EmailSettingSource.Settings);
        status.FromAddress.ShouldBe("mail@ryd-software.com");
    }

    [Fact]
    public async Task Without_a_stored_token_the_configurations_token_is_used_and_reported_as_such()
    {
        (await Provider().GetStatusAsync()).ServerTokenSource.ShouldBe(EmailSettingSource.Configuration);
        (await Provider(o => o.ServerToken = null).GetStatusAsync()).ServerTokenSource.ShouldBe(EmailSettingSource.None);
    }

    [Fact]
    public async Task No_log_line_holds_the_token_an_address_the_subject_or_the_body()
    {
        await Provider().SendAsync(Mail(), CancellationToken.None);
        _handler.Respond = _ => ScriptedPostmarkHandler.Json((HttpStatusCode)422, """{"ErrorCode":300,"Message":"Invalid email request"}""");
        await Should.ThrowAsync<EmailRejectedException>(() => Provider().SendAsync(Mail(), CancellationToken.None));
        _handler.Throw = new TaskCanceledException();
        await Should.ThrowAsync<EmailDeliveryUnavailableException>(() => Provider().SendAsync(Mail(), CancellationToken.None));

        _log.Lines.ShouldNotBeEmpty();
        foreach (var secret in new[] { Token, Recipient, "cc@klant.example", "planning@bakker.example", "Orderbevestiging", BodyMarker })
        {
            _log.Lines.ShouldAllBe(line => !line.Contains(secret), $"'{secret}' must never be logged");
        }
    }
}
