using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenTms.Integrations.Routing;
using OpenTms.PlanningExecution.Legs;
using OpenTms.PlanningExecution.TransportTrips;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Entities;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Tracing;
using Volo.Abp.Uow;

namespace OpenTms.PlanningExecution.Planboard;

/// <summary>Ids only: Hangfire stores job arguments in plain text.</summary>
[BackgroundJobName("OpenTms.PlanningExecution.PlanboardStopOptimization")]
public class PlanboardStopOptimizationArgs
{
    public Guid? TenantId { get; set; }

    public Guid TripId { get; set; }

    public string? CorrelationId { get; set; }
}

/// <summary>
/// Design 68 §5: optimises a trip's stop order in the requesting tenant's own transaction (constitution rule 1: the tenant
/// comes from the arguments, never from elsewhere). The free stops (<see cref="PlanboardStopOptimization"/>) go to
/// <see cref="IRouteOptimizer"/> with the start (the last fixed stop, else the departure address unless it is the first
/// stop), the end (the fixed last stop, else the arrival address unless it is the last stop), the legs' windows and the
/// pickup-before-delivery pairs; the answer is applied with <see cref="TransportTrip.MoveStop"/>, then the trip is saved,
/// its calculation requested and the record channel notified (<see cref="TripOperations.ChangedAsync"/>). Idempotent: a
/// second run finds the optimised order and changes nothing. A trip that is gone, final, archived or has fewer than two
/// free stops, or a free stop without coordinates, is skipped with a log line.
/// </summary>
public class PlanboardStopOptimizationJob : AsyncBackgroundJob<PlanboardStopOptimizationArgs>, ITransientDependency
{
    private readonly ICurrentTenant _currentTenant;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly ICorrelationIdProvider _correlationIds;
    private readonly TripOperations _ops;
    private readonly MasterDataReferences _masterData;
    private readonly IRouteOptimizer _optimizer;

    public PlanboardStopOptimizationJob(
        ICurrentTenant currentTenant,
        IUnitOfWorkManager unitOfWorkManager,
        ICorrelationIdProvider correlationIds,
        TripOperations ops,
        MasterDataReferences masterData,
        IRouteOptimizer optimizer)
    {
        _currentTenant = currentTenant;
        _unitOfWorkManager = unitOfWorkManager;
        _correlationIds = correlationIds;
        _ops = ops;
        _masterData = masterData;
        _optimizer = optimizer;
    }

    public override async Task ExecuteAsync(PlanboardStopOptimizationArgs args)
    {
        using var correlation = _correlationIds.Change(string.IsNullOrWhiteSpace(args.CorrelationId) ? null : args.CorrelationId);
        using var tenant = _currentTenant.Change(args.TenantId);
        using var unitOfWork = _unitOfWorkManager.Begin(new AbpUnitOfWorkOptions(isTransactional: true), requiresNew: true);

        TransportTrip trip;
        try
        {
            trip = await _ops.GetWithDetailsAsync(args.TripId);
        }
        catch (EntityNotFoundException)
        {
            Logger.LogInformation("Stop optimisation of trip {TripId} (tenant {TenantId}) skipped: the trip no longer exists.", args.TripId, args.TenantId);
            await unitOfWork.CompleteAsync();
            return;
        }

        var plan = PlanboardStopOptimization.Of(trip);
        if (!plan.CanOptimize(trip))
        {
            Logger.LogInformation("Stop optimisation of trip {TripId} (tenant {TenantId}) skipped: it takes no changes or has fewer than two free stops.", trip.Id, args.TenantId);
            await unitOfWork.CompleteAsync();
            return;
        }

        if (plan.Free.Any(s => !s.HasCoordinates))
        {
            Logger.LogInformation("Stop optimisation of trip {TripId} (tenant {TenantId}) skipped: a stop has no coordinates.", trip.Id, args.TenantId);
            await unitOfWork.CompleteAsync();
            return;
        }

        await _ops.Notices.TripAsync(trip);
        var request = await RequestAsync(trip, plan);
        var result = await _optimizer.OptimizeAsync(request);
        if (result == null)
        {
            Logger.LogInformation("Stop optimisation of trip {TripId} (tenant {TenantId}): the optimiser found no order.", trip.Id, args.TenantId);
            await unitOfWork.CompleteAsync();
            return;
        }

        var current = plan.Free.Select(s => s.Id).ToList();
        if (result.StopIds.Count != current.Count || result.StopIds.Except(current).Any() || result.StopIds.SequenceEqual(current))
        {
            Logger.LogInformation("Stop optimisation of trip {TripId} (tenant {TenantId}): the order stays ({Method}).", trip.Id, args.TenantId, result.Method);
            await unitOfWork.CompleteAsync();
            return;
        }

        var position = plan.Prefix.Count + 1;
        foreach (var stopId in result.StopIds)
        {
            if (trip.GetStop(stopId).Position != position)
            {
                trip.MoveStop(stopId, position);
            }

            position++;
        }

        await _ops.ChangedAsync(trip, TripChangeKind.Stops, recalculate: true);
        await unitOfWork.CompleteAsync();
        Logger.LogInformation("Stop optimisation of trip {TripId} (tenant {TenantId}) reordered {Count} stops ({Method}).", trip.Id, args.TenantId, current.Count, result.Method);
    }

