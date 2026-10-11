using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenTms.Platform.Administration.Maintenance;
using OpenTms.Platform.Grid;
using OpenTms.Platform.Maintenance;
using OpenTms.Platform.Maintenance.Health;
using OpenTms.Platform.Maintenance.Instances;
using Shouldly;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Repositories.EntityFrameworkCore;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;
using Xunit;

namespace OpenTms.PostgreSql.Maintenance;

/// <summary>
/// The instance registry against real PostgreSQL: born, alive, dying, as the worker drives it, and
/// the Health tab's view on it. Risk class: Critical — the registry is written in the host database
/// whatever tenant is current, and the view is host-only; High — statuses and the heartbeat's
/// health capture are what an operator reads.
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class InstanceRegistryIntegrationTests : OpenTmsPostgreSqlTestBase
{
    private readonly IInstanceRegistry _registry;
    private readonly CurrentApplicationInstance _current;
    private readonly IRepository<ApplicationInstance, Guid> _repository;
    private readonly IInstanceMaintenanceAppService _service;
    private readonly ICurrentTenant _currentTenant;
    private readonly FakeHealthReportProvider _health;
    private readonly InstanceRegistryOptions _options;

    public InstanceRegistryIntegrationTests()
    {
        _registry = GetRequiredService<IInstanceRegistry>();
        _current = GetRequiredService<CurrentApplicationInstance>();
        _repository = GetRequiredService<IRepository<ApplicationInstance, Guid>>();
        _service = GetRequiredService<IInstanceMaintenanceAppService>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
        _health = GetRequiredService<FakeHealthReportProvider>();
        _options = GetRequiredService<IOptions<InstanceRegistryOptions>>().Value;
    }

    private static async Task<long> CountRowsAsync(string database)
    {
        await using var connection = new Npgsql.NpgsqlConnection(PostgreSqlTestDatabases.ConnectionStringFor(database));
        await connection.OpenAsync();
        await using var command = new Npgsql.NpgsqlCommand("SELECT count(*) FROM platform.\"ApplicationInstances\"", connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static GridRequest Request(int start = 0, int end = 50, string? search = null) => new() { StartRow = start, EndRow = end, WildcardSearch = search };

    private Task<ApplicationInstance?> FindOwnRowInHostAsync() =>
        WithUnitOfWorkAsync(new AbpUnitOfWorkOptions(), async () =>
        {
            using (_currentTenant.Change(null))
            {
                return await _repository.FindAsync(_current.Id);
            }
        });

    [Fact]
    public async Task Born_alive_and_dying_are_recorded_in_the_host_database_even_when_a_tenant_is_current()
    {
        using (_currentTenant.Change(PostgreSqlTestDatabases.TenantAId))
        {
            await _registry.RegisterAsync();
        }

        var born = await FindOwnRowInHostAsync();
        born.ShouldNotBeNull();
        born.Role.ShouldBe(_options.Role);
        born.MachineName.ShouldBe(Environment.MachineName);
        born.ProcessId.ShouldBe(Environment.ProcessId);
        born.StoppedAt.ShouldBeNull();
        born.HealthStatus.ShouldBeNull();

        /* Registering twice (a retry) keeps one row. */
        await _registry.RegisterAsync();

        _health.Report = new HealthReport("Degraded", TimeSpan.FromMilliseconds(5), [new HealthCheckEntry("db", "Degraded", "slow", TimeSpan.FromMilliseconds(5), ["ready"], null)]);
        using (_currentTenant.Change(PostgreSqlTestDatabases.TenantAId))
        {
            await _registry.HeartbeatAsync();
        }

        var alive = await FindOwnRowInHostAsync();
        alive.ShouldNotBeNull();
        alive.LastHeartbeatAt.ShouldBeGreaterThan(born.LastHeartbeatAt);
        alive.HealthStatus.ShouldBe("Degraded");
        alive.HealthDetails.ShouldNotBeNull();
        alive.HealthDetails.ShouldContain("\"db\"");
        alive.WorkingSetBytes.ShouldBeGreaterThan(0);

        var detail = await _service.GetAsync(_current.Id);
        detail.IsCurrent.ShouldBeTrue();
        detail.Status.ShouldBe(ApplicationInstanceStatuses.Running);
        detail.HealthStatus.ShouldBe("Degraded");
        detail.HealthEntries.ShouldHaveSingleItem().Name.ShouldBe("db");
        detail.RuntimeVersion.ShouldNotBeNullOrWhiteSpace();

        await _registry.MarkStoppedAsync();
        var gone = await FindOwnRowInHostAsync();
        gone.ShouldNotBeNull();
        gone.StoppedAt.ShouldNotBeNull();
        (await _service.GetAsync(_current.Id)).Status.ShouldBe(ApplicationInstanceStatuses.Stopped);

        /* The tenant database never gets a row (checked on the database itself), and a repository
         * resolved inside the tenant's scope still reads the host database: ABP routes every entity
         * that is not IMultiTenant to the host connection string (EfCoreRepository.GetDbContextAsync),
         * which is the rule the registry relies on and the reason its view is host-only. */
        (await CountRowsAsync(PostgreSqlTestDatabases.TenantADatabase)).ShouldBe(0);
        (await CountRowsAsync(PostgreSqlTestDatabases.HostDatabase)).ShouldBeGreaterThanOrEqualTo(1);
        using (_currentTenant.Change(PostgreSqlTestDatabases.TenantAId))
        using (var scope = ServiceProvider.CreateScope())
        {
            var scopedRepository = (IEfCoreRepository<ApplicationInstance, Guid>)scope.ServiceProvider.GetRequiredService<IRepository<ApplicationInstance, Guid>>();
            var unitOfWorkManager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            using (var unitOfWork = unitOfWorkManager.Begin(new AbpUnitOfWorkOptions(), requiresNew: true))
            {
                (await scopedRepository.GetDbContextAsync()).Database.GetDbConnection().Database.ShouldBe(PostgreSqlTestDatabases.HostDatabase);
                (await scopedRepository.CountAsync()).ShouldBeGreaterThanOrEqualTo(1);
                await unitOfWork.CompleteAsync();
            }
        }

        _health.Report = new HealthReport("Healthy", TimeSpan.Zero, []);
    }

    [Fact]
    public async Task The_list_shows_every_instance_with_its_status_running_first_and_the_overview_counts_them()
    {
        var silentId = Guid.NewGuid();
        var stoppedId = Guid.NewGuid();
        var longAgo = DateTime.UtcNow.AddHours(-1);
        await WithUnitOfWorkAsync(new AbpUnitOfWorkOptions { IsTransactional = true }, async () =>
        {
            using (_currentTenant.Change(null))
            {
                await _repository.InsertAsync(new ApplicationInstance(silentId, "Jobs", "jobs-1", 1, "1.0", "Test", ".NET", 2, false, longAgo));
                var stopped = new ApplicationInstance(stoppedId, "Api", "api-9", 2, "1.0", "Test", ".NET", 2, true, longAgo);
                stopped.MarkStopped(longAgo.AddMinutes(10));
                await _repository.InsertAsync(stopped);
            }
        });
        await _registry.RegisterAsync();
        await _registry.HeartbeatAsync();

        var page = await _service.GetListAsync(Request());
        page.Rows.First().Status.ShouldBe(ApplicationInstanceStatuses.Running);
        var own = page.Rows.Single(r => r.Id == _current.Id);
        own.IsCurrent.ShouldBeTrue();
        own.Status.ShouldBe(ApplicationInstanceStatuses.Running);
        page.Rows.Single(r => r.Id == silentId).Status.ShouldBe(ApplicationInstanceStatuses.Unresponsive);
        page.Rows.Single(r => r.Id == stoppedId).Status.ShouldBe(ApplicationInstanceStatuses.Stopped);
        page.Rows.Select(r => r.Status).ToList().IndexOf(ApplicationInstanceStatuses.Stopped)
            .ShouldBeGreaterThan(page.Rows.Select(r => r.Status).ToList().IndexOf(ApplicationInstanceStatuses.Running));

        (await _service.GetListAsync(Request(search: "jobs-1"))).Rows.ShouldHaveSingleItem().Role.ShouldBe("Jobs");

        var overview = await _service.GetOverviewAsync();
        overview.Running.ShouldBeGreaterThanOrEqualTo(1);
        overview.Unresponsive.ShouldBeGreaterThanOrEqualTo(1);
        overview.Stopped.ShouldBeGreaterThanOrEqualTo(1);
        overview.CurrentInstanceId.ShouldBe(_current.Id);
        overview.HeartbeatPeriod.ShouldBe(_options.HeartbeatPeriod);

        await Should.ThrowAsync<EntityNotFoundException>(() => _service.GetAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Housekeeping_removes_rows_past_retention_on_heartbeat()
    {
        var ancientId = Guid.NewGuid();
        var ancient = DateTime.UtcNow - _options.Retention - TimeSpan.FromDays(1);
        await WithUnitOfWorkAsync(new AbpUnitOfWorkOptions { IsTransactional = true }, async () =>
        {
            using (_currentTenant.Change(null))
            {
                await _repository.InsertAsync(new ApplicationInstance(ancientId, "Api", "old", 3, "0.9", "Test", ".NET", 1, true, ancient));
            }
        });

        await _registry.HeartbeatAsync();

        await WithUnitOfWorkAsync(new AbpUnitOfWorkOptions(), async () =>
        {
            using (_currentTenant.Change(null))
            {
                (await _repository.FindAsync(ancientId)).ShouldBeNull();
                (await _repository.FindAsync(_current.Id)).ShouldNotBeNull();
            }
        });
    }

    [Fact]
    public async Task A_tenant_cannot_see_the_instances()
    {
        using (_currentTenant.Change(PostgreSqlTestDatabases.TenantAId))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.GetOverviewAsync());
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.GetListAsync(Request()));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.GetAsync(_current.Id));
        }
    }
}
