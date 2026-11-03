using System;
using System.Threading;
using System.Threading.Tasks;
using OpenTms.Orders.Intake;

namespace OpenTms.Integrations.OrderIntake;

/// <summary>
/// The import pipeline (design 72 "The pipeline", steps 1–6): extract, fingerprint, match a profile or ask the AI (one call
/// per message), map to <see cref="OrderIntakeDto"/>s with provenance, and submit each to Orders'
/// <c>IOrderIntakeService</c>, writing the step log on the way and rolling up the message's status. Runs in the
/// message's tenant and in the caller's transaction; re-running a message or document creates no intake twice.
/// </summary>
public interface IInboundMessageProcessor
{
    /// <summary>Processes every Pending document of the message (the pipeline job; also a reprocessed document).</summary>
    Task ProcessAsync(Guid inboundMessageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The intake API's JSON path, in the request: the recorded message's one ApiJson document is already the canonical
    /// model (<paramref name="order"/>), so no extraction, profile or AI runs (<c>MappingMethod.Canonical</c>); returns what
    /// Orders made of it.
    /// </summary>
    Task<IntakeResultDto> ProcessCanonicalAsync(Guid inboundMessageId, OrderIntakeDto order, CancellationToken cancellationToken = default);
}
