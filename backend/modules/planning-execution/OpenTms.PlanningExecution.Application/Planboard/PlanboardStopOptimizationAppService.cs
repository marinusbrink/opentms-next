using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using OpenTms.PlanningExecution.Permissions;
using OpenTms.Platform.Features;
using Volo.Abp;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.Features;
using Volo.Abp.Tracing;

namespace OpenTms.PlanningExecution.Planboard;

/// <summary>
/// Design 68 §5 (PO decision D20): checks the trip and the caller's permission, queues
/// <see cref="PlanboardStopOptimizationJob"/> in the tenant's context and returns at once; the job reorders the stops,
/// requests the calculation and notifies the record channel. Reached only through
/// <c>OpenTms.Controllers.Planning.PlanboardStopOptimizationController</c> (<c>POST /api/planning/planboard/{id}/optimize-stops</c>,
/// 202 Accepted).
/// </summary>
[RemoteService(IsEnabled = false)]
[RequiresFeature(AppFeatures.Planboard)]
public class PlanboardStopOptimizationAppService : PlanningExecutionAppServiceBase, IPlanboardStopOptimizationAppService
{
    private readonly TransportTrips.TripOperations _ops;
    private readonly IBackgroundJobManager _jobs;
    private readonly ICorrelationIdProvider _correlationIds;

    public PlanboardStopOptimizationAppService(TransportTrips.TripOperations ops, IBackgroundJobManager jobs, ICorrelationIdProvider correlationIds)
    {
        _ops = ops;
        _jobs = jobs;
        _correlationIds = correlationIds;
    }

    [Authorize(PlanningExecutionPermissions.TransportTrips.ManageStops)]
    public virtual async Task<PlanboardOptimizeStopsDto> OptimizeStopsAsync(Guid id)
    {
        var trip = await _ops.GetWithDetailsAsync(id);
        trip.EnsureAssignable();
        if (!PlanboardStopOptimization.Of(trip).CanOptimize(trip))
        {
            throw new BusinessException(PlanningExecutionErrorCodes.StopsNotOptimizable).WithData("number", trip.Number);
        }

        var jobId = await _jobs.EnqueueAsync(new PlanboardStopOptimizationArgs
        {
            TenantId = CurrentTenant.Id,
            TripId = id,
            CorrelationId = _correlationIds.Get()
        });
        Logger.LogInformation("Stop optimisation of trip {TripId} queued for tenant {TenantId} as job {JobId}.", id, CurrentTenant.Id, jobId);
        return new PlanboardOptimizeStopsDto { TripId = id, JobId = jobId };
    }
}
