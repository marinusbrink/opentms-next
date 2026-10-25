using System;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Platform.Administration.Maintenance;
using OpenTms.Platform.Grid;
using Shouldly;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Entities;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace OpenTms.PostgreSql.Maintenance;

/// <summary>
/// The Databases tab against real PostgreSQL: the host database, one tenant with its own database
/// and one shared-tier tenant (PostgreSqlTestDatabases).
/// Risk class: Critical — host-only (the list names every tenant's database and server) and the
/// catalog queries must read the real sizes; High — the detail must describe the schema that ships.
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class DatabaseMaintenanceAppServiceTests : OpenTmsPostgreSqlTestBase
{
    private readonly IDatabaseMaintenanceAppService _service;
    private readonly ICurrentTenant _currentTenant;

    public DatabaseMaintenanceAppServiceTests()
    {
        _service = GetRequiredService<IDatabaseMaintenanceAppService>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
    }

    private static GridRequest Request(int start = 0, int end = 50, string? search = null, SortModel? sort = null) => new()
    {
        StartRow = start,
        EndRow = end,
        WildcardSearch = search,
        SortModels = sort == null ? [] : [sort]
    };

    [Fact]
    public async Task The_host_sees_the_host_database_first_and_every_tenant_database_with_real_figures()
    {
        var page = await _service.GetListAsync(Request());

        /* The test databases: the host (with the two shared-tier tenants: outbox-tenant-shared, transport-run-shared),
         * tenant A's own database and the committing tests' own database (transport-run-a; PostgreSqlTestDatabases). */
        page.TotalCount.ShouldBe(3);
        page.Rows.Count.ShouldBe(3);

        var host = page.Rows[0];
        host.IsHost.ShouldBeTrue();
        host.Key.ShouldBe("host");
        host.DatabaseName.ShouldBe(PostgreSqlTestDatabases.HostDatabase);
        host.ServerName.ShouldContain(":");
        host.ServerName.ShouldNotContain("Password", Case.Insensitive);
        host.SharedTenantCount.ShouldBe(2);
        host.IsReachable.ShouldBeTrue();
        host.SizeBytes.ShouldNotBeNull();
        host.SizeBytes.Value.ShouldBeGreaterThan(0);
        host.TransactionsCommitted.ShouldNotBeNull();

        page.Rows.Skip(1).Select(r => r.TenantId).ShouldBe([PostgreSqlTestDatabases.TenantAId, PostgreSqlTestDatabases.TransportTenantAId], ignoreOrder: true);
        var tenant = page.Rows.Single(r => r.TenantId == PostgreSqlTestDatabases.TenantAId);
        tenant.IsHost.ShouldBeFalse();
        tenant.TenantId.ShouldBe(PostgreSqlTestDatabases.TenantAId);
        tenant.Key.ShouldBe(PostgreSqlTestDatabases.TenantAId.ToString("D"));
        tenant.TenantName.ShouldBe("outbox-tenant-a");
        tenant.DatabaseName.ShouldBe(PostgreSqlTestDatabases.TenantADatabase);
        tenant.IsReachable.ShouldBeTrue();
        tenant.SizeBytes.ShouldNotBeNull();
        tenant.SizeBytes.Value.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Search_sort_and_paging_apply_to_the_list()
    {
        var searched = await _service.GetListAsync(Request(search: "tenant-a"));
        searched.FilteredCount.ShouldBe(1);
        searched.TotalCount.ShouldBe(3);
        searched.Rows.ShouldHaveSingleItem().TenantName.ShouldBe("outbox-tenant-a");

        var sorted = await _service.GetListAsync(Request(sort: new SortModel("databaseName", "desc")));
        sorted.Rows.Select(r => r.DatabaseName).ShouldBe(sorted.Rows.Select(r => r.DatabaseName).OrderByDescending(n => n, StringComparer.Ordinal));

        var second = await _service.GetListAsync(Request(1, 2));
        second.Rows.ShouldHaveSingleItem();
        second.TotalCount.ShouldBe(3);
    }

    [Fact]
    public async Task The_summary_counts_databases_and_measures_the_host_blob_store()
    {
        var summary = await _service.GetSummaryAsync();

        /* The test databases: the host (with the two shared-tier tenants: outbox-tenant-shared, transport-run-shared),
         * tenant A's own database and the committing tests' own database (transport-run-a; PostgreSqlTestDatabases). */
        summary.DatabaseCount.ShouldBe(3);
        summary.TenantDatabaseCount.ShouldBe(2);
        summary.SharedTenantCount.ShouldBe(2);
        summary.UnreachableCount.ShouldBe(0);
        summary.TotalSizeBytes.ShouldBeGreaterThan(0);
        summary.HostBlobStorage.ShouldNotBeNull();
        summary.HostBlobStorage.BlobCount.ShouldBeGreaterThanOrEqualTo(0);
        summary.HostBlobStorage.TableSizeBytes.ShouldBeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task The_host_detail_lists_the_module_schemas_the_largest_tables_and_the_shared_tenants()
    {
        var detail = await _service.GetAsync("host");

        detail.IsHost.ShouldBeTrue();
        detail.DatabaseName.ShouldBe(PostgreSqlTestDatabases.HostDatabase);
        detail.SizeBytes.ShouldNotBeNull();
        detail.SharedTenantNames.ShouldBe(["outbox-tenant-shared"]);
        detail.Schemas.Select(s => s.SchemaName).ShouldContain("platform");
        detail.Schemas.Select(s => s.SchemaName).ShouldContain("public");
        detail.Schemas.First(s => s.SchemaName == "platform").TableCount.ShouldBeGreaterThan(0);
        detail.LargestTables.ShouldNotBeEmpty();
        detail.LargestTables.Count.ShouldBeLessThanOrEqualTo(20);
        detail.LargestTables.Select(t => t.TotalSizeBytes).ShouldBe(detail.LargestTables.Select(t => t.TotalSizeBytes).OrderByDescending(x => x));
        detail.BlobStorage.ShouldNotBeNull();
    }

    [Fact]
    public async Task A_tenant_database_detail_is_read_from_that_database()
    {
        var detail = await _service.GetAsync(PostgreSqlTestDatabases.TenantAId.ToString("D"));

        detail.IsHost.ShouldBeFalse();
        detail.TenantName.ShouldBe("outbox-tenant-a");
        detail.DatabaseName.ShouldBe(PostgreSqlTestDatabases.TenantADatabase);
        detail.SharedTenantNames.ShouldBeEmpty();
        detail.Schemas.Select(s => s.SchemaName).ShouldContain("platform");
    }

    [Fact]
    public async Task A_shared_tier_tenant_and_an_unknown_key_have_no_database_of_their_own()
    {
        await Should.ThrowAsync<EntityNotFoundException>(() => _service.GetAsync(PostgreSqlTestDatabases.SharedTenantId.ToString("D")));
        await Should.ThrowAsync<EntityNotFoundException>(() => _service.GetAsync(Guid.NewGuid().ToString("D")));
        await Should.ThrowAsync<EntityNotFoundException>(() => _service.GetAsync("not-a-key"));
    }

    [Fact]
    public async Task A_tenant_cannot_list_or_open_any_database()
    {
        using (_currentTenant.Change(PostgreSqlTestDatabases.TenantAId))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.GetListAsync(Request()));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.GetSummaryAsync());
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.GetAsync("host"));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.GetAsync(PostgreSqlTestDatabases.TenantAId.ToString("D")));
        }
    }
}
