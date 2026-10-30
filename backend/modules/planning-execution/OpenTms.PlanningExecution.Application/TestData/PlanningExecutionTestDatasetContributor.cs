using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTms.Financial.Tariffs;
using OpenTms.MasterData.Fleet;
using OpenTms.MasterData.Relations;
using OpenTms.MasterData.TestData;
using OpenTms.Orders.TestData;
using OpenTms.Orders.TransportOrders;
using OpenTms.PlanningExecution.Legs;
using OpenTms.PlanningExecution.TransportTrips;
using OpenTms.PlanningExecution.ViaDepots;
using OpenTms.Platform.DerivedValues;
using OpenTms.Platform.NumberSequences;
using OpenTms.Platform.TestData;
using Volo.Abp.Auditing;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace OpenTms.PlanningExecution.TestData;

/// <summary>
/// Planning &amp; Execution's part of "Reset to dataset" (docs/designs/65-tenant-test-datasets.md), Order 400: the last
/// contributor, it builds legs and trips from <see cref="OrdersTestDataset"/> and <see cref="MasterDataTestDataset"/> and
/// then requests every derived value of the run.
/// <para>
/// <b>Wipe:</b> transport trips (their stops, stop-leg links, vehicles and drivers go with them through their cascading
/// foreign keys), legs and via-depot rules (their depots are Master Data addresses, wiped too). Kept, as tenant
/// configuration: transport trip types (their number formats, execution and composition defaults; the Master Data ids among
/// their defaults are cleared because those records are wiped) and trip attribute definitions. Nothing here references
/// another schema by foreign key, so Orders and Financial being wiped after this contributor is safe.
/// </para>
/// <para>
/// <b>Generate:</b> a few via-depot rules (customer and consignee rules to the nearest depot, so the routing through depots
/// shows), then per order with legs the legs exactly as <c>OrderLegGeneration</c> makes them on <c>TransportOrderConfirmed</c>
/// (the same <see cref="LegGenerator"/>, input mapped like <c>OrderLegGeneration.ToInput</c>, the waypoint types' successors
/// from Orders' output), and per day the trips (<see cref="PlanningTestTripPlanner"/>), numbered from the trip type's real
/// sequence in the trip date's period, counters raised once per period afterwards. Processed day by day, so memory holds a
/// few days of legs, not the whole set.
/// </para>
/// <para>
/// <b>Skipped on purpose</b> (one per record would flood the outbox; nothing reacts to them that the derived-value requests
/// below do not cover): the distributed events <c>LegsGenerated</c>, <c>LegAssigned</c>, <c>TransportTripCreated</c>,
/// <c>TransportTripLegsChanged</c>, <c>TransportTripStatusChanged</c>; trips requested on an order ("Tevens" trips, the
/// Orders output carries no trip requests); agreed purchase prices of charter trips; the functional logbook
/// (<c>[DisableAuditing]</c>).
/// </para>
/// <para>
/// <b>Derived values:</b> at the end, in transactions of <see cref="RequestChunkSize"/> ids, the five types the normal create
/// path ends up requesting: <c>PlanningExecution.Leg</c> (route, CO₂), <c>PlanningExecution.TransportTrip</c> (times, load,
/// warnings, CO₂, allocation), <c>Financial.OrderRateLines</c>, <c>Financial.TripRateLines</c> and <c>Orders.TransportOrder</c>
/// (the order summary). The scheduler writes one outbox row per thousand ids, but its handler enqueues one Hangfire job per
/// record (design 49), and the calculators request their dependants again as they finish.
/// </para>
/// </summary>
[DisableAuditing]
public class PlanningExecutionTestDatasetContributor : ITestDatasetContributor, ITransientDependency
{
    public const int LegBatchSize = 1_000;
    public const int TripBatchSize = 200;

    /// <summary>Ids per derived-value request transaction (at most <see cref="DerivedValueConsts.MaxTargetsPerRequest"/>).</summary>
    public const int RequestChunkSize = 5_000;

    private const string RequestReason = "TestDatasetReset";

