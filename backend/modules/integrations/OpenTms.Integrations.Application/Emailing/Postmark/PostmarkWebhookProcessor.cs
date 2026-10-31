using System;
using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenTms.Platform.Emailing;
using Volo.Abp.DependencyInjection;

namespace OpenTms.Integrations.Emailing.Postmark;

/// <summary>
/// The ingest side of Postmark (design 64, 07-ingest): the only place that knows Postmark's webhook format. It checks the
/// Basic credentials Postmark sends (constant time; without configured credentials nothing is accepted), turns a
/// Delivery, Bounce, SpamComplaint, Open or Click record into Platform's <see cref="EmailDeliveryReport"/> and hands it to
/// the e-mail status sink. It applies no business rule and never decides the tenant: the sink finds it in Platform's own
/// route. Logs carry the record type and Postmark's MessageID only.
/// </summary>
public class PostmarkWebhookProcessor : IPostmarkWebhookProcessor, ITransientDependency
{
    private readonly IEmailStatusSink _sink;
    private readonly PostmarkSettingsResolver _settings;
    private readonly ILogger<PostmarkWebhookProcessor> _logger;

    public PostmarkWebhookProcessor(IEmailStatusSink sink, PostmarkSettingsResolver settings, ILogger<PostmarkWebhookProcessor> logger)
    {
        _sink = sink;
        _settings = settings;
        _logger = logger;
    }

    public async Task<PostmarkWebhookOutcome> ProcessAsync(string? authorizationHeader, string body)
    {
        if (!IsAuthorized(authorizationHeader, (await _settings.ResolveAsync()).Options))
        {
            _logger.LogWarning("A Postmark webhook call was refused: missing or wrong credentials.");
            return PostmarkWebhookOutcome.Unauthorized;
        }

        if (!TryParse(body, out var recordType, out var report))
        {
            return PostmarkWebhookOutcome.Malformed;
        }

        if (report == null)
        {
            _logger.LogDebug("Postmark record type {RecordType} is not kept; acknowledged.", recordType);
            return PostmarkWebhookOutcome.Accepted;
        }

        var matched = await _sink.RecordAsync(report);
        _logger.LogInformation("Postmark {RecordType} for {ProviderMessageId}: {Result}.", recordType, report.ProviderMessageId, matched ? "applied" : "no mail of this system");
        return PostmarkWebhookOutcome.Accepted;
    }

    private static bool IsAuthorized(string? authorizationHeader, PostmarkOptions options)
    {
        if (!options.IsWebhookConfigured
            || !AuthenticationHeaderValue.TryParse(authorizationHeader, out var header)
            || !string.Equals(header.Scheme, "Basic", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrEmpty(header.Parameter))
        {
            return false;
        }

        byte[] presented;
        try
        {
            presented = Convert.FromBase64String(header.Parameter);
        }
        catch (FormatException)
        {
            return false;
        }

        var expected = Encoding.UTF8.GetBytes(options.WebhookUsername + ":" + options.WebhookPassword);
        return CryptographicOperations.FixedTimeEquals(presented, expected);
    }

    /// <returns>False when the body is not a Postmark record; true with a null report for a record type that is not kept.</returns>
    private static bool TryParse(string body, out string? recordType, out EmailDeliveryReport? report)
    {
        recordType = null;
        report = null;
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("RecordType", out var type) || type.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("MessageID", out var messageId) || messageId.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(messageId.GetString()))
            {
                return false;
            }

            recordType = type.GetString();
            (EmailDeliveryReportType? reportType, string? timeProperty) = recordType switch
            {
                "Delivery" => (EmailDeliveryReportType.Delivered, "DeliveredAt"),
                "Bounce" => (EmailDeliveryReportType.Bounced, "BouncedAt"),
                "SpamComplaint" => (EmailDeliveryReportType.SpamComplaint, "BouncedAt"),
                "Open" => (EmailDeliveryReportType.Opened, "ReceivedAt"),
                "Click" => (EmailDeliveryReportType.Clicked, "ReceivedAt"),
                _ => ((EmailDeliveryReportType?, string?))(null, null)
            };
            if (reportType == null)
            {
                return true;
            }

            if (!TryGetTime(root, timeProperty!, out var occurredAt))
            {
                return false;
            }

            report = new EmailDeliveryReport
            {
                ProviderMessageId = messageId.GetString()!,
                Type = reportType.Value,
                OccurredAt = occurredAt,
                BounceType = reportType == EmailDeliveryReportType.Bounced ? GetString(root, "Type") : null,
                Description = reportType == EmailDeliveryReportType.Bounced ? GetString(root, "Description") : null
            };
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryGetTime(JsonElement root, string name, out DateTime value)
    {
        value = default;
        return root.TryGetProperty(name, out var time) && time.ValueKind == JsonValueKind.String
               && DateTimeOffset.TryParse(time.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
               && (value = parsed.UtcDateTime) != default;
    }

    private static string? GetString(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
