using System;
using System.Threading;
using System.Threading.Tasks;

namespace OpenTms.Integrations.DocumentRendering.PdfGeneratorApi;

/// <summary>The wait between two attempts of an external call; tests replace it so they need no real waits.</summary>
public interface IRetryDelay
{
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

/// <summary>The real wait, cancelled with the caller's token.</summary>
public class TaskRetryDelay : IRetryDelay
{
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.Delay(delay, cancellationToken);
}