    private readonly TestDatasetWriter _writer;
    private readonly IRepository<Leg, Guid> _legs;
    private readonly IRepository<TransportTrip, Guid> _trips;
    private readonly IRepository<ViaDepotRule, Guid> _rules;
    private readonly IRepository<TransportTripType, Guid> _tripTypes;
    private readonly LegGenerator _generator;
    private readonly LegAssignmentService _assignment;
    private readonly MasterDataReferences _masterData;
    private readonly INumberSequenceCounterStore _counters;
    private readonly IDerivedValueScheduler _scheduler;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly IGuidGenerator _guids;
    private readonly ICurrentTenant _currentTenant;
    private readonly IClock _clock;
    private readonly PlanningExecutionClock _planningClock;

    public ILogger<PlanningExecutionTestDatasetContributor> Logger { get; set; } = NullLogger<PlanningExecutionTestDatasetContributor>.Instance;

    public PlanningExecutionTestDatasetContributor(
        TestDatasetWriter writer,
        IRepository<Leg, Guid> legs,
        IRepository<TransportTrip, Guid> trips,
        IRepository<ViaDepotRule, Guid> rules,
        IRepository<TransportTripType, Guid> tripTypes,
        LegGenerator generator,
        LegAssignmentService assignment,
        MasterDataReferences masterData,
        INumberSequenceCounterStore counters,
        IDerivedValueScheduler scheduler,
        IUnitOfWorkManager unitOfWorkManager,
        IGuidGenerator guids,
        ICurrentTenant currentTenant,
        IClock clock,
        PlanningExecutionClock planningClock)
    {
        _writer = writer;
        _legs = legs;
        _trips = trips;
        _rules = rules;
        _tripTypes = tripTypes;
        _generator = generator;
        _assignment = assignment;
        _masterData = masterData;
        _counters = counters;
        _scheduler = scheduler;
        _unitOfWorkManager = unitOfWorkManager;
        _guids = guids;
        _currentTenant = currentTenant;
        _clock = clock;
        _planningClock = planningClock;
    }

    public int Order => 400;

    public async Task WipeAsync(TestDatasetContext context)
    {
        await _writer.DeleteAllAsync(_trips);
        await _writer.DeleteAllAsync(_legs);
        await _writer.DeleteAllAsync(_rules);
        var cleared = await ClearTripTypeDefaultsAsync();
        Logger.LogInformation(
            "Tenant {TenantId}: Planning & Execution wiped (trips, legs, via-depot rules; Master Data defaults cleared on {TypeCount} trip types).",
            context.TenantId, cleared);
    }

    public async Task GenerateAsync(TestDatasetContext context)
    {
        var masterData = context.Get<MasterDataTestDataset>();
        var orders = context.Find<OrdersTestDataset>();
        var withLegs = (orders?.Orders ?? [])
            .Where(o => o.HasLegs && o.Stops.Any())
            .Select(o => (Order: o, First: o.Stops.Min(s => s.Date)))
            .OrderBy(x => x.First)
            .ToList();
        if (orders == null || withLegs.Count == 0)
        {
            Logger.LogWarning("Tenant {TenantId}: no confirmed orders in the dataset; Planning & Execution generated nothing.", context.TenantId);
            return;
        }

        var refs = await LoadReferencesAsync(masterData);

        var rules = BuildViaDepotRules(context, masterData, orders, refs.Depots);
        await _writer.InsertAsync(_rules, rules);
        var depots = refs.Depots.ToDictionary(d => d.AddressId);
        DepotLocation? DepotOf(Guid addressId) => depots.GetValueOrDefault(addressId);
        Guid? SuccessorOf(Guid typeId) => orders.WaypointTypes.TryGetValue(typeId, out var t) ? t.SuccessiveWaypointTypeId : null;

        /* The trip types' own sequences (design 50), one counter per period of the trip dates, drawn in date order here and
         * raised once per period at the end (as Orders does for its order numbers). */
        var last = new Dictionary<(Guid TypeId, string Period), long>();
        var types = new[] { refs.OwnType, refs.CharterType }.Where(t => t != null).Select(t => t!).DistinctBy(t => t.Id).ToList();
        var firstDate = withLegs[0].First;
        var lastDate = withLegs.Max(x => x.Order.Stops.Max(s => s.Date)).AddDays(3);
        using (var unitOfWork = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true))
        {
            foreach (var type in types)
            {
                var periods = Enumerable.Range(0, lastDate.DayNumber - firstDate.DayNumber + 1).Select(firstDate.AddDays).Select(type.Numbering.Period).Distinct();
                foreach (var period in periods)
                {
                    last[(type.Id, period)] = await _counters.GetLastValueAsync(type.SequenceKey, period);
                }
            }

            await unitOfWork.CompleteAsync();
        }

