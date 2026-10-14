using System;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Platform.Editions;
using OpenTms.Platform.Grid;
using OpenTms.Platform.Permissions;
using OpenTms.Platform.Tenants;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Domain.Entities;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Validation;
using Xunit;
using TenantDto = OpenTms.Platform.Tenants.TenantDto;
using TenantCreateDto = OpenTms.Platform.Tenants.TenantCreateDto;
using TenantUpdateDto = OpenTms.Platform.Tenants.TenantUpdateDto;

namespace OpenTms.EntityFrameworkCore.Platform.Tenants;

/// <summary>
/// The Editions app's application service through the real container (docs/designs/tenants-and-editions.md).
/// Risk class: Critical — Platform, host-owned data (authorization / tenant isolation), and an edition carries the
/// feature values that gate tenant behaviour (constitution rule 4); High — name uniqueness and the in-use guard.
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class EditionAdministrationAppServiceTests : OpenTmsTestBase<TenantAdministrationTestModule>
{
    private readonly IEditionAdministrationAppService _service;
    private readonly ITenantAdministrationAppService _tenants;
    private readonly ICurrentTenant _currentTenant;

    public EditionAdministrationAppServiceTests()
    {
        _service = GetRequiredService<IEditionAdministrationAppService>();
        _tenants = GetRequiredService<ITenantAdministrationAppService>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
    }

    private static GridRequest Request(string? search = null, SortModel? sort = null) => new()
    {
        StartRow = 0,
        EndRow = 50,
        WildcardSearch = search,
        SortModels = sort == null ? [] : [sort]
    };

    private Task<EditionRowDto> CreateAsync(string name) =>
        _service.CreateAsync(new EditionCreateUpdateDto { DisplayName = name });

    private Task<TenantDto> CreateTenantAsync(string name, Guid? editionId) =>
        _tenants.CreateAsync(new TenantCreateDto { Name = name, AdminEmailAddress = "admin@acme.test", EditionId = editionId });

    [Fact]
    public async Task Create_get_update_and_delete_an_edition()
    {
        var created = await CreateAsync("  Gold  ");
        created.DisplayName.ShouldBe("Gold");
        created.TenantCount.ShouldBe(0);

        (await _service.GetAsync(created.Id)).DisplayName.ShouldBe("Gold");

        var updated = await _service.UpdateAsync(created.Id, new EditionCreateUpdateDto { DisplayName = "Platinum" });
        updated.DisplayName.ShouldBe("Platinum");
        (await _service.GetAsync(created.Id)).DisplayName.ShouldBe("Platinum");

        await _service.DeleteAsync(created.Id);
        await Should.ThrowAsync<EntityNotFoundException>(() => _service.GetAsync(created.Id));
    }

    [Fact]
    public async Task A_name_in_use_is_refused_on_create_also_with_surrounding_spaces()
    {
        await CreateAsync("Gold");

        await Should.ThrowAsync<UserFriendlyException>(() => CreateAsync("Gold"));
        await Should.ThrowAsync<UserFriendlyException>(() => CreateAsync("  Gold "));

        (await _service.GetListAsync(Request())).TotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task Renaming_onto_another_editions_name_is_refused_but_keeping_the_own_name_is_fine()
    {
        await CreateAsync("Gold");
        var silver = await CreateAsync("Silver");

        await Should.ThrowAsync<UserFriendlyException>(() =>
            _service.UpdateAsync(silver.Id, new EditionCreateUpdateDto { DisplayName = "Gold" }));

        (await _service.UpdateAsync(silver.Id, new EditionCreateUpdateDto { DisplayName = "Silver" })).DisplayName.ShouldBe("Silver");
    }

    [Fact]
    public async Task The_name_of_a_deleted_edition_can_be_used_again()
    {
        var gold = await CreateAsync("Gold");
        await _service.DeleteAsync(gold.Id);

        (await CreateAsync("Gold")).Id.ShouldNotBe(gold.Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task A_name_is_required(string? name)
    {
        await Should.ThrowAsync<AbpValidationException>(() => _service.CreateAsync(new EditionCreateUpdateDto { DisplayName = name! }));
    }

    [Fact]
    public async Task A_name_longer_than_256_characters_is_refused()
    {
        await Should.ThrowAsync<AbpValidationException>(() => CreateAsync(new string('x', 257)));
    }

    [Fact]
    public async Task Delete_is_refused_while_tenants_are_on_the_edition()
    {
        var gold = await CreateAsync("Gold");
        await CreateTenantAsync("acme", gold.Id);
        await CreateTenantAsync("beta", gold.Id);

        var refused = await Should.ThrowAsync<EditionInUseException>(() => _service.DeleteAsync(gold.Id));

        refused.TenantCount.ShouldBe(2);
        refused.DisplayName.ShouldBe("Gold");
        (await _service.GetAsync(gold.Id)).TenantCount.ShouldBe(2);
    }

    [Fact]
    public async Task Deleted_tenants_do_not_keep_an_edition_in_use()
    {
        var gold = await CreateAsync("Gold");
        var acme = await CreateTenantAsync("acme", gold.Id);
        await GetRequiredService<TenantStateWriter>().SetStateAsync(acme.Id, TenantProvisioningState.Failed);
        await _tenants.DeleteAsync(acme.Id);

        (await _service.GetAsync(gold.Id)).TenantCount.ShouldBe(0);
        await _service.DeleteAsync(gold.Id);
    }

    [Fact]
    public async Task Moving_the_tenants_away_frees_the_edition()
    {
        var gold = await CreateAsync("Gold");
        var silver = await CreateAsync("Silver");
        var acme = await CreateTenantAsync("acme", gold.Id);

        await _tenants.UpdateAsync(acme.Id, new TenantUpdateDto { Name = "acme", EditionId = silver.Id });

        await _service.DeleteAsync(gold.Id);
        (await _service.GetAsync(silver.Id)).TenantCount.ShouldBe(1);
    }

    [Fact]
    public async Task The_list_counts_tenants_per_edition_and_searches_and_sorts()
    {
        var gold = await CreateAsync("Gold");
        var silver = await CreateAsync("Silver");
        await CreateAsync("Bronze");
        await CreateTenantAsync("acme", gold.Id);
        await CreateTenantAsync("beta", gold.Id);
        await CreateTenantAsync("gamma", silver.Id);
        await CreateTenantAsync("delta", null);

        var all = await _service.GetListAsync(Request());
        all.TotalCount.ShouldBe(3);
        all.Rows.Select(r => r.DisplayName).ShouldBe(new[] { "Bronze", "Gold", "Silver" });
        all.Rows.Select(r => r.TenantCount).ShouldBe(new[] { 0, 2, 1 });

        var byCount = await _service.GetListAsync(Request(sort: new SortModel("tenantCount", "desc")));
        byCount.Rows.Select(r => r.DisplayName).ShouldBe(new[] { "Gold", "Silver", "Bronze" });

        var searched = await _service.GetListAsync(Request("ilv"));
        searched.FilteredCount.ShouldBe(1);
        searched.Rows.ShouldHaveSingleItem().DisplayName.ShouldBe("Silver");
    }

    [Fact]
    public async Task Inside_a_tenant_every_method_is_refused_even_with_permissions_relaxed()
    {
        var gold = await CreateAsync("Gold");

        using (_currentTenant.Change(Guid.NewGuid()))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.GetListAsync(Request()));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.GetAsync(gold.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => CreateAsync("Intruder"));
            await Should.ThrowAsync<AbpAuthorizationException>(() =>
                _service.UpdateAsync(gold.Id, new EditionCreateUpdateDto { DisplayName = "Hijacked" }));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.DeleteAsync(gold.Id));
        }

        var list = await _service.GetListAsync(Request());
        list.Rows.ShouldHaveSingleItem().DisplayName.ShouldBe("Gold");
    }

    [Fact]
    public async Task The_tenant_and_edition_permissions_are_host_side_only()
    {
        var manager = GetRequiredService<IPermissionDefinitionManager>();
        string[] names =
        [
            PlatformPermissions.Tenants.Default,
            PlatformPermissions.Tenants.Create,
            PlatformPermissions.Tenants.Update,
            PlatformPermissions.Tenants.Delete,
            PlatformPermissions.Editions.Default,
            PlatformPermissions.Editions.Create,
            PlatformPermissions.Editions.Update,
            PlatformPermissions.Editions.Delete,
            PlatformPermissions.Editions.ManageFeatures,
        ];

        foreach (var name in names)
        {
            var permission = await manager.GetAsync(name);
            permission.MultiTenancySide.ShouldBe(MultiTenancySides.Host, name);
        }

        /* Every permission under the two roots is covered: a child added later must be host-only too. */
        var all = await manager.GetPermissionsAsync();
        all.Where(p => p.Name.StartsWith(PlatformPermissions.Tenants.Default, StringComparison.Ordinal)
                       || p.Name.StartsWith(PlatformPermissions.Editions.Default, StringComparison.Ordinal))
            .ShouldAllBe(p => p.MultiTenancySide == MultiTenancySides.Host);
    }

    [Fact]
    public void The_permission_names_are_stable()
    {
        PlatformPermissions.Tenants.Default.ShouldBe("Platform.Tenants");
        PlatformPermissions.Tenants.Create.ShouldBe("Platform.Tenants.Create");
        PlatformPermissions.Tenants.Update.ShouldBe("Platform.Tenants.Update");
        PlatformPermissions.Tenants.Delete.ShouldBe("Platform.Tenants.Delete");
        PlatformPermissions.Editions.Default.ShouldBe("Platform.Editions");
        PlatformPermissions.Editions.Create.ShouldBe("Platform.Editions.Create");
        PlatformPermissions.Editions.Update.ShouldBe("Platform.Editions.Update");
        PlatformPermissions.Editions.Delete.ShouldBe("Platform.Editions.Delete");
        PlatformPermissions.Editions.ManageFeatures.ShouldBe("Platform.Editions.ManageFeatures");
    }
}
