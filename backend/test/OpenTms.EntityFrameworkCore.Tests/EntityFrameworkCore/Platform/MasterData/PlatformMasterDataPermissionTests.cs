using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Localization;
using OpenTms.Platform.Administration;
using OpenTms.Platform.Permissions;
using Shouldly;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Data;
using Volo.Abp.Localization;
using Volo.Abp.MultiTenancy;
using Volo.Abp.PermissionManagement;
using Volo.Abp.TenantManagement;
using Xunit;

namespace OpenTms.EntityFrameworkCore.Platform.MasterData;

/// <summary>
/// The sixteen Platform master-data permissions through the real definition manager and the admin seed
/// (docs/designs/49-platform-master-data.md, "Feature flags and permissions": both sides, Create/Update/Delete children of
/// each list's view permission, "Seeded for the admin role on both sides by AdministrationPermissionDataSeedContributor").
/// Risk class: Critical (authorization).
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class PlatformMasterDataPermissionTests : OpenTmsEntityFrameworkCoreTestBase
{
    private static readonly string[] All = PlatformPermissions.MasterData.All.SelectMany(p => new[] { p.Default, p.Create, p.Update, p.Delete }).ToArray();

    [Fact]
    public async Task Sixteen_permissions_on_both_sides_each_list_with_its_three_children()
    {
        All.Length.ShouldBe(16);
        var manager = GetRequiredService<IPermissionDefinitionManager>();

        foreach (var (entity, view, create, update, delete) in PlatformPermissions.MasterData.All)
        {
            var parent = await manager.GetAsync(view);
            parent.Parent.ShouldBeNull(view);
            parent.MultiTenancySide.ShouldBe(MultiTenancySides.Both, view);
            parent.Children.Select(c => c.Name).ShouldBe([create, update, delete], ignoreOrder: true);
            parent.Children.ShouldAllBe(c => c.MultiTenancySide == MultiTenancySides.Both && c.Children.Count == 0);
            entity.ShouldNotBeNullOrWhiteSpace();
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("nl")]
    public async Task Every_permission_has_a_display_name_in_both_languages(string culture)
    {
        var manager = GetRequiredService<IPermissionDefinitionManager>();
        var factory = GetRequiredService<IStringLocalizerFactory>();
        using (CultureHelper.Use(CultureInfo.GetCultureInfo(culture)))
        {
            foreach (var name in All)
            {
                var text = (await manager.GetAsync(name)).DisplayName.Localize(factory);
                text.ResourceNotFound.ShouldBeFalse($"{culture}: {name}");
                text.Value.ShouldNotBeNullOrWhiteSpace();
            }
        }
    }

    [Fact]
    public async Task The_seed_grants_all_sixteen_to_the_admin_role_of_the_host_and_of_a_tenant_and_a_second_run_changes_nothing()
    {
        var tenantId = await WithUnitOfWorkAsync(async () =>
        {
            var tenant = await GetRequiredService<ITenantManager>().CreateAsync("platform-md");
            await GetRequiredService<ITenantRepository>().InsertAsync(tenant, autoSave: true);
            return tenant.Id;
        });

        var contributor = GetRequiredService<AdministrationPermissionDataSeedContributor>();
        var currentTenant = GetRequiredService<ICurrentTenant>();
        var grants = GetRequiredService<IPermissionGrantRepository>();

        for (var run = 0; run < 2; run++)
        {
            await WithUnitOfWorkAsync(() => contributor.SeedAsync(new DataSeedContext(null)));
            await WithUnitOfWorkAsync(async () =>
            {
                using (currentTenant.Change(tenantId))
                {
                    await contributor.SeedAsync(new DataSeedContext(tenantId));
                }
            });
        }

        await WithUnitOfWorkAsync(async () =>
        {
            foreach (var side in new Guid?[] { null, tenantId })
            {
                using (currentTenant.Change(side))
                {
                    foreach (var name in All)
                    {
                        (await grants.FindAsync(name, RolePermissionValueProvider.ProviderName, "admin")).ShouldNotBeNull($"{name} for the admin role of {(side == null ? "the host" : "the tenant")}");
                    }

                    var list = await grants.GetListAsync(RolePermissionValueProvider.ProviderName, "admin");
                    list.Where(g => All.Contains(g.Name)).Count().ShouldBe(16, "one grant per permission after two runs");
                }
            }
        });
    }
}