        string NextNumber(TransportTripType type, DateOnly date)
        {
            var key = (type.Id, type.Numbering.Period(date));
            var value = last.GetValueOrDefault(key) + 1;
            last[key] = value;
            return type.Numbering.Format(value, date);
        }

        var planner = refs.OwnType == null
            ? null
            : new PlanningTestTripPlanner(context.Random, DateOnly.FromDateTime(context.Today), Now(), _planningClock.Zone, _currentTenant.Id, _guids.Create,
                _assignment, refs.Fleet, refs.OwnType, refs.CharterType ?? refs.OwnType, NextNumber,
                withLegs.ToDictionary(x => x.Order.Id, x => x.Order));
        if (planner == null)
        {
            Logger.LogWarning("Tenant {TenantId}: no transport trip type; legs are generated, trips are not.", context.TenantId);
        }

        var legIds = new List<Guid>();
        var tripIds = new List<Guid>();
        var orderIds = new List<Guid>();
        var viaLegs = 0;
        var buckets = new SortedDictionary<DateOnly, List<Leg>>();
        var next = 0;
        for (var day = firstDate; next < withLegs.Count || buckets.Count > 0; day = day.AddDays(1))
        {
            /* A leg departs on or after its order's first stop date: every leg of this day exists once these orders are generated. */
            for (; next < withLegs.Count && withLegs[next].First <= day; next++)
            {
                var order = withLegs[next].Order;
                var result = _generator.Generate(ToInput(order), rules, DepotOf, SuccessorOf, [], _currentTenant.Id, _guids.Create);
                foreach (var leg in result.Created)
                {
                    if (!buckets.TryGetValue(leg.OriginPlannedDate, out var bucket))
                    {
                        buckets[leg.OriginPlannedDate] = bucket = new List<Leg>();
                    }

                    bucket.Add(leg);
                    viaLegs += leg.IsViaLeg ? 1 : 0;
                }

                if (result.Created.Count > 0)
                {
                    orderIds.Add(order.Id);
                }
            }

            foreach (var date in buckets.Keys.TakeWhile(d => d <= day).ToList())
            {
                var legs = buckets[date];
                buckets.Remove(date);
                var trips = planner?.PlanDay(date, legs) ?? [];
                await _writer.InsertAsync(_legs, legs, LegBatchSize);
                await _writer.InsertAsync(_trips, trips, TripBatchSize);
                legIds.AddRange(legs.Select(l => l.Id));
                tripIds.AddRange(trips.Select(t => t.Id));
            }
        }

