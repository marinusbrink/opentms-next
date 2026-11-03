using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta.Messages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace OpenTms.Integrations.OrderIntake.Ai;

/// <summary>
/// The Anthropic adapter of <see cref="ILanguageModelClient"/> (design 72 "The AI step", AD-21), on the official
/// <c>Anthropic</c> SDK. One request: the configured model, adaptive thinking, effort high, structured output
/// (<c>output_config.format</c> with the caller's JSON schema), <c>max_tokens</c> from the request, the system block
/// marked for prompt caching, a PDF as a base64 document block, server-side refusal fallback (<c>fallbacks: "default"</c>,
/// beta <c>server-side-fallback-2026-07-01</c>), streamed and aggregated into the final message (a long answer never runs
/// into the non-streaming timeout). A refusal that survives the fallback chain comes back as
/// <see cref="LanguageModelStopReason.Refusal"/>. Errors: 429, 5xx (529 overloaded included), timeouts and network
/// failures are transient; every other failure (400, 401, 403, 404, a missing key) is not. The key is resolved per call
/// from <see cref="OrderIntakeAiOptions"/>, so a saved host setting applies at once. Logs carry ids and figures only.
/// </summary>
public class AnthropicLanguageModelClient : ILanguageModelClient
{
    public const string FallbackBeta = "server-side-fallback-2026-07-01";

    /// <summary>The ceiling of one call; a learning call with high effort may think for minutes.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(10);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<OrderIntakeAiOptions> _options;

    public ILogger<AnthropicLanguageModelClient> Logger { get; set; } = NullLogger<AnthropicLanguageModelClient>.Instance;

    public AnthropicLanguageModelClient(IHttpClientFactory httpClientFactory, IOptions<OrderIntakeAiOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
    }

    public async Task<LanguageModelResponse> CompleteAsync(LanguageModelRequest request, CancellationToken cancellationToken = default)
    {
        var apiKey = _options.Value.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new LanguageModelException("The Anthropic API key is not configured (Administration → Services → Integrations).", isTransient: false);
        }

        var client = new AnthropicClient
        {
            ApiKey = apiKey,
            HttpClient = _httpClientFactory.CreateClient(nameof(AnthropicLanguageModelClient)),
            /* The job retries transient failures with Hangfire's back-off; the SDK's own retries cover a short blip. */
            MaxRetries = 2,
            Timeout = Timeout
        };

        var parameters = BuildParameters(request);
        BetaMessage message;
        try
        {
            message = await client.Beta.Messages.CreateStreaming(parameters, cancellationToken).Aggregate();
        }
        catch (Exception ex) when (ex is not LanguageModelException && !(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
        {
            throw Translate(ex);
        }

        var response = new LanguageModelResponse
        {
            ModelId = message.Model.Raw() ?? request.ModelId,
            StopReason = MapStopReason(message.StopReason?.Raw()),
            Json = FinalText(message.Content),
            Usage = new LanguageModelUsage
            {
                InputTokens = (int)message.Usage.InputTokens,
                OutputTokens = (int)message.Usage.OutputTokens,
                CacheReadTokens = (int)(message.Usage.CacheReadInputTokens ?? 0),
                CacheWriteTokens = (int)(message.Usage.CacheCreationInputTokens ?? 0)
            }
        };

        Logger.LogInformation(
            "Language model {ModelId} answered with stop reason {StopReason}: {InputTokens} input, {OutputTokens} output, {CacheReadTokens} cache-read tokens.",
            response.ModelId, response.StopReason, response.Usage.InputTokens, response.Usage.OutputTokens, response.Usage.CacheReadTokens);
        return response;
    }

    public static MessageCreateParams BuildParameters(LanguageModelRequest request)
    {
        var messages = request.Messages.Select(m => m.Role == LanguageModelRole.Assistant
            ? (BetaMessageParam)new BetaAssistantMessageParam { Content = m.Parts.Select(ToBlock).ToList() }
            : new BetaUserMessageParam { Content = m.Parts.Select(ToBlock).ToList() }).ToList();

        var system = new List<BetaTextBlockParam>
        {
            request.CacheSystemText
                ? new BetaTextBlockParam { Text = request.SystemText, CacheControl = new BetaCacheControlEphemeral() }
                : new BetaTextBlockParam { Text = request.SystemText }
        };

        var outputConfig = new BetaOutputConfig { Effort = Effort.High };
        if (!string.IsNullOrWhiteSpace(request.JsonSchema))
        {
            outputConfig = new BetaOutputConfig
            {
                Effort = Effort.High,
                Format = new BetaJsonOutputFormat
                {
                    Schema = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(request.JsonSchema)!
                }
            };
        }

        return new MessageCreateParams
        {
            Model = request.ModelId,
            MaxTokens = request.MaxTokens,
            Betas = [FallbackBeta],
            Fallbacks = new Default(),
            Thinking = new BetaThinkingConfigAdaptive(),
            OutputConfig = outputConfig,
            System = system,
            Messages = messages
        };
    }

    /// <summary>
    /// The answer's text. A server-side fallback in mid-output leaves a fallback block after the declined model's partial
    /// text: only the text after the last fallback block is the answer.
    /// </summary>
    private static string FinalText(IReadOnlyList<BetaContentBlock> content)
    {
        var builder = new StringBuilder();
        foreach (var block in content)
        {
            if (block.TryPickFallback(out _))
            {
                builder.Clear();
            }
            else if (block.TryPickText(out var text))
            {
                builder.Append(text.Text);
            }
        }

        return builder.ToString();
    }

    private static BetaContentBlockParam ToBlock(LanguageModelContentPart part) => part.Kind switch
    {
        LanguageModelContentKind.Pdf => new BetaRequestDocumentBlock
        {
            Source = new BetaBase64PdfSource { Data = Convert.ToBase64String(part.Data ?? []) },
            Title = part.Title
        },
        _ => new BetaTextBlockParam { Text = part.Text ?? string.Empty }
    };

    public static LanguageModelStopReason MapStopReason(string? stopReason) => (stopReason ?? string.Empty).ToLowerInvariant() switch
    {
        "end_turn" or "endturn" or "stop_sequence" or "stopsequence" => LanguageModelStopReason.EndTurn,
        "max_tokens" or "maxtokens" or "model_context_window_exceeded" or "modelcontextwindowexceeded" => LanguageModelStopReason.MaxTokens,
        "refusal" => LanguageModelStopReason.Refusal,
        _ => LanguageModelStopReason.Other
    };

    /// <summary>429, 5xx, timeouts and network failures are transient (the job retries); everything else is not.</summary>
    public static LanguageModelException Translate(Exception ex)
    {
        var status = StatusOf(ex);
        var transient = ex switch
        {
            AnthropicRateLimitException => true,
            Anthropic5xxException => true,
            AnthropicIOException => true,
            AnthropicSseException => true,
            HttpRequestException => true,
            TimeoutException => true,
            OperationCanceledException => true,
            _ => status is HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout || (int?)status >= 500
        };
        var code = status != null ? ((int)status.Value).ToString(System.Globalization.CultureInfo.InvariantCulture) : ex.GetType().Name;
        return new LanguageModelException($"The language model call failed ({code}).", transient, ex);
    }

    private static HttpStatusCode? StatusOf(Exception ex)
    {
        var property = ex.GetType().GetProperty("StatusCode");
        return property?.GetValue(ex) switch
        {
            HttpStatusCode code => code,
            int number => (HttpStatusCode)number,
            _ => (ex as HttpRequestException)?.StatusCode
        };
    }
}
