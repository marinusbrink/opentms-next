using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using OpenTms.MasterData.Fleet;
using OpenTms.MasterData.Published;
using OpenTms.PlanningExecution.Permissions;
using OpenTms.PlanningExecution.TransportTrips;
using OpenTms.Platform.Features;
using OpenTms.Platform.ReferenceData;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Features;

namespace OpenTms.PlanningExecution.Planboard;

/// <summary>
/// Design 68 §6 and "API contract — reads" (PO decisions D1, D11, D19): the Scheduler reads and the Planboard's narrow
/// commands. Reads are SQL projections over Planning's own tables plus one batched Master Data read per kind (rows from
/// the published resource query of design 69, names from the reference lookup); nothing is calculated but the page's
/// figures. Every command is one unit of work on one trip, takes the trip's concurrency stamp (a stale one is refused
/// with 409), reuses <see cref="TripOperations"/> and <see cref="LegAssignmentService"/>, requests the trip's calculation
/// and notifies the trip (before and after) and the legs it touched on the record channel. Feature: the Planboard app.
/// </summary>
[RequiresFeature(AppFeatures.Planboard)]
public class PlanboardAppService : PlanningExecutionAppServiceBase, IPlanboardAppService
{
    private static readonly VehicleTypeKind[] MotorKinds = [VehicleTypeKind.Tractor, VehicleTypeKind.Rigid, VehicleTypeKind.Van];

    private readonly IRepository<TransportTrip, Guid> _trips;
    private readonly IRepository<TripVehicle> _tripVehicles;
    private readonly IRepository<TripDriver> _tripDrivers;
    private readonly IRepository<TransportTripType, Guid> _types;
    private readonly TripOperations _ops;
    private readonly MasterDataReferences _masterData;
    private readonly IMasterDataResourceQuery _resources;

    public PlanboardAppService(
        IRepository<TransportTrip, Guid> trips,
        IRepository<TripVehicle> tripVehicles,
        IRepository<TripDriver> tripDrivers,
        IRepository<TransportTripType, Guid> types,
        TripOperations ops,
        MasterDataReferences masterData,
        IMasterDataResourceQuery resources)
    {
        _trips = trips;
        _tripVehicles = tripVehicles;
        _tripDrivers = tripDrivers;
        _types = types;
        _ops = ops;
        _masterData = masterData;
        _resources = resources;
    }

    /* Reads */

    /// <summary>
    /// A page of resource rows of the mode (≤ 200, from Master Data) and the bars of every trip that uses one of them in
    /// the window: neither cancelled nor archived, planned departure before the window's end and planned arrival (else
    /// departure) at or after its start. Statements: the rows (Master Data), the matching trip ids, the bars, their vehicles,
    /// their drivers, and one name lookup per kind (vehicles, drivers, carriers).
    /// </summary>
    [Authorize(PlanningExecutionPermissions.TransportTrips.Default)]
    public virtual async Task<PlanboardSchedulerDto> GetSchedulerAsync(PlanboardSchedulerRequestDto input)
    {
        if (input.To <= input.From || input.To - input.From > TimeSpan.FromDays(PlanningExecutionConsts.MaxSchedulerWindowDays))
        {
            throw new BusinessException(PlanningExecutionErrorCodes.InvalidSchedulerWindow).WithData("max", PlanningExecutionConsts.MaxSchedulerWindowDays);
        }

        var take = Math.Clamp(input.Take, 1, PlanningExecutionConsts.MaxPlanboardPageSize);
        var (rows, totalRows) = await RowsAsync(input, take);
        var rowIds = rows.Select(r => r.Id).ToList();

        var fromUtc = input.From.UtcDateTime;
        var toUtc = input.To.UtcDateTime;
        var bars = new List<PlanboardBarDto>();
        var truncated = false;
        if (rowIds.Count > 0)
        {
            var trips = Live(await _trips.GetQueryableAsync())
                .Where(t => t.PlannedDepartureUtc < toUtc && (t.PlannedArrivalUtc ?? t.PlannedDepartureUtc) >= fromUtc);
            var matches = input.Mode == PlanboardResourceMode.Driver
                ? from d in await _tripDrivers.GetQueryableAsync()
                  where rowIds.Contains(d.DriverId)
                  join t in trips on d.TripId equals t.Id
                  select new { t.Id, t.PlannedDepartureUtc }
                : from v in await _tripVehicles.GetQueryableAsync()
                  where rowIds.Contains(v.VehicleId)
                  join t in trips on v.TripId equals t.Id
                  select new { t.Id, t.PlannedDepartureUtc };
            var tripIds = await AsyncExecuter.ToListAsync(matches.Distinct()
                .OrderBy(m => m.PlannedDepartureUtc).ThenBy(m => m.Id)
                .Select(m => m.Id)
                .Take(PlanningExecutionConsts.MaxSchedulerBars + 1));
            truncated = tripIds.Count > PlanningExecutionConsts.MaxSchedulerBars;
            bars = await BarsAsync(tripIds.Take(PlanningExecutionConsts.MaxSchedulerBars).ToList());
        }

        return new PlanboardSchedulerDto
        {
            Rows = rows,
            Bars = bars,
            /* The availability part of design 68 (§2) fills the blocks of the rows in the window. */
            Blocks = [],
            Names = await NamesAsync(bars),
            Kpis = Kpis(rows, bars, input.Mode, input.From, input.To),
            TotalRows = totalRows,
            BarsTruncated = truncated
        };
    }

