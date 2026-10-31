using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenTms.Platform.Emailing;
using Shouldly;
using Xunit;

namespace OpenTms.Integrations.Emailing.Postmark;

/// <summary>
/// The ingest side of Postmark (docs/designs/64-email-delivery-postmark.md, "Delivery reports"). Risk class Critical for
/// the webhook authentication (Basic credentials, constant-time compare, nothing accepted without configured credentials)
/// and for never taking the tenant from the payload (the report handed to the sink carries no tenant); Medium for parsing
/// each record type. The sink is a recorder: what the sink does is covered by the send-log tests.
/// </summary>
public class PostmarkWebhookProcessorTests
{
    private const string User = "postmark-qq";
    private const string Password = "qq-webhook-pass-8812";
    private const string MessageId = "883953f4-6105-42a2-a16a-77a8eac79483";

    private readonly RecordingEmailStatusSink _sink = new();
    private readonly CapturingLogger<PostmarkWebhookProcessor> _log = new();

    private PostmarkWebhookProcessor Processor(string? user = User, string? password = Password, DictionarySettingProvider? settings = null)
        => new(_sink, PostmarkTestSettings.Resolver(new PostmarkOptions { WebhookUsername = user, WebhookPassword = password }, settings), _log);

    private static string Basic(string user, string password) => "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(user + ":" + password));

    private static readonly string Auth = Basic(User, Password);

    private const string Delivery = $$$"""{"RecordType":"Delivery","ServerID":23,"MessageStream":"outbound","MessageID":"{{{MessageId}}}","Recipient":"john@example.com","Tag":"welcome-email","DeliveredAt":"2026-10-30T16:33:54.9070259Z","Details":"Test delivery webhook details","Metadata":{"opentms-tenant-id":"aaaaaaaa-0000-0000-0000-000000000000"}}""";

