using System;
using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging;

namespace OpenTms.PostgreSql;

/// <summary>
/// Keeps error-level log entries (with their exceptions) so a failing test-database set-up can show
/// what the DbMigrator service logged instead of only the outer exception message.
/// </summary>
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    private static readonly ConcurrentQueue<string> Entries = new();

    public static string Drain()
    {
        var builder = new StringBuilder();
        while (Entries.TryDequeue(out var entry))
        {
            builder.AppendLine(entry);
        }

        return builder.ToString();
    }

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger : ILogger
    {
        private readonly string _category;

        public CapturingLogger(string category)
        {
            _category = category;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            Entries.Enqueue($"[{logLevel}] {_category}: {formatter(state, exception)}{(exception != null ? Environment.NewLine + exception : string.Empty)}");
            while (Entries.Count > 50 && Entries.TryDequeue(out _))
            {
            }
        }
    }
}
