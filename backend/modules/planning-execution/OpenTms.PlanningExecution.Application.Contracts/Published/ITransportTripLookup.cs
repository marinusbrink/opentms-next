using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OpenTms.PlanningExecution.TransportTrips;

namespace OpenTms.PlanningExecution.Published;

/// <summary>
/// Short references of trips for other modules' grids and documents (design 56 "Ownership", the CMR's boxes 16/17/21).
/// In-process only; at most <see cref="MaxKeys"/> ids.
/// </summary>
public interface ITransportTripLookup
{
    Task<IReadOnlyList<TransportTripReferenceDto>> GetAsync(IReadOnlyCollection<Guid> tripIds, CancellationToken cancellationToken = default);

    const int MaxKeys = 500;
}

public class TransportTripReferenceDto
{
    public Guid Id { get; set; }

    public string Number { get; set; } = string.Empty;

    public TransportTripStatus Status { get; set; }

    public DateOnly TripDate { get; set; }

    public Guid? CarrierRelationId { get; set; }

    public string? CarrierName { get; set; }

    public Guid? DepartureRelationId { get; set; }

    public Guid? ArrivalRelationId { get; set; }

    public List<string> VehicleNames { get; set; } = new();

    public List<string> LicencePlates { get; set; } = new();

    public List<string> DriverNames { get; set; } = new();

    public DateTimeOffset PlannedDeparture { get; set; }
}
