using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using OpenTms.Integrations.DocumentRendering.PdfGeneratorApi;
using OpenTms.Platform;
using OpenTms.Platform.DocumentRendering;
using OpenTms.Platform.ReportDefinitions;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;

namespace OpenTms.EntityFrameworkCore.Platform.DocumentRendering;

/// <summary>
/// The SQLite test application of design 55 with the rendering provider replaced by <see cref="FakeReportRenderingProvider"/>
/// (the pdfgeneratorapi adapter is removed, so nothing can reach the provider) and an authorization service that refuses
/// the policies a test names (the base test module allows everything). Everything else is the production wiring: the
/// feature interceptor and checker, the tenant filter, the localization.
/// </summary>
[DependsOn(typeof(OpenTmsEntityFrameworkCoreTestModule))]
public class ReportRenderingTestModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.RemoveAll<IReportRenderingProvider>();
        var provider = new FakeReportRenderingProvider();
        context.Services.AddSingleton(provider);
        context.Services.AddSingleton<IReportRenderingProvider>(provider);

        context.Services.AddSingleton<DenyingAuthorizationService>();
        context.Services.Replace(ServiceDescriptor.Singleton<IAuthorizationService>(s => s.GetRequiredService<DenyingAuthorizationService>()));
        context.Services.Replace(ServiceDescriptor.Singleton<IAbpAuthorizationService>(s => s.GetRequiredService<DenyingAuthorizationService>()));

        var log = new AllLevelsLog();
        context.Services.AddSingleton(log);
        context.Services.AddLogging(logging => logging.AddProvider(log));
    }
}

/// <summary>
/// The SQLite test application with the real pdfgeneratorapi adapter, its typed client and IHttpClientFactory pipeline
/// (logging handlers included), but the primary handler replaced by <see cref="ScriptedPdfHandler"/> and the base URL
/// pointed at a reserved ".invalid" host: even a wiring mistake cannot reach FiLogic's production account. Key and secret
/// are test values; every log line of every level and category is captured.
/// </summary>
[DependsOn(typeof(OpenTmsEntityFrameworkCoreTestModule))]
public class PdfGeneratorApiPipelineTestModule : AbpModule
{
    public const string Key = "qq-pipeline-key-5d1e";
    public const string Secret = "qq-pipeline-secret-9b7c33f0";
    public const string BaseUrl = "https://pdfgeneratorapi.invalid/api/v4/";

    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.Configure<PdfGeneratorApiOptions>(options =>
        {
            options.BaseUrl = BaseUrl;
            options.Key = Key;
            options.Secret = Secret;
        });

        var handler = new ScriptedPdfHandler();
        context.Services.AddSingleton(handler);
        context.Services.AddHttpClient<PdfGeneratorApiClient>().ConfigurePrimaryHttpMessageHandler(() => handler);

        var time = new ManualTimeProvider(new DateTimeOffset(2026, 10, 21, 9, 30, 0, TimeSpan.Zero));
        var delay = new RecordingRetryDelay(time);
        context.Services.AddSingleton(delay);
        context.Services.Replace(ServiceDescriptor.Singleton<TimeProvider>(time));
        context.Services.Replace(ServiceDescriptor.Singleton<IRetryDelay>(delay));

        var log = new AllLevelsLog();
        context.Services.AddSingleton(log);
        context.Services.AddLogging(logging =>
        {
            logging.AddProvider(log);
            logging.SetMinimumLevel(LogLevel.Trace);
        });
    }
}

/// <summary>One call the fake provider received, with the tenant that was current.</summary>
public sealed record ProviderCall(string Operation, Guid? Tenant, string? TemplateId, object? Argument = null, string? Language = null);

/// <summary>A scriptable rendering provider that records every call and the tenant it ran in.</summary>
public sealed class FakeReportRenderingProvider : IReportRenderingProvider
{
    private readonly object _gate = new();

    public ICurrentTenant? CurrentTenant { get; set; }

    public List<ProviderCall> Calls { get; } = new();

    public ReportRenderingProvider Provider { get; set; } = ReportRenderingProvider.PdfGeneratorApi;

    public bool IsConfigured { get; set; } = true;

    public bool SupportsTemplateEditor { get; set; } = true;

    public bool SupportsTemplateCopy { get; set; } = true;

    /// <summary>What GetTemplateAsync answers; null means the provider does not know the template.</summary>
    public ProviderTemplate? Template { get; set; } = new() { Id = "tpl-1", Name = "Factuur NL (provider)", OwnedByWorkspace = true };

    public Exception? GetTemplateFailure { get; set; }

    public JsonElement? SampleData { get; set; }

    public string EditorUrl { get; set; } = "https://us1.pdfgeneratorapi.com/editor/?token=QQ";

    public string CopiedTemplateId { get; set; } = "tpl-copy-77";

    public byte[] Document { get; set; } = "%PDF-1.7 fake"u8.ToArray();

    public string ContentType { get; set; } = "application/pdf";

    public RenderRequest? LastRender { get; private set; }

