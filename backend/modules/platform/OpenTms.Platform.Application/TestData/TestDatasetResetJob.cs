using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenTms.Platform.Tenants;
using Volo.Abp.Auditing;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Timing;

namespace OpenTms.Platform.TestData;

/* "Reset to dataset" (docs/designs/65-tenant-test-datasets.md), in the tenant's context:
 *   1. every module wipes its business data (descending Order, so the dependants go first);
 *   2. the normal seed runs, restoring the reference data the modules seed;
 *   3. every module generates its part of the dataset (ascending Order).
 * The whole run is idempotent by construction: it always starts by wiping. The entity history (functional
 * logbook) is not written for generated records: no audit scope is opened, and the job is [DisableAuditing]. */
[DisableAuditing]
public class TestDatasetResetJob : AsyncBackgroundJob<TestDatasetResetArgs>, ITransientDependency
{
    private readonly TenantStateWriter _stateWriter;
    private readonly IEnumerable<ITestDatasetContributor> _contributors;
    private readonly IDataSeeder _dataSeeder;
    private readonly ICurrentTenant _currentTenant;
    private readonly TestAddressRegister _addressRegister;
    private readonly IClock _clock;

    public TestDatasetResetJob(
        TenantStateWriter stateWriter,
        IEnumerable<ITestDatasetContributor> contributors,
        IDataSeeder dataSeeder,
        ICurrentTenant currentTenant,
        TestAddressRegister addressRegister,
        IClock clock)
    {
        _stateWriter = stateWriter;
        _contributors = contributors;
        _dataSeeder = dataSeeder;
        _currentTenant = currentTenant;
        _addressRegister = addressRegister;
        _clock = clock;
    }

    public override async Task ExecuteAsync(TestDatasetResetArgs args)
    {
        var tenant = await _stateWriter.FindAsync(args.TenantId);
        if (tenant == null)
        {
            Logger.LogWarning("Tenant {TenantId} no longer exists; dataset reset skipped", args.TenantId);
            return;
        }

        if (tenant.GetProperty<Guid?>(TenantProperties.DatasetResetRequestId) == args.RequestId)
        {
            Logger.LogInformation("Dataset reset request for tenant {TenantId} already handled", tenant.Id);
            return;
        }

        var contributors = _contributors.OrderBy(c => c.Order).ToList();
        var stopwatch = Stopwatch.StartNew();
        Logger.LogInformation("Dataset reset {Dataset} started for tenant {TenantId}", args.Dataset, tenant.Id);
        await _stateWriter.SetDatasetResetStateAsync(tenant.Id, TestDatasetResetState.Running);

        try
        {
            /* Inside the try: Hangfire keeps queued arguments across deploys, and a dataset removed meanwhile must end in
             * Failed, not leave the tenant Queued (which refuses every new reset). */
            var profile = TestDatasetProfile.Get(args.Dataset);
            var context = new TestDatasetContext(
                tenant.Id,
                profile,
                new TestAddressBook(await _addressRegister.LoadAsync(), new Random(profile.Seed)),
                _clock.Now);

            using (_currentTenant.Change(tenant.Id, tenant.Name))
            {
                foreach (var contributor in Enumerable.Reverse(contributors))
                {
                    Logger.LogInformation("Tenant {TenantId}: wiping {Contributor}", tenant.Id, contributor.GetType().Name);
                    await contributor.WipeAsync(context);
                }

                await _dataSeeder.SeedAsync(new DataSeedContext(tenant.Id));

                foreach (var contributor in contributors)
                {
                    Logger.LogInformation(
                        "Tenant {TenantId}: generating {Contributor} ({Elapsed}s)",
                        tenant.Id, contributor.GetType().Name, (int)stopwatch.Elapsed.TotalSeconds);
                    await contributor.GenerateAsync(context);
                }
            }

            await _stateWriter.SetDatasetResetStateAsync(tenant.Id, TestDatasetResetState.Succeeded, args.RequestId);
            Logger.LogInformation(
                "Dataset reset {Dataset} succeeded for tenant {TenantId} in {Elapsed}s",
                args.Dataset, tenant.Id, (int)stopwatch.Elapsed.TotalSeconds);
        }
        catch (Exception ex)
        {
            /* Not rethrown: a Hangfire retry would overlap a new reset started from the Failed state. */
            await _stateWriter.SetDatasetResetStateAsync(
                tenant.Id, TestDatasetResetState.Failed, args.RequestId, $"{ex.GetType().Name}: {ex.Message}");
            Logger.LogError(ex, "Dataset reset {Dataset} FAILED for tenant {TenantId}", args.Dataset, tenant.Id);
        }
    }
}