    /// <summary>The bars of given trips (live-update refetch, ≤ 200): a cancelled, archived or deleted trip is absent.</summary>
    [Authorize(PlanningExecutionPermissions.TransportTrips.Default)]
    public virtual async Task<PlanboardBarsDto> GetSchedulerBarsAsync(PlanboardBarsRequestDto input)
    {
        if (input.Ids.Count > PlanningExecutionConsts.MaxPlanboardPageSize)
        {
            throw new BusinessException(PlanningExecutionErrorCodes.TooManyIds).WithData("max", PlanningExecutionConsts.MaxPlanboardPageSize);
        }

        var bars = await BarsAsync(input.Ids.Distinct().ToList());
        return new PlanboardBarsDto { Bars = bars, Names = await NamesAsync(bars) };
    }

    private async Task<(List<PlanboardResourceRowDto> Rows, long Total)> RowsAsync(PlanboardSchedulerRequestDto input, int take)
    {
        if (input.Mode == PlanboardResourceMode.Driver)
        {
            var drivers = await _resources.GetDriversAsync(new MasterDataDriverResourceQuery { Search = input.Search, Skip = input.Skip, Take = take });
            return (drivers.Items.Select(d => new PlanboardResourceRowDto { Id = d.Id, Name = d.Name, Code = d.Code }).ToList(), drivers.TotalCount);
        }

        var vehicles = await _resources.GetVehiclesAsync(new MasterDataVehicleResourceQuery
        {
            Kinds = input.Mode == PlanboardResourceMode.Trailer ? [VehicleTypeKind.Trailer] : MotorKinds.ToList(),
            OrganizationUnitIds = input.OrganizationUnitIds.Distinct().ToList(),
            Search = input.Search,
            Skip = input.Skip,
            Take = take
        });
        return (vehicles.Items.Select(v => new PlanboardResourceRowDto
        {
            Id = v.Id,
            Name = v.Name,
            Code = v.LicencePlate,
            Kind = v.Kind,
            TypeName = v.VehicleTypeName,
            DefaultDriverIds = v.DefaultDriverIds.ToList()
        }).ToList(), vehicles.TotalCount);
    }

    /// <summary>The trips a Scheduler shows: neither cancelled nor archived.</summary>
    private static IQueryable<TransportTrip> Live(IQueryable<TransportTrip> trips)
        => trips.Where(t => !t.IsArchived && t.Status != TransportTripStatus.Cancelled);

