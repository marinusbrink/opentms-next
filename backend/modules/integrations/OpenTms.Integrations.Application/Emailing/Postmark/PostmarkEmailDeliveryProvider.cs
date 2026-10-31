using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenTms.Platform.Emailing;

namespace OpenTms.Integrations.Emailing.Postmark;

/// <summary>
/// Postmark behind Platform's <see cref="IEmailDeliveryProvider"/> (design 64): <c>POST email</c> with the server token,
/// the settings combined per call by <see cref="PostmarkSettingsResolver"/> (host settings over configuration),
/// the configured From on the verified domain, open and link tracking, and the send log id and tenant as metadata (for
/// support in Postmark's activity view; never used for routing). One attempt within <see cref="Timeout"/>: the send job
/// retries. A 422 is a permanent rejection with Postmark's error code; 401, 403, 429, 5xx and an unreachable host are
/// transient; a timeout is transient with an unknown outcome. Logs carry the send log id, the tenant id, the status and
/// Postmark's error code only; never the token, an address, a subject or a body.
/// </summary>
public class PostmarkEmailDeliveryProvider : IEmailDeliveryProvider
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private const int MaxMessageLength = 500;

    private static readonly JsonSerializerOptions JsonOptions = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    private readonly HttpClient _http;
    private readonly PostmarkSettingsResolver _settings;
    private readonly ILogger<PostmarkEmailDeliveryProvider> _logger;

    public PostmarkEmailDeliveryProvider(HttpClient http, PostmarkSettingsResolver settings, ILogger<PostmarkEmailDeliveryProvider> logger)
    {
        _http = http;
        _settings = settings;
        _logger = logger;
    }

    public async Task<EmailDeliveryStatus> GetStatusAsync() => (await _settings.ResolveAsync()).ToStatus();

    public async Task<string> SendAsync(OutgoingEmail email, CancellationToken cancellationToken)
    {
        var options = (await _settings.ResolveAsync()).Options;
        if (!options.IsConfigured)
        {
            throw new EmailDeliveryUnavailableException("Postmark is not configured (Integrations:Postmark ServerToken and FromAddress).");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "email")
        {
            Content = JsonContent.Create(Body(email, options), options: JsonOptions)
        };
        request.Headers.Add("Accept", "application/json");
        request.Headers.Add("X-Postmark-Server-Token", options.ServerToken);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken);
        }
        catch (TaskCanceledException e) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Postmark did not answer for mail {EmailMessageId} of tenant {TenantId} in time.", email.EmailMessageId, email.TenantId);
            throw new EmailDeliveryUnavailableException("Postmark did not answer in time.", outcomeUnknown: true, e);
        }
        catch (HttpRequestException e)
        {
            _logger.LogWarning("Postmark could not be reached for mail {EmailMessageId} of tenant {TenantId}.", email.EmailMessageId, email.TenantId);
            throw new EmailDeliveryUnavailableException("Postmark could not be reached.", innerException: e);
        }

        using (response)
        {
            var answer = await ReadAnswerAsync(response, cancellationToken);
            if (response.IsSuccessStatusCode && answer.ErrorCode == 0 && !string.IsNullOrWhiteSpace(answer.MessageId))
            {
                return answer.MessageId!;
            }

            var status = (int)response.StatusCode;
            var message = Shorten(answer.Message) ?? response.ReasonPhrase ?? "no message";
            _logger.LogWarning(
                "Postmark refused mail {EmailMessageId} of tenant {TenantId}: HTTP {Status}, error code {ErrorCode}.",
                email.EmailMessageId, email.TenantId, status, answer.ErrorCode);

            if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
            {
                throw new EmailRejectedException(answer.ErrorCode.ToString(CultureInfo.InvariantCulture), message);
            }

            throw new EmailDeliveryUnavailableException($"Postmark answered HTTP {status}: {message}");
        }
    }

    private static Dictionary<string, object> Body(OutgoingEmail email, PostmarkOptions options)
    {
        var body = new Dictionary<string, object>
        {
            ["From"] = new System.Net.Mail.MailAddress(options.FromAddress, options.FromName).ToString(),
            ["To"] = email.To,
            ["Subject"] = email.Subject,
            ["MessageStream"] = options.MessageStream,
            ["TrackOpens"] = options.TrackOpens,
            ["TrackLinks"] = options.TrackLinks,
            ["Metadata"] = new Dictionary<string, string>
            {
                ["opentms-message-id"] = email.EmailMessageId.ToString(),
                ["opentms-tenant-id"] = email.TenantId?.ToString() ?? "host"
            }
        };
        Add(body, "Cc", email.Cc);
        Add(body, "Bcc", email.Bcc);
        Add(body, "ReplyTo", email.ReplyTo);
        Add(body, "HtmlBody", email.HtmlBody);
        Add(body, "TextBody", email.TextBody);
        Add(body, "Tag", email.Tag);
        return body;
    }

    private static void Add(Dictionary<string, object> body, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            body[name] = value;
        }
    }

    private static async Task<PostmarkAnswer> ReadAnswerAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<PostmarkAnswer>(cancellationToken) ?? new PostmarkAnswer();
        }
        catch (JsonException)
        {
            return new PostmarkAnswer();
        }
    }

    private static string? Shorten(string? text) => text == null || text.Length <= MaxMessageLength ? text : text[..MaxMessageLength];

    private sealed class PostmarkAnswer
    {
        public int ErrorCode { get; set; }

        public string? Message { get; set; }

        [JsonPropertyName("MessageID")]
        public string? MessageId { get; set; }
    }
}
