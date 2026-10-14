using System;
using System.Threading.Tasks;
using OpenTms.Platform.Tenants;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Volo.Abp.TenantManagement;
using Xunit;
using TenantCreateDto = OpenTms.Platform.Tenants.TenantCreateDto;
using TenantUpdateDto = OpenTms.Platform.Tenants.TenantUpdateDto;
using TenantDto = OpenTms.Platform.Tenants.TenantDto;

namespace OpenTms.EntityFrameworkCore.Platform.Tenants;

/// <summary>
/// The provisioning and drop jobs against the faked database server (<see cref="FakeTenantDatabaseProvisioner"/>):
/// the failure path of provisioning and the guards of the drop job. The success path and the real DDL run against
/// PostgreSQL in PostgreSql/Tenants.
/// Risk class: Critical — the drop job is irreversible; a provisioning failure must leave the tenant retryable.
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class TenantJobTests : OpenTmsTestBase<TenantAdministrationTestModule>
{
    private readonly ITenantAdministrationAppService _service;
    private readonly ITenantRepository _tenantRepository;
    private readonly IDataFilter _dataFilter;
    private readonly FakeBackgroundJobManager _jobs;
    private readonly FakeTenantDatabaseProvisioner _provisioner;

    public TenantJobTests()
    {
        _service = GetRequiredService<ITenantAdministrationAppService>();
        _tenantRepository = GetRequiredService<ITenantRepository>();
        _dataFilter = GetRequiredService<IDataFilter>();
        _jobs = GetRequiredService<FakeBackgroundJobManager>();
        _provisioner = GetRequiredService<FakeTenantDatabaseProvisioner>();
    }

    private Task<TenantDto> CreateAsync(string name) =>
        _service.CreateAsync(new TenantCreateDto { Name = name, AdminEmailAddress = "admin@acme.test" });

    private Task<Tenant?> FindTenantAsync(Guid id) =>
        WithUnitOfWorkAsync(async () =>
        {
            using (_dataFilter.Disable<ISoftDelete>())
            {
                return await _tenantRepository.FindAsync(id, includeDetails: true);
            }
        });

    private Task RunProvisioningAsync(Guid tenantId) =>
        GetRequiredService<TenantProvisioningJob>().ExecuteAsync(new TenantProvisioningArgs { TenantId = tenantId });

    private Task RunDropAsync(TenantDatabaseDropArgs args) =>
        GetRequiredService<TenantDatabaseDropJob>().ExecuteAsync(args);

    /* ── Provisioning: failure path ─────────────────────────────────────────────────────────── */

    [Fact]
    public async Task A_failing_provisioning_records_Failed_with_the_error_and_does_not_rethrow()
    {
        var created = await CreateAsync("acme");
        _provisioner.EnsureFailure = new InvalidOperationException("could not create database");

        await Should.NotThrowAsync(() => RunProvisioningAsync(created.Id));

        var tenant = (await FindTenantAsync(created.Id))!;
        tenant.GetProperty<string?>(TenantProperties.ProvisioningState).ShouldBe(TenantProvisioningState.Failed);
        tenant.GetProperty<string?>(TenantProperties.ProvisioningError).ShouldBe("InvalidOperationException: could not create database");
        tenant.GetProperty<DateTime?>(TenantProperties.ProvisioningAt).ShouldNotBeNull();
        /* Kept so that a retry can still seed the administrator. */
        tenant.GetProperty<string?>(TenantProperties.PendingAdminEmail).ShouldBe("admin@acme.test");
        tenant.GetProperty<DateTime?>(TenantProperties.InvitationSentAt).ShouldBeNull();
        _provisioner.Ensured.ShouldHaveSingleItem().ShouldBe(tenant.FindDefaultConnectionString());
    }

    [Fact]
    public async Task After_a_failure_the_tenant_can_be_retried_once()
    {
        var created = await CreateAsync("acme");
        _provisioner.EnsureFailure = new InvalidOperationException("boom");
        await RunProvisioningAsync(created.Id);
        _jobs.Clear();

        await _service.RetryProvisioningAsync(created.Id);

        _jobs.JobsOf<TenantProvisioningArgs>().ShouldHaveSingleItem().TenantId.ShouldBe(created.Id);
        (await FindTenantAsync(created.Id))!.GetProperty<string?>(TenantProperties.ProvisioningState).ShouldBe(TenantProvisioningState.Queued);
    }

    [Fact]
    public async Task Provisioning_a_tenant_without_a_database_of_its_own_fails_without_touching_the_server()
    {
        var id = await WithUnitOfWorkAsync(async () =>
        {
            var tenant = await GetRequiredService<ITenantManager>().CreateAsync("shared");
            await _tenantRepository.InsertAsync(tenant, autoSave: true);
            return tenant.Id;
        });

        await Should.NotThrowAsync(() => RunProvisioningAsync(id));

        var tenant = (await FindTenantAsync(id))!;
        tenant.GetProperty<string?>(TenantProperties.ProvisioningState).ShouldBe(TenantProvisioningState.Failed);
        tenant.GetProperty<string?>(TenantProperties.ProvisioningError).ShouldNotBeNull().ShouldStartWith("InvalidOperationException");
        _provisioner.Ensured.ShouldBeEmpty();
    }

    [Fact]
    public async Task Provisioning_a_tenant_that_no_longer_exists_does_nothing()
    {
        await Should.NotThrowAsync(() => RunProvisioningAsync(Guid.NewGuid()));
        _provisioner.Ensured.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_failure_text_never_carries_the_database_password()
    {
        var created = await CreateAsync("acme");
        var connectionString = (await FindTenantAsync(created.Id))!.FindDefaultConnectionString()!;
        var password = new Npgsql.NpgsqlConnectionStringBuilder(connectionString).Password!;
        _provisioner.EnsureFailure = new InvalidOperationException("password authentication failed for user");

        await RunProvisioningAsync(created.Id);

        (await FindTenantAsync(created.Id))!.GetProperty<string?>(TenantProperties.ProvisioningError)!.ShouldNotContain(password);
        (await _service.GetAsync(created.Id)).ProvisioningError.ShouldNotBeNull();
    }

    /* ── Drop job ───────────────────────────────────────────────────────────────────────────── */

    private async Task<TenantDatabaseDropArgs> DeleteAndTakeDropArgsAsync(Guid tenantId)
    {
        /* A tenant is deletable once provisioning has finished or failed (review: no delete while Queued/Running). */
        await GetRequiredService<TenantStateWriter>().SetStateAsync(tenantId, TenantProvisioningState.Succeeded);
        _jobs.Clear();
        await _service.DeleteAsync(tenantId);
        return _jobs.JobsOf<TenantDatabaseDropArgs>().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task The_drop_job_drops_the_database_of_a_deleted_tenant()
    {
        var created = await CreateAsync("acme");
        var args = await DeleteAndTakeDropArgsAsync(created.Id);

        await RunDropAsync(args);

        var dropped = _provisioner.Dropped.ShouldHaveSingleItem();
        dropped.DatabaseName.ShouldBe(TenantDatabaseNaming.For("acme", created.Id));
        dropped.UserName.ShouldBe(dropped.DatabaseName);
        dropped.Host.ShouldBe("db.example.test");
        dropped.Port.ShouldBe(5432);
    }

    [Fact]
    public async Task The_drop_job_does_nothing_when_the_tenant_is_not_deleted()
    {
        var created = await CreateAsync("acme");
        var name = TenantDatabaseNaming.For("acme", created.Id);

        await RunDropAsync(new TenantDatabaseDropArgs
        {
            TenantId = created.Id,
            Host = "db.example.test",
            Port = 5432,
            DatabaseName = name,
            RoleName = name
        });

        _provisioner.Dropped.ShouldBeEmpty();
        (await FindTenantAsync(created.Id))!.IsDeleted.ShouldBeFalse();
    }

    [Fact]
    public async Task The_drop_job_never_drops_a_database_other_than_the_deleted_tenants_own()
    {
        /* Design, "Delete": the guards are checked in the app service AND again in the job — among them "the tenant
         * has its own connection string". Job arguments sit in Hangfire storage in plain text; a drop must not be
         * steerable to a living tenant's database by pairing it with the id of some deleted tenant. */
        var living = await CreateAsync("living");
        var deleted = await CreateAsync("deleted");
        var args = await DeleteAndTakeDropArgsAsync(deleted.Id);
        var livingName = TenantDatabaseNaming.For("living", living.Id);

        await RunDropAsync(new TenantDatabaseDropArgs
        {
            TenantId = args.TenantId,
            Host = args.Host,
            Port = args.Port,
            DatabaseName = livingName,
            RoleName = livingName
        });

        _provisioner.Dropped.ShouldNotContain(d => d.DatabaseName == livingName);
    }

    [Fact]
    public async Task The_drop_job_does_not_drop_for_a_deleted_shared_tier_tenant()
    {
        /* Design: "the tenant has its own connection string" is re-checked in the job. */
        var id = await WithUnitOfWorkAsync(async () =>
        {
            var tenant = await GetRequiredService<ITenantManager>().CreateAsync("shared");
            await _tenantRepository.InsertAsync(tenant, autoSave: true);
            return tenant.Id;
        });
        await _service.DeleteAsync(id);
        var name = TenantDatabaseNaming.For("shared", id);

        await RunDropAsync(new TenantDatabaseDropArgs { TenantId = id, Host = "db.example.test", Port = 5432, DatabaseName = name, RoleName = name });

        _provisioner.Dropped.ShouldBeEmpty();
    }
}
