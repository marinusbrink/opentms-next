using System;
using System.Collections.Generic;
using System.Linq;
using OpenTms.PlanningExecution.Legs;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace OpenTms.PlanningExecution.TransportTrips;

/// <summary>
/// A transport trip (OpenTMS TransportFile; design 59 "TransportTrip", PO decisions Q7, Q8): the vehicle's journey that
/// carries legs between its stops. One status instead of two booleans (design 56 "Status machines"); the planner's
/// commands change it, the calculators write the stored derived values. Cross-module ids carry no foreign key.
/// </summary>
public class TransportTrip : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }

    public string Number { get; private set; } = string.Empty;

    public Guid TransportTripTypeId { get; private set; }

    public DateOnly TripDate { get; private set; }

    public string? Name { get; private set; }

    public TransportTripStatus Status { get; private set; } = TransportTripStatus.Planned;

    public bool IsArchived { get; private set; }

    public TripExecution Execution { get; private set; }

    public Guid? CarrierRelationId { get; private set; }

    public Guid? CarrierContactPersonId { get; private set; }

    public Guid? DepartureRelationId { get; private set; }

    public Guid? DepartureAddressId { get; private set; }

    public Guid? ArrivalRelationId { get; private set; }

    public Guid? ArrivalAddressId { get; private set; }

    public DateTimeOffset PlannedDeparture { get; private set; }

    public DateTimeOffset? PlannedArrival { get; private set; }

    /// <summary>The planned instants in UTC: the sortable, indexable form the lists order and filter on (SQLite cannot order a DateTimeOffset).</summary>
    public DateTime PlannedDepartureUtc { get; private set; }

    public DateTime? PlannedArrivalUtc { get; private set; }

    public DateTimeOffset? ActualDeparture { get; private set; }

    public DateTimeOffset? ActualArrival { get; private set; }

    public StopCompositionMode StopCompositionMode { get; private set; }

    public FirstLastStopRule FirstLastStopRule { get; private set; }

    public Guid? VehicleCombinationId { get; private set; }

    public string? Remarks { get; private set; }

    public string? DriverInstructions { get; private set; }

    public Guid? OrganizationUnitId { get; private set; }

    public Dictionary<string, string> Attributes { get; private set; } = new(StringComparer.Ordinal);

    public Guid? CreatedFromOrderId { get; private set; }

    public int? PlannedDistanceM { get; private set; }

    public int? ActualDistanceM { get; private set; }

    /* Stored derived values (TripCalculator). */
    public int? PlannedDurationS { get; private set; }

    public decimal? LoadFactor { get; private set; }

    public double? LoadingMeters { get; private set; }

    public decimal? Co2Kg { get; private set; }

    public List<TripWarning> Warnings { get; private set; } = new();

    public decimal? CostAmount { get; private set; }

    public decimal? RevenueAmount { get; private set; }

    public DateTimeOffset? CalculatedAt { get; private set; }

    /// <summary>Lower-cased searchable text: number, name, carrier, vehicles, customers, stop cities (design 59 list search).</summary>
    public string SearchKey { get; private set; } = string.Empty;

    public virtual ICollection<TripVehicle> Vehicles { get; private set; } = new List<TripVehicle>();

    public virtual ICollection<TripDriver> Drivers { get; private set; } = new List<TripDriver>();

    public virtual ICollection<TripStop> Stops { get; private set; } = new List<TripStop>();

    protected TransportTrip()
    {
    }

    public TransportTrip(Guid id, Guid? tenantId, string number, Guid transportTripTypeId, DateOnly tripDate, TripExecution execution,
        StopCompositionMode stopCompositionMode, FirstLastStopRule firstLastStopRule, DateTimeOffset plannedDeparture)
        : base(id)
    {
        TenantId = tenantId;
        Number = Check.NotNullOrWhiteSpace(number, nameof(number), PlanningExecutionConsts.MaxNumberLength);
        TransportTripTypeId = transportTripTypeId;
        TripDate = tripDate;
        SetExecution(execution);
        SetComposition(stopCompositionMode, firstLastStopRule);
        SetPlannedTimes(plannedDeparture, null);
    }

    private void SetPlannedTimes(DateTimeOffset departure, DateTimeOffset? arrival)
    {
        PlannedDeparture = departure;
        PlannedArrival = arrival;
        PlannedDepartureUtc = departure.UtcDateTime;
        PlannedArrivalUtc = arrival?.UtcDateTime;
    }

    public bool IsFinal => Status is TransportTripStatus.Completed or TransportTripStatus.Cancelled;

    /// <summary>Planned or in progress, not archived: the trip takes legs and edits (design 59 "Refusals").</summary>
    public bool IsAssignable => !IsArchived && Status is TransportTripStatus.Planned or TransportTripStatus.InProgress;

    public DateOnly DepartureDate => DateOnly.FromDateTime(PlannedDeparture.Date);

    public DateOnly ArrivalDate => PlannedArrival == null ? DepartureDate : DateOnly.FromDateTime(PlannedArrival.Value.Date);

    public IEnumerable<TripStop> OrderedStops => Stops.OrderBy(s => s.Position);

    public IEnumerable<Guid> LegIds => Stops.SelectMany(s => s.Legs).Select(l => l.LegId).Distinct();

    public void EnsureAssignable()
    {
        if (!IsAssignable)
        {
            throw new BusinessException(PlanningExecutionErrorCodes.TripNotAssignable).WithData("status", Status.ToString()).WithData("number", Number);
        }
    }

    public void EnsureEditable()
    {
        if (IsArchived)
        {
            throw new BusinessException(PlanningExecutionErrorCodes.TripArchived).WithData("number", Number);
        }
    }

    public void SetHeader(string? name, DateOnly tripDate, DateTimeOffset plannedDeparture, DateTimeOffset? plannedArrival,
        Guid? departureRelationId, Guid? departureAddressId, Guid? arrivalRelationId, Guid? arrivalAddressId,
        string? remarks, string? driverInstructions, Guid? organizationUnitId, TripExecution execution,
        StopCompositionMode stopCompositionMode, FirstLastStopRule firstLastStopRule, IReadOnlyDictionary<string, string>? attributes)
    {
        EnsureEditable();
        if (plannedArrival != null && plannedArrival < plannedDeparture)
        {
            throw new BusinessException(PlanningExecutionErrorCodes.ArrivalBeforeDeparture);
        }

        Name = Text(name, PlanningExecutionConsts.MaxNameLength);
        TripDate = tripDate;
        SetPlannedTimes(plannedDeparture, plannedArrival);
        DepartureRelationId = departureRelationId;
        DepartureAddressId = departureAddressId;
        ArrivalRelationId = arrivalRelationId;
        ArrivalAddressId = arrivalAddressId;
        Remarks = Text(remarks, PlanningExecutionConsts.MaxRemarksLength);
        DriverInstructions = Text(driverInstructions, PlanningExecutionConsts.MaxRemarksLength);
        OrganizationUnitId = organizationUnitId;
        SetExecution(execution);
        SetComposition(stopCompositionMode, firstLastStopRule);
        SetAttributes(attributes);
    }

    public void SetExecution(TripExecution execution)
        => Execution = Enum.IsDefined(execution) ? execution : throw new BusinessException(Platform.ReferenceData.ReferenceDataErrorCodes.InvalidCode).WithData("field", "execution").WithData("value", (int)execution);

    public void SetComposition(StopCompositionMode mode, FirstLastStopRule rule)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new BusinessException(Platform.ReferenceData.ReferenceDataErrorCodes.InvalidCode).WithData("field", "stopCompositionMode").WithData("value", (int)mode);
        }

        if ((int)rule < 0 || (int)rule > (int)FirstLastStopRule.Both)
        {
            throw new BusinessException(Platform.ReferenceData.ReferenceDataErrorCodes.InvalidCode).WithData("field", "firstLastStopRule").WithData("value", (int)rule);
        }

        StopCompositionMode = mode;
        FirstLastStopRule = rule;
    }

    public void SetAttributes(IReadOnlyDictionary<string, string>? attributes)
    {
        var reason = TripAttributes.Validate(attributes);
        if (reason != null)
        {
            throw new BusinessException(PlanningExecutionErrorCodes.InvalidAttributes).WithData("reason", reason);
        }

        Attributes = attributes == null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : attributes.ToDictionary(a => a.Key.Trim(), a => a.Value ?? string.Empty, StringComparer.Ordinal);
    }

    public void SetCreatedFromOrder(Guid orderId) => CreatedFromOrderId = orderId;

    public void SetCarrier(Guid? carrierRelationId, Guid? carrierContactPersonId)
    {
        EnsureEditable();
        CarrierRelationId = carrierRelationId;
        CarrierContactPersonId = carrierRelationId == null ? null : carrierContactPersonId;
    }

    /// <summary>A combination or up to three vehicle ids, never both (design 59 "TransportTrip").</summary>
    public void SetVehicles(Guid? vehicleCombinationId, IReadOnlyList<Guid> vehicleIds)
    {
        EnsureEditable();
        var ids = vehicleIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (vehicleCombinationId != null && ids.Count > 0)
        {
            throw new BusinessException(PlanningExecutionErrorCodes.VehicleSourceConflict);
        }

        if (ids.Count > PlanningExecutionConsts.MaxVehiclesPerTrip)
        {
            throw new BusinessException(PlanningExecutionErrorCodes.TooManyVehicles).WithData("max", PlanningExecutionConsts.MaxVehiclesPerTrip);
        }

        VehicleCombinationId = vehicleCombinationId;
        ReplacePositioned(Vehicles, ids, (position, id) => new TripVehicle(TenantId, Id, position, id), v => v.Position, (v, id) => v.VehicleId == id);
    }

    public void SetDrivers(IReadOnlyList<Guid> driverIds)
    {
        EnsureEditable();
        var ids = driverIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (ids.Count > PlanningExecutionConsts.MaxDriversPerTrip)
        {
            throw new BusinessException(PlanningExecutionErrorCodes.TooManyDrivers).WithData("max", PlanningExecutionConsts.MaxDriversPerTrip);
        }

        ReplacePositioned(Drivers, ids, (position, id) => new TripDriver(TenantId, Id, position, id), d => d.Position, (d, id) => d.DriverId == id);
    }

    private static void ReplacePositioned<TChild>(ICollection<TChild> rows, List<Guid> ids, Func<int, Guid, TChild> create, Func<TChild, int> positionOf, Func<TChild, Guid, bool> sameId)
    {
        foreach (var stale in rows.Where(r => positionOf(r) > ids.Count || !sameId(r, ids[positionOf(r) - 1])).ToList())
        {
            rows.Remove(stale);
        }

        for (var position = 1; position <= ids.Count; position++)
        {
            if (rows.All(r => positionOf(r) != position))
            {
                rows.Add(create(position, ids[position - 1]));
            }
        }
    }

    /* Status machine (design 56): Planned ─Start→ InProgress ─Complete→ Completed; Planned/InProgress ─Cancel→ Cancelled. */

    public void Start(DateTimeOffset at)
    {
        EnsureEditable();
        if (Status != TransportTripStatus.Planned)
        {
            throw Transition("Start");
        }

        Status = TransportTripStatus.InProgress;
        ActualDeparture ??= at;
    }

    /// <summary>Completes the trip and fills the missing actuals of the trip and its stops (OpenTMS <c>Complete</c>).</summary>
    public void Complete(DateTimeOffset at)
    {
        EnsureEditable();
        if (Status != TransportTripStatus.InProgress)
        {
            throw Transition("Complete");
        }

        Status = TransportTripStatus.Completed;
        ActualDeparture ??= at;
        ActualArrival ??= at;
        foreach (var stop in Stops)
        {
            stop.FillActuals(at);
        }
    }

    public void Cancel()
    {
        EnsureEditable();
        if (IsFinal)
        {
            throw Transition("Cancel");
        }

        Status = TransportTripStatus.Cancelled;
    }

    public void Archive()
    {
        if (IsArchived || !IsFinal)
        {
            throw Transition("Archive");
        }

        IsArchived = true;
    }

    public void Unarchive()
    {
        if (!IsArchived)
        {
            throw Transition("Unarchive");
        }

        IsArchived = false;
    }

    public void EnsureDeletable()
    {
        if (Status != TransportTripStatus.Planned || IsArchived)
        {
            throw Transition("Delete");
        }
    }

    private BusinessException Transition(string action)
        => new BusinessException(PlanningExecutionErrorCodes.InvalidTransition).WithData("action", action).WithData("status", Status.ToString()).WithData("number", Number);

    /* Stops. */

    public TripStop AddManualStop(Guid id, int? position, Guid waypointTypeId, Guid relationId, Guid? relationAddressId, string name, string? city,
        double? latitude, double? longitude, string? timeZoneId, int? plannedDurationMin, string? remarks)
    {
        EnsureAssignable();
        if (Stops.Count >= PlanningExecutionConsts.MaxStopsPerTrip)
        {
            throw new BusinessException(PlanningExecutionErrorCodes.TooManyStops).WithData("max", PlanningExecutionConsts.MaxStopsPerTrip);
        }

        var stop = new TripStop(id, TenantId, Id, Stops.Count + 1, waypointTypeId, relationId, relationAddressId, name, city, latitude, longitude, timeZoneId, isManual: true);
        stop.SetPlannedDurationMin(plannedDurationMin);
        stop.SetRemarks(remarks);
        Stops.Add(stop);
        if (position != null)
        {
            MoveStop(stop.Id, position.Value);
        }
        else
        {
            Renumber();
        }

        ApplyFirstLastStopRule();
        return stop;
    }

    /// <summary>Moves a stop to a position (1..n); a stop with actuals stays where it was (trips.md 27).</summary>
    public void MoveStop(Guid stopId, int position)
    {
        EnsureAssignable();
        var stop = GetStop(stopId);
        if (stop.HasActuals)
        {
            throw new BusinessException(PlanningExecutionErrorCodes.StopHasActuals).WithData("stopId", stopId);
        }

        if (position < 1 || position > Stops.Count)
        {
            throw new BusinessException(PlanningExecutionErrorCodes.InvalidPosition).WithData("value", position).WithData("max", Stops.Count);
        }

        var ordered = OrderedStops.Where(s => s.Id != stopId).ToList();
        ordered.Insert(position - 1, stop);
        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].SetPosition(i + 1);
        }

        /* OpenTMS clears the calculated times of a re-sequenced trip; the calculator refills them. */
        foreach (var s in Stops)
        {
            s.ClearPlannedTimes();
        }

        ApplyFirstLastStopRule();
    }

    public void RemoveManualStop(Guid stopId)
    {
        EnsureAssignable();
        var stop = GetStop(stopId);
        if (!stop.IsManual)
        {
            throw new BusinessException(PlanningExecutionErrorCodes.NotManualStop).WithData("stopId", stopId);
        }

        if (stop.HasActuals)
        {
            throw new BusinessException(PlanningExecutionErrorCodes.StopHasActuals).WithData("stopId", stopId);
        }

        Stops.Remove(stop);
        Renumber();
        ApplyFirstLastStopRule();
    }

    public void RecordStopActual(Guid stopId, DateTimeOffset? arrival, DateTimeOffset? departure, int? cumulativeDistanceM)
    {
        EnsureEditable();
        if (IsFinal && Status == TransportTripStatus.Cancelled)
        {
            throw Transition("RecordStopActual");
        }

        GetStop(stopId).RecordActuals(arrival, departure, cumulativeDistanceM);
    }

    public void SetActualDistance(int? actualDistanceM)
    {
        EnsureEditable();
        ActualDistanceM = actualDistanceM;
    }

    public TripStop GetStop(Guid stopId)
        => Stops.FirstOrDefault(s => s.Id == stopId)
           ?? throw new EntityNotFoundException(typeof(TripStop), stopId);

    /// <summary>Positions 1..n without gaps, in the current order (design 56 "Positions").</summary>
    public void Renumber()
    {
        var position = 1;
        foreach (var stop in Stops.OrderBy(s => s.Position).ThenBy(s => s.Id).ToList())
        {
            stop.SetPosition(position++);
        }
    }

    /// <summary>The first/last stop rule: departure and arrival follow the first and last stop (OpenTMS trip-type flags).</summary>
    public void ApplyFirstLastStopRule()
    {
        if (Stops.Count == 0)
        {
            return;
        }

        if (FirstLastStopRule.HasFlag(FirstLastStopRule.DepartureIsFirstStop))
        {
            var first = OrderedStops.First();
            DepartureRelationId = first.RelationId;
            DepartureAddressId = first.RelationAddressId;
        }

        if (FirstLastStopRule.HasFlag(FirstLastStopRule.ArrivalIsLastStop))
        {
            var last = OrderedStops.Last();
            ArrivalRelationId = last.RelationId;
            ArrivalAddressId = last.RelationAddressId;
        }
    }

    public void EnsureStopCount()
    {
        if (Stops.Count > PlanningExecutionConsts.MaxStopsPerTrip)
        {
            throw new BusinessException(PlanningExecutionErrorCodes.TooManyStops).WithData("max", PlanningExecutionConsts.MaxStopsPerTrip);
        }
    }

    /* Calculator writes. */

    public void SetPlannedRoute(int? plannedDistanceM, int? plannedDurationS, DateTimeOffset? plannedArrival)
    {
        PlannedDistanceM = plannedDistanceM;
        PlannedDurationS = plannedDurationS;
        if (plannedArrival != null)
        {
            SetPlannedTimes(PlannedDeparture, plannedArrival);
        }
    }

    public void SetLoad(decimal? loadFactor, double? loadingMeters)
    {
        LoadFactor = loadFactor == null ? null : Math.Round(loadFactor.Value, 4, MidpointRounding.AwayFromZero);
        LoadingMeters = loadingMeters;
    }

    public void SetCo2(decimal? kg) => Co2Kg = kg == null ? null : Math.Round(kg.Value, 3, MidpointRounding.AwayFromZero);

    public void SetMoney(decimal? costAmount, decimal? revenueAmount)
    {
        CostAmount = costAmount == null ? null : Math.Round(costAmount.Value, 2, MidpointRounding.AwayFromZero);
        RevenueAmount = revenueAmount == null ? null : Math.Round(revenueAmount.Value, 2, MidpointRounding.AwayFromZero);
    }

    public void SetWarnings(IEnumerable<TripWarning> warnings) => Warnings = warnings.Distinct().ToList();

    public void AddWarning(TripWarning warning)
    {
        if (!Warnings.Contains(warning))
        {
            Warnings = Warnings.Append(warning).ToList();
        }
    }

    public void MarkCalculated(DateTimeOffset at) => CalculatedAt = at;

    public void SetSearchKey(string key)
    {
        var lower = (key ?? string.Empty).Trim().ToLowerInvariant();
        SearchKey = lower.Length <= PlanningExecutionConsts.MaxSearchKeyLength ? lower : lower[..PlanningExecutionConsts.MaxSearchKeyLength];
    }

    private static string? Text(string? value, int maxLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}

