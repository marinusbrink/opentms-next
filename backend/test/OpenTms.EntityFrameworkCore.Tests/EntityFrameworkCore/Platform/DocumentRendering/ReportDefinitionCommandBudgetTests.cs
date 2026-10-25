using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTms.Platform;
using OpenTms.Platform.DocumentRendering;
using OpenTms.Platform.ReportDefinitions;
using Shouldly;
using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Threading;
using Xunit;

namespace OpenTms.EntityFrameworkCore.Platform.DocumentRendering;

/// <summary>
/// The interactive command budget of docs/designs/55-report-rendering.md ("Command budget", PO 2026-10-23): each
/// interactive command of the report definition service (rendering status, open editor, copy template, test render)
/// shares ONE budget across all its provider calls, configured here as 200 ms through
/// <c>services.Configure&lt;DocumentRenderingOptions&gt;</c>. Running out fails as DocumentRenderingFailed with "did not
/// answer in time" (nl/en) and a warning carrying the definition and tenant id, nothing else; for the advisory status the
/// text becomes the status message. The request's own cancellation passes through as OperationCanceledException. A zero
/// or negative setting falls back to the 20 s default instead of cancelling every command. Risk class: High (the design's
/// resilience row); the tenant id in the log line is the tenant-scoping obligation of constitution rule 1/5. SQLite with
/// a provider that waits on the token it is given; no provider is reached.
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class ReportDefinitionCommandBudgetTests : ReportRenderingTestBase<ReportRenderingBudgetTestModule>
{
    private const string DefinitionName = "QQ Budget Factuur Jansen";
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(15);

    private readonly StallingReportRenderingProvider _provider;

    public ReportDefinitionCommandBudgetTests()
    {
        _provider = GetRequiredService<StallingReportRenderingProvider>();
    }

    private Task Run(string command, Guid id) => command switch
    {
        "editor" => Definitions.OpenTemplateEditorAsync(id),
        "copy" => Definitions.CopyTemplateAsync(id),
        "test-render" => Definitions.TestRenderAsync(id),
        "status" => Definitions.GetRenderingStatusAsync(id),
        _ => throw new ArgumentOutOfRangeException(nameof(command))
    };

    /// <summary>The provider call each command stalls on (the last call it makes).</summary>
    private static string StalledCall(string command) => command switch
    {
        "editor" => "editor",
        "copy" => "copy",
        "test-render" => "render",
        "status" => "get-template",
        _ => throw new ArgumentOutOfRangeException(nameof(command))
    };

    private static string TimedOut(string culture) => culture == "nl"
        ? "de opmaakdienst antwoordde niet op tijd."
        : "the document service did not answer in time.";

    private List<AllLevelsLogEntry> BudgetWarnings() => GetRequiredService<AllLevelsLog>().Snapshot()
        .Where(e => e.Level == LogLevel.Warning && e.Message.Contains("command budget", StringComparison.Ordinal))
        .ToList();

    private void SetBudget(TimeSpan budget) => GetRequiredService<IOptions<DocumentRenderingOptions>>().Value.InteractiveCommandBudget = budget;

    [Fact]
    public void The_test_module_configures_a_200_ms_budget()
    {
        GetRequiredService<IOptions<DocumentRenderingOptions>>().Value.InteractiveCommandBudget.ShouldBe(TimeSpan.FromMilliseconds(200));
    }

    [Theory]
    [InlineData("editor", "en")]
    [InlineData("editor", "nl")]
    [InlineData("copy", "en")]
    [InlineData("copy", "nl")]
    [InlineData("test-render", "en")]
    [InlineData("test-render", "nl")]
    public async Task A_command_that_runs_out_of_budget_fails_as_DocumentRenderingFailed_with_the_timed_out_text(string command, string culture)
    {
        using var _ = InTenantA();
        using var on = RenderingOn();
        using var ui = new CultureScope(culture);
        var definition = await CreateDefinitionAsync(name: DefinitionName);
        _provider.Stall(StalledCall(command));

        var watch = Stopwatch.StartNew();
        var ex = await ShouldFailAsync(() => Run(command, definition.Id).WaitAsync(Guard), PlatformErrorCodes.DocumentRenderingFailed);
        watch.Stop();

        ex.Data["message"].ShouldBe(TimedOut(culture));
        watch.Elapsed.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(150), "the budget, not an immediate failure");
        _provider.TokenOf(StalledCall(command)).IsCancellationRequested.ShouldBeTrue("the stalled call got the budget's token");

        var warning = BudgetWarnings().ShouldHaveSingleItem();
        warning.Values.ShouldContain("ReportDefinitionId=" + definition.Id);
        warning.Values.ShouldContain("TenantId=" + TenantA);
        foreach (var entry in GetRequiredService<AllLevelsLog>().Snapshot())
        {
            entry.Everything.ShouldNotContain(DefinitionName, Case.Insensitive, "no names in the logs: " + entry.Message);
            entry.Everything.ShouldNotContain(_provider.TemplateName, Case.Insensitive, "no template names in the logs: " + entry.Message);
        }

        if (command == "copy")
        {
            (await Definitions.GetListAsync(new ReportDefinitionListRequestDto { StartRow = 0, EndRow = 100, IncludeArchived = true })).TotalCount
                .ShouldBe(1, "a copy that ran out of budget writes no definition");
        }
    }

    [Theory]
    [InlineData("editor")]
    [InlineData("test-render")]
    public async Task The_budget_is_shared_by_all_provider_calls_of_one_command(string command)
    {
        /* Each call alone fits in 200 ms; together they do not. Editor: template lookup, template data, editor URL.
         * Test render: template data, render. */
        using var _ = InTenantA();
        using var on = RenderingOn();
        using var ui = new CultureScope("en");
        var definition = await CreateDefinitionAsync();
        var calls = command == "editor" ? new[] { "get-template", "sample-data", "editor" } : new[] { "sample-data", "render" };
        foreach (var call in calls)
        {
            _provider.Stall(call, TimeSpan.FromMilliseconds(150));
        }

        var ex = await ShouldFailAsync(() => Run(command, definition.Id).WaitAsync(Guard), PlatformErrorCodes.DocumentRenderingFailed);

        ex.Data["message"].ShouldBe(TimedOut("en"));
        BudgetWarnings().ShouldHaveSingleItem();
    }

    [Theory]
    [InlineData("editor")]
    [InlineData("copy")]
    [InlineData("test-render")]
    [InlineData("status")]
    public async Task A_command_whose_calls_fit_the_budget_succeeds(string command)
    {
        using var _ = InTenantA();
        using var on = RenderingOn();
        var definition = await CreateDefinitionAsync();
        _provider.Stall(StalledCall(command), TimeSpan.FromMilliseconds(30));

        await Run(command, definition.Id).WaitAsync(Guard);

        BudgetWarnings().ShouldBeEmpty();
    }

    [Theory]
    [InlineData("en", "The rendering provider could not handle the request: the document service did not answer in time.")]
    [InlineData("nl", "De opmaakdienst kon het verzoek niet verwerken: de opmaakdienst antwoordde niet op tijd.")]
    public async Task The_status_turns_the_budget_failure_into_its_message(string culture, string message)
    {
        using var _ = InTenantA();
        using var on = RenderingOn();
        using var ui = new CultureScope(culture);
        var definition = await CreateDefinitionAsync();
        _provider.Stall("get-template");

        var status = await Definitions.GetRenderingStatusAsync(definition.Id).WaitAsync(Guard);

        status.Message.ShouldBe(message);
        status.ProviderAvailable.ShouldBeTrue();
        status.ProviderConfigured.ShouldBeTrue();
        status.FeatureEnabled.ShouldBeTrue();
        status.TemplateFound.ShouldBeNull("the template is unknown, not 'not found'");
        status.TemplateName.ShouldBeNull();
        var warning = BudgetWarnings().ShouldHaveSingleItem();
        warning.Values.ShouldContain("ReportDefinitionId=" + definition.Id);
        warning.Values.ShouldContain("TenantId=" + TenantA);
    }

    [Theory]
    [InlineData("editor")]
    [InlineData("copy")]
    [InlineData("test-render")]
    [InlineData("status")]
    public async Task The_requests_own_cancellation_passes_through_as_OperationCanceledException(string command)
    {
        SetBudget(TimeSpan.FromSeconds(30));
        using var _ = InTenantA();
        using var on = RenderingOn();
        var definition = await CreateDefinitionAsync();
        var stalled = _provider.Stall(StalledCall(command));
        using var request = new CancellationTokenSource();

        /* The request's token, as ASP.NET Core's provider gives it; the scope stays open for the whole command. */
        using var scope = GetRequiredService<ICancellationTokenProvider>().Use(request.Token);
        var call = Run(command, definition.Id);
        await stalled.Task.WaitAsync(Guard);
        await request.CancelAsync();

        var ex = await Should.ThrowAsync<Exception>(() => call.WaitAsync(Guard));
        ex.ShouldBeAssignableTo<OperationCanceledException>($"{command}: the browser went away; not a provider failure ({ex.GetType().Name}: {ex.Message})");
        BudgetWarnings().ShouldBeEmpty("nothing ran out of budget");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-5000)]
    public async Task A_zero_or_negative_budget_falls_back_to_the_default_instead_of_cancelling_every_command(int milliseconds)
    {
        SetBudget(TimeSpan.FromMilliseconds(milliseconds));
        using var _ = InTenantA();
        using var on = RenderingOn();
        var definition = await CreateDefinitionAsync();
        _provider.Stall("sample-data", TimeSpan.FromMilliseconds(250));
        _provider.Stall("render", TimeSpan.FromMilliseconds(250));
        _provider.Stall("get-template", TimeSpan.FromMilliseconds(250));

        var file = await Definitions.TestRenderAsync(definition.Id).WaitAsync(Guard);
        var status = await Definitions.GetRenderingStatusAsync(definition.Id).WaitAsync(Guard);

        file.FileName.ShouldEndWith(".pdf");
        status.Message.ShouldBeNull();
        status.TemplateFound.ShouldBe(true);
        BudgetWarnings().ShouldBeEmpty("well past 200 ms and nowhere near the 20 s default");
    }
}

