using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace OpenTms.EntityFrameworkCore;

/// <summary>
/// Captures the SQL text of every EF Core command executed in the current async flow while the capture is open, through
/// EF Core's DiagnosticListener ("Microsoft.EntityFrameworkCore", CommandExecuting). Only commands of this flow are kept
/// (AsyncLocal), so test classes running in parallel do not leak into each other. Works on SQLite and PostgreSQL alike;
/// nothing in the application is replaced. Open it with <see cref="Begin"/> (a synchronous call, so the AsyncLocal
/// flows into the caller) before the call under test.
/// </summary>
/// <remarks>
/// EF Core caches "is logging enabled" per context for up to one second (the logging cache time): a context that already
/// ran a command before the capture opened may skip the first second. Tests that reuse one context (a rolled-back
/// transaction) wait <see cref="LoggingCacheTime"/> after opening; each test also asserts a control command it must see.
/// </remarks>
public sealed class EfCommandCapture : IDisposable
{
    public static readonly TimeSpan LoggingCacheTime = TimeSpan.FromMilliseconds(1_100);

    private static readonly AsyncLocal<EfCommandCapture?> Active = new();

    private readonly ConcurrentQueue<string> _commands = new();
    private readonly List<IDisposable> _subscriptions = [];
    private readonly IDisposable _allListeners;
    private readonly EfCommandCapture? _previous;

    private EfCommandCapture()
    {
        _previous = Active.Value;
        Active.Value = this;
        _allListeners = DiagnosticListener.AllListeners.Subscribe(new Observer<DiagnosticListener>(OnListener));
    }

    public static EfCommandCapture Begin() => new();

    /// <summary>The captured command texts, in execution order.</summary>
    public IReadOnlyList<string> Commands => _commands.ToList();

    /// <summary>The captured commands that read from or write to <paramref name="table"/> (quoted, as EF Core writes it).</summary>
    public IReadOnlyList<string> On(string table) => Commands.Where(c => c.Contains($"\"{table}\"", StringComparison.Ordinal)).ToList();

    /// <summary>Forgets what was captured so far (the capture stays open).</summary>
    public void Clear() => _commands.Clear();

    public void Dispose()
    {
        _allListeners.Dispose();
        lock (_subscriptions)
        {
            foreach (var subscription in _subscriptions)
            {
                subscription.Dispose();
            }

            _subscriptions.Clear();
        }

        Active.Value = _previous;
    }

    private void OnListener(DiagnosticListener listener)
    {
        if (listener.Name != DbLoggerCategory.Name)
        {
            return;
        }

        var subscription = listener.Subscribe(
            new Observer<KeyValuePair<string, object?>>(OnEvent),
            name => name == RelationalEventId.CommandExecuting.Name);
        lock (_subscriptions)
        {
            _subscriptions.Add(subscription);
        }
    }

    private void OnEvent(KeyValuePair<string, object?> e)
    {
        if (ReferenceEquals(Active.Value, this) && e.Value is CommandEventData data)
        {
            _commands.Enqueue(data.Command.CommandText);
        }
    }

    private sealed class Observer<T>(Action<T> onNext) : IObserver<T>
    {
        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(T value) => onNext(value);
    }
}