    private void Record(ProviderCall call)
    {
        lock (_gate)
        {
            Calls.Add(call);
        }
    }

    private Guid? Tenant => CurrentTenant?.Id;

    public Task<ReportRenderingProviderStatus> GetStatusAsync(CancellationToken ct)
    {
        Record(new ProviderCall("status", Tenant, null));
        return Task.FromResult(new ReportRenderingProviderStatus { IsConfigured = IsConfigured, Message = IsConfigured ? null : "not configured" });
    }

    public Task<RenderedDocument> RenderAsync(RenderRequest request, CancellationToken ct)
    {
        Record(new ProviderCall("render", Tenant, request.Templates.FirstOrDefault()?.TemplateId, request));
        LastRender = request;
        return Task.FromResult(new RenderedDocument { Content = Document, FileName = request.FileName, ContentType = ContentType });
    }

    public Task<string> GetTemplateEditorUrlAsync(string templateId, JsonElement? sampleData, string? language, CancellationToken ct)
    {
        Record(new ProviderCall("editor", Tenant, templateId, sampleData, language));
        return Task.FromResult(EditorUrl);
    }

    public Task<ProviderTemplate> CopyTemplateAsync(string templateId, string newName, CancellationToken ct)
    {
        Record(new ProviderCall("copy", Tenant, templateId, newName));
        return Task.FromResult(new ProviderTemplate { Id = CopiedTemplateId, Name = newName, OwnedByWorkspace = true });
    }

    public Task<ProviderTemplate?> GetTemplateAsync(string templateId, CancellationToken ct)
    {
        Record(new ProviderCall("get-template", Tenant, templateId));
        if (GetTemplateFailure != null)
        {
            throw GetTemplateFailure;
        }

        return Task.FromResult(Template);
    }

    public Task<JsonElement?> GetTemplateSampleDataAsync(string templateId, CancellationToken ct)
    {
        Record(new ProviderCall("sample-data", Tenant, templateId));
        return Task.FromResult(SampleData);
    }

    /// <summary>Calls that would reach the provider (the status is local by contract).</summary>
    public IEnumerable<ProviderCall> RemoteCalls => Calls.Where(c => c.Operation != "status");
}

/// <summary>
/// Allows everything except the policies of <see cref="Deny"/> for the current async flow: enough to exercise a
/// permission the service checks inside its body (CopyTemplateAsync checks Create on top of its [Authorize]).
/// </summary>
public sealed class DenyingAuthorizationService : IAbpAuthorizationService
{
    private static readonly AsyncLocal<ImmutableHashSet<string>?> Denied = new();

    public DenyingAuthorizationService(IServiceProvider serviceProvider)
    {
        ServiceProvider = serviceProvider;
    }

    public IServiceProvider ServiceProvider { get; }

    public ClaimsPrincipal CurrentPrincipal { get; } = new(new ClaimsIdentity());

    public static IDisposable Deny(params string[] policies)
    {
        var previous = Denied.Value;
        Denied.Value = (previous ?? ImmutableHashSet<string>.Empty).Union(policies);
        return new Restore(() => Denied.Value = previous);
    }

    private static bool IsDenied(string name) => Denied.Value?.Contains(name) == true;

    public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal? user, object? resource, IEnumerable<IAuthorizationRequirement> requirements)
    {
        var denied = requirements.OfType<PermissionRequirement>().Any(r => IsDenied(r.PermissionName))
                     || requirements.OfType<PermissionsRequirement>().Any(r => r.PermissionNames.Any(IsDenied));
        return Task.FromResult(denied ? AuthorizationResult.Failed() : AuthorizationResult.Success());
    }

    public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal? user, object? resource, string policyName)
        => Task.FromResult(IsDenied(policyName) ? AuthorizationResult.Failed() : AuthorizationResult.Success());

    private sealed class Restore(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }
}

/// <summary>One captured log entry of any level: category, formatted message, structured values and exception.</summary>
public sealed record AllLevelsLogEntry(LogLevel Level, string Category, string Message, string Values, string? Exception)
{
    public string Everything => Category + "|" + Message + "|" + Values + "|" + Exception;
}

/// <summary>Captures every entry of every level and category for the lifetime of one test application.</summary>
public sealed class AllLevelsLog : ILoggerProvider
{
    private readonly object _gate = new();

    public List<AllLevelsLogEntry> Entries { get; } = new();

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    public void Dispose()
    {
    }

    public List<AllLevelsLogEntry> Snapshot()
    {
        lock (_gate)
        {
            return Entries.ToList();
        }
    }

    private sealed class Logger(AllLevelsLog owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var values = state is IEnumerable<KeyValuePair<string, object?>> pairs
                ? string.Join(";", pairs.Select(p => p.Key + "=" + p.Value))
                : state?.ToString() ?? string.Empty;
            lock (owner._gate)
            {
                owner.Entries.Add(new AllLevelsLogEntry(logLevel, category, formatter(state, exception), values, exception?.ToString()));
            }
        }
    }
}
