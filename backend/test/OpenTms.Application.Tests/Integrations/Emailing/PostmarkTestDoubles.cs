using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenTms.Platform.Emailing;

namespace OpenTms.Integrations.Emailing.Postmark;

/// <summary>A transport that answers from a script and records every request with its body.</summary>
public sealed class ScriptedPostmarkHandler : HttpMessageHandler
{
    public List<(HttpRequestMessage Request, string? Body)> Requests { get; } = new();

    public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = _ => Json(HttpStatusCode.OK,
        """{"To":"x","SubmittedAt":"2026-10-30T10:00:00Z","MessageID":"b7bc2f4a-e38e-4336-af7d-e6c392c2f817","ErrorCode":0,"Message":"OK"}""");

    public Exception? Throw { get; set; }

    public static HttpResponseMessage Json(HttpStatusCode status, string json)
        => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request, body));
        if (Throw != null)
        {
            throw Throw;
        }

        return Respond(request);
    }
}

/// <summary>Captures every log line of any level as one string.</summary>
public sealed class CapturingLogger<T> : ILogger<T>
{
    public List<string> Lines { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var values = state is IEnumerable<KeyValuePair<string, object?>> pairs ? string.Join(";", pairs) : string.Empty;
        Lines.Add(logLevel + "|" + formatter(state, exception) + "|" + values + "|" + exception);
    }
}

/// <summary>A status sink that records the reports it is handed.</summary>
public sealed class RecordingEmailStatusSink : IEmailStatusSink
{
    public List<EmailDeliveryReport> Reports { get; } = new();

    public bool Matches { get; set; } = true;

    public Task<bool> RecordAsync(EmailDeliveryReport report)
    {
        Reports.Add(report);
        return Task.FromResult(Matches);
    }
}

/// <summary>A setting provider over a dictionary: the host's e-mail settings as a test sets them.</summary>
public sealed class DictionarySettingProvider : Volo.Abp.Settings.ISettingProvider
{
    public Dictionary<string, string?> Values { get; } = new();

    public Task<string?> GetOrNullAsync(string name) => Task.FromResult(Values.TryGetValue(name, out var value) ? value : null);

    public Task<List<Volo.Abp.Settings.SettingValue>> GetAllAsync(string[] names)
        => Task.FromResult(names.Select(n => new Volo.Abp.Settings.SettingValue(n, Values.GetValueOrDefault(n))).ToList());

    public Task<List<Volo.Abp.Settings.SettingValue>> GetAllAsync()
        => Task.FromResult(Values.Select(v => new Volo.Abp.Settings.SettingValue(v.Key, v.Value)).ToList());
}

public static class PostmarkTestSettings
{
    public static PostmarkSettingsResolver Resolver(PostmarkOptions configuration, DictionarySettingProvider? settings = null)
        => new(settings ?? new DictionarySettingProvider(),
            new Volo.Abp.MultiTenancy.CurrentTenant(Volo.Abp.MultiTenancy.AsyncLocalCurrentTenantAccessor.Instance),
            Microsoft.Extensions.Options.Options.Create(configuration));
}
