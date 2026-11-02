using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using OpenTms.PostgreSql.MasterData;
using OpenTms.Platform;
using OpenTms.Platform.PredefinedRoles;
using Shouldly;
using Volo.Abp.Data;
using Volo.Abp.Identity;
using Volo.Abp.PermissionManagement;
using Xunit;

namespace OpenTms.PostgreSql.Platform;

/// <summary>
/// The predefined roles of design 71 against real PostgreSQL. The DbMigrator that created the test databases ran the
/// contributor for the host, tenant A (own database) and the shared tenant (host database). Risk class: Critical —
/// authorization in every tenant. Covers: the roles and exactly their grants on each side, static and marked; "seed and
/// add new" (a second run changes nothing, a removed grant stays removed, a permission new to the definition is granted);
/// a tenant's own role of the same name is never widened; and tenant isolation (a seed touches only its own tenant).
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class PredefinedRoleSeedTests : MasterDataPostgreSqlTestBase
{
    private const string RoleProvider = "R";

    private PredefinedRoleDataSeedContributor Contributor => GetRequiredService<PredefinedRoleDataSeedContributor>();
    private IIdentityRoleRepository Roles => GetRequiredService<IIdentityRoleRepository>();
    private IdentityRoleManager RoleManager => GetRequiredService<IdentityRoleManager>();
    private IPermissionGrantRepository Grants => GetRequiredService<IPermissionGrantRepository>();
    private PredefinedRoleOptions Options => GetRequiredService<IOptions<PredefinedRoleOptions>>().Value;

    private static readonly string[] TenantRoles =
    [
        PlatformRoleNames.OrderEntry, PlatformRoleNames.Planner, PlatformRoleNames.Finance,
        PlatformRoleNames.MasterDataManager, PlatformRoleNames.ReadOnly,
    ];

    private static readonly string[] HostRoles = [PlatformRoleNames.PlatformOperator, PlatformRoleNames.TenantManager];

    private PredefinedRoleDefinition Definition(string role) => Options.Roles.Single(r => r.Name == role);

    private async Task<List<string>> GrantedAsync(string role)
        => (await Grants.GetListAsync(RoleProvider, role)).Select(g => g.Name).Order(StringComparer.Ordinal).ToList();

    private async Task<List<string>> PredefinedRoleNamesAsync()
        => (await Roles.GetListAsync())
            .Where(r => TenantRoles.Contains(r.Name) || HostRoles.Contains(r.Name))
            .Select(r => r.Name).Order(StringComparer.Ordinal).ToList();

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Every_tenant_has_the_five_tenant_roles_static_with_exactly_their_grants(bool ownDatabase)
    {
        var tenantId = ownDatabase ? TenantA : SharedTenant;
        await InRolledBackTransactionAsync(tenantId, async () =>
        {
            (await PredefinedRoleNamesAsync()).ShouldBe(TenantRoles.Order(StringComparer.Ordinal).ToList(), "no host role in a tenant");

            foreach (var name in TenantRoles)
            {
                var role = (await RoleManager.FindByNameAsync(name)).ShouldNotBeNull();
                (role.TenantId, role.IsStatic, role.IsDefault).ShouldBe((tenantId, true, false), name);
                role.GetProperty(PredefinedRoleDataSeedContributor.PredefinedProperty, false).ShouldBeTrue(name);
                (await GrantedAsync(name)).ShouldBe(Definition(name).Permissions.ToList(), name);
                (await Grants.GetListAsync(RoleProvider, name)).ShouldAllBe(g => g.TenantId == tenantId);
            }
        });
    }

    [Fact]
    public async Task The_host_has_the_two_host_roles_with_exactly_their_grants()
    {
        await InRolledBackTransactionAsync(null, async () =>
        {
            (await PredefinedRoleNamesAsync()).ShouldBe(HostRoles.Order(StringComparer.Ordinal).ToList(), "no tenant role in the host");

            foreach (var name in HostRoles)
            {
                var role = (await RoleManager.FindByNameAsync(name)).ShouldNotBeNull();
                (role.TenantId, role.IsStatic).ShouldBe((null, true), name);
                (await GrantedAsync(name)).ShouldBe(Definition(name).Permissions.ToList(), name);
            }
        });
    }

    [Theory]
    [InlineData(null)]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_second_run_changes_nothing(bool? ownDatabase)
    {
        Guid? tenantId = ownDatabase == null ? null : ownDatabase.Value ? TenantA : SharedTenant;
        await InRolledBackTransactionAsync(tenantId, async () =>
        {
            var roles = (await Roles.GetListAsync()).Select(r => (r.Id, r.Name, r.ConcurrencyStamp)).OrderBy(r => r.Id).ToList();
            var grants = (await Grants.GetListAsync()).Select(g => g.Id).Order().ToList();

            await Contributor.SeedAsync(new DataSeedContext(tenantId));
            await FlushAsync();

            (await Roles.GetListAsync()).Select(r => (r.Id, r.Name, r.ConcurrencyStamp)).OrderBy(r => r.Id).ToList().ShouldBe(roles);
            (await Grants.GetListAsync()).Select(g => g.Id).Order().ToList().ShouldBe(grants);
        });
    }

    [Fact]
    public async Task A_grant_the_tenant_removed_stays_removed()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var removed = Definition(PlatformRoleNames.Planner).Permissions.First();
            await Grants.DeleteAsync((await Grants.FindAsync(removed, RoleProvider, PlatformRoleNames.Planner))!, autoSave: true);

            await Contributor.SeedAsync(new DataSeedContext(TenantA));
            await FlushAsync();

            (await Grants.FindAsync(removed, RoleProvider, PlatformRoleNames.Planner)).ShouldBeNull();
        });
    }

    [Fact]
    public async Task A_permission_new_to_the_definition_is_granted_to_the_existing_role()
    {
        await InRolledBackTransactionAsync(SharedTenant, async () =>
        {
            /* As after a release that adds a permission: the role's seeded set does not list it and it has no grant. */
            var added = Definition(PlatformRoleNames.Finance).Permissions.Last();
            var role = (await RoleManager.FindByNameAsync(PlatformRoleNames.Finance))!;
            var seeded = role.GetProperty<string>(PredefinedRoleDataSeedContributor.SeededPermissionsProperty)!.Split(',');
            role.SetProperty(PredefinedRoleDataSeedContributor.SeededPermissionsProperty, string.Join(',', seeded.Where(p => p != added)));
            await Roles.UpdateAsync(role, autoSave: true);
            await Grants.DeleteAsync((await Grants.FindAsync(added, RoleProvider, PlatformRoleNames.Finance))!, autoSave: true);

            await Contributor.SeedAsync(new DataSeedContext(SharedTenant));
            await FlushAsync();

            (await Grants.FindAsync(added, RoleProvider, PlatformRoleNames.Finance)).ShouldNotBeNull().TenantId.ShouldBe(SharedTenant);
            (await RoleManager.FindByNameAsync(PlatformRoleNames.Finance))!
                .GetProperty<string>(PredefinedRoleDataSeedContributor.SeededPermissionsProperty)!.Split(',')
                .ShouldBe(Definition(PlatformRoleNames.Finance).Permissions.ToArray());
        });
    }

    [Fact]
    public async Task A_new_tenant_gets_the_roles_once_and_its_neighbours_are_untouched()
    {
        var newTenant = Guid.NewGuid();
        await InRolledBackTransactionAsync(newTenant, async () =>
        {
            int Count(IEnumerable<PermissionGrant> grants, Guid? tenant) => grants.Count(g => g.TenantId == tenant);

            List<PermissionGrant> sharedBefore, hostBefore;
            using (CurrentTenant.Change(SharedTenant)) sharedBefore = await Grants.GetListAsync();
            using (CurrentTenant.Change(null)) hostBefore = await Grants.GetListAsync();

            (await PredefinedRoleNamesAsync()).ShouldBeEmpty();
            await Contributor.SeedAsync(new DataSeedContext(newTenant));
            await FlushAsync();
            /* The DbMigrator's later run after the provisioning job's adds nothing. */
            await Contributor.SeedAsync(new DataSeedContext(newTenant));
            await FlushAsync();

            (await PredefinedRoleNamesAsync()).ShouldBe(TenantRoles.Order(StringComparer.Ordinal).ToList());
            (await Roles.GetListAsync()).ShouldAllBe(r => r.TenantId == newTenant);
            var expected = TenantRoles.Sum(r => Definition(r).Permissions.Count);
            Count(await Grants.GetListAsync(), newTenant).ShouldBe(expected);

            /* Negative tenant isolation: the shared tenant and the host, in the same database, are untouched. */
            using (CurrentTenant.Change(SharedTenant))
            {
                (await Grants.GetListAsync()).Count.ShouldBe(sharedBefore.Count);
                (await Roles.GetListAsync()).ShouldAllBe(r => r.TenantId == SharedTenant);
            }

            using (CurrentTenant.Change(null))
            {
                (await Grants.GetListAsync()).Count.ShouldBe(hostBefore.Count);
                (await PredefinedRoleNamesAsync()).ShouldBe(HostRoles.Order(StringComparer.Ordinal).ToList());
            }
        });
    }

    [Fact]
    public async Task A_tenant_role_of_the_same_name_that_the_seed_did_not_create_is_never_widened()
    {
        var newTenant = Guid.NewGuid();
        await InRolledBackTransactionAsync(newTenant, async () =>
        {
            var own = new IdentityRole(Guid.NewGuid(), PlatformRoleNames.Planner, newTenant);
            (await RoleManager.CreateAsync(own)).Succeeded.ShouldBeTrue();
            await FlushAsync();

            await Contributor.SeedAsync(new DataSeedContext(newTenant));
            await FlushAsync();

            var kept = (await RoleManager.FindByNameAsync(PlatformRoleNames.Planner))!;
            (kept.Id, kept.IsStatic).ShouldBe((own.Id, false));
            (await GrantedAsync(PlatformRoleNames.Planner)).ShouldBeEmpty();
            (await RoleManager.FindByNameAsync(PlatformRoleNames.Finance)).ShouldNotBeNull("the other roles are still seeded");
        });
    }
}
