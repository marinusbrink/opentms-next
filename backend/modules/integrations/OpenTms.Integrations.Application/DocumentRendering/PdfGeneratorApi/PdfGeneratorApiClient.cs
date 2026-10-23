using System;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTms.Platform;
using OpenTms.Platform.DocumentRendering;
using OpenTms.Platform.Localization;
using Volo.Abp;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Tracing;

namespace OpenTms.Integrations.DocumentRendering.PdfGeneratorApi;

/// <summary>
/// The typed HTTP client of pdfgeneratorapi.com API v4 (design 50, "Resilience"). One host, TLS validation on (the
/// handler's default), a fresh JWT per attempt in the current tenant's workspace, the correlation id on every request.
/// Two call modes (<see cref="RenderCallMode"/>), timed per request with a token linked to the caller's, because one
/// HttpClient serves both: <b>Interactive</b> (a user waits: editor URL, copy, template lookup, template data, health,
/// the test render) makes ONE attempt within <see cref="InteractiveTimeout"/>, and a 429 says the service is busy;
/// <b>Background</b> (the renders of jobs) allows <see cref="Timeout"/> per attempt and retries 429 and 5xx up to four
/// times after 3, 6, 12 and 24 seconds (OpenTMS's schedule). The HttpClient's own timeout (<see cref="Timeout"/>) stays
/// as the ceiling of any one attempt. Any other 4xx fails at once with the provider's message as
/// <see cref="PlatformErrorCodes.DocumentRenderingFailed"/>; so do a timeout and an unreachable host. Logs carry the
/// operation, the call mode, the template id, the tenant id, the status code and the attempt; never the key, the
/// secret, the token, the payload or the document.
/// </summary>
public class PdfGeneratorApiClient
{
    public const int MaxRetries = 4;

    /// <summary>The timeout of one background attempt, and the HttpClient's own timeout (the ceiling of any attempt).</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    /// <summary>The timeout of an interactive call: its only attempt, so a click never waits longer.</summary>
    public static readonly TimeSpan InteractiveTimeout = TimeSpan.FromSeconds(15);

