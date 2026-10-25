using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Tracing;

namespace OpenTms.Integrations.DocumentRendering.PdfGeneratorApi;

/// <summary>
/// The fakes of design 55's adapter tests: no request ever leaves the process (the pdfgeneratorapi account is FiLogic's
/// production account). The handler records every request as values and answers from a script; the clock and the retry
/// delay are manual, so the backoff schedule is asserted without real waits.
/// </summary>
public sealed record RecordedPdfRequest(HttpMethod Method, Uri Uri, string? Body, string? Bearer, string? CorrelationId, string? Accept)
{
    public JsonElement Json => JsonDocument.Parse(Body!).RootElement.Clone();

    /// <summary>The path below the base URL plus the query, e.g. "templates/123/editor".</summary>
    public string Relative => Uri.AbsoluteUri.Substring(PdfHarness.BaseUrl.Length);
}

public sealed class ScriptedPdfHandler : HttpMessageHandler
{
    private readonly object _gate = new();
    private readonly Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _script = new();

    public List<RecordedPdfRequest> Requests { get; } = new();

    /// <summary>What every request gets once the script is used up.</summary>
    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Fallback { get; set; } =
        (_, _) => Task.FromResult(Json("""{"response":{}}"""));

    public ScriptedPdfHandler Then(Func<HttpResponseMessage> response)
    {
        _script.Enqueue((_, _) => Task.FromResult(response()));
        return this;
    }

    public ScriptedPdfHandler ThenJson(string json) => Then(() => Json(json));

    public ScriptedPdfHandler ThenStatus(HttpStatusCode status, string body = "{}") => Then(() => Status(status, body));

    public ScriptedPdfHandler ThenAsync(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer)
    {
        _script.Enqueue(answer);
        return this;
    }

    public void Always(HttpStatusCode status, string body = "{}") => Fallback = (_, _) => Task.FromResult(Status(status, body));

    public static HttpResponseMessage Json(string json) => Status(HttpStatusCode.OK, json);

    public static HttpResponseMessage Status(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer;
        lock (_gate)
        {
            Requests.Add(new RecordedPdfRequest(
                request.Method,
                request.RequestUri!,
                body,
                request.Headers.Authorization?.Scheme == "Bearer" ? request.Headers.Authorization.Parameter : null,
                request.Headers.TryGetValues("X-Correlation-Id", out var ids) ? ids.Single() : null,
                request.Headers.Accept.ToString()));
            answer = _script.Count > 0 ? _script.Dequeue() : Fallback;
        }

        return await answer(request, cancellationToken);
    }
}

/// <summary>A clock that moves only when told (the retry delay moves it by the delay it is asked to wait).</summary>
public sealed class ManualTimeProvider : TimeProvider
{
    public ManualTimeProvider(DateTimeOffset now)
    {
        Now = now;
    }

    public DateTimeOffset Now { get; set; }

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>Records each requested wait, advances the manual clock by it, and honours the caller's token.</summary>
public sealed class RecordingRetryDelay : IRetryDelay
{
    private readonly ManualTimeProvider _time;

    public RecordingRetryDelay(ManualTimeProvider time)
    {
        _time = time;
    }

    public List<TimeSpan> Delays { get; } = new();

    /// <summary>Runs inside each wait, before the token is checked (e.g. to cancel the caller's token).</summary>
    public Action? OnDelay { get; set; }

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        Delays.Add(delay);
        _time.Now += delay;
        OnDelay?.Invoke();
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}

public sealed class FakeCurrentTenant : ICurrentTenant
{
    private readonly AsyncLocal<Guid?> _id = new();

    public bool IsAvailable => Id.HasValue;

    public Guid? Id => _id.Value;

    public string? Name => Id?.ToString();

    public IDisposable Change(Guid? id, string? name = null)
    {
        var previous = _id.Value;
        _id.Value = id;
        return new Restore(() => _id.Value = previous);
    }

    private sealed class Restore(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }
}

public sealed class FakeCorrelationIdProvider : ICorrelationIdProvider
{
    public string? Value { get; set; }

    public string? Get() => Value;

    public IDisposable Change(string? correlationId)
    {
        var previous = Value;
        Value = correlationId;
        return new Restore(() => Value = previous);
    }

    private sealed class Restore(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }
}

/// <summary>One captured log entry at any level: the formatted message, every structured value and the exception.</summary>
public sealed record CapturedLogEntry(LogLevel Level, string Message, string Values, string? Exception)
{
    public string Everything => Message + "|" + Values + "|" + Exception;
}

/// <summary>Captures every entry of every level, Trace included: the strictest view of what a log sink could see.</summary>
public sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly object _gate = new();

    public List<CapturedLogEntry> Entries { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var values = state is IEnumerable<KeyValuePair<string, object?>> pairs
            ? string.Join(";", pairs.Select(p => p.Key + "=" + p.Value))
            : state?.ToString() ?? string.Empty;
        lock (_gate)
        {
            Entries.Add(new CapturedLogEntry(logLevel, formatter(state, exception), values, exception?.ToString()));
        }
    }
}

/// <summary>Everything an adapter test needs, wired as production wires it, minus the network.</summary>
public sealed class PdfHarness
{
    public const string BaseUrl = "https://us1.pdfgeneratorapi.com/api/v4/";
    public const string Key = "qq-api-key-7f3e9a51";
    public const string Secret = "qq-api-secret-41c8d2b6e0aa";

    public static readonly DateTimeOffset Start = new(2026, 10, 21, 9, 30, 0, TimeSpan.Zero);

    public PdfHarness(Action<PdfGeneratorApiOptions>? configure = null)
    {
        Options = new PdfGeneratorApiOptions { Key = Key, Secret = Secret };
        configure?.Invoke(Options);
        Time = new ManualTimeProvider(Start);
        Delay = new RecordingRetryDelay(Time);
    }

    public ScriptedPdfHandler Handler { get; } = new();

    public PdfGeneratorApiOptions Options { get; }

    public ManualTimeProvider Time { get; }

    public RecordingRetryDelay Delay { get; }

    public FakeCurrentTenant CurrentTenant { get; } = new();

    public FakeCorrelationIdProvider CorrelationIds { get; } = new();

    public CapturingLogger<PdfGeneratorApiClient> Log { get; } = new();

    public TimeSpan Timeout { get; set; } = PdfGeneratorApiClient.Timeout;

    public PdfGeneratorApiClient Client() => new(
        new HttpClient(Handler, disposeHandler: false) { BaseAddress = new Uri(BaseUrl), Timeout = Timeout },
        Microsoft.Extensions.Options.Options.Create(Options),
        CurrentTenant,
        Time,
        Delay,
        CorrelationIds,
        Microsoft.Extensions.Options.Options.Create(new AbpCorrelationIdOptions()),
        Log);

    public PdfGeneratorApiRenderingProvider Provider() => new(Client());

    public PdfGeneratorApiHealthCheck HealthCheck() => new(Client());

    /// <summary>The decoded JWT header and payload of a recorded request.</summary>
    public static (JsonElement Header, JsonElement Payload, string Signature) Decode(string token)
    {
        var parts = token.Split('.');
        parts.Length.ShouldBe(3, "a JWT has three parts");
        return (Part(parts[0]), Part(parts[1]), parts[2]);

        static JsonElement Part(string part) =>
            JsonDocument.Parse(System.Buffers.Text.Base64Url.DecodeFromChars(part)).RootElement.Clone();
    }
}