        using (var unitOfWork = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true))
        {
            var typesById = types.ToDictionary(t => t.Id);
            foreach (var ((typeId, period), value) in last.Where(p => p.Value > 0))
            {
                await _counters.RaiseLastValueAsync(typesById[typeId].SequenceKey, period, value);
            }

            await unitOfWork.CompleteAsync();
        }

        Logger.LogInformation(
            "Tenant {TenantId}: Planning & Execution generated {RuleCount} via-depot rules, {LegCount} legs ({ViaLegCount} via a depot) for {OrderCount} orders, " +
            "{TripCount} trips ({CompletedCount} completed, {InProgressCount} in progress, {PlannedCount} planned; {CharterCount} chartered); " +
            "{PlannedLegCount} legs planned, {UnplannedLegCount} unplanned.",
            context.TenantId, rules.Count, legIds.Count, viaLegs, orderIds.Count, tripIds.Count, planner?.CompletedTrips ?? 0, planner?.InProgressTrips ?? 0,
            planner?.PlannedTrips ?? 0, planner?.CharterTrips ?? 0, planner?.PlannedLegs ?? 0, planner?.UnplannedLegs ?? legIds.Count);

        /* Upstream first: routes and trips, then the rate lines, then the order summaries that read all of them. */
        var requested = 0;
        requested += await RequestAsync(PlanningExecutionApplicationModule.LegTargetType, legIds);
        requested += await RequestAsync(PlanningExecutionApplicationModule.TripTargetType, tripIds);
        requested += await RequestAsync(FinancialDerivedValueTypes.OrderRateLines, orderIds);
        requested += await RequestAsync(FinancialDerivedValueTypes.TripRateLines, tripIds);
        requested += await RequestAsync(OrdersConsts.OrderSummaryTargetType, orderIds);
        Logger.LogInformation("Tenant {TenantId}: {RequestCount} derived-value calculations requested for the dataset.", context.TenantId, requested);
    }

    /// <summary>The order's facts as <c>OrderLegGeneration.ToInput</c> maps Orders' pricing snapshot (no region ids: the generated rules are not region-scoped).</summary>
    private static LegGenerationInput ToInput(TestTransportOrder order) => new()
    {
        OrderId = order.Id,
        CustomerRelationId = order.CustomerRelationId,
        IsCancelled = !order.HasLegs,
        Lines = order.Lines
            .OrderBy(l => l.Position)
            .Select(l => new LegGenerationLine
            {
                LineId = l.Id,
                Quantities = new LegQuantities(l.Quantity, l.WeightKg, l.VolumeM3, l.LoadingMeters, l.Epq, l.LoadFactor, l.TransportUnitId, l.ProductId, l.TemperatureId,
                    l.RequiredTransportCharacteristicIds.ToList()),
                Stops = l.Stops
                    .OrderBy(s => s.Position)
                    .Select(s => new LegGenerationStop
                    {
                        Snapshot = new LegEndSnapshot(s.Id, s.WaypointTypeId, s.RelationId, s.RelationAddressId, s.Name ?? string.Empty, s.City, s.CountryCode,
                            s.Latitude, s.Longitude, s.TimeZoneId, s.Date, s.WindowFrom, s.WindowTill, s.Reference, s.PlannedDurationMin)
                    })
                    .ToList()
            })
            .ToList()
    };

    private sealed record References(TransportTripType? OwnType, TransportTripType? CharterType, TestPlanningFleet Fleet, IReadOnlyList<DepotLocation> Depots);

    /// <summary>
    /// The trip types (seeded, read whole like the seed does) and, through the Master Data published lookup (the stated
    /// synchronous call of design 56), the fleet with its names and plates, the active drivers and the depot addresses.
    /// </summary>
    private async Task<References> LoadReferencesAsync(MasterDataTestDataset masterData)
    {
        using var unitOfWork = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: false);

        var types = (await _tripTypes.GetListAsync(t => !t.IsArchived)).OrderByDescending(t => t.IsDefault).ThenBy(t => t.Code, StringComparer.Ordinal).ToList();
        var ownType = types.FirstOrDefault(t => t.IsDefault) ?? types.FirstOrDefault();
        var charterType = types.FirstOrDefault(t => t.DefaultExecution == TripExecution.Subcontracted) ?? ownType;

        var fleet = new TestPlanningFleet();
        var combinations = await _masterData.CombinationsAsync(masterData.VehicleCombinationIds.Select(id => (Guid?)id));
        var vehicles = await _masterData.VehiclesAsync(masterData.VehicleIds);
        var coupled = combinations.Values.SelectMany(c => c.VehicleIds).ToHashSet();
        foreach (var id in masterData.VehicleCombinationIds)
        {
            if (combinations.TryGetValue(id, out var combination) && !combination.IsArchived && combination.VehicleIds.Count > 0)
            {
                var members = combination.VehicleIds.Where(vehicles.ContainsKey).Select(v => vehicles[v]).ToList();
                fleet.Units.Add(new TestFleetUnit(id, null,
                    [combination.Name, .. members.Select(m => m.Name), .. members.Select(m => m.LicencePlate).OfType<string>()]));
            }
        }

        foreach (var id in masterData.VehicleIds)
        {
            if (vehicles.TryGetValue(id, out var vehicle) && !vehicle.IsArchived && !coupled.Contains(id) && vehicle.Kind is VehicleTypeKind.Rigid or VehicleTypeKind.Van)
            {
                fleet.Units.Add(new TestFleetUnit(null, id, [vehicle.Name, .. new[] { vehicle.LicencePlate }.OfType<string>()]));
            }
        }

        var drivers = await _masterData.DriversAsync(masterData.DriverIds);
        fleet.DriverIds.AddRange(masterData.DriverIds.Where(id => drivers.TryGetValue(id, out var d) && !d.IsArchived));
        fleet.Carriers.AddRange(masterData.Relations
            .Where(r => r.Roles.HasFlag(RelationRoles.Carrier) && !r.Roles.HasFlag(RelationRoles.OwnCompany))
            .Select(r => new TestCarrier(r.Id, r.Name)));

        var own = masterData.OwnCompanyRelationId is { } ownId ? masterData.Relations.FirstOrDefault(r => r.Id == ownId) : null;
        if (own is { Addresses.Count: > 0 })
        {
            var address = own.Addresses[0];
            fleet.Home = new TestHomeBase(own.Id, address.Id, address.Latitude, address.Longitude);
        }

        var depotAddressIds = masterData.Relations
            .Where(r => r.Roles.HasFlag(RelationRoles.Depot) && r.Addresses.Count > 0)
            .Select(r => (Guid?)r.Addresses[0].Id)
            .ToList();
        var depots = (await _masterData.AddressesAsync(depotAddressIds)).Values
            .Where(a => a.Latitude != null && a.Longitude != null)
            .Select(MasterDataReferences.ToDepot)
            .OrderBy(d => depotAddressIds.IndexOf(d.AddressId))
            .ToList();

        await unitOfWork.CompleteAsync();
        return new References(ownType, charterType, fleet, depots);
    }

    /// <summary>
    /// A handful of rules (5 for the average set, 20 for the large one), as a forwarder keeps them: deliveries of some
    /// customers (Customer scope) or to some consignees (Relation scope) are unloaded at the nearest depot and loaded again
    /// from there, overnight for some. Region-scoped rules are not generated (stops carry no region ids in the run).
    /// </summary>
    private List<ViaDepotRule> BuildViaDepotRules(TestDatasetContext context, MasterDataTestDataset masterData, OrdersTestDataset orders, IReadOnlyList<DepotLocation> depots)
    {
        var unload = FirstType(orders, WaypointActivity.Unload, "UNLOAD");
        var load = FirstType(orders, WaypointActivity.Load, "LOAD");
        var confirmed = orders.Orders.Where(o => o.HasLegs).ToList();
        var rules = new List<ViaDepotRule>();
        if (unload == null || depots.Count == 0 || confirmed.Count == 0)
        {
            return rules;
        }

        var random = context.Random;
        var relations = masterData.Relations.ToDictionary(r => r.Id);
        var count = Math.Clamp(context.Profile.Relations / 10_000 + 5, 5, 20);
        var used = new HashSet<Guid>();
        int customerPosition = 0, relationPosition = 0;
        for (var attempt = 0; rules.Count < count && attempt < count * 20; attempt++)
        {
            var order = confirmed[random.Next(confirmed.Count)];
            var byCustomer = random.NextDouble() < 0.6;
            Guid relationId;
            double latitude, longitude;
            if (byCustomer)
            {
                relationId = order.CustomerRelationId;
                if (!relations.TryGetValue(relationId, out var customer) || customer.Addresses.Count == 0)
                {
                    continue;
                }

                (latitude, longitude) = (customer.Addresses[0].Latitude, customer.Addresses[0].Longitude);
            }
            else
            {
                var drops = order.Stops.Where(s => s.WaypointTypeId == unload.Id).ToList();
                if (drops.Count == 0)
                {
                    continue;
                }

                var drop = drops[random.Next(drops.Count)];
                (relationId, latitude, longitude) = (drop.RelationId, drop.Latitude, drop.Longitude);
            }

            if ((relations.TryGetValue(relationId, out var relation) && relation.Roles.HasFlag(RelationRoles.Depot)) || !used.Add(relationId))
            {
                continue;
            }

            /* The nearest depot that is not next door: a depot ten minutes away is not worth a cross-dock. */
            var depot = depots
                .Select(d => (Depot: d, Km: Km(latitude, longitude, d.Latitude!.Value, d.Longitude!.Value)))
                .Where(d => d.Km >= 15)
                .OrderBy(d => d.Km)
                .Select(d => d.Depot)
                .FirstOrDefault();
            if (depot == null)
            {
                continue;
            }

            var rule = new ViaDepotRule(_guids.Create(), _currentTenant.Id, byCustomer ? ViaDepotScope.Customer : ViaDepotScope.Relation, relationId, null,
                depot.RelationId, depot.AddressId, unload.Id);
            rule.SetStopWaypointType(unload.Id);
            rule.SetWaypointTypes(unload.Id, load?.Id);
            rule.SetOvernight(random.NextDouble() < 0.4);
            rule.SetPosition(byCustomer ? ++customerPosition : ++relationPosition);
            rules.Add(rule);
        }

        return rules;
    }

    private static TestWaypointType? FirstType(OrdersTestDataset orders, WaypointActivity activity, string code)
        => orders.WaypointTypes.Values
            .Where(t => t.Activity == activity)
            .OrderBy(t => t.Code == code ? 0 : 1)
            .ThenBy(t => t.Code, StringComparer.Ordinal)
            .FirstOrDefault();

    /// <summary>Trip types stay (tenant configuration, number formats); their defaults that point at wiped Master Data records go.</summary>
    private async Task<int> ClearTripTypeDefaultsAsync()
    {
        using var unitOfWork = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var cleared = 0;
        foreach (var type in await _tripTypes.GetListAsync())
        {
            if (type.DefaultCarrierRelationId == null && type.DefaultCarrierContactPersonId == null && type.DefaultVehicleCombinationId == null
                && type.DefaultVehicleIds.Count == 0 && type.DefaultDriverIds.Count == 0 && type.DefaultDepartureRelationId == null
                && type.DefaultDepartureAddressId == null && type.DefaultArrivalRelationId == null && type.DefaultArrivalAddressId == null)
            {
                continue;
            }

            type.SetDefaultCarrier(null, null);
            type.SetDefaultVehicles(null, null);
            type.SetDefaultDrivers(null);
            type.SetDefaultDeparture(null, null, type.DefaultDepartureTime);
            type.SetDefaultArrival(null, null);
            await _tripTypes.UpdateAsync(type);
            cleared++;
        }

        await unitOfWork.CompleteAsync();
        return cleared;
    }

    /// <summary>Requests one target type for the ids in transactions of <see cref="RequestChunkSize"/> (each commits its outbox rows).</summary>
    private async Task<int> RequestAsync(string targetType, List<Guid> ids)
    {
        var count = 0;
        foreach (var chunk in ids.Chunk(RequestChunkSize))
        {
            using var unitOfWork = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
            count += await _scheduler.RequestManyAsync(targetType, chunk, RequestReason);
            await unitOfWork.CompleteAsync();
        }

        return count;
    }

    private DateTimeOffset Now()
    {
        var now = _clock.Now;
        var utc = now.Kind == DateTimeKind.Local ? now.ToUniversalTime() : DateTime.SpecifyKind(now, DateTimeKind.Utc);
        return new DateTimeOffset(utc, TimeSpan.Zero);
    }

    private static double Km(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371.0;
        var dLat = (lat2 - lat1) * Math.PI / 180;
        var dLon = (lon2 - lon1) * Math.PI / 180;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                + Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * R * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }
}