    /// <summary>Three statements: the trips' scalar columns with their stop figures, their vehicle rows, their driver rows.</summary>
    private async Task<List<PlanboardBarDto>> BarsAsync(List<Guid> tripIds)
    {
        if (tripIds.Count == 0)
        {
            return [];
        }

        var sources = await AsyncExecuter.ToListAsync(Live(await _trips.GetQueryableAsync())
            .Where(t => tripIds.Contains(t.Id))
            .Select(t => new
            {
                t.Id,
                t.Number,
                t.Name,
                t.Status,
                t.Execution,
                t.PlannedDeparture,
                t.PlannedArrival,
                t.PlannedDepartureUtc,
                t.ActualDeparture,
                t.ActualArrival,
                t.CarrierRelationId,
                t.OrganizationUnitId,
                t.LoadFactor,
                t.Warnings,
                t.ConcurrencyStamp,
                StopCount = t.Stops.Count,
                FirstStopCity = t.Stops.OrderBy(s => s.Position).Select(s => s.City).FirstOrDefault(),
                LastStopCity = t.Stops.OrderByDescending(s => s.Position).Select(s => s.City).FirstOrDefault()
            }));
        var vehicles = (await AsyncExecuter.ToListAsync((await _tripVehicles.GetQueryableAsync())
                .Where(v => tripIds.Contains(v.TripId))
                .Select(v => new { v.TripId, v.Position, v.VehicleId })))
            .ToLookup(v => v.TripId);
        var drivers = (await AsyncExecuter.ToListAsync((await _tripDrivers.GetQueryableAsync())
                .Where(d => tripIds.Contains(d.TripId))
                .Select(d => new { d.TripId, d.Position, d.DriverId })))
            .ToLookup(d => d.TripId);

        return sources.OrderBy(t => t.PlannedDepartureUtc).ThenBy(t => t.Number, StringComparer.Ordinal).Select(t => new PlanboardBarDto
        {
            Id = t.Id,
            Number = t.Number,
            Name = t.Name,
            Status = t.Status,
            Execution = t.Execution,
            PlannedDeparture = t.PlannedDeparture,
            PlannedArrival = t.PlannedArrival,
            ActualDeparture = t.ActualDeparture,
            ActualArrival = t.ActualArrival,
            VehicleIds = vehicles[t.Id].OrderBy(v => v.Position).Select(v => v.VehicleId).ToList(),
            DriverIds = drivers[t.Id].OrderBy(d => d.Position).Select(d => d.DriverId).ToList(),
            CarrierRelationId = t.CarrierRelationId,
            OrganizationUnitId = t.OrganizationUnitId,
            FirstStopCity = t.FirstStopCity,
            LastStopCity = t.LastStopCity,
            StopCount = t.StopCount,
            LoadFactor = t.LoadFactor,
            WarningCodes = t.Warnings.Select(w => w.Code).Distinct().ToList(),
            ConcurrencyStamp = t.ConcurrencyStamp
        }).ToList();
    }

    /// <summary>Every vehicle, driver and carrier on the bars named: one batched lookup per kind.</summary>
    private async Task<Dictionary<Guid, string>> NamesAsync(List<PlanboardBarDto> bars)
    {
        var names = new Dictionary<Guid, string>();
        if (bars.Count == 0)
        {
            return names;
        }

        foreach (var (id, name) in await _masterData.VehicleNamesAsync(bars.SelectMany(b => b.VehicleIds)))
        {
            names[id] = name;
        }

        foreach (var (id, name) in await _masterData.DriverNamesAsync(bars.SelectMany(b => b.DriverIds)))
        {
            names[id] = name;
        }

        foreach (var (id, name) in await _masterData.RelationNamesAsync(bars.Select(b => b.CarrierRelationId)))
        {
            names[id] = name;
        }

        return names;
    }

    /// <summary>The page's figures: utilisation = the rows' planned trip hours inside the window / (rows × window hours).</summary>
    private static PlanboardSchedulerKpisDto Kpis(List<PlanboardResourceRowDto> rows, List<PlanboardBarDto> bars, PlanboardResourceMode mode, DateTimeOffset from, DateTimeOffset to)
    {
        var plannedHours = 0d;
        var rowsWithoutTrip = 0;
        foreach (var row in rows)
        {
            var rowBars = bars.Where(b => mode == PlanboardResourceMode.Driver ? b.DriverIds.Contains(row.Id) : b.VehicleIds.Contains(row.Id)).ToList();
            if (rowBars.Count == 0)
            {
                rowsWithoutTrip++;
            }

            foreach (var bar in rowBars)
            {
                var start = bar.PlannedDeparture > from ? bar.PlannedDeparture : from;
                var arrival = bar.PlannedArrival ?? bar.PlannedDeparture;
                var end = arrival < to ? arrival : to;
                if (end > start)
                {
                    plannedHours += (end - start).TotalHours;
                }
            }
        }

        var capacity = rows.Count * (to - from).TotalHours;
        return new PlanboardSchedulerKpisDto
        {
            Rows = rows.Count,
            Trips = bars.Count,
            InProgress = bars.Count(b => b.Status == TransportTripStatus.InProgress),
            RowsWithoutTrip = rowsWithoutTrip,
            UtilisationPercent = capacity <= 0 ? 0 : Math.Round((decimal)(plannedHours / capacity * 100), 1, MidpointRounding.AwayFromZero)
        };
    }