/// <summary>A vehicle on the trip, positioned 1..3 (OpenTMS primary/secondary/third vehicle).</summary>
public class TripVehicle : Entity, IMultiTenant
{
    public Guid? TenantId { get; private set; }

    public Guid TripId { get; private set; }

    public int Position { get; private set; }

    public Guid VehicleId { get; private set; }

    protected TripVehicle()
    {
    }

    public TripVehicle(Guid? tenantId, Guid tripId, int position, Guid vehicleId)
    {
        TenantId = tenantId;
        TripId = tripId;
        Position = position;
        VehicleId = vehicleId;
    }

    public override object[] GetKeys() => [TripId, Position];
}

/// <summary>A driver on the trip (Master Data driver id; design 43), positioned 1..3.</summary>
public class TripDriver : Entity, IMultiTenant
{
    public Guid? TenantId { get; private set; }

    public Guid TripId { get; private set; }

    public int Position { get; private set; }

    public Guid DriverId { get; private set; }

    protected TripDriver()
    {
    }

    public TripDriver(Guid? tenantId, Guid tripId, int position, Guid driverId)
    {
        TenantId = tenantId;
        TripId = tripId;
        Position = position;
        DriverId = driverId;
    }

    public override object[] GetKeys() => [TripId, Position];
}

/// <summary>
/// One stop of a trip (design 59 "TripStop"): composed from the legs' ends or added by the planner (manual). Planned
/// times and cumulative distance are the calculator's; estimated equal planned until the ingest run (assumption 3);
/// actuals come from commands. Coordinates and the zone are snapshotted so the calculator can route without Master Data.
/// </summary>
public class TripStop : Entity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }

    public Guid TripId { get; private set; }

    public int Position { get; private set; }

    public Guid WaypointTypeId { get; private set; }

    public Guid RelationId { get; private set; }

    public Guid? RelationAddressId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string? City { get; private set; }

    public double? Latitude { get; private set; }

    public double? Longitude { get; private set; }

    public string? TimeZoneId { get; private set; }

    public bool IsManual { get; private set; }

    public int? PlannedDurationMin { get; private set; }

    public DateTimeOffset? PlannedArrival { get; private set; }

    public DateTimeOffset? PlannedDeparture { get; private set; }

    public DateTimeOffset? EstimatedArrival { get; private set; }

    public DateTimeOffset? EstimatedDeparture { get; private set; }

    public DateTimeOffset? ActualArrival { get; private set; }

    public DateTimeOffset? ActualDeparture { get; private set; }

    public int? PlannedCumulativeDistanceM { get; private set; }

    public int? ActualCumulativeDistanceM { get; private set; }

    public string? Remarks { get; private set; }

    public virtual ICollection<TripStopLeg> Legs { get; private set; } = new List<TripStopLeg>();

    protected TripStop()
    {
    }

    public TripStop(Guid id, Guid? tenantId, Guid tripId, int position, Guid waypointTypeId, Guid relationId, Guid? relationAddressId, string name, string? city,
        double? latitude, double? longitude, string? timeZoneId, bool isManual)
        : base(id)
    {
        TenantId = tenantId;
        TripId = tripId;
        Position = position;
        WaypointTypeId = waypointTypeId;
        RelationId = relationId;
        RelationAddressId = relationAddressId;
        Name = Cut(name, PlanningExecutionConsts.MaxSnapshotNameLength) ?? string.Empty;
        City = Cut(city, PlanningExecutionConsts.MaxCityLength);
        Latitude = latitude;
        Longitude = longitude;
        TimeZoneId = Cut(timeZoneId, PlanningExecutionConsts.MaxTimeZoneIdLength);
        IsManual = isManual;
    }

    public bool HasActuals => ActualArrival != null || ActualDeparture != null;

    public bool HasCoordinates => Latitude != null && Longitude != null;

    /// <summary>The composer merges on the address and the activity (OpenTMS: relation + waypoint type).</summary>
    public bool IsSamePlaceAndActivity(Guid relationId, Guid? relationAddressId, Guid waypointTypeId)
        => RelationId == relationId && RelationAddressId == relationAddressId && WaypointTypeId == waypointTypeId;

    public void SetPosition(int position) => Position = position;

    public void SetPlannedDurationMin(int? minutes) => PlannedDurationMin = minutes is < 0 ? null : minutes;

    public void SetRemarks(string? remarks) => Remarks = Cut(remarks, PlanningExecutionConsts.MaxRemarksLength);

    public void AddLeg(Guid legId, StopLegRole role)
    {
        if (Legs.All(l => l.LegId != legId || l.Role != role))
        {
            Legs.Add(new TripStopLeg(TenantId, Id, legId, role));
        }
    }

    public void RemoveLeg(Guid legId, StopLegRole? role = null)
    {
        foreach (var link in Legs.Where(l => l.LegId == legId && (role == null || l.Role == role)).ToList())
        {
            Legs.Remove(link);
        }
    }

    public void SetPlanned(DateTimeOffset? arrival, DateTimeOffset? departure, int? cumulativeDistanceM)
    {
        PlannedArrival = arrival;
        PlannedDeparture = departure;
        PlannedCumulativeDistanceM = cumulativeDistanceM;
        if (ActualArrival == null)
        {
            EstimatedArrival = arrival;
        }

        if (ActualDeparture == null)
        {
            EstimatedDeparture = departure;
        }
    }

    public void ClearPlannedTimes()
    {
        PlannedArrival = null;
        PlannedDeparture = null;
        PlannedCumulativeDistanceM = null;
        if (ActualArrival == null)
        {
            EstimatedArrival = null;
        }

        if (ActualDeparture == null)
        {
            EstimatedDeparture = null;
        }
    }

    public void RecordActuals(DateTimeOffset? arrival, DateTimeOffset? departure, int? cumulativeDistanceM)
    {
        if (arrival != null)
        {
            ActualArrival = arrival;
        }

        if (departure != null)
        {
            ActualDeparture = departure;
        }

        if (cumulativeDistanceM != null)
        {
            ActualCumulativeDistanceM = cumulativeDistanceM;
        }
    }

    /// <summary>OpenTMS <c>Complete</c>: actuals that are still empty get the completion instant.</summary>
    public void FillActuals(DateTimeOffset at)
    {
        ActualArrival ??= at;
        ActualDeparture ??= at;
    }

    private static string? Cut(string? value, int maxLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}

/// <summary>A leg's end at a stop; unique per leg and role (design 59 "TripStopLeg").</summary>
public class TripStopLeg : Entity, IMultiTenant
{
    public Guid? TenantId { get; private set; }

    public Guid TripStopId { get; private set; }

    public Guid LegId { get; private set; }

    public StopLegRole Role { get; private set; }

    protected TripStopLeg()
    {
    }

    public TripStopLeg(Guid? tenantId, Guid tripStopId, Guid legId, StopLegRole role)
    {
        TenantId = tenantId;
        TripStopId = tripStopId;
        LegId = legId;
        Role = role;
    }

    public override object[] GetKeys() => [TripStopId, LegId, Role];
}
