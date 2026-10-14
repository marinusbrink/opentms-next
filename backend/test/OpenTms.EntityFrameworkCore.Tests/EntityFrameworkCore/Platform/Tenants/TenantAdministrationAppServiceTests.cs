using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OpenTms.Platform.Editions;
using OpenTms.Platform.Grid;
using OpenTms.Platform.Tenants;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Volo.Abp.TenantManagement;
using Volo.Abp.Uow;
using Volo.Abp.Validation;
using Xunit;
using TenantCreateDto = OpenTms.Platform.Tenants.TenantCreateDto;
using TenantUpdateDto = OpenTms.Platform.Tenants.TenantUpdateDto;
using TenantDto = OpenTms.Platform.Tenants.TenantDto;

namespace OpenTms.EntityFrameworkCore.Platform.Tenants;

/// <summary>
/// The Tenants app's application service through the real container (docs/designs/tenants-and-editions.md), with
/// the job queue and the database server faked (<see cref="TenantAdministrationTestModule"/>).
/// Risk class: Critical — Platform; create decides which database a tenant gets and with which credentials, delete
/// decides which database is dropped, and every method acts on host-owned data (tenant isolation / authorization).
/// The PostgreSQL side of the same flow is covered in PostgreSql/Tenants.
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class TenantAdministrationAppServiceTests : OpenTmsTestBase<TenantAdministrationTestModule>
{
    private readonly ITenantAdministrationAppService _service;
    private readonly IEditionAdministrationAppService _editions;
    private readonly ITenantRepository _tenantRepository;
    private readonly ITenantManager _tenantManager;
    private readonly ITenantEditionStore _editionStore;
    private readonly ICurrentTenant _currentTenant;
    private readonly IDataFilter _dataFilter;
    private readonly FakeBackgroundJobManager _jobs;
    private readonly FakeTenantDatabaseProvisioner _provisioner;

    public TenantAdministrationAppServiceTests()
    {
        _service = GetRequiredService<ITenantAdministrationAppService>();
        _editions = GetRequiredService<IEditionAdministrationAppService>();
        _tenantRepository = GetRequiredService<ITenantRepository>();
        _tenantManager = GetRequiredService<ITenantManager>();
        _editionStore = GetRequiredService<ITenantEditionStore>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
        _dataFilter = GetRequiredService<IDataFilter>();
        _jobs = GetRequiredService<FakeBackgroundJobManager>();
        _provisioner = GetRequiredService<FakeTenantDatabaseProvisioner>();
    }

    private static GridRequest Request(string? search = null) => new() { StartRow = 0, EndRow = 50, WildcardSearch = search };

    private Task<TenantDto> CreateAsync(string name, Guid? editionId = null, string email = "admin@acme.test") =>
        _service.CreateAsync(new TenantCreateDto { Name = name, AdminEmailAddress = email, EditionId = editionId });

    private Task<Tenant?> FindTenantAsync(Guid id, bool includeDeleted = false) =>
        WithUnitOfWorkAsync(async () =>
        {
            using (includeDeleted ? _dataFilter.Disable<ISoftDelete>() : null)
            {
                return await _tenantRepository.FindAsync(id, includeDetails: true);
            }
        });

    private async Task SetStateAsync(Guid id, string? state)
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var tenant = await _tenantRepository.GetAsync(id);
            tenant.SetProperty(TenantProperties.ProvisioningState, state);
            await _tenantRepository.UpdateAsync(tenant, autoSave: true);
        });
    }

    /// <summary>A tenant created the way an operator did before this app: no connection string, or a hand-made one.</summary>
    private async Task<Guid> CreateTenantDirectlyAsync(string name, string? connectionString)
    {
        return await WithUnitOfWorkAsync(async () =>
        {
            var tenant = await _tenantManager.CreateAsync(name);
            if (connectionString != null)
            {
                tenant.SetDefaultConnectionString(connectionString);
            }

            await _tenantRepository.InsertAsync(tenant, autoSave: true);
            return tenant.Id;
        });
    }

    /* ── Create ─────────────────────────────────────────────────────────────────────────────── */

    [Fact]
    public async Task Create_gives_the_tenant_its_own_generated_database_and_queues_provisioning()
    {
        var edition = await _editions.CreateAsync(new EditionCreateUpdateDto { DisplayName = "Gold" });

        var dto = await CreateAsync("  Acme Transport  ", edition.Id, "  admin@acme.test ");

        var tenant = (await FindTenantAsync(dto.Id)).ShouldNotBeNull();
        tenant.Name.ShouldBe("Acme Transport");

        var expectedName = TenantDatabaseNaming.For("Acme Transport", tenant.Id);
        var connectionString = new NpgsqlConnectionStringBuilder(tenant.FindDefaultConnectionString().ShouldNotBeNull());
        connectionString.Host.ShouldBe("db.example.test");
        connectionString.Port.ShouldBe(5432);
        connectionString.Database.ShouldBe(expectedName);
        connectionString.Username.ShouldBe(expectedName);
        connectionString.Password.ShouldNotBeNullOrWhiteSpace();
        connectionString.Password.ShouldNotBe("host-secret");
        connectionString.Password!.Length.ShouldBeGreaterThanOrEqualTo(40);
        connectionString.MinPoolSize.ShouldBe(0);
        connectionString.MaxPoolSize.ShouldBe(10);
        connectionString.ConnectionIdleLifetime.ShouldBe(60);
        connectionString.ConnectionPruningInterval.ShouldBe(10);

        tenant.GetProperty<string?>(TenantProperties.ProvisioningState).ShouldBe(TenantProvisioningState.Queued);
        tenant.GetProperty<string?>(TenantProperties.PendingAdminEmail).ShouldBe("admin@acme.test");
        tenant.GetProperty<Guid?>(TenantProperties.EditionId).ShouldBe(edition.Id);

        _jobs.Jobs.ShouldHaveSingleItem().ShouldBeOfType<TenantProvisioningArgs>().TenantId.ShouldBe(tenant.Id);
        _provisioner.Ensured.ShouldBeEmpty("the database is created by the job, never in the request");

        dto.ProvisioningState.ShouldBe(TenantProvisioningState.Queued);
        dto.EditionId.ShouldBe(edition.Id);
        dto.EditionName.ShouldBe("Gold");
        dto.DatabaseName.ShouldBe(expectedName);
        dto.ServerName.ShouldBe("db.example.test:5432");
        dto.ConnectionString.ShouldNotBeNull().ShouldNotContain(connectionString.Password!);
    }

    [Fact]
    public async Task Two_tenants_get_different_databases_and_different_passwords()
    {
        var first = await CreateAsync("acme");
        var second = await CreateAsync("acme two");

        var a = new NpgsqlConnectionStringBuilder((await FindTenantAsync(first.Id))!.FindDefaultConnectionString());
        var b = new NpgsqlConnectionStringBuilder((await FindTenantAsync(second.Id))!.FindDefaultConnectionString());

        a.Database.ShouldNotBe(b.Database);
        a.Username.ShouldNotBe(b.Username);
        a.Password.ShouldNotBe(b.Password);
    }

    [Fact]
    public async Task The_provisioning_job_is_queued_only_after_the_unit_of_work_commits()
    {
        var unitOfWorkManager = GetRequiredService<IUnitOfWorkManager>();

        using (var unitOfWork = unitOfWorkManager.Begin(requiresNew: true))
        {
            await CreateAsync("committed");
            _jobs.Jobs.ShouldBeEmpty("a job must not see a tenant that may still roll back");
            await unitOfWork.CompleteAsync();
        }

        _jobs.JobsOf<TenantProvisioningArgs>().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_rolled_back_create_queues_nothing()
    {
        var unitOfWorkManager = GetRequiredService<IUnitOfWorkManager>();

        using (unitOfWorkManager.Begin(requiresNew: true))
        {
            await CreateAsync("rolled back");
            /* disposed without CompleteAsync */
        }

        _jobs.Jobs.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_job_arguments_carry_no_address_password_or_connection_string()
    {
        await CreateAsync("acme", email: "secret.person@acme.test");

        var json = JsonSerializer.Serialize(_jobs.Jobs.ShouldHaveSingleItem(), _jobs.Jobs[0].GetType());

        json.ShouldNotContain("secret.person");
        json.ShouldNotContain("Password", Case.Insensitive);
        json.ShouldNotContain("Host=", Case.Insensitive);
    }

    [Fact]
    public async Task Create_with_an_unknown_edition_is_refused_and_creates_nothing()
    {
        await Should.ThrowAsync<UserFriendlyException>(() => CreateAsync("acme", Guid.NewGuid()));

        (await WithUnitOfWorkAsync(() => _tenantRepository.GetCountAsync())).ShouldBe(0);
        _jobs.Jobs.ShouldBeEmpty();
    }

    [Fact]
    public async Task Create_with_a_name_in_use_is_refused_and_queues_nothing_more()
    {
        await CreateAsync("acme");

        await Should.ThrowAsync<BusinessException>(() => CreateAsync("acme"));

        _jobs.Jobs.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData("", "admin@acme.test")]
    [InlineData("acme", "")]
    [InlineData("acme", "not-an-email")]
    public async Task Create_requires_a_name_and_a_valid_admin_email(string name, string email)
    {
        await Should.ThrowAsync<AbpValidationException>(() => CreateAsync(name, email: email));

        _jobs.Jobs.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_connection_preview_masks_the_password_and_shows_x_for_the_id_part()
    {
        var preview = await _service.GetConnectionPreviewAsync("Acme Transport");

        preview.DatabaseName.ShouldBe("t_acme_transport_xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx");
        preview.ServerName.ShouldBe("db.example.test:5432");
        preview.ConnectionString.ShouldContain("t_acme_transport_xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx");
        preview.ConnectionString.ShouldNotContain("host-secret");
        preview.ConnectionString.ShouldNotContain("preview");
    }

    /* ── Read ───────────────────────────────────────────────────────────────────────────────── */

    [Fact]
    public async Task Get_and_list_never_return_a_password()
    {
        var created = await CreateAsync("acme");
        var password = new NpgsqlConnectionStringBuilder((await FindTenantAsync(created.Id))!.FindDefaultConnectionString()).Password!;

        var dto = await _service.GetAsync(created.Id);
        var list = await _service.GetListAsync(Request());

        JsonSerializer.Serialize(dto).ShouldNotContain(password);
        JsonSerializer.Serialize(list).ShouldNotContain(password);
        JsonSerializer.Serialize(dto).ShouldNotContain("admin@acme.test");
        JsonSerializer.Serialize(list).ShouldNotContain("admin@acme.test");
        dto.ConnectionString.ShouldNotBeNull().ShouldContain("Password=********");
    }

    [Fact]
    public async Task The_list_shows_edition_database_server_and_state_and_filters_by_name()
    {
        var edition = await _editions.CreateAsync(new EditionCreateUpdateDto { DisplayName = "Silver" });
        var acme = await CreateAsync("acme", edition.Id);
        await CreateAsync("beta");
        var shared = await CreateTenantDirectlyAsync("gamma shared", null);

        var all = await _service.GetListAsync(Request());
        all.TotalCount.ShouldBe(3);
        all.Rows.Select(r => r.Name).ShouldBe(new[] { "acme", "beta", "gamma shared" });

        var row = all.Rows[0];
        row.Id.ShouldBe(acme.Id);
        row.EditionName.ShouldBe("Silver");
        row.DatabaseName.ShouldBe(TenantDatabaseNaming.For("acme", acme.Id));
        row.ServerName.ShouldBe("db.example.test:5432");
        row.ProvisioningState.ShouldBe(TenantProvisioningState.Queued);

        var sharedRow = all.Rows.Single(r => r.Id == shared);
        sharedRow.DatabaseName.ShouldBeNull();
        sharedRow.ServerName.ShouldBeNull();

        var filtered = await _service.GetListAsync(Request("bet"));
        filtered.FilteredCount.ShouldBe(1);
        filtered.TotalCount.ShouldBe(3);
        filtered.Rows.ShouldHaveSingleItem().Name.ShouldBe("beta");
    }

    [Fact]
    public async Task The_edition_lookup_lists_every_edition_by_name()
    {
        await _editions.CreateAsync(new EditionCreateUpdateDto { DisplayName = "Silver" });
        await _editions.CreateAsync(new EditionCreateUpdateDto { DisplayName = "Bronze" });

        var lookup = await _service.GetEditionLookupAsync();

        lookup.Select(e => e.DisplayName).ShouldBe(new[] { "Bronze", "Silver" });
    }

    /* ── Update ─────────────────────────────────────────────────────────────────────────────── */

    [Fact]
    public async Task Update_changes_name_and_edition_but_never_the_database()
    {
        var gold = await _editions.CreateAsync(new EditionCreateUpdateDto { DisplayName = "Gold" });
        var silver = await _editions.CreateAsync(new EditionCreateUpdateDto { DisplayName = "Silver" });
        var created = await CreateAsync("acme", gold.Id);
        var before = (await FindTenantAsync(created.Id))!.FindDefaultConnectionString();

        var updated = await _service.UpdateAsync(created.Id, new TenantUpdateDto { Name = "Acme Renamed", EditionId = silver.Id });

        var tenant = (await FindTenantAsync(created.Id))!;
        tenant.Name.ShouldBe("Acme Renamed");
        tenant.GetProperty<Guid?>(TenantProperties.EditionId).ShouldBe(silver.Id);
        tenant.FindDefaultConnectionString().ShouldBe(before);
        updated.EditionName.ShouldBe("Silver");
        updated.DatabaseName.ShouldBe(TenantDatabaseNaming.For("acme", created.Id));
    }

    [Fact]
    public async Task Update_invalidates_the_cached_edition_of_the_tenant()
    {
        var gold = await _editions.CreateAsync(new EditionCreateUpdateDto { DisplayName = "Gold" });
        var silver = await _editions.CreateAsync(new EditionCreateUpdateDto { DisplayName = "Silver" });
        var created = await CreateAsync("acme", gold.Id);

        (await _editionStore.GetEditionIdAsync(created.Id)).ShouldBe(gold.Id);

        await _service.UpdateAsync(created.Id, new TenantUpdateDto { Name = "acme", EditionId = silver.Id });
        (await _editionStore.GetEditionIdAsync(created.Id)).ShouldBe(silver.Id);

        await _service.UpdateAsync(created.Id, new TenantUpdateDto { Name = "acme", EditionId = null });
        (await _editionStore.GetEditionIdAsync(created.Id)).ShouldBeNull();
    }

    [Fact]
    public async Task Update_to_an_unknown_edition_is_refused()
    {
        var created = await CreateAsync("acme");

        await Should.ThrowAsync<UserFriendlyException>(() =>
            _service.UpdateAsync(created.Id, new TenantUpdateDto { Name = "acme", EditionId = Guid.NewGuid() }));

        (await FindTenantAsync(created.Id))!.GetProperty<Guid?>(TenantProperties.EditionId).ShouldBeNull();
    }

    /* ── Delete ─────────────────────────────────────────────────────────────────────────────── */

    [Fact]
    public async Task Delete_soft_deletes_and_queues_the_drop_of_the_tenants_own_database()
    {
        var created = await CreateAsync("acme");
        await SetStateAsync(created.Id, TenantProvisioningState.Succeeded);
        _jobs.Clear();

        await _service.DeleteAsync(created.Id);

        (await FindTenantAsync(created.Id)).ShouldBeNull();
        (await FindTenantAsync(created.Id, includeDeleted: true)).ShouldNotBeNull().IsDeleted.ShouldBeTrue();

        var drop = _jobs.Jobs.ShouldHaveSingleItem().ShouldBeOfType<TenantDatabaseDropArgs>();
        var name = TenantDatabaseNaming.For("acme", created.Id);
        drop.TenantId.ShouldBe(created.Id);
        drop.DatabaseName.ShouldBe(name);
        drop.RoleName.ShouldBe(name);
        drop.Host.ShouldBe("db.example.test");
        drop.Port.ShouldBe(5432);
        _provisioner.Dropped.ShouldBeEmpty("the drop runs in the job, never in the request");
    }

    [Fact]
    public async Task Deleting_a_shared_tier_tenant_queues_no_drop()
    {
        var id = await CreateTenantDirectlyAsync("shared", null);

        await _service.DeleteAsync(id);

        (await FindTenantAsync(id, includeDeleted: true))!.IsDeleted.ShouldBeTrue();
        _jobs.Jobs.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("Host=db.example.test;Port=5432;Database=OpenTms;Username=postgres;Password=x")]
    [InlineData("Host=db.example.test;Port=5432;Database=OpenTms_Demo;Username=postgres;Password=x")]
    [InlineData("Host=other.example.test;Port=5432;Database=postgres;Username=postgres;Password=x")]
    public async Task Deleting_a_tenant_whose_database_was_not_generated_here_queues_no_drop(string connectionString)
    {
        var id = await CreateTenantDirectlyAsync("hand-made", connectionString);

        await _service.DeleteAsync(id);

        (await FindTenantAsync(id, includeDeleted: true))!.IsDeleted.ShouldBeTrue();
        _jobs.Jobs.ShouldBeEmpty();
    }

    [Fact]
    public async Task Deleting_an_unknown_tenant_is_not_found()
    {
        await Should.ThrowAsync<EntityNotFoundException>(() => _service.DeleteAsync(Guid.NewGuid()));
        _jobs.Jobs.ShouldBeEmpty();
    }

    /* ── Retry provisioning / resend invitation ─────────────────────────────────────────────── */

    [Theory]
    [InlineData(TenantProvisioningState.Queued)]
    [InlineData(TenantProvisioningState.Running)]
    [InlineData(TenantProvisioningState.Succeeded)]
    [InlineData(null)]
    public async Task Retry_is_refused_unless_provisioning_failed(string? state)
    {
        var created = await CreateAsync("acme");
        await SetStateAsync(created.Id, state);
        _jobs.Clear();

        await Should.ThrowAsync<UserFriendlyException>(() => _service.RetryProvisioningAsync(created.Id));

        _jobs.Jobs.ShouldBeEmpty();
    }

    [Fact]
    public async Task Retry_of_a_failed_tenant_requeues_provisioning()
    {
        var created = await CreateAsync("acme");
        await SetStateAsync(created.Id, TenantProvisioningState.Failed);
        _jobs.Clear();

        await _service.RetryProvisioningAsync(created.Id);

        _jobs.Jobs.ShouldHaveSingleItem().ShouldBeOfType<TenantProvisioningArgs>().TenantId.ShouldBe(created.Id);
        (await FindTenantAsync(created.Id))!.GetProperty<string?>(TenantProperties.ProvisioningState).ShouldBe(TenantProvisioningState.Queued);

        /* A second click while the first retry is queued is refused: runs never overlap. */
        await Should.ThrowAsync<UserFriendlyException>(() => _service.RetryProvisioningAsync(created.Id));
        _jobs.Jobs.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData(TenantProvisioningState.Queued)]
    [InlineData(TenantProvisioningState.Running)]
    [InlineData(TenantProvisioningState.Failed)]
    [InlineData(null)]
    public async Task Resend_is_refused_unless_provisioning_succeeded(string? state)
    {
        var created = await CreateAsync("acme");
        await SetStateAsync(created.Id, state);
        _jobs.Clear();

        await Should.ThrowAsync<UserFriendlyException>(() => _service.ResendInvitationAsync(created.Id));

        _jobs.Jobs.ShouldBeEmpty();
    }

    [Fact]
    public async Task Resend_for_a_ready_tenant_queues_an_invitation()
    {
        var created = await CreateAsync("acme");
        await SetStateAsync(created.Id, TenantProvisioningState.Succeeded);
        _jobs.Clear();

        await _service.ResendInvitationAsync(created.Id);

        _jobs.Jobs.ShouldHaveSingleItem().ShouldBeOfType<TenantInvitationArgs>().TenantId.ShouldBe(created.Id);
    }

    /* ── Tenant isolation: host only, even with authorization relaxed ───────────────────────── */

    [Fact]
    public async Task Inside_a_tenant_every_method_is_refused_even_with_permissions_relaxed()
    {
        /* The test container runs with AlwaysAllowAuthorization: this is the HostOnlyGuard alone. */
        var created = await CreateAsync("victim");
        await SetStateAsync(created.Id, TenantProvisioningState.Failed);
        _jobs.Clear();

        foreach (var tenantId in new[] { Guid.NewGuid(), created.Id })
        {
            using (_currentTenant.Change(tenantId))
            {
                await Should.ThrowAsync<AbpAuthorizationException>(() => _service.GetListAsync(Request()));
                await Should.ThrowAsync<AbpAuthorizationException>(() => _service.GetAsync(created.Id));
                await Should.ThrowAsync<AbpAuthorizationException>(() => _service.GetEditionLookupAsync());
                await Should.ThrowAsync<AbpAuthorizationException>(() => _service.GetConnectionPreviewAsync("x"));
                await Should.ThrowAsync<AbpAuthorizationException>(() => CreateAsync("intruder"));
                await Should.ThrowAsync<AbpAuthorizationException>(() =>
                    _service.UpdateAsync(created.Id, new TenantUpdateDto { Name = "hijacked" }));
                await Should.ThrowAsync<AbpAuthorizationException>(() => _service.DeleteAsync(created.Id));
                await Should.ThrowAsync<AbpAuthorizationException>(() => _service.RetryProvisioningAsync(created.Id));
                await Should.ThrowAsync<AbpAuthorizationException>(() => _service.ResendInvitationAsync(created.Id));
            }
        }

        _jobs.Jobs.ShouldBeEmpty();
        var tenant = (await FindTenantAsync(created.Id)).ShouldNotBeNull();
        tenant.Name.ShouldBe("victim");
        (await WithUnitOfWorkAsync(() => _tenantRepository.GetCountAsync())).ShouldBe(1);
    }
}