    /* Commands */

    [Authorize(PlanningExecutionPermissions.TransportTrips.Update)]
    public virtual async Task<PlanboardChangeDto> RescheduleAsync(Guid id, PlanboardRescheduleDto input)
    {
        var trip = await LoadAsync(id, input.ConcurrencyStamp);
        var tripDate = DateOnly.FromDateTime(input.PlannedDeparture.Date);
        if (tripDate != trip.TripDate)
        {
            /* As the editor's date change: the trip date may not leave the numbering period of its number. */
            var type = await _types.GetAsync(trip.TransportTripTypeId);
            if (type.Numbering.Period(tripDate) != type.Numbering.Period(trip.TripDate))
            {
                throw new BusinessException(PlanningExecutionErrorCodes.TripDateLocked).WithData("number", trip.Number).WithData("value", tripDate.ToString("yyyy-MM-dd"));
            }
        }

        trip.Reschedule(input.PlannedDeparture, input.PlannedArrival);
        await _ops.ChangedAsync(trip, TripChangeKind.Header, recalculate: true);
        return await SavedAsync(trip);
    }

    /// <summary>
    /// A bar dropped on another row, or a resource dropped on a bar. D11: a tractor fills only the empty slots of the
    /// trip — drivers from its default drivers, the carrier from its owner (not the own company), trailers from its
    /// standing combination (Master Data has none today: skipped); it never overwrites what is set. A trip that carried a
    /// combination keeps its vehicles as loose vehicles once one of them is replaced.
    /// </summary>
    [Authorize(PlanningExecutionPermissions.TransportTrips.Update)]
    public virtual async Task<PlanboardChangeDto> ChangeResourceAsync(Guid id, PlanboardChangeResourceDto input)
    {
        var trip = await LoadAsync(id, input.ConcurrencyStamp);
        if (input.Mode == PlanboardResourceMode.Driver)
        {
            await _ops.ValidateDriversAsync([input.ToResourceId]);
            var drivers = trip.DriverIdsInOrder.ToList();
            Replace(drivers, input.FromResourceId, input.ToResourceId, append: true);
            trip.SetDrivers(drivers.Distinct().ToList());
            await _ops.ChangedAsync(trip, TripChangeKind.Drivers, recalculate: true);
            return await SavedAsync(trip);
        }

        var current = trip.VehicleIdsInOrder.ToList();
        var vehicles = await _masterData.VehiclesAsync(current.Append(input.ToResourceId));
        EnsureKind(vehicles, input.ToResourceId, input.Mode, "toResourceId");
        if (input.FromResourceId != null)
        {
            Replace(current, input.FromResourceId, input.ToResourceId, append: false);
        }
        else
        {
            Place(current, input.Mode, input.ToResourceId, vehicles);
        }

        trip.SetVehicles(null, current.Distinct().ToList());
        if (input.Mode == PlanboardResourceMode.Tractor)
        {
            var defaults = await TractorDefaultsAsync(input.ToResourceId, trip.Drivers.Count == 0, trip.CarrierRelationId == null);
            if (defaults.DriverIds.Count > 0)
            {
                trip.SetDrivers(defaults.DriverIds);
                await _ops.ChangedAsync(trip, TripChangeKind.Drivers, recalculate: false);
            }

            if (defaults.CarrierRelationId != null)
            {
                trip.SetCarrier(defaults.CarrierRelationId, null);
                await _ops.ChangedAsync(trip, TripChangeKind.Carrier, recalculate: false);
            }
        }

        await _ops.VehiclesChangedAsync(trip);
        return await SavedAsync(trip);
    }

