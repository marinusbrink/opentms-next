using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenTms.PlanningExecution.TransportTrips;
using OpenTms.Platform.Localization;
using OpenTms.Platform.NumberSequences;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;

namespace OpenTms.PlanningExecution.Seed;

/// <summary>
/// Seeds the default transport trip type per tenant database (design 53, Migration strategy), run by the DbMigrator
/// and the provisioning job through ABP's data seeder. Idempotent on the code: an existing or soft-deleted row is never
/// touched. Skips the host (host-level master data is parked, assessment §4). Logs tenant id and counts only.
/// </summary>
public class PlanningExecutionDataSeedContributor : IDataSeedContributor, ITransientDependency
{
    private readonly IRepository<TransportTripType, Guid> _tripTypes;
    private readonly ICurrentTenant _currentTenant;
    private readonly IDataFilter _dataFilter;
    private readonly IGuidGenerator _guidGenerator;
    private readonly ILogger<PlanningExecutionDataSeedContributor> _logger;

    public PlanningExecutionDataSeedContributor(
        IRepository<TransportTripType, Guid> tripTypes,
        ICurrentTenant currentTenant,
        IDataFilter dataFilter,
        IGuidGenerator guidGenerator,
        ILogger<PlanningExecutionDataSeedContributor> logger)
    {
        _tripTypes = tripTypes;
        _currentTenant = currentTenant;
        _dataFilter = dataFilter;
        _guidGenerator = guidGenerator;
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
            List<TransportTripType> existing;
            using (_dataFilter.Disable<ISoftDelete>())
            {
                existing = await _tripTypes.GetListAsync();
            }

            if (existing.Any(t => t.Code == "STD"))
            {
                return;
            }

            var standard = new TransportTripType(
                _guidGenerator.Create(),
                context.TenantId,
                "STD",
                LocalizedName.Create(new Dictionary<string, string?> { ["en"] = "Standard", ["nl"] = "Standaard" }),
                NumberSequenceFormat.Create("TR", includeYear: true, includeMonth: false, separator: "-", digits: 5));
            standard.SetDefaultExecution(TripExecution.OwnFleet);
            if (!existing.Any(t => t.IsDefault && !t.IsDeleted))
            {
                standard.SetDefault(true);
            }

            await _tripTypes.InsertAsync(standard);
            _logger.LogInformation("Planning & Execution seed for tenant {TenantId}: 1 transport trip type inserted.", context.TenantId);
        }
    }
}
