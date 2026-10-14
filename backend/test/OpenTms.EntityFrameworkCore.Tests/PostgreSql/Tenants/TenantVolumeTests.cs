using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using OpenTms.Platform.Editions;
using OpenTms.Platform.Grid;
using OpenTms.Platform.Tenants;
using Shouldly;
using Volo.Abp.Data;
using Volo.Abp.MultiTenancy;
using Volo.Abp.TenantManagement;
using Volo.Abp.Uow;
using Xunit;
using Xunit.Abstractions;

namespace OpenTms.PostgreSql.Tenants;

/// <summary>
/// VOLUME TEST (AD-03: 1,000 tenants expected; × 5 per the test-engineer matrix; performance-budgets interactive reads
/// p95 &lt; 300 ms): 5,000 tenant rows, each with its own connection string, spread over 10 editions, in the host
/// database; then the Tenants grid (paged, filtered, sorted) and the edition list with its tenant counts
/// (EfCoreTenantEditionQuery) are timed. Same convention as the other *VolumeTests: each query runs once untimed to
/// warm EF Core's query cache, then timed; the assertion is the 1,500 ms CI tripwire (the budget itself is measured by
/// the nightly k6 run), and every measurement is printed against the 300 ms budget. The rows live in one rolled-back
/// transaction, so nothing stays behind. Risk class: Critical — Platform (the host's tenant administration).
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class TenantVolumeTests : OpenTmsPostgreSqlTestBase
{
    private const int Tenants = 5_000;
    private const int Editions = 10;
    private static readonly TimeSpan Budget = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan Tripwire = TimeSpan.FromMilliseconds(1_500);

    private readonly ITestOutputHelper _output;

    public TenantVolumeTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static Tenant NewTenant(Guid id, string name)
    {
        var ctor = typeof(Tenant).GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .OrderByDescending(c => c.GetParameters().Length)
            .First(c => c.GetParameters().FirstOrDefault()?.ParameterType == typeof(Guid));
        var args = ctor.GetParameters()
            .Select((p, i) => i == 0 ? (object?)id : p.Name!.Contains("ormalized") ? name.ToUpperInvariant() : p.ParameterType == typeof(string) ? name : null)
            .ToArray();
        return (Tenant)ctor.Invoke(args);
    }

    private static GridRequest Page(int start = 0, int end = 100, string? search = null, string? sortBy = null, string? sortDir = null) => new()
    {
        StartRow = start,
        EndRow = end,
        WildcardSearch = search,
        SortModels = sortBy == null ? [] : [new SortModel(sortBy, sortDir ?? "asc")]
    };

    [Fact]
    public async Task The_tenants_grid_and_the_edition_tenant_counts_stay_within_the_interactive_read_budget_at_five_times_the_expected_volume()
    {
        var tenantService = GetRequiredService<ITenantAdministrationAppService>();
        var editionService = GetRequiredService<IEditionAdministrationAppService>();
        var editionQuery = GetRequiredService<ITenantEditionQuery>();
        var tenantRepository = GetRequiredService<ITenantRepository>();

        using (GetRequiredService<ICurrentTenant>().Change(null))
        using (var unitOfWork = GetRequiredService<IUnitOfWorkManager>().Begin(new AbpUnitOfWorkOptions { IsTransactional = true }, requiresNew: true))
        {
            try
            {
                var existingTenants = await tenantRepository.GetCountAsync();
                var insert = Stopwatch.StartNew();

                var editions = new List<EditionRowDto>();
                for (var e = 0; e < Editions; e++)
                {
                    editions.Add(await editionService.CreateAsync(new EditionCreateUpdateDto { DisplayName = $"Volume edition {e:D2}" }));
                }

                var tenants = new List<Tenant>(Tenants);
                var states = new[] { TenantProvisioningState.Succeeded, TenantProvisioningState.Failed, TenantProvisioningState.Queued, null };
                for (var i = 0; i < Tenants; i++)
                {
                    var tenant = NewTenant(Guid.NewGuid(), $"volume-tenant-{i:D5}");
                    tenant.SetDefaultConnectionString(
                        $"Host=localhost;Port=5432;Database={TenantDatabaseNaming.For(tenant.Name, tenant.Id)};Username={TenantDatabaseNaming.For(tenant.Name, tenant.Id)};Password=volume");
                    tenant.SetProperty(TenantProperties.EditionId, editions[i % Editions].Id);
                    tenant.SetProperty(TenantProperties.ProvisioningState, states[i % states.Length]);
                    tenants.Add(tenant);
                }

                await tenantRepository.InsertManyAsync(tenants, autoSave: true);
                _output.WriteLine($"inserted {Tenants} tenants over {Editions} editions in {insert.Elapsed.TotalSeconds:F1} s");

                var editionIds = editions.Select(e => e.Id).ToList();
                var measurements = new List<(string Name, TimeSpan Elapsed)>
                {
                    await MeasureAsync("tenant list page 1 (100 rows, sorted by name)", () => tenantService.GetListAsync(Page())),
                    await MeasureAsync("tenant list page 25", () => tenantService.GetListAsync(Page(2_400, 2_500))),
                    await MeasureAsync("tenant list last page", () => tenantService.GetListAsync(Page(4_900, 5_000))),
                    await MeasureAsync("tenant list filtered (search)", () => tenantService.GetListAsync(Page(search: "volume-tenant-012"))),
                    await MeasureAsync("tenant list sorted by creation time desc", () => tenantService.GetListAsync(Page(sortBy: "creationTime", sortDir: "desc"))),
                    await MeasureAsync("tenant list sorted by name desc", () => tenantService.GetListAsync(Page(sortBy: "name", sortDir: "desc"))),
                    await MeasureAsync("edition list with tenant counts", () => editionService.GetListAsync(Page())),
                    await MeasureAsync("tenant counts of 10 editions (EfCoreTenantEditionQuery)", () => editionQuery.CountTenantsByEditionAsync(editionIds)),
                    await MeasureAsync("tenant count of one edition", () => editionService.GetAsync(editionIds[3])),
                };

                foreach (var (name, elapsed) in measurements)
                {
                    _output.WriteLine($"{name}: {elapsed.TotalMilliseconds:F0} ms (budget {Budget.TotalMilliseconds} ms, tripwire {Tripwire.TotalMilliseconds} ms)");
                }

                measurements.Where(m => m.Elapsed > Tripwire).Select(m => $"{m.Name}: {m.Elapsed.TotalMilliseconds:F0} ms").ToList()
                    .ShouldBeEmpty($"queries over the {Tripwire.TotalMilliseconds} ms tripwire (budget {Budget.TotalMilliseconds} ms)");

                /* The results are the right ones, not just fast. */
                var first = await tenantService.GetListAsync(Page());
                first.TotalCount.ShouldBe(existingTenants + Tenants);
                first.Rows.Count.ShouldBe(100);

                var filtered = await tenantService.GetListAsync(Page(search: "volume-tenant-012"));
                filtered.FilteredCount.ShouldBe(100);
                filtered.Rows.ShouldAllBe(r => r.Name!.StartsWith("volume-tenant-012"));

                var byNameDesc = await tenantService.GetListAsync(Page(sortBy: "name", sortDir: "desc"));
                byNameDesc.Rows.Select(r => r.Name).ShouldBe(byNameDesc.Rows.Select(r => r.Name).OrderByDescending(n => n, StringComparer.Ordinal));

                var counts = await editionQuery.CountTenantsByEditionAsync(editionIds);
                counts.Count.ShouldBe(Editions);
                counts.Values.ShouldAllBe(c => c == Tenants / Editions);
                (await editionService.GetAsync(editionIds[3])).TenantCount.ShouldBe(Tenants / Editions);
                (await editionService.GetListAsync(Page())).Rows.Where(r => r.DisplayName!.StartsWith("Volume edition"))
                    .ShouldAllBe(r => r.TenantCount == Tenants / Editions);
            }
            finally
            {
                await unitOfWork.RollbackAsync();
            }
        }
    }

    private static async Task<(string Name, TimeSpan Elapsed)> MeasureAsync<T>(string name, Func<Task<T>> query)
    {
        await query();
        var stopwatch = Stopwatch.StartNew();
        await query();
        stopwatch.Stop();
        return (name, stopwatch.Elapsed);
    }
}