    /// <summary>S:Q-15: a driver from the Resources pane goes in the next position (at most three); one already on the trip is a no-op.</summary>
    [Authorize(PlanningExecutionPermissions.TransportTrips.Update)]
    public virtual async Task<PlanboardChangeDto> AddDriverAsync(Guid id, PlanboardAddDriverDto input)
    {
        var trip = await LoadAsync(id, input.ConcurrencyStamp);
        if (trip.Drivers.Any(d => d.DriverId == input.DriverId))
        {
            return Change(trip);
        }

        await _ops.ValidateDriversAsync([input.DriverId]);
        trip.SetDrivers(trip.DriverIdsInOrder.Append(input.DriverId).ToList());
        await _ops.ChangedAsync(trip, TripChangeKind.Drivers, recalculate: true);
        return await SavedAsync(trip);
    }

    [Authorize(PlanningExecutionPermissions.TransportTrips.Update)]
    public virtual async Task<PlanboardChangeDto> SetFieldAsync(Guid id, PlanboardUpdateFieldDto input)
    {
        var trip = await LoadAsync(id, input.ConcurrencyStamp);
        switch (input.Field)
        {
            case PlanboardTripField.Name:
                trip.SetName(input.Value);
                break;
            case PlanboardTripField.Remarks:
                trip.SetRemarks(input.Value);
                break;
            case PlanboardTripField.DriverInstructions:
                trip.SetDriverInstructions(input.Value);
                break;
            default:
                throw new BusinessException(ReferenceDataErrorCodes.InvalidCode).WithData("field", "field").WithData("value", (int)input.Field);
        }

        await _ops.ChangedAsync(trip, TripChangeKind.Header, recalculate: true);
        return await SavedAsync(trip);
    }

    /// <summary>
    /// A new trip from an empty cell or a leg drop on a row: the type's defaults fill the empty groups (design 61), then the
    /// row's resource takes its mode's slot (a tractor also fills the empty driver and carrier slots, D11); the dropped legs
    /// are assigned in the same unit of work whatever their window.
    /// </summary>
    [Authorize(PlanningExecutionPermissions.TransportTrips.Create)]
    public virtual async Task<PlanboardChangeDto> CreateTripAsync(PlanboardCreateTripDto input)
    {
        if (input.LegIds.Count > 0)
        {
            await AuthorizationService.CheckAsync(PlanningExecutionPermissions.TransportTrips.AssignLegs);
        }

        if (input.ResourceId != null && input.Mode == null)
        {
            throw new BusinessException(PlanningExecutionErrorCodes.ResourceKindMismatch).WithData("field", "mode");
        }

        var typeId = input.TransportTripTypeId ?? await DefaultTripTypeIdAsync();
        var legs = input.LegIds.Count > 0 ? await _ops.LegsAsync(input.LegIds) : [];
        var trip = await _ops.CreateAsync(
            new TransportTripCreateDto { TransportTripTypeId = typeId, PlannedDeparture = input.PlannedDeparture },
            null, null, null,
            input.ResourceId == null ? null : filled => PlaceInNewTripAsync(filled, input.Mode!.Value, input.ResourceId.Value));
        if (legs.Count > 0)
        {
            await _ops.AssignAsync(trip, legs, allowOutsideWindow: true, afterStopId: null, moving: false);
        }

        await _ops.RequestRecalculationAsync(trip.Id, "Created");
        return await SavedAsync(trip);
    }

    [Authorize(PlanningExecutionPermissions.TransportTrips.AssignLegs)]
    public virtual async Task<PlanboardChangeDto> AssignLegsAsync(Guid id, PlanboardLegsDto input)
    {
        var trip = await LoadAsync(id, input.ConcurrencyStamp);
        var legs = await _ops.LegsAsync(input.LegIds);
        await _ops.AssignAsync(trip, legs, input.AllowOutsideWindow, afterStopId: null, moving: false);
        return await SavedAsync(trip);
    }

