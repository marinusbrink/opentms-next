using OpenTms.Platform.Localization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Localization;
using Volo.Abp.MultiTenancy;

namespace OpenTms.Platform.Permissions;

public class PlatformPermissionDefinitionProvider : PermissionDefinitionProvider
{
    public override void Define(IPermissionDefinitionContext context)
    {
        var group = context.AddGroup(PlatformPermissions.GroupName, L("Permission:Platform"));

        group.AddPermission(
            PlatformPermissions.TenantProvisioning.Manage,
            L("Permission:TenantProvisioning.Manage"),
            MultiTenancySides.Host);

        /* Tenants and editions are host data: a tenant can never be granted these
         * (docs/designs/tenants-and-editions.md). */
        var host = MultiTenancySides.Host;
        /* Design 49: tenant administrators re-run calculations of their own records; the host for shared-tier work. */
        group.AddPermission(PlatformPermissions.DerivedValues.Recalculate, L("Permission:DerivedValues.Recalculate"), MultiTenancySides.Both);

        var tenants = group.AddPermission(PlatformPermissions.Tenants.Default, L("Permission:Tenants"), host);
        tenants.AddChild(PlatformPermissions.Tenants.Create, L("Permission:Tenants.Create"), host);
        tenants.AddChild(PlatformPermissions.Tenants.Update, L("Permission:Tenants.Update"), host);
        tenants.AddChild(PlatformPermissions.Tenants.Delete, L("Permission:Tenants.Delete"), host);
        tenants.AddChild(PlatformPermissions.Tenants.Impersonation, L("Permission:Tenants.Impersonation"), host);

        var editions = group.AddPermission(PlatformPermissions.Editions.Default, L("Permission:Editions"), host);
        editions.AddChild(PlatformPermissions.Editions.Create, L("Permission:Editions.Create"), host);
        editions.AddChild(PlatformPermissions.Editions.Update, L("Permission:Editions.Update"), host);
        editions.AddChild(PlatformPermissions.Editions.Delete, L("Permission:Editions.Delete"), host);
        editions.AddChild(PlatformPermissions.Editions.ManageFeatures, L("Permission:Editions.ManageFeatures"), host);

        var bothSides = MultiTenancySides.Host | MultiTenancySides.Tenant;

        var users = group.AddPermission(
            PlatformPermissions.Administration.Users.Default,
            L("Permission:Administration.Users"),
            bothSides);
        users.AddChild(PlatformPermissions.Administration.Users.Create, L("Permission:Administration.Users.Create"), bothSides);
        users.AddChild(PlatformPermissions.Administration.Users.Update, L("Permission:Administration.Users.Update"), bothSides);
        users.AddChild(PlatformPermissions.Administration.Users.Delete, L("Permission:Administration.Users.Delete"), bothSides);
        users.AddChild(PlatformPermissions.Administration.Users.BulkDelete, L("Permission:Administration.Users.BulkDelete"), bothSides);
        users.AddChild(PlatformPermissions.Administration.Users.ResetPassword, L("Permission:Administration.Users.ResetPassword"), bothSides);
        users.AddChild(PlatformPermissions.Administration.Users.Unlock, L("Permission:Administration.Users.Unlock"), bothSides);

        var roles = group.AddPermission(
            PlatformPermissions.Administration.Roles.Default,
            L("Permission:Administration.Roles"),
            bothSides);
        roles.AddChild(PlatformPermissions.Administration.Roles.Create, L("Permission:Administration.Roles.Create"), bothSides);
        roles.AddChild(PlatformPermissions.Administration.Roles.Update, L("Permission:Administration.Roles.Update"), bothSides);
        roles.AddChild(PlatformPermissions.Administration.Roles.Delete, L("Permission:Administration.Roles.Delete"), bothSides);
        roles.AddChild(PlatformPermissions.Administration.Roles.BulkDelete, L("Permission:Administration.Roles.BulkDelete"), bothSides);

        var organizationUnits = group.AddPermission(
            PlatformPermissions.Administration.OrganizationUnits.Default,
            L("Permission:Administration.OrganizationUnits"),
            bothSides);
        organizationUnits.AddChild(PlatformPermissions.Administration.OrganizationUnits.Create, L("Permission:Administration.OrganizationUnits.Create"), bothSides);
        organizationUnits.AddChild(PlatformPermissions.Administration.OrganizationUnits.Update, L("Permission:Administration.OrganizationUnits.Update"), bothSides);
        organizationUnits.AddChild(PlatformPermissions.Administration.OrganizationUnits.Delete, L("Permission:Administration.OrganizationUnits.Delete"), bothSides);
        organizationUnits.AddChild(PlatformPermissions.Administration.OrganizationUnits.ManageMembers, L("Permission:Administration.OrganizationUnits.ManageMembers"), bothSides);

        var logbook = group.AddPermission(
            PlatformPermissions.Administration.Logbook.Default,
            L("Permission:Administration.Logbook"),
            bothSides);
        logbook.AddChild(PlatformPermissions.Administration.Logbook.Functional, L("Permission:Administration.Logbook.Functional"), bothSides);
        logbook.AddChild(PlatformPermissions.Administration.Logbook.Technical, L("Permission:Administration.Logbook.Technical"), bothSides);

        /* Maintenance: the view itself and the Outbox tab exist on both sides; the tabs that act on the
         * process or on host-owned stores are host-only (docs/designs/maintenance-view.md). */
        var maintenance = group.AddPermission(
            PlatformPermissions.Administration.Maintenance.Default,
            L("Permission:Administration.Maintenance"),
            bothSides);
        var caches = maintenance.AddChild(PlatformPermissions.Administration.Maintenance.Caches, L("Permission:Administration.Maintenance.Caches"), MultiTenancySides.Host);
        caches.AddChild(PlatformPermissions.Administration.Maintenance.CachesClear, L("Permission:Administration.Maintenance.Caches.Clear"), MultiTenancySides.Host);
        maintenance.AddChild(PlatformPermissions.Administration.Maintenance.Databases, L("Permission:Administration.Maintenance.Databases"), MultiTenancySides.Host);
        var outbox = maintenance.AddChild(PlatformPermissions.Administration.Maintenance.Outbox, L("Permission:Administration.Maintenance.Outbox"), bothSides);
        outbox.AddChild(PlatformPermissions.Administration.Maintenance.OutboxDispatch, L("Permission:Administration.Maintenance.Outbox.Dispatch"), bothSides);
        maintenance.AddChild(PlatformPermissions.Administration.Maintenance.Health, L("Permission:Administration.Maintenance.Health"), MultiTenancySides.Host);
        var backgroundJobs = maintenance.AddChild(PlatformPermissions.Administration.Maintenance.BackgroundJobs, L("Permission:Administration.Maintenance.BackgroundJobs"), MultiTenancySides.Host);
        backgroundJobs.AddChild(PlatformPermissions.Administration.Maintenance.BackgroundJobsManage, L("Permission:Administration.Maintenance.BackgroundJobs.Manage"), MultiTenancySides.Host);
    }

    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<PlatformResource>(name);
    }
}
