using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OpenTms.Integrations.DocumentRendering.PdfGeneratorApi;
using OpenTms.Integrations.Routing;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;
using Xunit;

namespace OpenTms.PostgreSql.Integrations;

/// <summary>
/// The route cache against real PostgreSQL (design 60, test risk analysis: "Cache: hit, miss, expiry, per tenant",
/// Critical — constitution rule 1): a lane computed by tenant A (own database) is unknown to the shared-tier tenant and
/// the host (host database), each of which asks the engine for itself and keeps its own row; the unique key is per tenant
/// with NULLS NOT DISTINCT; an expired row is refreshed in place; the purge removes expired rows of the host and of every
/// tenant and keeps the live ones. Rows are written in their own unit of work (so a calculation job's transaction never
/// fails on a cache write), which is why the tests wipe the table before and after instead of rolling back.
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class RouteCachePostgreSqlTests : OpenTmsTestBase<RoutingPostgreSqlTestModule>, IAsyncLifetime
{
    private static Guid TenantA => PostgreSqlTestDatabases.TenantAId;

    private static Guid SharedTenant => PostgreSqlTestDatabases.SharedTenantId;

    private static readonly GeoPoint A = RoutingHarness.Amsterdam;
    private static readonly GeoPoint B = RoutingHarness.Rotterdam;
    private static readonly GeoPoint C = RoutingHarness.Utrecht;

    private readonly IRoutingService _routing;
    private readonly RouteCachePurger _purger;
    private readonly IRepository<RouteCacheEntry, Guid> _cache;
    private readonly ICurrentTenant _currentTenant;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly RoutingHttpHandler _handler;
    private readonly ManualTimeProvider _time;

    public RouteCachePostgreSqlTests()
    {
        _routing = GetRequiredService<IRoutingService>();
        _purger = GetRequiredService<RouteCachePurger>();
        _cache = GetRequiredService<IRepository<RouteCacheEntry, Guid>>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
        _unitOfWorkManager = GetRequiredService<IUnitOfWorkManager>();
        _handler = GetRequiredService<RoutingHttpHandler>();
        _time = GetRequiredService<ManualTimeProvider>();
        _handler.Reset();
        _time.Now = RoutingPostgreSqlTestModule.Start;
        GetRequiredService<RoutingProbeCache>().Clear();
    }

    public Task InitializeAsync() => WipeAllAsync();

    public Task DisposeAsync() => WipeAllAsync();

    [Fact]
    public async Task A_lane_of_tenant_A_is_unknown_to_the_shared_tenant_and_the_host_which_keep_their_own_rows()
    {
        using (_currentTenant.Change(TenantA))
        {
            (await _routing.GetRouteAsync(A, B, VehicleSpec.Default, RoutingOptions.DistanceOnly)).ShouldNotBeNull().FromCache.ShouldBeFalse();
            (await _routing.GetRouteAsync(A, B, VehicleSpec.Default, RoutingOptions.DistanceOnly)).ShouldNotBeNull().FromCache.ShouldBeTrue();
        }

        _handler.Requests.Count.ShouldBe(1);

        using (_currentTenant.Change(SharedTenant))
        {
            (await _routing.GetRouteAsync(A, B, VehicleSpec.Default, RoutingOptions.DistanceOnly)).ShouldNotBeNull().FromCache.ShouldBeFalse("tenant A's row is not the shared tenant's");
        }

        using (_currentTenant.Change(null))
        {
            (await _routing.GetRouteAsync(A, B, VehicleSpec.Default, RoutingOptions.DistanceOnly)).ShouldNotBeNull().FromCache.ShouldBeFalse("nor the host's");
        }

        _handler.Requests.Count.ShouldBe(3, "three contexts, three engine calls");
        (await CountAsync(TenantA)).ShouldBe(1);
        (await CountAsync(SharedTenant)).ShouldBe(1);
        (await CountAsync(null)).ShouldBe(1);
        (await _routing.GetStatusAsync()).CacheRows.ShouldBe(1, "the status counts the host's rows outside a tenant");

        /* Where the rows physically live: tenant A's in its own database, the shared tenant's and the host's in the host database. */
        (await SqlCountAsync(PostgreSqlTestDatabases.TenantADatabase)).ShouldBe(1);
        (await SqlCountAsync(PostgreSqlTestDatabases.HostDatabase)).ShouldBe(2);
        (await SqlCountAsync(PostgreSqlTestDatabases.HostDatabase, "\"TenantId\" IS NULL")).ShouldBe(1);
    }

    [Fact]
    public async Task The_unique_key_is_per_tenant_and_treats_the_hosts_null_tenant_as_a_value()
    {
        var key = "qq|unique|" + Guid.NewGuid();
        await InsertAsync(TenantA, key, expired: false);
        await InsertAsync(SharedTenant, key, expired: false);
        await InsertAsync(null, key, expired: false);

        var duplicateInTenant = await Should.ThrowAsync<DbUpdateException>(() => InsertAsync(TenantA, key, expired: false));
        duplicateInTenant.InnerException.ShouldBeOfType<PostgresException>().ConstraintName.ShouldBe("IX_RouteCache_TenantId_Key");

        var duplicateOnHost = await Should.ThrowAsync<DbUpdateException>(() => InsertAsync(null, key, expired: false));
        var postgres = duplicateOnHost.InnerException.ShouldBeOfType<PostgresException>();
        postgres.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
        postgres.ConstraintName.ShouldBe("IX_RouteCache_TenantId_Key", "NULLS NOT DISTINCT: two host rows with one key are a duplicate");
    }

    [Fact]
    public async Task An_expired_row_is_not_served_and_is_refreshed_in_place_by_the_next_answer()
    {
        var key = RouteCacheKey.Build(A, C, null, "Osrm", RoutingOptions.DistanceOnly);
        var expiredId = await InsertAsync(TenantA, key, expired: true);

        using (_currentTenant.Change(TenantA))
        {
            var route = await _routing.GetRouteAsync(A, C, null, RoutingOptions.DistanceOnly);
            route.ShouldNotBeNull().FromCache.ShouldBeFalse("the expired row does not count");
            _handler.Requests.Count.ShouldBe(1);

            var rows = await ListAsync(TenantA);
            var row = rows.ShouldHaveSingleItem();
            row.Id.ShouldBe(expiredId, "refreshed, not duplicated");
            row.DistanceM.ShouldBe(72485);
            row.ExpiresAt.ShouldBe(RoutingPostgreSqlTestModule.Start.UtcDateTime.AddDays(30));

            (await _routing.GetRouteAsync(A, C, null, RoutingOptions.DistanceOnly)).ShouldNotBeNull().FromCache.ShouldBeTrue();
            _handler.Requests.Count.ShouldBe(1);
        }
    }

    [Fact]
    public async Task The_matrix_caches_each_pair_per_tenant_and_a_second_call_needs_no_engine()
    {
        using (_currentTenant.Change(TenantA))
        {
            var matrix = await _routing.GetMatrixAsync([A, B, C], VehicleSpec.Default);
            matrix.ShouldNotBeNull().Legs.Count.ShouldBe(2);
            _handler.Requests.Count.ShouldBe(1);

            var again = await _routing.GetMatrixAsync([A, B, C], VehicleSpec.Default);
            again.ShouldNotBeNull().Legs.ShouldAllBe(l => l.FromCache);
            _handler.Requests.Count.ShouldBe(1);
        }

        using (_currentTenant.Change(SharedTenant))
        {
            var other = await _routing.GetMatrixAsync([A, B, C], VehicleSpec.Default);
            other.ShouldNotBeNull().Legs.ShouldAllBe(l => !l.FromCache, "another tenant's pairs are its own");
            _handler.Requests.Count.ShouldBe(2);
        }

        (await CountAsync(TenantA)).ShouldBe(2);
        (await CountAsync(SharedTenant)).ShouldBe(2);
        (await CountAsync(null)).ShouldBe(0);
    }

    [Fact]
    public async Task The_purge_removes_expired_rows_of_the_host_and_of_every_tenant_and_keeps_the_live_ones()
    {
        foreach (var tenant in new Guid?[] { null, TenantA, SharedTenant })
        {
            await InsertAsync(tenant, "qq|expired|" + Guid.NewGuid(), expired: true);
            await InsertAsync(tenant, "qq|live|" + Guid.NewGuid(), expired: false);
        }

        var removed = await _purger.PurgeAsync();

        removed.ShouldBe(3);
        foreach (var tenant in new Guid?[] { null, TenantA, SharedTenant })
        {
            var rows = await ListAsync(tenant);
            rows.ShouldHaveSingleItem().Key.ShouldStartWith("qq|live|");
        }

        (await _purger.PurgeAsync()).ShouldBe(0, "nothing left to purge");
    }

    private async Task<Guid> InsertAsync(Guid? tenantId, string key, bool expired)
    {
        var now = RoutingPostgreSqlTestModule.Start.UtcDateTime;
        var entry = new RouteCacheEntry(Guid.NewGuid(), tenantId, key, "Osrm", 1000, 60, null, null, null, null, null, now.AddDays(-31), expired ? now.AddMinutes(-1) : now.AddDays(1));
        using (_currentTenant.Change(tenantId))
        using (var unitOfWork = _unitOfWorkManager.Begin(new AbpUnitOfWorkOptions { IsTransactional = false }, requiresNew: true))
        {
            await _cache.InsertAsync(entry, autoSave: true);
            await unitOfWork.CompleteAsync();
        }

        return entry.Id;
    }

    private async Task<List<RouteCacheEntry>> ListAsync(Guid? tenantId)
    {
        using (_currentTenant.Change(tenantId))
        using (var unitOfWork = _unitOfWorkManager.Begin(new AbpUnitOfWorkOptions { IsTransactional = false }, requiresNew: true))
        {
            var rows = await _cache.GetListAsync();
            await unitOfWork.CompleteAsync();
            return rows;
        }
    }

    private async Task<long> CountAsync(Guid? tenantId) => (await ListAsync(tenantId)).Count;

    private async Task WipeAllAsync()
    {
        foreach (var tenant in new Guid?[] { null, TenantA, SharedTenant })
        {
            using (_currentTenant.Change(tenant))
            using (var unitOfWork = _unitOfWorkManager.Begin(new AbpUnitOfWorkOptions { IsTransactional = false }, requiresNew: true))
            {
                await _cache.DeleteDirectAsync(e => true);
                await unitOfWork.CompleteAsync();
            }
        }
    }

    private static async Task<long> SqlCountAsync(string database, string? where = null)
    {
        await using var connection = new NpgsqlConnection(PostgreSqlTestDatabases.ConnectionStringFor(database));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT count(*) FROM integrations.\"RouteCache\"" + (where == null ? string.Empty : " WHERE " + where), connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
