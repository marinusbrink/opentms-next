using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenTms.PlanningExecution.TransportTrips;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;

namespace OpenTms.PlanningExecution.Seed;

/// <summary>
/// Design 68 §1, the companion of migration <c>PlanningPlanboard</c> ("PlanningPlanboardBackfill"): a trip saved with a
/// vehicle combination before the members were stored gets its combination's active members as vehicle rows
/// (<see cref="TransportTrip.FillCombinationMembers"/>), so the Scheduler finds it on its tractor and trailer rows.
/// Idempotent: only trips with a combination and no vehicle rows are read, in batches, with one batched Master Data
/// lookup per batch; a combination that no longer resolves (or has no members) leaves its trips as they are and they are
/// skipped for the rest of the run. Runs per tenant
/// through ABP's data seeder wherever the application layer (and so Master Data's published lookup) is loaded; logs
/// tenant id and counts only.
/// </summary>
public class PlanningPlanboardBackfillContributor : IDataSeedContributor, ITransientDependency
{
    private const int BatchSize = 200;

    private readonly IRepository<TransportTrip, Guid> _trips;
    private readonly MasterDataReferences _masterData;
    private readonly ICurrentTenant _currentTenant;
    private readonly ILogger<PlanningPlanboardBackfillContributor> _logger;

    public PlanningPlanboardBackfillContributor(
        IRepository<TransportTrip, Guid> trips,
        MasterDataReferences masterData,
        ICurrentTenant currentTenant,
        ILogger<PlanningPlanboardBackfillContributor> logger)
    {
        _trips = trips;
        _masterData = masterData;
        _currentTenant = currentTenant;
        _logger = logger;
    }

    [UnitOfWork]
    public virtual async Task SeedAsync(DataSeedContext context)
    {
        if (context.TenantId == null)
        {
            return;
        }

        using (_currentTenant.Change(context.TenantId))
        {
            var filled = 0;
            var skipped = new List<Guid>();
            while (true)
            {
                var exclude = skipped.ToList();
                var batch = await _trips.AsyncExecuter.ToListAsync((await _trips.WithDetailsAsync(t => t.Vehicles))
                    .Where(t => t.VehicleCombinationId != null && !t.Vehicles.Any() && !exclude.Contains(t.Id))
                    .OrderBy(t => t.Id)
                    .Take(BatchSize));
                if (batch.Count == 0)
                {
                    break;
                }

                var combinations = await _masterData.CombinationsAsync(batch.Select(t => t.VehicleCombinationId));
                var changed = batch.Where(t => combinations.TryGetValue(t.VehicleCombinationId!.Value, out var combination)
                                               && t.FillCombinationMembers(combination.VehicleIds)).ToList();
                skipped.AddRange(batch.Except(changed).Select(t => t.Id));
                if (changed.Count > 0)
                {
                    await _trips.UpdateManyAsync(changed, autoSave: true);
                    filled += changed.Count;
                }

                if (batch.Count < BatchSize)
                {
                    break;
                }
            }

            if (filled > 0)
            {
                _logger.LogInformation("Planning & Execution backfill for tenant {TenantId}: combination members stored on {Count} transport trips.", context.TenantId, filled);
            }
        }
    }
}
