using System;
using System.Collections.Generic;
using OpenTms.Integrations.Permissions;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using OpenTms.Financial.Permissions;
using OpenTms.MasterData.Permissions;
using OpenTms.Orders.Permissions;
using OpenTms.PlanningExecution.Permissions;
using OpenTms.Platform;
using OpenTms.Platform.Permissions;
using OpenTms.Platform.PredefinedRoles;
using Shouldly;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace OpenTms.EntityFrameworkCore.Platform.PredefinedRoles;

/// <summary>
/// The definitions of the predefined roles (docs/designs/71-predefined-roles.md) through the real permission definition
/// manager, as every module configured them. Risk class: Critical (authorization): every grant exists and fits the role's
/// side, Read-only holds views only, and no predefined role reaches what stays with the administrator.
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class PredefinedRoleDefinitionTests : OpenTmsEntityFrameworkCoreTestBase
{
    private PredefinedRoleOptions Options => GetRequiredService<IOptions<PredefinedRoleOptions>>().Value;
    private IPermissionDefinitionManager Definitions => GetRequiredService<IPermissionDefinitionManager>();

    [Fact]
    public void Five_tenant_roles_and_two_host_roles_each_with_permissions()
    {
        Options.Roles.Where(r => !r.IsHostRole).Select(r => r.Name).ShouldBe(
            [PlatformRoleNames.OrderEntry, PlatformRoleNames.Planner, PlatformRoleNames.Finance, PlatformRoleNames.MasterDataManager, PlatformRoleNames.ReadOnly],
            ignoreOrder: true);
        Options.Roles.Where(r => r.IsHostRole).Select(r => r.Name).ShouldBe(
            [PlatformRoleNames.PlatformOperator, PlatformRoleNames.TenantManager], ignoreOrder: true);
        Options.Roles.ShouldAllBe(r => r.Permissions.Count > 0);
    }

    [Fact]
    public async Task Every_granted_permission_exists_and_belongs_to_the_side_of_its_role()
    {
        foreach (var role in Options.Roles)
        {
            var side = role.IsHostRole ? MultiTenancySides.Host : MultiTenancySides.Tenant;
            foreach (var name in role.Permissions)
            {
                var permission = (await Definitions.GetOrNullAsync(name)).ShouldNotBeNull($"{role.Name}: {name}");
                permission.MultiTenancySide.HasFlag(side).ShouldBeTrue($"{role.Name}: {name} is {permission.MultiTenancySide}");
            }
        }
    }

    [Fact]
    public async Task A_child_permission_is_granted_only_with_its_parent()
    {
        foreach (var role in Options.Roles)
        {
            foreach (var name in role.Permissions)
            {
                var parent = (await Definitions.GetAsync(name)).Parent;
                if (parent != null)
                {
                    role.Permissions.ShouldContain(parent.Name, $"{role.Name}: {name} without {parent.Name}");
                }
            }
        }
    }

    [Fact]
    public async Task Read_only_holds_view_permissions_only()
    {
        foreach (var name in Options.Roles.Single(r => r.Name == PlatformRoleNames.ReadOnly).Permissions)
        {
            (await Definitions.GetAsync(name)).Parent.ShouldBeNull(name);
        }
    }

    [Fact]
    public void Approving_orders_belongs_to_order_entry_and_finance_only()
    {
        Options.Roles.Where(r => r.Permissions.Contains(OrdersPermissions.TransportOrders.Approve)).Select(r => r.Name)
            .ShouldBe([PlatformRoleNames.OrderEntry, PlatformRoleNames.Finance], ignoreOrder: true);
    }

    [Fact]
    public void No_predefined_role_reaches_what_stays_with_the_administrator()
    {
        string[] adminOnly =
        [
            PlatformPermissions.Administration.Users.Default, PlatformPermissions.Administration.Users.Create,
            PlatformPermissions.Administration.Users.Update, PlatformPermissions.Administration.Users.Delete,
            PlatformPermissions.Administration.Users.BulkDelete, PlatformPermissions.Administration.Users.ResetPassword,
            PlatformPermissions.Administration.Users.Unlock,
            PlatformPermissions.Administration.Roles.Default, PlatformPermissions.Administration.Roles.Create,
            PlatformPermissions.Administration.Roles.Update, PlatformPermissions.Administration.Roles.Delete,
            PlatformPermissions.Administration.Roles.BulkDelete,
            PlatformPermissions.Administration.OrganizationUnits.Default, PlatformPermissions.Administration.OrganizationUnits.Create,
            PlatformPermissions.Administration.OrganizationUnits.Update, PlatformPermissions.Administration.OrganizationUnits.Delete,
            PlatformPermissions.Administration.OrganizationUnits.ManageMembers,
            PlatformPermissions.Tenants.Delete, PlatformPermissions.Tenants.Impersonation,
            PlatformPermissions.Editions.Delete,
            MasterDataPermissions.Drivers.Anonymise,
        ];

        foreach (var role in Options.Roles)
        {
            role.Permissions.Intersect(adminOnly).ShouldBeEmpty(role.Name);
        }
    }

    [Fact]
    public void Each_role_holds_its_core_permissions()
    {
        Permissions(PlatformRoleNames.OrderEntry).ShouldContain(OrdersPermissions.TransportOrders.Create);
        Permissions(PlatformRoleNames.OrderEntry).ShouldNotContain(PlanningExecutionPermissions.TransportTrips.Create);
        Permissions(PlatformRoleNames.Planner).ShouldContain(PlanningExecutionPermissions.TransportTrips.AssignLegs);
        Permissions(PlatformRoleNames.Planner).ShouldNotContain(OrdersPermissions.TransportOrders.Create);
        Permissions(PlatformRoleNames.Finance).ShouldContain(FinancialPermissions.PriceLists.Adjust);
        Permissions(PlatformRoleNames.MasterDataManager).ShouldContain(MasterDataPermissions.Relations.Delete);
        Permissions(PlatformRoleNames.MasterDataManager).ShouldNotContain(OrdersPermissions.TransportOrders.Default);
        Permissions(PlatformRoleNames.PlatformOperator).ShouldContain(PlatformPermissions.Administration.Maintenance.Health);
        Permissions(PlatformRoleNames.TenantManager).ShouldContain(PlatformPermissions.Tenants.Create);
    }

    [Theory]
    [InlineData(PlatformRoleNames.OrderEntry, 55)]
    [InlineData(PlatformRoleNames.Planner, 52)]
    [InlineData(PlatformRoleNames.Finance, 63)]
    [InlineData(PlatformRoleNames.MasterDataManager, 142)]
    [InlineData(PlatformRoleNames.ReadOnly, 45)]
    [InlineData(PlatformRoleNames.PlatformOperator, 21)]
    [InlineData(PlatformRoleNames.TenantManager, 9)]
    public void Each_role_has_as_many_permissions_as_design_71_lists(string role, int count)
    {
        /* Design 71 lists every permission per role; a change here changes that list in the same pull request. Design 72
         * "Predefined roles" adds: Order entry +4, Planner +2, Master data manager +8, Read-only +5 (see the test below). */
        Permissions(role).Count.ShouldBe(count);
    }

    /// <summary>Design 72 "Predefined roles", exactly: the order-intake permissions per role; the intake API's never in a role.</summary>
    [Fact]
    public void The_order_intake_permissions_are_granted_per_design_72()
    {
        static string[] IntakeOf(IEnumerable<string> permissions) => permissions
            .Where(p => p.StartsWith("Orders.OrderIntakes", StringComparison.Ordinal)
                        || p.StartsWith("Integrations.Mailboxes", StringComparison.Ordinal)
                        || p.StartsWith("Integrations.ApiClients", StringComparison.Ordinal)
                        || p.StartsWith("Integrations.ImportProfiles", StringComparison.Ordinal)
                        || p.StartsWith("Integrations.InboundMessages", StringComparison.Ordinal)
                        || p.StartsWith("Integrations.IntakeApi", StringComparison.Ordinal))
            .ToArray();

        IntakeOf(Permissions(PlatformRoleNames.OrderEntry)).ShouldBe(
        [
            OrdersPermissions.OrderIntakes.Default, OrdersPermissions.OrderIntakes.Resolve, OrdersPermissions.OrderIntakes.Reject,
            IntegrationsPermissions.InboundMessages.Default
        ], ignoreOrder: true);
        IntakeOf(Permissions(PlatformRoleNames.Planner)).ShouldBe(
            [OrdersPermissions.OrderIntakes.Default, IntegrationsPermissions.InboundMessages.Default], ignoreOrder: true);
        IntakeOf(Permissions(PlatformRoleNames.MasterDataManager)).ShouldBe(
        [
            IntegrationsPermissions.Mailboxes.Default, IntegrationsPermissions.Mailboxes.Manage, IntegrationsPermissions.ApiClients.Default,
            IntegrationsPermissions.ApiClients.Manage, IntegrationsPermissions.ImportProfiles.Default, IntegrationsPermissions.ImportProfiles.Manage,
            IntegrationsPermissions.InboundMessages.Default, IntegrationsPermissions.InboundMessages.Reprocess
        ], ignoreOrder: true);
        IntakeOf(Permissions(PlatformRoleNames.ReadOnly)).ShouldBe(
        [
            OrdersPermissions.OrderIntakes.Default, IntegrationsPermissions.InboundMessages.Default, IntegrationsPermissions.Mailboxes.Default,
            IntegrationsPermissions.ApiClients.Default, IntegrationsPermissions.ImportProfiles.Default
        ], ignoreOrder: true);
        foreach (var role in new[] { PlatformRoleNames.Finance, PlatformRoleNames.PlatformOperator, PlatformRoleNames.TenantManager })
        {
            IntakeOf(Permissions(role)).ShouldBeEmpty(role);
        }

        foreach (var role in Options.Roles)
        {
            role.Permissions.ShouldNotContain(IntegrationsPermissions.IntakeApi.Submit, role.Name + ": granted only to API clients, never to a role");
        }
    }

    [Fact]
    public void A_grant_to_an_unknown_role_fails()
    {
        Should.Throw<ArgumentException>(() => new PredefinedRoleOptions().Grant("Dispatcher", OrdersPermissions.TransportOrders.Default));
    }

    private System.Collections.Generic.SortedSet<string> Permissions(string role) => Options.Roles.Single(r => r.Name == role).Permissions;
}
