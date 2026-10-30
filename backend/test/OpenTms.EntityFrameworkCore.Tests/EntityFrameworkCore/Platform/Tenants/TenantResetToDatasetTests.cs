using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using OpenTms.Platform.Grid;
using OpenTms.Platform.Permissions;
using OpenTms.Platform.Tenants;
using OpenTms.Platform.TestData;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Security.Claims;
using Volo.Abp.TenantManagement;
using Volo.Abp.Uow;
using Volo.Abp.Validation;
using Xunit;
using TenantCreateDto = OpenTms.Platform.Tenants.TenantCreateDto;

namespace OpenTms.EntityFrameworkCore.Platform.Tenants;

/// <summary>
/// "Reset to dataset" through the Tenants app's application service (docs/designs/65-tenant-test-datasets.md,
/// Behaviour 2), with the job queue faked (<see cref="TenantAdministrationTestModule"/>): every server-side guard
/// (dataset, typed name, provisioning state, a reset already pending, host only), what a successful request records,
/// that the job is queued only after commit, the permission, and the dataset fields of the list and detail DTOs.
/// Risk class: Critical — a host action that deletes a tenant's data (Platform; authorization and tenant isolation).
/// The job itself: TestDatasetResetJobTests (orchestration) and PostgreSql/TestData (the real contributors).
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class TenantResetToDatasetTests : OpenTmsTestBase<TenantAdministrationTestModule>
{
    private readonly ITenantAdministrationAppService _service;
    private readonly ITenantRepository _tenantRepository;
    private readonly ITenantManager _tenantManager;
    private readonly ICurrentTenant _currentTenant;
    private readonly FakeBackgroundJobManager _jobs;

    public TenantResetToDatasetTests()
    {
        _service = GetRequiredService<ITenantAdministrationAppService>();
        _tenantRepository = GetRequiredService<ITenantRepository>();
        _tenantManager = GetRequiredService<ITenantManager>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
        _jobs = GetRequiredService<FakeBackgroundJobManager>();
    }

    private static TenantResetToDatasetDto Input(string dataset, string name) => new() { Dataset = dataset, ConfirmationName = name };

    private Task ResetAsync(Guid id, string dataset = TestDatasets.Average, string name = "acme") =>
        _service.ResetToDatasetAsync(id, Input(dataset, name));

    /// <summary>A provisioned tenant (Succeeded) with no reset yet; the provisioning job is cleared from the fake queue.</summary>
    private async Task<Guid> ReadyTenantAsync(string name = "acme", string? provisioningState = TenantProvisioningState.Succeeded)
    {
        var created = await _service.CreateAsync(new TenantCreateDto { Name = name, AdminEmailAddress = "admin@acme.test" });
        await SetPropertiesAsync(created.Id, (TenantProperties.ProvisioningState, provisioningState));
        _jobs.Clear();
        return created.Id;
    }

    private async Task SetPropertiesAsync(Guid id, params (string Name, object? Value)[] properties)
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var tenant = await _tenantRepository.GetAsync(id);
            foreach (var (name, value) in properties)
            {
                if (value == null)
                {
                    tenant.RemoveProperty(name);
                }
                else
                {
                    tenant.SetProperty(name, value);
                }
            }

            await _tenantRepository.UpdateAsync(tenant, autoSave: true);
        });
    }

    private Task<Tenant> GetTenantAsync(Guid id) =>
        WithUnitOfWorkAsync(() => _tenantRepository.GetAsync(id, includeDetails: true));

    private async Task ShouldBeUntouchedAsync(Guid id, string? resetState = null)
    {
        _jobs.JobsOf<TestDatasetResetArgs>().ShouldBeEmpty();
        var tenant = await GetTenantAsync(id);
        tenant.GetProperty<string?>(TenantProperties.DatasetResetState).ShouldBe(resetState);
        if (resetState == null)
        {
            tenant.GetProperty<string?>(TenantProperties.DatasetName).ShouldBeNull();
        }
    }

    /* ── Guards ─────────────────────────────────────────────────────────────────────────────── */

    [Theory]
    [InlineData("Medium")]
    [InlineData("average")]
    [InlineData("LARGE")]
    [InlineData(" Average")]
    [InlineData("Average; DROP TABLE")]
    public async Task An_unknown_dataset_is_refused_and_nothing_is_queued(string dataset)
    {
        var id = await ReadyTenantAsync();

        await Should.ThrowAsync<UserFriendlyException>(() => ResetAsync(id, dataset));

        await ShouldBeUntouchedAsync(id);
    }

    [Theory]
    [InlineData("acm")]
    [InlineData("acme2")]
    [InlineData("beta")]
    [InlineData("a c m e")]
    public async Task A_wrong_confirmation_name_is_refused(string typed)
    {
        var id = await ReadyTenantAsync();

        await Should.ThrowAsync<UserFriendlyException>(() => ResetAsync(id, name: typed));

        await ShouldBeUntouchedAsync(id);
    }

    [Fact]
    public async Task The_name_of_another_tenant_does_not_confirm()
    {
        var acme = await ReadyTenantAsync("acme");
        await ReadyTenantAsync("beta");

        await Should.ThrowAsync<UserFriendlyException>(() => ResetAsync(acme, name: "beta"));

        await ShouldBeUntouchedAsync(acme);
    }

    [Theory]
    [InlineData("", "acme")]
    [InlineData("Average", "")]
    public async Task Dataset_and_confirmation_name_are_required(string dataset, string name)
    {
        var id = await ReadyTenantAsync();

        await Should.ThrowAsync<AbpValidationException>(() => ResetAsync(id, dataset, name));

        await ShouldBeUntouchedAsync(id);
    }

    [Theory]
    [InlineData(TenantProvisioningState.Queued)]
    [InlineData(TenantProvisioningState.Running)]
    [InlineData(TenantProvisioningState.Failed)]
    public async Task A_tenant_that_is_not_provisioned_is_refused(string provisioningState)
    {
        var id = await ReadyTenantAsync(provisioningState: provisioningState);

        await Should.ThrowAsync<UserFriendlyException>(() => ResetAsync(id));

        await ShouldBeUntouchedAsync(id);
    }

    [Fact]
    public async Task A_tenant_from_before_the_Tenants_app_without_provisioning_state_may_be_reset()
    {
        var id = await WithUnitOfWorkAsync(async () =>
        {
            var tenant = await _tenantManager.CreateAsync("legacy");
            await _tenantRepository.InsertAsync(tenant, autoSave: true);
            return tenant.Id;
        });

        await ResetAsync(id, name: "legacy");

        _jobs.JobsOf<TestDatasetResetArgs>().ShouldHaveSingleItem().TenantId.ShouldBe(id);
    }

    [Theory]
    [InlineData(TestDatasetResetState.Queued)]
    [InlineData(TestDatasetResetState.Running)]
    public async Task A_reset_while_one_is_pending_is_refused(string resetState)
    {
        var id = await ReadyTenantAsync();
        await SetPropertiesAsync(id, (TenantProperties.DatasetResetState, resetState), (TenantProperties.DatasetName, TestDatasets.Large));

        await Should.ThrowAsync<UserFriendlyException>(() => ResetAsync(id, TestDatasets.Average));

        _jobs.JobsOf<TestDatasetResetArgs>().ShouldBeEmpty();
        var tenant = await GetTenantAsync(id);
        tenant.GetProperty<string?>(TenantProperties.DatasetResetState).ShouldBe(resetState);
        tenant.GetProperty<string?>(TenantProperties.DatasetName).ShouldBe(TestDatasets.Large);
    }

    [Theory]
    [InlineData(TestDatasetResetState.Succeeded)]
    [InlineData(TestDatasetResetState.Failed)]
    public async Task A_finished_reset_may_be_followed_by_a_new_one_which_clears_the_old_error(string resetState)
    {
        var id = await ReadyTenantAsync();
        await SetPropertiesAsync(id,
            (TenantProperties.DatasetResetState, resetState),
            (TenantProperties.DatasetName, TestDatasets.Large),
            (TenantProperties.DatasetResetError, "InvalidOperationException: earlier run"));

        await ResetAsync(id, TestDatasets.Average);

        var tenant = await GetTenantAsync(id);
        tenant.GetProperty<string?>(TenantProperties.DatasetResetState).ShouldBe(TestDatasetResetState.Queued);
        tenant.GetProperty<string?>(TenantProperties.DatasetName).ShouldBe(TestDatasets.Average);
        tenant.GetProperty<string?>(TenantProperties.DatasetResetError).ShouldBeNull();
        _jobs.JobsOf<TestDatasetResetArgs>().ShouldHaveSingleItem().Dataset.ShouldBe(TestDatasets.Average);
    }

    [Fact]
    public async Task An_unknown_tenant_is_not_found()
    {
        await Should.ThrowAsync<EntityNotFoundException>(() => ResetAsync(Guid.NewGuid()));
        _jobs.Jobs.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_deleted_tenant_is_not_found()
    {
        var id = await ReadyTenantAsync();
        await _service.DeleteAsync(id);
        _jobs.Clear();

        await Should.ThrowAsync<EntityNotFoundException>(() => ResetAsync(id));
        _jobs.Jobs.ShouldBeEmpty();
    }

    /* ── Host only / authorization ──────────────────────────────────────────────────────────── */

    [Fact]
    public async Task Inside_a_tenant_the_reset_is_refused_even_for_the_tenant_itself()
    {
        /* The test container grants every permission to the signed-in admin: this is the HostOnlyGuard alone. */
        var id = await ReadyTenantAsync();

        foreach (var tenantId in new[] { Guid.NewGuid(), id })
        {
            using (_currentTenant.Change(tenantId))
            {
                await Should.ThrowAsync<AbpAuthorizationException>(() => ResetAsync(id));
            }
        }

        await ShouldBeUntouchedAsync(id);
    }

    [Fact]
    public async Task A_caller_without_a_user_is_refused()
    {
        var id = await ReadyTenantAsync();

        using (GetRequiredService<ICurrentPrincipalAccessor>().Change(new ClaimsPrincipal(new ClaimsIdentity())))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => ResetAsync(id));
        }

        await ShouldBeUntouchedAsync(id);
    }

    [Fact]
    public async Task The_permission_is_a_host_side_child_of_Tenants()
    {
        PlatformPermissions.Tenants.ResetToDataset.ShouldBe("Platform.Tenants.ResetToDataset");

        var definitions = GetRequiredService<IPermissionDefinitionManager>();
        var permission = (await definitions.GetOrNullAsync(PlatformPermissions.Tenants.ResetToDataset)).ShouldNotBeNull();
        permission.MultiTenancySide.ShouldBe(MultiTenancySides.Host);
        permission.Parent.ShouldNotBeNull().Name.ShouldBe(PlatformPermissions.Tenants.Default);
        permission.IsEnabled.ShouldBeTrue();
    }

    [Fact]
    public void The_service_method_requires_the_ResetToDataset_permission()
    {
        var method = typeof(TenantAdministrationAppService).GetMethod(nameof(TenantAdministrationAppService.ResetToDatasetAsync))!;

        method.GetCustomAttributes<AuthorizeAttribute>().Select(a => a.Policy)
            .ShouldBe(new[] { PlatformPermissions.Tenants.ResetToDataset });
    }

    /* ── Success ────────────────────────────────────────────────────────────────────────────── */

    [Theory]
    [InlineData(TestDatasets.Average)]
    [InlineData(TestDatasets.Large)]
    public async Task A_valid_request_marks_the_tenant_Queued_and_queues_the_job(string dataset)
    {
        var id = await ReadyTenantAsync();
        var before = DateTime.Now.AddSeconds(-5);

        await ResetAsync(id, dataset, "acme");

        var tenant = await GetTenantAsync(id);
        tenant.GetProperty<string?>(TenantProperties.DatasetName).ShouldBe(dataset);
        tenant.GetProperty<string?>(TenantProperties.DatasetResetState).ShouldBe(TestDatasetResetState.Queued);
        tenant.GetProperty<DateTime?>(TenantProperties.DatasetResetAt).ShouldNotBeNull().ShouldBeGreaterThan(before);
        /* The request id of a finished run stays until the job finishes this one (redelivery guard). */
        tenant.GetProperty<Guid?>(TenantProperties.DatasetResetRequestId).ShouldBeNull();

        var args = _jobs.Jobs.ShouldHaveSingleItem().ShouldBeOfType<TestDatasetResetArgs>();
        args.TenantId.ShouldBe(id);
        args.Dataset.ShouldBe(dataset);
        args.RequestId.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task The_typed_name_may_differ_in_surrounding_spaces()
    {
        var id = await ReadyTenantAsync();

        await ResetAsync(id, name: "  acme ");

        _jobs.JobsOf<TestDatasetResetArgs>().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_second_click_while_the_first_is_queued_is_refused()
    {
        var id = await ReadyTenantAsync();
        await ResetAsync(id);

        await Should.ThrowAsync<UserFriendlyException>(() => ResetAsync(id, TestDatasets.Large));

        _jobs.JobsOf<TestDatasetResetArgs>().ShouldHaveSingleItem().Dataset.ShouldBe(TestDatasets.Average);
        (await GetTenantAsync(id)).GetProperty<string?>(TenantProperties.DatasetName).ShouldBe(TestDatasets.Average);
    }

    [Fact]
    public async Task Every_request_gets_its_own_request_id()
    {
        var id = await ReadyTenantAsync();
        await ResetAsync(id);
        await SetPropertiesAsync(id, (TenantProperties.DatasetResetState, TestDatasetResetState.Succeeded));
        await ResetAsync(id);

        var ids = _jobs.JobsOf<TestDatasetResetArgs>().Select(a => a.RequestId).ToList();
        ids.Count.ShouldBe(2);
        ids.Distinct().Count().ShouldBe(2);
    }

    [Fact]
    public async Task The_job_is_queued_only_after_the_unit_of_work_commits()
    {
        var id = await ReadyTenantAsync();
        var unitOfWorkManager = GetRequiredService<IUnitOfWorkManager>();

        using (var unitOfWork = unitOfWorkManager.Begin(requiresNew: true))
        {
            await ResetAsync(id);
            _jobs.Jobs.ShouldBeEmpty("the job must not run against a Queued state that may still roll back");
            await unitOfWork.CompleteAsync();
        }

        _jobs.JobsOf<TestDatasetResetArgs>().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_rolled_back_request_queues_nothing()
    {
        /* The SQLite test container disables transactions (AddAlwaysDisableUnitOfWorkTransaction), so only the queue is
         * asserted here; that the Queued state rolls back too is asserted against PostgreSQL (TestDatasetResetPostgreSqlTests). */
        var id = await ReadyTenantAsync();
        var unitOfWorkManager = GetRequiredService<IUnitOfWorkManager>();

        using (unitOfWorkManager.Begin(requiresNew: true))
        {
            await ResetAsync(id);
            /* disposed without CompleteAsync */
        }

        _jobs.Jobs.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_job_arguments_carry_ids_and_the_dataset_name_only()
    {
        var id = await ReadyTenantAsync();
        await ResetAsync(id);

        var json = JsonSerializer.Serialize(_jobs.Jobs.ShouldHaveSingleItem(), _jobs.Jobs[0].GetType());
        using var document = JsonDocument.Parse(json);
        document.RootElement.EnumerateObject().Select(p => p.Name)
            .ShouldBe(new[] { nameof(TestDatasetResetArgs.TenantId), nameof(TestDatasetResetArgs.Dataset), nameof(TestDatasetResetArgs.RequestId) }, ignoreOrder: true);
        json.ShouldNotContain("acme");
    }

    /* ── DTOs ───────────────────────────────────────────────────────────────────────────────── */

    [Fact]
    public async Task List_and_detail_show_the_dataset_its_state_and_time_and_the_detail_its_error()
    {
        var id = await ReadyTenantAsync();
        var at = new DateTime(2026, 10, 30, 12, 0, 0);
        await SetPropertiesAsync(id,
            (TenantProperties.DatasetName, TestDatasets.Large),
            (TenantProperties.DatasetResetState, TestDatasetResetState.Failed),
            (TenantProperties.DatasetResetAt, at),
            (TenantProperties.DatasetResetError, "InvalidOperationException: boom"));
        var untouched = await ReadyTenantAsync("beta");

        var list = await _service.GetListAsync(new GridRequest { StartRow = 0, EndRow = 50 });
        var row = list.Rows.Single(r => r.Id == id);
        row.DatasetName.ShouldBe(TestDatasets.Large);
        row.DatasetResetState.ShouldBe(TestDatasetResetState.Failed);
        row.DatasetResetAt.ShouldBe(at);
        JsonSerializer.Serialize(list).ShouldNotContain("boom", Case.Insensitive, "the list carries no error text");

        var other = list.Rows.Single(r => r.Id == untouched);
        other.DatasetName.ShouldBeNull();
        other.DatasetResetState.ShouldBeNull();
        other.DatasetResetAt.ShouldBeNull();

        var dto = await _service.GetAsync(id);
        dto.DatasetName.ShouldBe(TestDatasets.Large);
        dto.DatasetResetState.ShouldBe(TestDatasetResetState.Failed);
        dto.DatasetResetAt.ShouldBe(at);
        dto.DatasetResetError.ShouldBe("InvalidOperationException: boom");

        (await _service.GetAsync(untouched)).DatasetResetError.ShouldBeNull();
    }

    /* ── Wiring ─────────────────────────────────────────────────────────────────────────────── */

    [Fact]
    public void Every_module_contributes_once_in_the_documented_order()
    {
        var contributors = GetRequiredService<IEnumerable<ITestDatasetContributor>>().ToList();

        contributors.Select(c => c.Order).OrderBy(o => o).ShouldBe(new[] { 0, 100, 200, 300, 400 });
        contributors.Select(c => c.GetType().Name).ShouldBe(new[]
        {
            "PlatformTestDatasetContributor",
            "MasterDataTestDatasetContributor",
            "FinancialTestDatasetContributor",
            "OrdersTestDatasetContributor",
            "PlanningExecutionTestDatasetContributor"
        }, ignoreOrder: true);
    }
}
