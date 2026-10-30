using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OpenTms.PlanningExecution.TransportTrips;

namespace OpenTms.PlanningExecution.Published;

/// <summary>
/// The legs of an order or a trip for other modules (design 56: Financial prices with their distances, Orders prints the
/// CMR with their trips, the Transport app shows them; design 59 "Published contract"). In-process only, tenant-filtered,
/// stored values only.
/// </summary>
public interface ILegReader
{
    Task<IReadOnlyList<LegSnapshotDto>> GetByOrderAsync(Guid orderId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LegSnapshotDto>> GetByOrdersAsync(IReadOnlyCollection<Guid> orderIds, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LegSnapshotDto>> GetByTripAsync(Guid tripId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LegSnapshotDto>> GetAsync(IReadOnlyCollection<Guid> legIds, CancellationToken cancellationToken = default);

    const int MaxKeys = 500;
}

public class LegSnapshotDto
{
    public Guid Id { get; set; }

    public Guid OrderId { get; set; }

    public Guid OrderLineId { get; set; }

    public int Position { get; set; }

    public LegEndDto Origin { get; set; } = new();

    public LegEndDto Destination { get; set; } = new();

    public bool IsViaLeg { get; set; }

    public Guid? TripId { get; set; }

    public string? TripNumber { get; set; }

    public TransportTripStatus? TripStatus { get; set; }

    public int? DistanceM { get; set; }

    public int? DurationS { get; set; }

    public int? TollDistanceM { get; set; }

    public decimal? EstimatedCo2Kg { get; set; }

    public decimal? AllocatedCostAmount { get; set; }

    public decimal? AllocatedRevenueAmount { get; set; }

    public double? WeightKg { get; set; }

    public decimal Quantity { get; set; }
}

public class LegEndDto
{
    public Guid StopId { get; set; }

    public Guid WaypointTypeId { get; set; }

    public Guid RelationId { get; set; }

    public Guid RelationAddressId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? City { get; set; }

    public string? CountryCode { get; set; }

    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    public string? TimeZoneId { get; set; }

    public DateOnly PlannedDate { get; set; }

    public DateTimeOffset? WindowFrom { get; set; }

    public DateTimeOffset? WindowTill { get; set; }

    public string? Reference { get; set; }

    public DateTimeOffset? ActualAt { get; set; }
}
