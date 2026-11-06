using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OpenTms.PlanningExecution.TransportTrips;

namespace OpenTms.PlanningExecution.Published;

/// <summary>
/// What Reporting prepares its trip facts from (design 75, module boundary rule 3). In-process only, tenant-filtered, reads
/// stored values and never calculates; it has no permission of its own, because its callers are a derived-value job, two
/// workers and <c>IMetricQuery</c>, which authorizes itself.
/// </summary>
public interface ITripFactReader
{
    public const int MaxKeys = 500;

    /// <summary>The trip's current values; null when it does not exist or is deleted, so the fact goes too.</summary>
    Task<TripFactSourceDto?> GetAsync(Guid tripId, CancellationToken cancellationToken = default);

    /// <summary>All trips that are not deleted: a backfill is finished when the facts count the same.</summary>
    Task<long> GetCountAsync(CancellationToken cancellationToken = default);

    /// <summary>The next ids in id order, after <paramref name="afterId"/>; deleted trips are left out.</summary>
    Task<List<Guid>> GetIdsAfterAsync(Guid? afterId, int take, CancellationToken cancellationToken = default);

    /// <summary>
    /// The ids of trips created, changed or deleted since <paramref name="since"/>, in id order. Soft-deleted trips are
    /// included, so a lost delete can be repaired: the calculator finds no trip and removes the fact.
    /// </summary>
    Task<List<Guid>> GetIdsModifiedSinceAsync(DateTime since, Guid? afterId, int take, CancellationToken cancellationToken = default);

    /// <summary>The trip types' names in the current culture; unknown ids are absent. At most <see cref="MaxKeys"/>.</summary>
    Task<IReadOnlyDictionary<Guid, string>> GetTypeNamesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);
}

public class TripFactSourceDto
{
    public Guid Id { get; set; }

    public DateOnly TripDate { get; set; }

    public TransportTripStatus Status { get; set; }

    public TripExecution Execution { get; set; }

    public Guid? CarrierRelationId { get; set; }

    /// <summary>The first vehicle by position.</summary>
    public Guid? PrimaryVehicleId { get; set; }

    public Guid? OrganizationUnitId { get; set; }

    public Guid TransportTripTypeId { get; set; }

    public decimal? CostAmount { get; set; }

    public decimal? RevenueAmount { get; set; }

    public decimal? Co2Kg { get; set; }

    public decimal? LoadFactor { get; set; }

    public int? PlannedDistanceM { get; set; }

    public int? ActualDistanceM { get; set; }

    /// <summary>The stops' arrivals: what counts as on time is Reporting's definition.</summary>
    public List<TripFactStopDto> Stops { get; set; } = new();
}

public class TripFactStopDto
{
    public DateTimeOffset? PlannedArrival { get; set; }

    public DateTimeOffset? ActualArrival { get; set; }
}
