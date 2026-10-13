using System;
using System.Collections.Generic;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using OpenTms.Integrations.AddressLookup;
using Volo.Abp.Modularity;

namespace OpenTms.EntityFrameworkCore.Integrations;

/// <summary>
/// The SQLite test application with the outbound HTTP transport replaced by the fake of design 44's
/// test plan and a log capture for the "no address data in the log" rule (constitution rule 5).
/// Everything else — the feature interceptor, the distributed cache, the provider registration and
/// order, the options binding — is the production wiring.
/// </summary>
[DependsOn(typeof(OpenTmsEntityFrameworkCoreTestModule))]
public class AddressLookupTestModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var transport = new FakeHttpMessageHandler();
        var factory = new FakeHttpClientFactory(transport);
        context.Services.AddSingleton(transport);
        context.Services.AddSingleton(factory);
        context.Services.Replace(ServiceDescriptor.Singleton<IHttpClientFactory>(factory));

        var log = new CapturingLog();
        context.Services.AddSingleton(log);
        context.Services.AddLogging(logging => logging.AddProvider(log));
    }
}

/// <summary>One captured log entry: the formatted message plus the exception text, as a log sink would see them.</summary>
public sealed record LogEntry(LogLevel Level, string Category, string Message, string? Exception)
{
    public string Everything => Message + (Exception ?? string.Empty);
}

/// <summary>Captures every entry at Warning and above for the lifetime of one test application.</summary>
public sealed class CapturingLog : ILoggerProvider
{
    private readonly object _gate = new();

    public List<LogEntry> Entries { get; } = new();

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    public void Dispose()
    {
    }

    private void Add(LogEntry entry)
    {
        lock (_gate)
        {
            Entries.Add(entry);
        }
    }

    private sealed class Logger : ILogger
    {
        private readonly CapturingLog _owner;
        private readonly string _category;

        public Logger(CapturingLog owner, string category)
        {
            _owner = owner;
            _category = category;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                _owner.Add(new LogEntry(logLevel, _category, formatter(state, exception), exception?.ToString()));
            }
        }
    }
}
