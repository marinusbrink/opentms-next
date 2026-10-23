using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using OpenTms.Platform;
using OpenTms.Platform.DocumentRendering;
using OpenTms.Platform.Localization;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Tracing;
using Xunit;

namespace OpenTms.Integrations.DocumentRendering.PdfGeneratorApi;

/// <summary>
/// The two call modes of the pdfgeneratorapi client (docs/designs/50-report-rendering.md, "Resilience", PO 2026-10-23),
/// on a fake transport and a clock whose timers fire only when the test moves it, so the 15 s and 30 s limits are
/// asserted without real waits. <b>Interactive</b> (editor URL, copy, template lookup, owner list, template data,
/// health check, a render whose request says so): one attempt, a 15 s timer, a 429 is "busy" at once, a 5xx the
/// provider's text at once. <b>Background</b> (a render's default): 30 s per attempt, the 3/6/12/24 s retries.
/// <see cref="RenderRequest.CallMode"/> is honoured and never inferred from <see cref="RenderRequest.IsTest"/>. A
/// caller that goes away gets OperationCanceledException, not a provider failure. Risk class: High (external
/// integration, the design's "Retry/backoff, timeouts, error mapping" row). No request leaves the process.
/// </summary>
public class PdfGeneratorApiCallModeTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(10);

    private static JsonElement Data() =>
        JsonDocument.Parse("""{"Entity":{"EntityNumber":"F-2026-0001"},"EntityType":"Invoice"}""").RootElement.Clone();

    private static RenderRequest Render(RenderCallMode? mode = null, bool isTest = false) => mode == null
        ? new RenderRequest
        {
            Templates = [new RenderTemplate { TemplateId = "123456", Data = Data() }],
            Format = ReportRenderFormat.Pdf,
            FileName = "factuur-F-2026-0001.pdf",
            IsTest = isTest
        }
        : new RenderRequest
        {
            Templates = [new RenderTemplate { TemplateId = "123456", Data = Data() }],
            Format = ReportRenderFormat.Pdf,
            FileName = "factuur-F-2026-0001.pdf",
            IsTest = isTest,
            CallMode = mode.Value
        };

    private const string Generated = """{"response":"JVBERi0xLjc=","meta":{"content_type":"application/pdf"}}""";

    private static async Task<BusinessException> FailsWith(Func<Task> action, string code)
    {
        var ex = await Should.ThrowAsync<BusinessException>(action);
        ex.Code.ShouldBe(code, ex.Message);
        return ex;
    }

    // ── Interactive: one attempt ───────────────────────────────────────────────

    [Fact]
    public async Task An_interactive_render_makes_one_attempt_and_a_429_says_busy_without_a_retry()
    {
        var rig = new Rig();
        rig.Handler.Always(HttpStatusCode.TooManyRequests, """{"message":"Too many requests, slow down"}""");

        var ex = await FailsWith(() => rig.Provider().RenderAsync(Render(RenderCallMode.Interactive), CancellationToken.None), PlatformErrorCodes.DocumentRenderingFailed);

        ex.Data["message"].ShouldBe(PdfGeneratorApiClient.BusyMessage, "a user waits: 'try again in a moment', not the provider's text");
        ex.Data["status"].ShouldBe(429);
        rig.Handler.Requests.Count.ShouldBe(1);
        rig.Delay.Delays.ShouldBeEmpty();
        rig.Log.Entries.ShouldContain(e => e.Level == LogLevel.Warning && e.Values.Contains("CallMode=Interactive") && e.Values.Contains("StatusCode=429"));
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task An_interactive_5xx_fails_at_once_with_the_providers_text(HttpStatusCode status)
    {
        var rig = new Rig();
        rig.Handler.Always(status, """{"message":"Service temporarily unavailable"}""");

        var ex = await FailsWith(() => rig.Provider().RenderAsync(Render(RenderCallMode.Interactive), CancellationToken.None), PlatformErrorCodes.DocumentRenderingFailed);

        ex.Data["message"].ShouldBe("Service temporarily unavailable");
        ex.Data["status"].ShouldBe((int)status);
        rig.Handler.Requests.Count.ShouldBe(1, $"{(int)status} is not retried while a user waits");
        rig.Delay.Delays.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_interactive_call_waits_at_most_15_seconds_and_is_not_retried()
    {
        var rig = new Rig();
        var arrived = rig.StallEveryRequest();

        var call = rig.Provider().RenderAsync(Render(RenderCallMode.Interactive), CancellationToken.None);
        await arrived.Task.WaitAsync(Guard);
        rig.Time.DueTimes.ShouldBe([PdfGeneratorApiClient.InteractiveTimeout]);
        PdfGeneratorApiClient.InteractiveTimeout.ShouldBe(TimeSpan.FromSeconds(15));

        rig.Time.Advance(TimeSpan.FromSeconds(14.9));
        await Task.Delay(100);
        call.IsCompleted.ShouldBeFalse("14.9 s is still inside the interactive limit");

        rig.Time.Advance(TimeSpan.FromSeconds(0.1));
        var ex = await FailsWith(() => call.WaitAsync(Guard), PlatformErrorCodes.DocumentRenderingFailed);

        ex.Data["message"].ShouldBe("No answer within 15 seconds.");
        rig.Handler.Requests.Count.ShouldBe(1);
        rig.Delay.Delays.ShouldBeEmpty();
        rig.Log.Entries.ShouldContain(e => e.Level == LogLevel.Warning && e.Values.Contains("CallMode=Interactive") && e.Values.Contains("TimeoutSeconds=15"));
    }

    [Fact]
    public async Task A_background_attempt_waits_up_to_30_seconds()
    {
        var rig = new Rig();
        var arrived = rig.StallEveryRequest();

        var call = rig.Provider().RenderAsync(Render(), CancellationToken.None);
        await arrived.Task.WaitAsync(Guard);
        rig.Time.DueTimes.ShouldBe([PdfGeneratorApiClient.Timeout]);

        rig.Time.Advance(TimeSpan.FromSeconds(15));
        await Task.Delay(100);
        call.IsCompleted.ShouldBeFalse("the interactive limit does not apply to a background render");

        rig.Time.Advance(TimeSpan.FromSeconds(15));
        var ex = await FailsWith(() => call.WaitAsync(Guard), PlatformErrorCodes.DocumentRenderingFailed);

        ex.Data["message"].ShouldBe("No answer within 30 seconds.");
        rig.Handler.Requests.Count.ShouldBe(1, "a timeout is not retried");
    }

    [Fact]
    public async Task A_caller_that_goes_away_during_an_interactive_call_gets_OperationCanceledException_not_a_provider_failure()
    {
        var rig = new Rig();
        var arrived = rig.StallEveryRequest();
        using var caller = new CancellationTokenSource();

        var call = rig.Provider().RenderAsync(Render(RenderCallMode.Interactive), caller.Token);
        await arrived.Task.WaitAsync(Guard);
        await caller.CancelAsync();

        var ex = await Should.ThrowAsync<Exception>(() => call.WaitAsync(Guard));
        ex.ShouldBeAssignableTo<OperationCanceledException>();
        rig.Log.Entries.ShouldNotContain(e => e.Message.Contains("timed out", StringComparison.Ordinal), "the caller left; nothing timed out");
    }

    // ── Background: the retry schedule; the request's mode decides ─────────────

    [Fact]
    public void A_render_request_is_background_unless_it_says_otherwise()
    {
        new RenderRequest().CallMode.ShouldBe(RenderCallMode.Background);
        Render(isTest: true).CallMode.ShouldBe(RenderCallMode.Background, "never inferred from IsTest");
        ((int)RenderCallMode.Background).ShouldBe(0);
    }

    [Fact]
    public async Task A_background_render_keeps_the_3_6_12_24_second_retries()
    {
        var rig = new Rig();
        rig.Handler
            .ThenStatus(HttpStatusCode.TooManyRequests)
            .ThenStatus(HttpStatusCode.ServiceUnavailable)
            .ThenStatus(HttpStatusCode.TooManyRequests)
            .ThenStatus(HttpStatusCode.InternalServerError)
            .ThenJson(Generated);

        var document = await rig.Provider().RenderAsync(Render(RenderCallMode.Background), CancellationToken.None);

        document.ContentType.ShouldBe("application/pdf");
        rig.Handler.Requests.Count.ShouldBe(5);
        rig.Delay.Delays.ShouldBe([TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(24)]);
        rig.Time.DueTimes.ShouldAllBe(t => t == PdfGeneratorApiClient.Timeout);
        rig.Log.Entries.ShouldContain(e => e.Level == LogLevel.Warning && e.Values.Contains("CallMode=Background"));
    }

    [Fact]
    public async Task The_mode_follows_the_request_not_IsTest()
    {
        /* A test render without an explicit mode retries (background); a real render marked interactive does not. */
        var background = new Rig();
        background.Handler.ThenStatus(HttpStatusCode.TooManyRequests).ThenJson(Generated);
        await background.Provider().RenderAsync(Render(isTest: true), CancellationToken.None);
        background.Handler.Requests.Count.ShouldBe(2);
        background.Delay.Delays.ShouldBe([TimeSpan.FromSeconds(3)]);

        var interactive = new Rig();
        interactive.Handler.ThenStatus(HttpStatusCode.TooManyRequests).ThenJson(Generated);
        await FailsWith(() => interactive.Provider().RenderAsync(Render(RenderCallMode.Interactive, isTest: false), CancellationToken.None), PlatformErrorCodes.DocumentRenderingFailed);
        interactive.Handler.Requests.Count.ShouldBe(1);
        interactive.Delay.Delays.ShouldBeEmpty();
    }

    // ── Template operations and the health check: always interactive ──────────

    public static TheoryData<string> TemplateOperations => new() { "editor", "copy", "get-template", "owner-list", "sample-data" };

    private static Task RunOperation(PdfGeneratorApiRenderingProvider provider, string operation) => operation switch
    {
        "editor" => provider.GetTemplateEditorUrlAsync("2002", Data(), "nl", CancellationToken.None),
        "copy" => provider.CopyTemplateAsync("2002", "Factuur (copy)", CancellationToken.None),
        "get-template" or "owner-list" => provider.GetTemplateAsync("2002", CancellationToken.None),
        "sample-data" => provider.GetTemplateSampleDataAsync("2002", CancellationToken.None),
        _ => throw new ArgumentOutOfRangeException(nameof(operation))
    };

    [Theory]
    [MemberData(nameof(TemplateOperations))]
    public async Task Every_template_operation_is_one_interactive_attempt_and_a_429_says_busy(string operation)
    {
        var rig = new Rig();
        if (operation == "owner-list")
        {
            /* The single template lacks "owner": the list call decides, and it is interactive as well. */
            rig.Handler.ThenJson("""{"response":{"id":2002,"name":"Eigen factuur"}}""");
        }

        rig.Handler.Always(HttpStatusCode.TooManyRequests);

        var ex = await FailsWith(() => RunOperation(rig.Provider(), operation), PlatformErrorCodes.DocumentRenderingFailed);

        ex.Data["message"].ShouldBe(PdfGeneratorApiClient.BusyMessage);
        rig.Handler.Requests.Count.ShouldBe(operation == "owner-list" ? 2 : 1);
        rig.Delay.Delays.ShouldBeEmpty();
        rig.Time.DueTimes.ShouldAllBe(t => t == PdfGeneratorApiClient.InteractiveTimeout);
        rig.Log.Entries.Where(e => e.Level == LogLevel.Warning).ShouldAllBe(e => e.Values.Contains("CallMode=Interactive"));
    }

    [Theory]
    [MemberData(nameof(TemplateOperations))]
    public async Task Every_template_operation_fails_a_5xx_at_once(string operation)
    {
        var rig = new Rig();
        if (operation == "owner-list")
        {
            rig.Handler.ThenJson("""{"response":{"id":2002,"name":"Eigen factuur"}}""");
        }

        rig.Handler.Always(HttpStatusCode.BadGateway, """{"message":"Upstream down"}""");

        var ex = await FailsWith(() => RunOperation(rig.Provider(), operation), PlatformErrorCodes.DocumentRenderingFailed);

        ex.Data["message"].ShouldBe("Upstream down");
        rig.Handler.Requests.Count.ShouldBe(operation == "owner-list" ? 2 : 1);
        rig.Delay.Delays.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_health_check_is_one_interactive_attempt_that_reports_busy_on_a_429()
    {
        var rig = new Rig();
        rig.Handler.Always(HttpStatusCode.TooManyRequests);
        var check = new PdfGeneratorApiHealthCheck(rig.Client());

        var result = await check.CheckHealthAsync(Context(check));

        result.Status.ShouldBe(HealthStatus.Unhealthy);
        result.Description.ShouldBe($"pdfgeneratorapi: HTTP 429: {PdfGeneratorApiClient.BusyMessage}");
        rig.Handler.Requests.Count.ShouldBe(1);
        rig.Delay.Delays.ShouldBeEmpty();
        rig.Time.DueTimes.ShouldBe([PdfGeneratorApiClient.InteractiveTimeout]);
    }

    [Fact]
    public async Task The_health_check_gives_up_after_15_seconds()
    {
        var rig = new Rig();
        var arrived = rig.StallEveryRequest();
        var check = new PdfGeneratorApiHealthCheck(rig.Client());

        var call = check.CheckHealthAsync(Context(check));
        await arrived.Task.WaitAsync(Guard);
        rig.Time.Advance(PdfGeneratorApiClient.InteractiveTimeout);
        var result = await call.WaitAsync(Guard);

        result.Status.ShouldBe(HealthStatus.Unhealthy);
        result.Description.ShouldBe("pdfgeneratorapi: No answer within 15 seconds.");
        rig.Handler.Requests.Count.ShouldBe(1);
    }

    // ── The busy text ──────────────────────────────────────────────────────────

    [Fact]
    public async Task The_busy_text_comes_from_Platforms_localizer_and_falls_back_to_English_without_the_key()
    {
        var localized = new Rig { Localizer = new FakePlatformLocalizer(("Platform:DocumentRenderingBusy", "QQ bezet")) };
        localized.Handler.Always(HttpStatusCode.TooManyRequests);
        (await FailsWith(() => localized.Provider().RenderAsync(Render(RenderCallMode.Interactive), CancellationToken.None), PlatformErrorCodes.DocumentRenderingFailed))
            .Data["message"].ShouldBe("QQ bezet");

        var missing = new Rig { Localizer = new FakePlatformLocalizer() };
        missing.Handler.Always(HttpStatusCode.TooManyRequests);
        (await FailsWith(() => missing.Provider().RenderAsync(Render(RenderCallMode.Interactive), CancellationToken.None), PlatformErrorCodes.DocumentRenderingFailed))
            .Data["message"].ShouldBe(PdfGeneratorApiClient.BusyMessage);
    }

    private static HealthCheckContext Context(IHealthCheck check) => new()
    {
        Registration = new HealthCheckRegistration(PdfGeneratorApiHealthCheck.Name, check, HealthStatus.Unhealthy, [PdfGeneratorApiHealthCheck.Tag])
    };

    /// <summary>The client as production builds it (30 s HttpClient ceiling), on the stepped clock.</summary>
    private sealed class Rig
    {
        public ScriptedPdfHandler Handler { get; } = new();

        public SteppedTimeProvider Time { get; } = new(PdfHarness.Start);

        public RecordingRetryDelay Delay { get; } = new(new ManualTimeProvider(PdfHarness.Start));

        public CapturingLogger<PdfGeneratorApiClient> Log { get; } = new();

        public IStringLocalizer<PlatformResource>? Localizer { get; init; }

        public PdfGeneratorApiClient Client() => new(
            new HttpClient(Handler, disposeHandler: false) { BaseAddress = new Uri(PdfHarness.BaseUrl), Timeout = PdfGeneratorApiClient.Timeout },
            Microsoft.Extensions.Options.Options.Create(new PdfGeneratorApiOptions { Key = PdfHarness.Key, Secret = PdfHarness.Secret }),
            new FakeCurrentTenant(),
            Time,
            Delay,
            new FakeCorrelationIdProvider(),
            Microsoft.Extensions.Options.Options.Create(new AbpCorrelationIdOptions()),
            Log,
            Localizer);

        public PdfGeneratorApiRenderingProvider Provider() => new(Client());

        /// <summary>Every request waits until its token is cancelled; the returned source completes when one arrives.</summary>
        public TaskCompletionSource StallEveryRequest()
        {
            var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Handler.Fallback = async (_, ct) =>
            {
                arrived.TrySetResult();
                await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, ct);
                throw new InvalidOperationException("unreachable");
            };
            return arrived;
        }
    }
}

/// <summary>
/// A clock whose timers fire only when <see cref="Advance"/> moves it past their due time; records the due time of every
/// timer created (the client creates one per attempt through <c>new CancellationTokenSource(timeout, timeProvider)</c>).
/// </summary>
public sealed class SteppedTimeProvider : TimeProvider
{
    private readonly object _gate = new();
    private readonly List<SteppedTimer> _timers = new();
    private readonly List<TimeSpan> _dueTimes = new();
    private DateTimeOffset _now;

    public SteppedTimeProvider(DateTimeOffset start)
    {
        _now = start;
    }

    public IReadOnlyList<TimeSpan> DueTimes
    {
        get
        {
            lock (_gate)
            {
                return _dueTimes.ToList();
            }
        }
    }

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            return _now;
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new SteppedTimer(this, callback, state);
        lock (_gate)
        {
            _dueTimes.Add(dueTime);
            _timers.Add(timer);
            timer.Arm(dueTime);
        }

        return timer;
    }

    public void Advance(TimeSpan by)
    {
        List<SteppedTimer> due;
        lock (_gate)
        {
            _now += by;
            due = _timers.Where(t => t.Due is { } at && at <= _now).ToList();
            foreach (var timer in due)
            {
                timer.Due = null;
            }
        }

        foreach (var timer in due)
        {
            timer.Fire();
        }
    }

    private void Remove(SteppedTimer timer)
    {
        lock (_gate)
        {
            _timers.Remove(timer);
        }
    }

    private sealed class SteppedTimer(SteppedTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        public DateTimeOffset? Due { get; set; }

        public void Arm(TimeSpan dueTime) => Due = dueTime == global::System.Threading.Timeout.InfiniteTimeSpan ? null : owner._now + dueTime;

        public void Fire() => callback(state);

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (owner._gate)
            {
                Arm(dueTime);
            }

            return true;
        }

        public void Dispose() => owner.Remove(this);

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>Platform's localizer with only the given texts; any other key is ResourceNotFound.</summary>
public sealed class FakePlatformLocalizer(params (string Key, string Value)[] texts) : IStringLocalizer<PlatformResource>
{
    private readonly Dictionary<string, string> _texts = texts.ToDictionary(t => t.Key, t => t.Value);

    public LocalizedString this[string name] => _texts.TryGetValue(name, out var value)
        ? new LocalizedString(name, value)
        : new LocalizedString(name, name, resourceNotFound: true);

    public LocalizedString this[string name, params object[] arguments] => this[name];

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => _texts.Select(t => new LocalizedString(t.Key, t.Value));
}