/// <summary>
/// The SQLite test application of design 55 with a 200 ms interactive command budget and the provider replaced by
/// <see cref="StallingReportRenderingProvider"/>.
/// </summary>
[DependsOn(typeof(ReportRenderingTestModule))]
public class ReportRenderingBudgetTestModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.Configure<DocumentRenderingOptions>(options => options.InteractiveCommandBudget = TimeSpan.FromMilliseconds(200));

        context.Services.RemoveAll<IReportRenderingProvider>();
        var provider = new StallingReportRenderingProvider();
        context.Services.AddSingleton(provider);
        context.Services.AddSingleton<IReportRenderingProvider>(provider);
    }
}

/// <summary>
/// A configured provider that answers at once unless told to wait on an operation: for a while, or until the token it
/// was given is cancelled. Records the token of every call.
/// </summary>
public sealed class StallingReportRenderingProvider : IReportRenderingProvider
{
    private readonly ConcurrentDictionary<string, TimeSpan> _waits = new();
    private readonly ConcurrentDictionary<string, CancellationToken> _tokens = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _reached = new();

    public string TemplateName => "QQ Provider Template Bakker";

    public ReportRenderingProvider Provider => ReportRenderingProvider.PdfGeneratorApi;

    public bool SupportsTemplateEditor => true;

