using OpenTms.Platform.PredefinedRoles;
using P = OpenTms.Platform.Permissions.PlatformPermissions;

namespace OpenTms.Platform.Permissions;

/// <summary>
/// The Platform permissions of the predefined roles (docs/designs/71-predefined-roles.md). User administration stays with
/// the administrator; deleting tenants and editions too.
/// </summary>
public static class PlatformPredefinedRoles
{
    public static void Configure(PredefinedRoleOptions options)
    {
        options.Grant(PlatformRoleNames.Finance,
            P.DerivedValues.Recalculate);

        options.Grant(PlatformRoleNames.MasterDataManager,
            P.MasterData.EmailTemplates.Default, P.MasterData.EmailTemplates.Create, P.MasterData.EmailTemplates.Update, P.MasterData.EmailTemplates.Delete,
            P.MasterData.ReportDefinitions.Default, P.MasterData.ReportDefinitions.Create, P.MasterData.ReportDefinitions.Update, P.MasterData.ReportDefinitions.Delete,
            P.MasterData.ReportDefinitions.EditTemplate,
            P.MasterData.DocumentTypes.Default, P.MasterData.DocumentTypes.Create, P.MasterData.DocumentTypes.Update, P.MasterData.DocumentTypes.Delete);

        options.Grant(PlatformRoleNames.ReadOnly,
            P.MasterData.EmailTemplates.Default, P.MasterData.ReportDefinitions.Default, P.MasterData.DocumentTypes.Default);

        options.Grant(PlatformRoleNames.PlatformOperator,
            P.Administration.Maintenance.Default, P.Administration.Maintenance.Outbox, P.Administration.Maintenance.OutboxDispatch,
            P.Administration.Maintenance.Caches, P.Administration.Maintenance.CachesClear, P.Administration.Maintenance.Databases,
            P.Administration.Maintenance.Health, P.Administration.Maintenance.BackgroundJobs, P.Administration.Maintenance.BackgroundJobsManage,
            P.Administration.EmailSettings.Default, P.Administration.EmailSettings.Update, P.Administration.EmailSettings.SendTest,
            P.Administration.Logbook.Default, P.Administration.Logbook.Functional, P.Administration.Logbook.Technical,
            P.Tenants.Default, P.TenantProvisioning.Manage, P.DerivedValues.Recalculate);

        options.Grant(PlatformRoleNames.TenantManager,
            P.Tenants.Default, P.Tenants.Create, P.Tenants.Update, P.Tenants.ResetToDataset,
            P.Editions.Default, P.Editions.Create, P.Editions.Update, P.Editions.ManageFeatures,
            P.TenantProvisioning.Manage);
    }
}