    [Authorize(PlanningExecutionPermissions.TransportTrips.AssignLegs)]
    public virtual async Task<PlanboardChangeDto> UnassignLegsAsync(Guid id, PlanboardLegsDto input)
    {
        var trip = await LoadAsync(id, input.ConcurrencyStamp);
        var legs = await _ops.LegsAsync(input.LegIds);
        await _ops.UnassignAsync(trip, legs);
        return await SavedAsync(trip);
    }

    /// <summary>P:Q17: a running trip completed with both actual times; stops keep their recorded actuals.</summary>
    [Authorize(PlanningExecutionPermissions.TransportTrips.Complete)]
    public virtual async Task<PlanboardChangeDto> CompleteAsync(Guid id, PlanboardCompleteDto input)
    {
        var trip = await LoadAsync(id, input.ConcurrencyStamp);
        trip.Complete(input.ActualDeparture, input.ActualArrival);
        await _ops.StatusChangedAsync(trip, (await _ops.LegsOnTripAsync(id)).Select(l => l.OrderId));
        await _ops.RequestRecalculationAsync(trip.Id, "Completed");
        return await SavedAsync(trip);
    }

    /// <summary>
    /// The trip with its children, refused with 409 (ABP's <see cref="AbpDbConcurrencyException"/>) when the caller's
    /// stamp is not the stored one; a concurrent save between this read and the commit is refused by ABP's stamp check on
    /// save. The trip's state before the command goes to the record channel (merged with the state after).
    /// </summary>
    private async Task<TransportTrip> LoadAsync(Guid id, string concurrencyStamp)
    {
        var trip = await _ops.GetWithDetailsAsync(id);
        if (!string.Equals(trip.ConcurrencyStamp, concurrencyStamp, StringComparison.Ordinal))
        {
            throw new AbpDbConcurrencyException($"Transport trip {id} was changed after it was read.");
        }

        await _ops.Notices.TripAsync(trip);
        return trip;
    }

    private async Task<PlanboardChangeDto> SavedAsync(TransportTrip trip)
    {
        await CurrentUnitOfWork!.SaveChangesAsync();
        return Change(trip);
    }

    private static PlanboardChangeDto Change(TransportTrip trip)
        => new() { Id = trip.Id, ConcurrencyStamp = trip.ConcurrencyStamp, LastModificationTime = trip.LastModificationTime };

    /// <summary>The tenant's default trip type, else its first active one by code.</summary>
    private async Task<Guid> DefaultTripTypeIdAsync()
    {
        var type = await AsyncExecuter.FirstOrDefaultAsync((await _types.GetQueryableAsync())
            .Where(t => !t.IsArchived)
            .OrderByDescending(t => t.IsDefault).ThenBy(t => t.Code)
            .Select(t => new { t.Id }));
        return type?.Id ?? throw new BusinessException(PlanningExecutionErrorCodes.NoTripType);
    }

    /// <summary>The row's resource in the filled request of a new trip (after the type's defaults).</summary>
    private async Task PlaceInNewTripAsync(TransportTripCreateDto filled, PlanboardResourceMode mode, Guid resourceId)
    {
        if (mode == PlanboardResourceMode.Driver)
        {
            /* The driver row's driver is the first driver; the type's other drivers follow. */
            filled.DriverIds = new[] { resourceId }.Concat(filled.DriverIds.Where(d => d != resourceId)).Take(PlanningExecutionConsts.MaxDriversPerTrip).ToList();
            return;
        }

        var current = filled.VehicleIds.Where(v => v != Guid.Empty).ToList();
        if (filled.VehicleCombinationId != null)
        {
            /* The type's combination becomes its members as loose vehicles, so the row's vehicle can take its slot. */
            current = await _masterData.TripVehicleIdsAsync(filled.VehicleCombinationId, []);
            filled.VehicleCombinationId = null;
        }

        var vehicles = await _masterData.VehiclesAsync(current.Append(resourceId));
        EnsureKind(vehicles, resourceId, mode, "resourceId");
        Place(current, mode, resourceId, vehicles);
        filled.VehicleIds = current.Distinct().ToList();

        if (mode == PlanboardResourceMode.Tractor)
        {
            var defaults = await TractorDefaultsAsync(resourceId, filled.DriverIds.Count == 0, filled.CarrierRelationId == null);
            if (defaults.DriverIds.Count > 0)
            {
                filled.DriverIds = defaults.DriverIds;
            }

            if (defaults.CarrierRelationId != null)
            {
                filled.CarrierRelationId = defaults.CarrierRelationId;
                filled.CarrierContactPersonId = null;
            }
        }
    }