    public bool SupportsTemplateCopy => true;

    /// <summary>Makes <paramref name="operation"/> wait (until its token is cancelled when no time is given).</summary>
    public TaskCompletionSource Stall(string operation, TimeSpan? wait = null)
    {
        _waits[operation] = wait ?? Timeout.InfiniteTimeSpan;
        return _reached.GetOrAdd(operation, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
    }

    public CancellationToken TokenOf(string operation) => _tokens[operation];

    private async Task CallAsync(string operation, CancellationToken ct)
    {
        _tokens[operation] = ct;
        if (_waits.TryGetValue(operation, out var wait))
        {
            _reached.GetOrAdd(operation, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult();
            await Task.Delay(wait, ct);
        }
    }

    public async Task<ReportRenderingProviderStatus> GetStatusAsync(CancellationToken ct)
    {
        await CallAsync("status", ct);
        return new ReportRenderingProviderStatus { IsConfigured = true };
    }

    public async Task<RenderedDocument> RenderAsync(RenderRequest request, CancellationToken ct)
    {
        await CallAsync("render", ct);
        return new RenderedDocument { Content = "%PDF-1.7 budget"u8.ToArray(), FileName = request.FileName, ContentType = "application/pdf" };
    }

    public async Task<string> GetTemplateEditorUrlAsync(string templateId, JsonElement? sampleData, string? language, CancellationToken ct)
    {
        await CallAsync("editor", ct);
        return "https://us1.pdfgeneratorapi.com/editor/?token=QQ";
    }

    public async Task<ProviderTemplate> CopyTemplateAsync(string templateId, string newName, CancellationToken ct)
    {
        await CallAsync("copy", ct);
        return new ProviderTemplate { Id = "tpl-copy-budget", Name = newName, OwnedByWorkspace = true };
    }

    public async Task<ProviderTemplate?> GetTemplateAsync(string templateId, CancellationToken ct)
    {
        await CallAsync("get-template", ct);
        return new ProviderTemplate { Id = templateId, Name = TemplateName, OwnedByWorkspace = true };
    }

    public async Task<JsonElement?> GetTemplateSampleDataAsync(string templateId, CancellationToken ct)
    {
        await CallAsync("sample-data", ct);
        return null;
    }
}
