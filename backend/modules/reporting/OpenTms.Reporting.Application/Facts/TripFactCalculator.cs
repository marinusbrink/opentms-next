using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.PlanningExecution.Published;
using OpenTms.Platform.DerivedValues;
using OpenTms.Reporting.Features;
using OpenTms.Reporting.Metrics;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Features;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Timing;

namespace OpenTms.Reporting.Facts;

/// <summary>
/// The calculator of <see cref="ReportingFactConsts.TripFactTargetType"/> (design 75, flow 2): reads the trip through Planning's
/// published reader and upserts its fact, or deletes the fact when the trip is gone. Idempotent; runs in the job's transaction
/// under the record's tenant, and a newer save discards an older run.
/// </summary>
public class TripFactCalculator : IDerivedValueCalculator, ITransientDependency
{
    private readonly ITripFactReader _trips;
    private readonly IRepository<TripFact, Guid> _facts;
    private readonly IFeatureChecker _features;
    private readonly ICurrentTenant _currentTenant;
    private readonly IClock _clock;

    public TripFactCalculator(ITripFactReader trips, IRepository<TripFact, Guid> facts, IFeatureChecker features, ICurrentTenant currentTenant, IClock clock)
    {
        _trips = trips;
        _facts = facts;
        _features = features;
        _currentTenant = currentTenant;
        _clock = clock;
    }

    public async Task CalculateAsync(DerivedValueCalculationContext context)
    {
        /* A job requested just before the feature went off: without the feature no fact is written. */
        if (!await _features.IsEnabledAsync(ReportingFeatures.Metrics))
        {
            return;
        }

        var source = await _trips.GetAsync(context.TargetId, context.CancellationToken);
        var fact = await _facts.FindAsync(context.TargetId, cancellationToken: context.CancellationToken);
        if (source == null)
        {
            if (fact != null)
            {
                await _facts.DeleteAsync(fact, cancellationToken: context.CancellationToken);
            }

            return;
        }

        var (measured, onTime) = CountStops(source.Stops);
        var isNew = fact == null;
        fact ??= new TripFact(context.TargetId, _currentTenant.Id);
        var changed = fact.Apply(
            source.TripDate,
            source.Status,
            source.Execution,
            source.CarrierRelationId,
            source.PrimaryVehicleId,
            source.OrganizationUnitId,
            source.TransportTripTypeId,
            source.CostAmount,
            source.RevenueAmount,
            source.Co2Kg,
            source.LoadFactor,
            source.PlannedDistanceM,
            source.ActualDistanceM,
            measured,
            onTime,
            _clock.Now);

        if (isNew)
        {
            await _facts.InsertAsync(fact, cancellationToken: context.CancellationToken);
        }
        else if (changed)
        {
            await _facts.UpdateAsync(fact, cancellationToken: context.CancellationToken);
        }
    }

    /// <summary>
    /// Stops with both a planned and an actual arrival; of those, at most <see cref="MetricCatalog.OnTimeToleranceMinutes"/> minutes
    /// after the planned arrival, exactly that many included. Early counts as on time.
    /// </summary>
    public static (int Measured, int OnTime) CountStops(IReadOnlyCollection<TripFactStopDto> stops)
    {
        var tolerance = TimeSpan.FromMinutes(MetricCatalog.OnTimeToleranceMinutes);
        var measured = stops.Where(s => s.PlannedArrival != null && s.ActualArrival != null).ToList();
        return (measured.Count, measured.Count(s => s.ActualArrival!.Value <= s.PlannedArrival!.Value + tolerance));
    }
}
