using System;
using System.Threading.Tasks;
using OpenTms.Platform.Tenants;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Volo.Abp.MultiTenancy;
using Volo.Abp.TenantManagement;
using Volo.Abp.Uow;
using Xunit;
using TenantCreateDto = OpenTms.Platform.Tenants.TenantCreateDto;
using TenantDto = OpenTms.Platform.Tenants.TenantDto;

namespace OpenTms.EntityFrameworkCore.Platform.Tenants;

/// <summary>
/// The review fixes of 2026-10-14 on the host-side tenant services (docs/designs/tenants-and-editions.md): delete is
/// refused while provisioning is queued or running and erases the pending administrator address before the soft
/// delete (GDPR art. 17); the older provisioning endpoint (TenantProvisioningAppService) follows the Tenants app's
/// rules — host only, only a failed tenant, queued after commit. Through the real container on SQLite with the job
/// queue and the database server faked. Risk class: Critical — Platform (a drop racing a running provisioning; two
/// provisioning runs overlapping; personal data left on a deleted tenant).
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class TenantReviewFixesTests : OpenTmsTestBase<TenantAdministrationTestModule>
{
    private readonly ITenantAdministrationAppService _tenants;
    private readonly ITenantProvisioningAppService _provisioning;
    private readonly ITenantRepository _tenantRepository;
    private readonly IDataFilter _dataFilter;
    private readonly ICurrentTenant _currentTenant;
    private readonly FakeBackgroundJobManager _jobs;

    public TenantReviewFixesTests()
    {
        _tenants = GetRequiredService<ITenantAdministrationAppService>();
        _provisioning = GetRequiredService<ITenantProvisioningAppService>();
        _tenantRepository = GetRequiredService<ITenantRepository>();
        _dataFilter = GetRequiredService<IDataFilter>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
        _jobs = GetRequiredService<FakeBackgroundJobManager>();
    }

    private Task<TenantDto> CreateAsync(string name) =>
        _tenants.CreateAsync(new TenantCreateDto { Name = name, AdminEmailAddress = "admin@" + name + ".test" });

    private Task<Tenant?> FindAsync(Guid id, bool includeDeleted = false) =>
        WithUnitOfWorkAsync(async () =>
        {
            using (includeDeleted ? _dataFilter.Disable<ISoftDelete>() : null)
            {
                return await _tenantRepository.FindAsync(id, includeDetails: true);
            }
        });

    private Task SetStateAsync(Guid id, string? state) =>
        WithUnitOfWorkAsync(async () =>
        {
            var tenant = await _tenantRepository.GetAsync(id);
            tenant.SetProperty(TenantProperties.ProvisioningState, state);
            await _tenantRepository.UpdateAsync(tenant, autoSave: true);
        });

    private async Task<string?> StateAsync(Guid id) => (await FindAsync(id))!.GetProperty<string?>(TenantProperties.ProvisioningState);

    // ── Delete ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(TenantProvisioningState.Queued)]
    [InlineData(TenantProvisioningState.Running)]
    public async Task Delete_is_refused_while_provisioning_is_queued_or_running_and_changes_nothing(string state)
    {
        var created = await CreateAsync("acme");
        await SetStateAsync(created.Id, state);
        _jobs.Clear();

        var ex = await Should.ThrowAsync<UserFriendlyException>(() => _tenants.DeleteAsync(created.Id));
        ex.Message.ShouldNotContain("admin@");

        var tenant = (await FindAsync(created.Id)).ShouldNotBeNull();
        tenant.IsDeleted.ShouldBeFalse();
        tenant.GetProperty<string?>(TenantProperties.ProvisioningState).ShouldBe(state);
        tenant.GetProperty<string?>(TenantProperties.PendingAdminEmail).ShouldBe("admin@acme.test", "a refused delete erases nothing");
        _jobs.Jobs.ShouldBeEmpty("no drop is queued");
    }

    [Fact]
    public async Task A_freshly_created_tenant_is_queued_and_therefore_not_deletable_yet()
    {
        var created = await CreateAsync("acme");
        (await StateAsync(created.Id)).ShouldBe(TenantProvisioningState.Queued);
        await Should.ThrowAsync<UserFriendlyException>(() => _tenants.DeleteAsync(created.Id));
    }

    [Fact]
    public async Task Deleting_a_failed_tenant_erases_the_pending_administrator_address_from_the_soft_deleted_row()
    {
        var created = await CreateAsync("acme");
        await SetStateAsync(created.Id, TenantProvisioningState.Failed);
        (await FindAsync(created.Id))!.GetProperty<string?>(TenantProperties.PendingAdminEmail).ShouldBe("admin@acme.test");
        _jobs.Clear();

        await _tenants.DeleteAsync(created.Id);

        var deleted = (await FindAsync(created.Id, includeDeleted: true)).ShouldNotBeNull();
        deleted.IsDeleted.ShouldBeTrue();
        deleted.GetProperty<string?>(TenantProperties.PendingAdminEmail).ShouldBeNull();
        deleted.ExtraProperties.ShouldNotContainKey(TenantProperties.PendingAdminEmail);
        _jobs.JobsOf<TenantDatabaseDropArgs>().ShouldHaveSingleItem().TenantId.ShouldBe(created.Id);
    }

    [Theory]
    [InlineData(TenantProvisioningState.Succeeded)]
    [InlineData(null)]
    public async Task A_ready_tenant_or_one_from_before_the_app_is_deletable(string? state)
    {
        var created = await CreateAsync("acme");
        await SetStateAsync(created.Id, state);

        await _tenants.DeleteAsync(created.Id);

        (await FindAsync(created.Id)).ShouldBeNull();
    }

    // ── TenantProvisioningAppService.RequestProvisioningAsync ─────────────────

    [Fact]
    public async Task Request_is_host_only()
    {
        var created = await CreateAsync("acme");
        await SetStateAsync(created.Id, TenantProvisioningState.Failed);
        _jobs.Clear();

        using (_currentTenant.Change(created.Id))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() =>
                _provisioning.RequestProvisioningAsync(new TenantProvisioningRequestDto { TenantId = created.Id }));
        }

        (await StateAsync(created.Id)).ShouldBe(TenantProvisioningState.Failed);
        _jobs.Jobs.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(TenantProvisioningState.Queued)]
    [InlineData(TenantProvisioningState.Running)]
    [InlineData(TenantProvisioningState.Succeeded)]
    [InlineData(null)]
    public async Task Request_is_refused_unless_provisioning_failed(string? state)
    {
        var created = await CreateAsync("acme");
        await SetStateAsync(created.Id, state);
        _jobs.Clear();

        var ex = await Should.ThrowAsync<UserFriendlyException>(() =>
            _provisioning.RequestProvisioningAsync(new TenantProvisioningRequestDto { TenantId = created.Id }));
        ex.Message.ShouldBe(GetRequiredService<Microsoft.Extensions.Localization.IStringLocalizer<OpenTms.Platform.Localization.PlatformResource>>()["Tenants:RetryOnlyWhenFailed"]);

        (await StateAsync(created.Id)).ShouldBe(state);
        _jobs.Jobs.ShouldBeEmpty();
    }

    [Fact]
    public async Task Request_of_an_unknown_tenant_is_not_found()
    {
        await Should.ThrowAsync<EntityNotFoundException>(() =>
            _provisioning.RequestProvisioningAsync(new TenantProvisioningRequestDto { TenantId = Guid.NewGuid() }));
        _jobs.Jobs.ShouldBeEmpty();
    }

    [Fact]
    public async Task Request_of_a_failed_tenant_sets_queued_and_queues_one_run_without_a_job_id_and_a_second_request_is_refused()
    {
        var created = await CreateAsync("acme");
        await SetStateAsync(created.Id, TenantProvisioningState.Failed);
        _jobs.Clear();

        var result = await _provisioning.RequestProvisioningAsync(new TenantProvisioningRequestDto { TenantId = created.Id });

        result.TenantId.ShouldBe(created.Id);
        result.Status.ShouldBe(TenantProvisioningState.Queued);
        result.JobId.ShouldBeNull("the job is queued after commit, when the response is already built");
        (await StateAsync(created.Id)).ShouldBe(TenantProvisioningState.Queued);
        _jobs.JobsOf<TenantProvisioningArgs>().ShouldHaveSingleItem().TenantId.ShouldBe(created.Id);

        await Should.ThrowAsync<UserFriendlyException>(() =>
            _provisioning.RequestProvisioningAsync(new TenantProvisioningRequestDto { TenantId = created.Id }));
        _jobs.Jobs.Count.ShouldBe(1, "two runs never overlap");
    }

    [Fact]
    public async Task The_job_is_queued_only_once_the_unit_of_work_commits()
    {
        var created = await CreateAsync("acme");
        await SetStateAsync(created.Id, TenantProvisioningState.Failed);
        _jobs.Clear();
        var unitOfWorkManager = GetRequiredService<IUnitOfWorkManager>();

        using (var unitOfWork = unitOfWorkManager.Begin(requiresNew: true, isTransactional: true))
        {
            await _provisioning.RequestProvisioningAsync(new TenantProvisioningRequestDto { TenantId = created.Id });
            _jobs.Jobs.ShouldBeEmpty("not before the commit");
            await unitOfWork.CompleteAsync();
        }

        _jobs.JobsOf<TenantProvisioningArgs>().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_unit_of_work_that_does_not_complete_queues_no_job()
    {
        var created = await CreateAsync("acme");
        await SetStateAsync(created.Id, TenantProvisioningState.Failed);
        _jobs.Clear();
        var unitOfWorkManager = GetRequiredService<IUnitOfWorkManager>();

        using (unitOfWorkManager.Begin(requiresNew: true, isTransactional: true))
        {
            await _provisioning.RequestProvisioningAsync(new TenantProvisioningRequestDto { TenantId = created.Id });
        }

        _jobs.Jobs.ShouldBeEmpty();
    }
}