    [Fact]
    public async Task A_delivery_with_the_right_credentials_reaches_the_sink_without_any_tenant()
    {
        (await Processor().ProcessAsync(Auth, Delivery)).ShouldBe(PostmarkWebhookOutcome.Accepted);

        var report = _sink.Reports.ShouldHaveSingleItem();
        report.ProviderMessageId.ShouldBe(MessageId);
        report.Type.ShouldBe(EmailDeliveryReportType.Delivered);
        report.OccurredAt.ShouldBe(new DateTime(2026, 10, 30, 16, 33, 54, DateTimeKind.Utc).AddTicks(9070259));
        report.OccurredAt.Kind.ShouldBe(DateTimeKind.Utc);
        typeof(EmailDeliveryReport).GetProperties().ShouldNotContain(p => p.Name.Contains("Tenant"), "the sink finds the tenant in Platform's own route, never in the payload");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Bearer abc")]
    [InlineData("Basic not-base64!")]
    public async Task Missing_or_malformed_credentials_are_refused_before_anything_is_read(string? header)
    {
        (await Processor().ProcessAsync(header, Delivery)).ShouldBe(PostmarkWebhookOutcome.Unauthorized);
        _sink.Reports.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_wrong_password_or_user_is_refused()
    {
        (await Processor().ProcessAsync(Basic(User, "wrong"), Delivery)).ShouldBe(PostmarkWebhookOutcome.Unauthorized);
        (await Processor().ProcessAsync(Basic("other", Password), Delivery)).ShouldBe(PostmarkWebhookOutcome.Unauthorized);
        (await Processor().ProcessAsync(Basic(User, Password + "x"), Delivery)).ShouldBe(PostmarkWebhookOutcome.Unauthorized);
        _sink.Reports.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(User, null)]
    [InlineData(null, Password)]
    public async Task Without_configured_credentials_nothing_is_accepted(string? user, string? password)
    {
        (await Processor(user, password).ProcessAsync(Basic(user ?? "", password ?? ""), Delivery)).ShouldBe(PostmarkWebhookOutcome.Unauthorized);
        (await Processor(user, password).ProcessAsync(null, Delivery)).ShouldBe(PostmarkWebhookOutcome.Unauthorized);
        _sink.Reports.ShouldBeEmpty();
    }

    [Fact]
    public async Task Credentials_stored_in_the_hosts_settings_replace_the_configured_ones()
    {
        var settings = new DictionarySettingProvider();
        settings.Values[EmailSettingNames.WebhookUsername] = "host-user";
        settings.Values[EmailSettingNames.WebhookPassword] = "host-pass-77";

        (await Processor(settings: settings).ProcessAsync(Basic("host-user", "host-pass-77"), Delivery)).ShouldBe(PostmarkWebhookOutcome.Accepted);
        (await Processor(settings: settings).ProcessAsync(Auth, Delivery)).ShouldBe(PostmarkWebhookOutcome.Unauthorized, "the configuration's pair no longer applies");
    }

    [Fact]
    public async Task A_bounce_carries_its_type_and_description()
    {
        var body = $$"""{"RecordType":"Bounce","ID":4323372036854775807,"Type":"HardBounce","TypeCode":1,"MessageID":"{{MessageId}}","Description":"The server was unable to deliver your message (ex: unknown user, mailbox not found).","Email":"john@example.com","BouncedAt":"2026-10-30T16:09:19Z","Inactive":true}""";

        (await Processor().ProcessAsync(Auth, body)).ShouldBe(PostmarkWebhookOutcome.Accepted);

        var report = _sink.Reports.ShouldHaveSingleItem();
        report.Type.ShouldBe(EmailDeliveryReportType.Bounced);
        report.BounceType.ShouldBe("HardBounce");
        report.Description.ShouldStartWith("The server was unable");
        report.OccurredAt.ShouldBe(new DateTime(2026, 10, 30, 16, 9, 19, DateTimeKind.Utc));
    }

    [Theory]
    [InlineData("SpamComplaint", "BouncedAt", EmailDeliveryReportType.SpamComplaint)]
    [InlineData("Open", "ReceivedAt", EmailDeliveryReportType.Opened)]
    [InlineData("Click", "ReceivedAt", EmailDeliveryReportType.Clicked)]
    public async Task Spam_complaints_opens_and_clicks_map_to_their_report_type_without_bounce_details(string recordType, string timeProperty, EmailDeliveryReportType expected)
    {
        var body = $$"""{"RecordType":"{{recordType}}","MessageID":"{{MessageId}}","Type":"SpamComplaint","Description":"x","{{timeProperty}}":"2026-10-30T12:00:00+02:00","Geo":{"City":"Utrecht"},"UserAgent":"Mozilla","OriginalLink":"https://example.com"}""";

        (await Processor().ProcessAsync(Auth, body)).ShouldBe(PostmarkWebhookOutcome.Accepted);

        var report = _sink.Reports.ShouldHaveSingleItem();
        report.Type.ShouldBe(expected);
        report.OccurredAt.ShouldBe(new DateTime(2026, 10, 30, 10, 0, 0, DateTimeKind.Utc), "times are stored in UTC");
        report.BounceType.ShouldBeNull();
        report.Description.ShouldBeNull();
    }

    [Fact]
    public async Task A_record_type_the_send_log_does_not_keep_is_acknowledged_and_ignored()
    {
        var body = $$"""{"RecordType":"SubscriptionChange","MessageID":"{{MessageId}}","ChangedAt":"2026-10-30T12:00:00Z"}""";

        (await Processor().ProcessAsync(Auth, body)).ShouldBe(PostmarkWebhookOutcome.Accepted);
        _sink.Reports.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_report_for_another_systems_mail_is_still_acknowledged()
    {
        _sink.Matches = false;

        (await Processor().ProcessAsync(Auth, Delivery)).ShouldBe(PostmarkWebhookOutcome.Accepted, "Postmark must not retry a report that belongs to another sender on the server");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"MessageID":"x","DeliveredAt":"2026-10-30T12:00:00Z"}""")]
    [InlineData("""{"RecordType":"Delivery","DeliveredAt":"2026-10-30T12:00:00Z"}""")]
    [InlineData("""{"RecordType":"Delivery","MessageID":"x"}""")]
    [InlineData("""{"RecordType":"Delivery","MessageID":"x","DeliveredAt":"yesterday"}""")]
    public async Task A_body_that_is_not_a_Postmark_report_is_malformed(string body)
    {
        (await Processor().ProcessAsync(Auth, body)).ShouldBe(PostmarkWebhookOutcome.Malformed);
        _sink.Reports.ShouldBeEmpty();
    }

    [Fact]
    public async Task No_log_line_holds_the_credentials_or_the_recipient()
    {
        await Processor().ProcessAsync(Auth, Delivery);
        await Processor().ProcessAsync(Basic(User, "wrong"), Delivery);

        _log.Lines.ShouldAllBe(line => !line.Contains(Password) && !line.Contains("john@example.com") && !line.Contains(Auth));
    }
}