    /// <summary>A vehicle of the mode's kinds: a trailer for the trailer mode, a motor vehicle for the tractor mode.</summary>
    private static void EnsureKind(Dictionary<Guid, MasterDataVehicleReferenceDto> vehicles, Guid vehicleId, PlanboardResourceMode mode, string field)
    {
        if (!vehicles.TryGetValue(vehicleId, out var vehicle))
        {
            throw new BusinessException(ReferenceDataErrorCodes.ReferenceNotFound).WithData("field", field).WithData("value", vehicleId);
        }

        if ((mode == PlanboardResourceMode.Trailer) != (vehicle.Kind == VehicleTypeKind.Trailer))
        {
            throw new BusinessException(PlanningExecutionErrorCodes.ResourceKindMismatch).WithData("field", field);
        }
    }

    /// <summary>
    /// A vehicle put on a trip without a row it came from: a tractor replaces the trip's motor vehicle (position 1; placed
    /// before the trailers when there is none), a trailer takes the next free position. Already on the trip: unchanged.
    /// </summary>
    private static void Place(List<Guid> current, PlanboardResourceMode mode, Guid vehicleId, Dictionary<Guid, MasterDataVehicleReferenceDto> vehicles)
    {
        if (current.Contains(vehicleId))
        {
            return;
        }

        if (mode == PlanboardResourceMode.Trailer)
        {
            current.Add(vehicleId);
            return;
        }

        var motor = current.FindIndex(id => vehicles.TryGetValue(id, out var v) && v.Kind != VehicleTypeKind.Trailer);
        if (motor >= 0)
        {
            current[motor] = vehicleId;
        }
        else
        {
            current.Insert(0, vehicleId);
        }
    }

    /// <summary>Replaces <paramref name="from"/>'s position; without <paramref name="from"/> the id is appended when <paramref name="append"/>.</summary>
    private static void Replace(List<Guid> current, Guid? from, Guid to, bool append)
    {
        if (from == null)
        {
            if (append && !current.Contains(to))
            {
                current.Add(to);
            }

            return;
        }

        var index = current.IndexOf(from.Value);
        if (index < 0)
        {
            throw new BusinessException(PlanningExecutionErrorCodes.ResourceNotOnTrip).WithData("resourceId", from.Value);
        }

        current[index] = to;
    }

    /// <summary>D11: what a tractor brings for the empty slots — its active default drivers and its owner as carrier (not the own company).</summary>
    private async Task<(List<Guid> DriverIds, Guid? CarrierRelationId)> TractorDefaultsAsync(Guid tractorId, bool driversEmpty, bool carrierEmpty)
    {
        if (!driversEmpty && !carrierEmpty)
        {
            return ([], null);
        }

        var tractor = (await _resources.GetVehiclesAsync(new MasterDataVehicleResourceQuery { Kinds = MotorKinds.ToList(), Ids = [tractorId], IncludeArchived = true, Take = 1 }))
            .Items.FirstOrDefault();
        if (tractor == null)
        {
            return ([], null);
        }

        var driverIds = new List<Guid>();
        if (driversEmpty && tractor.DefaultDriverIds.Count > 0)
        {
            var drivers = await _masterData.DriversAsync(tractor.DefaultDriverIds);
            driverIds = tractor.DefaultDriverIds.Where(d => drivers.TryGetValue(d, out var driver) && !driver.IsArchived)
                .Distinct().Take(PlanningExecutionConsts.MaxDriversPerTrip).ToList();
        }

        Guid? carrier = null;
        if (carrierEmpty && tractor.OwnerRelationId != Guid.Empty && tractor.OwnerRelationId != await _masterData.OwnCompanyIdAsync())
        {
            carrier = tractor.OwnerRelationId;
        }

        return (driverIds, carrier);
    }
}