    public static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(24)
    ];

    /// <summary>The English text of a 429 on an interactive call, used when no localizer is at hand.</summary>
    public const string BusyMessage = "the document service is busy, try again in a moment.";

    private const string BusyMessageKey = "Platform:DocumentRenderingBusy";

    private const int MaxMessageLength = 500;

    /* Null properties are left out; the provider builds its bodies from dictionaries without null entries. */
    private static readonly JsonSerializerOptions JsonOptions = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    private readonly HttpClient _http;
    private readonly PdfGeneratorApiOptions _options;
    private readonly ICurrentTenant _currentTenant;
    private readonly TimeProvider _time;
    private readonly IRetryDelay _delay;
    private readonly ICorrelationIdProvider _correlationIds;
    private readonly AbpCorrelationIdOptions _correlationOptions;
    private readonly IStringLocalizer<PlatformResource>? _localizer;

    public ILogger<PdfGeneratorApiClient> Logger { get; }

    /// <param name="localizer">Platform's texts for the "busy" message; optional, so the client also works where
    /// localization is not configured (the English <see cref="BusyMessage"/> then).</param>
    public PdfGeneratorApiClient(
        HttpClient http,
        IOptions<PdfGeneratorApiOptions> options,
        ICurrentTenant currentTenant,
        TimeProvider time,
        IRetryDelay delay,
        ICorrelationIdProvider correlationIds,
        IOptions<AbpCorrelationIdOptions> correlationOptions,
        ILogger<PdfGeneratorApiClient> logger,
        IStringLocalizer<PlatformResource>? localizer = null)
    {
        _http = http;
        _options = options.Value;
        _currentTenant = currentTenant;
        _time = time;
        _delay = delay;
        _correlationIds = correlationIds;
        _correlationOptions = correlationOptions.Value;
        _localizer = localizer;
        Logger = logger;
    }

    public bool IsConfigured => _options.IsConfigured;

    public string DefaultWorkspace => _options.DefaultWorkspace;

    /// <summary>The host's workspace for the host; the tenant's own workspace for a tenant.</summary>
    public string CurrentWorkspace => _currentTenant.Id is { } tenantId
        ? string.Format(CultureInfo.InvariantCulture, _options.TenantWorkspace, tenantId)
        : _options.DefaultWorkspace;

    /// <summary>
    /// Sends one call and returns the JSON answer. A 404 returns null when <paramref name="allowNotFound"/> is set, and
    /// is <see cref="PlatformErrorCodes.TemplateNotFound"/> when the call names a template.
    /// </summary>
    /// <param name="path">Relative to the base URL, without a leading slash ("documents/generate").</param>
    /// <param name="operation">A short name for the logs ("generate", "editor").</param>
    /// <param name="mode">Interactive: one attempt within <see cref="InteractiveTimeout"/>. Background: the retry schedule.</param>
    /// <param name="workspace">Overrides the current tenant's workspace (the health check uses the default workspace).</param>
    public async Task<JsonElement?> SendAsync(
        HttpMethod method,
        string path,
        object? body,
        string operation,
        string? templateId,
        RenderCallMode mode,
        CancellationToken cancellationToken,
        bool allowNotFound = false,
        string? workspace = null)
    {
        if (!IsConfigured)
        {
            throw new BusinessException(PlatformErrorCodes.RenderingNotConfigured).WithData("provider", "PdfGeneratorApi");
        }

        var targetWorkspace = workspace ?? CurrentWorkspace;
        var payload = body == null ? null : JsonSerializer.Serialize(body, JsonOptions);
        var interactive = mode == RenderCallMode.Interactive;
        var maxAttempts = interactive ? 1 : MaxRetries + 1;
        var attemptTimeout = interactive ? InteractiveTimeout : Timeout;
        /* What the caller can actually wait: the HttpClient's own timeout cuts an attempt short as well. */
        var effectiveTimeout = _http.Timeout == System.Threading.Timeout.InfiniteTimeSpan || _http.Timeout > attemptTimeout
            ? attemptTimeout
            : _http.Timeout;

        for (var attempt = 1; ; attempt++)
        {
            using var request = new HttpRequestMessage(method, path);
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer", PdfGeneratorApiJwt.Create(_options.Key!, _options.Secret!, targetWorkspace, _time.GetUtcNow()));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            var correlationId = _correlationIds.Get();
            if (!string.IsNullOrEmpty(correlationId))
            {
                request.Headers.TryAddWithoutValidation(_correlationOptions.HttpHeaderName, correlationId);
            }

            if (payload != null)
            {
                request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
            }

            HttpResponseMessage response;
            /* Per request, not HttpClient.Timeout: the same client serves both modes. Linked, so the caller's
             * cancellation (the browser going away) still stops the call at once. */
            using var timeout = new CancellationTokenSource(attemptTimeout, _time);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            try
            {
                response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, linked.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                Logger.LogWarning(
                    "pdfgeneratorapi {Operation} ({CallMode}) of template {TemplateId} for tenant {TenantId} timed out after {TimeoutSeconds} s on attempt {Attempt}",
                    operation, mode, templateId, _currentTenant.Id, effectiveTimeout.TotalSeconds, attempt);
                throw Failed($"No answer within {effectiveTimeout.TotalSeconds:0} seconds.");
            }
            catch (HttpRequestException e)
            {
                Logger.LogWarning(
                    "pdfgeneratorapi {Operation} ({CallMode}) of template {TemplateId} for tenant {TenantId} could not be reached on attempt {Attempt}: {RequestError}",
                    operation, mode, templateId, _currentTenant.Id, attempt, e.HttpRequestError);
                throw Failed("The service could not be reached.");
            }

            using (response)
            {
                var status = (int)response.StatusCode;
                if (response.IsSuccessStatusCode)
                {
                    Logger.LogDebug(
                        "pdfgeneratorapi {Operation} ({CallMode}) of template {TemplateId} for tenant {TenantId} answered {StatusCode} on attempt {Attempt}",
                        operation, mode, templateId, _currentTenant.Id, status, attempt);
                    return await ReadJsonAsync(response, cancellationToken);
                }

                if (status == 404 && allowNotFound)
                {
                    return null;
                }

                var retryable = status == 429 || status >= 500;
                if (retryable && attempt < maxAttempts)
                {
                    var delay = RetryDelays[attempt - 1];
                    Logger.LogWarning(
                        "pdfgeneratorapi {Operation} ({CallMode}) of template {TemplateId} for tenant {TenantId} answered {StatusCode} on attempt {Attempt} of {MaxAttempts}; retrying in {DelaySeconds} s",
                        operation, mode, templateId, _currentTenant.Id, status, attempt, maxAttempts, delay.TotalSeconds);
                    await _delay.DelayAsync(delay, cancellationToken);
                    continue;
                }

                Logger.LogWarning(
                    "pdfgeneratorapi {Operation} ({CallMode}) of template {TemplateId} for tenant {TenantId} failed with {StatusCode} on attempt {Attempt} of {MaxAttempts}",
                    operation, mode, templateId, _currentTenant.Id, status, attempt, maxAttempts);

                if (status == 404 && templateId != null)
                {
                    throw new BusinessException(PlatformErrorCodes.TemplateNotFound).WithData("templateId", templateId);
                }

                if (status == 429 && interactive)
                {
                    /* A user waits: no backoff in the request, just "try again in a moment". */
                    throw Failed(Busy()).WithData("status", status);
                }

                var message = await ReadErrorMessageAsync(response, cancellationToken);
                throw Failed(message ?? $"HTTP {status} {response.ReasonPhrase}".TrimEnd()).WithData("status", status);
            }
        }
    }

    private string Busy()
    {
        var text = _localizer?[BusyMessageKey];
        return text == null || text.ResourceNotFound ? BusyMessage : text.Value;
    }

    public static BusinessException Failed(string message)
        => new BusinessException(PlatformErrorCodes.DocumentRenderingFailed).WithData("message", message);

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            throw Failed("The service sent an answer that is not JSON.");
        }
    }

    /// <summary>The provider's error text: <c>{ "message": "…" }</c> in API v4; shortened, never logged.</summary>
    private static async Task<string?> ReadErrorMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string text;
        try
        {
            text = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            foreach (var property in new[] { "message", "error" })
            {
                if (document.RootElement.ValueKind == JsonValueKind.Object
                    && document.RootElement.TryGetProperty(property, out var value)
                    && value.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(value.GetString()))
                {
                    return Shorten(value.GetString()!);
                }
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Shorten(string text) => text.Length <= MaxMessageLength ? text : text[..MaxMessageLength] + "…";
}