    private async Task<RouteOptimizationRequest> RequestAsync(TransportTrip trip, PlanboardStopOptimization plan)
    {
        var lastFixed = plan.Prefix.LastOrDefault();
        var firstFixed = plan.Suffix.FirstOrDefault();
        var departureId = lastFixed == null && !trip.FirstLastStopRule.HasFlag(FirstLastStopRule.DepartureIsFirstStop) ? trip.DepartureAddressId : null;
        var arrivalId = firstFixed == null && !trip.FirstLastStopRule.HasFlag(FirstLastStopRule.ArrivalIsLastStop) ? trip.ArrivalAddressId : null;
        var addresses = departureId == null && arrivalId == null ? new() : await _masterData.AddressesAsync([departureId, arrivalId]);

        GeoPoint? Address(Guid? id) => id != null && addresses.TryGetValue(id.Value, out var a) && a.Latitude != null && a.Longitude != null
            ? new GeoPoint(a.Latitude.Value, a.Longitude.Value)
            : null;

        GeoPoint? StopPoint(TripStop? stop) => stop is { HasCoordinates: true } ? new GeoPoint(stop.Latitude!.Value, stop.Longitude!.Value) : null;

        var legs = (await _ops.LegsOnTripAsync(trip.Id)).ToDictionary(l => l.Id);
        var free = plan.Free.ToDictionary(s => s.Id);
        var stops = plan.Free.Select(stop =>
        {
            var windows = stop.Legs.Where(link => legs.ContainsKey(link.LegId)).Select(link => legs[link.LegId].End(link.Role)).ToList();
            var from = windows.Where(w => w.WindowFrom != null).Select(w => w.WindowFrom!.Value).DefaultIfEmpty().Max();
            var till = windows.Where(w => w.WindowTill != null).Select(w => w.WindowTill!.Value).DefaultIfEmpty(DateTimeOffset.MaxValue).Min();
            return new RouteOptimizationStop(
                stop.Id,
                new GeoPoint(stop.Latitude!.Value, stop.Longitude!.Value),
                windows.Any(w => w.WindowFrom != null) ? from : null,
                windows.Any(w => w.WindowTill != null) ? till : null,
                stop.PlannedDurationMin ?? 0);
        }).ToList();

        /* Pickup before delivery: the stop holding a leg's origin before the stop holding its destination. */
        var originOf = new Dictionary<Guid, Guid>();
        var destinationOf = new Dictionary<Guid, Guid>();
        foreach (var stop in plan.Free)
        {
            foreach (var link in stop.Legs)
            {
                (link.Role == StopLegRole.Origin ? originOf : destinationOf)[link.LegId] = stop.Id;
            }
        }

        var precedences = originOf
            .Where(o => destinationOf.TryGetValue(o.Key, out var d) && d != o.Value && free.ContainsKey(d))
            .Select(o => new RouteOptimizationPrecedence(o.Value, destinationOf[o.Key]))
            .Distinct()
            .ToList();

        var vehicles = await _masterData.VehiclesAsync(trip.VehicleIdsInOrder);
        var spec = vehicles.Count == 0 ? null : MasterDataReferences.SpecOf(trip.VehicleIdsInOrder.Where(vehicles.ContainsKey).Select(id => vehicles[id]).ToList());
        return new RouteOptimizationRequest(
            StopPoint(lastFixed) ?? Address(departureId),
            StopPoint(firstFixed) ?? Address(arrivalId),
            stops,
            precedences,
            spec);
    }
}
