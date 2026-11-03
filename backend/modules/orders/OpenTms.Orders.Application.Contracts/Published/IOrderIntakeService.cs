using System;
using System.Threading;
using System.Threading.Tasks;
using OpenTms.Orders.Intake;

namespace OpenTms.Orders.Published;

/// <summary>
/// The order intake's published surface for Integrations' import pipeline and intake API (design 72 "Domain impact",
/// module boundary rule 3: synchronous calls 1–3). In-process only, called inside the caller's unit of work and tenant;
/// tenant-filtered like every repository read. Becomes an HTTP call to the Api role once the Agents role is split off
/// (AP-04).
/// </summary>
public interface IOrderIntakeService
{
    /// <summary>
    /// Records the intake, resolves it against master data and creates the order (Draft, or Confirmed when trusted,
    /// auto-confirmed and fully resolved), or leaves it NeedsReview. Idempotent on (SourceDocumentId, IntakeIndex): the
    /// intake Orders already holds is returned unchanged.
    /// </summary>
    Task<OrderIntakeSubmissionResultDto> SubmitAsync(OrderIntakeSubmissionDto input, CancellationToken cancellationToken = default);

    /// <summary>The values a clerk corrected, to repair an import profile; null when the intake is unknown or was not corrected.</summary>
    Task<OrderIntakeDto?> GetReviewedPayloadAsync(Guid intakeId, CancellationToken cancellationToken = default);

    /// <summary>The names the AI may choose from: order types, waypoint types with their activity, transport units.</summary>
    Task<IntakeLearningContextDto> GetLearningContextAsync(CancellationToken cancellationToken = default);
}
