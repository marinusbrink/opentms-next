using System.Threading.Tasks;
using OpenTms.Platform.Permissions;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.PermissionManagement;
using Volo.Abp.Uow;

namespace OpenTms.Platform.Administration;

// Grants Administration permissions to the built-in "admin" role on every seed run.
// Design §Migration strategy assumption 10.
public class AdministrationPermissionDataSeedContributor : IDataSeedContributor, ITransientDependency
{
    // "R" is the ABP role permission value provider name (RolePermissionValueProvider.ProviderName).
    private const string RoleProviderName = "R";
    private const string AdminRoleName = "admin";

    private readonly IPermissionManager _permissionManager;
    private readonly IUnitOfWorkManager _unitOfWorkManager;

    public AdministrationPermissionDataSeedContributor(IPermissionManager permissionManager, IUnitOfWorkManager unitOfWorkManager)
    {
        _permissionManager = permissionManager;
        _unitOfWorkManager = unitOfWorkManager;
    }

    public async Task SeedAsync(DataSeedContext context)
    {
        /* ABP's own PermissionDataSeedContributor may have run first in this unit of work and added the same
         * admin grants without saving them. SetAsync looks for an existing grant in the database, so flush first:
         * otherwise a fresh tenant database gets every grant twice and the seed fails on the unique index.
         * Contributor order follows module registration, which differs between the DbMigrator and the host's
         * tenant-provisioning job (docs/designs/tenants-and-editions.md). */
        if (_unitOfWorkManager.Current != null)
        {
            await _unitOfWorkManager.Current.SaveChangesAsync();
        }

        string[] permissions =
        [
            PlatformPermissions.Administration.Users.Default,
            PlatformPermissions.Administration.Users.Create,
            PlatformPermissions.Administration.Users.Update,
            PlatformPermissions.Administration.Users.Delete,
            PlatformPermissions.Administration.Users.BulkDelete,
            PlatformPermissions.Administration.Users.ResetPassword,
            PlatformPermissions.Administration.Users.Unlock,
            PlatformPermissions.Administration.Roles.Default,
            PlatformPermissions.Administration.Roles.Create,
            PlatformPermissions.Administration.Roles.Update,
            PlatformPermissions.Administration.Roles.Delete,
            PlatformPermissions.Administration.Roles.BulkDelete,
            PlatformPermissions.Administration.OrganizationUnits.Default,
            PlatformPermissions.Administration.OrganizationUnits.Create,
            PlatformPermissions.Administration.OrganizationUnits.Update,
            PlatformPermissions.Administration.OrganizationUnits.Delete,
            PlatformPermissions.Administration.OrganizationUnits.ManageMembers,
            PlatformPermissions.Administration.Logbook.Default,
            PlatformPermissions.Administration.Logbook.Functional,
            PlatformPermissions.Administration.Logbook.Technical,
            PlatformPermissions.Administration.Maintenance.Default,
            PlatformPermissions.Administration.Maintenance.Outbox,
            PlatformPermissions.Administration.Maintenance.OutboxDispatch,
            /* The Platform master-data lists (design 49). */
            PlatformPermissions.MasterData.EmailTemplates.Default,
            PlatformPermissions.MasterData.EmailTemplates.Create,
            PlatformPermissions.MasterData.EmailTemplates.Update,
            PlatformPermissions.MasterData.EmailTemplates.Delete,
            PlatformPermissions.MasterData.ReportDefinitions.Default,
            PlatformPermissions.MasterData.ReportDefinitions.Create,
            PlatformPermissions.MasterData.ReportDefinitions.Update,
            PlatformPermissions.MasterData.ReportDefinitions.Delete,
            PlatformPermissions.MasterData.ReportDefinitions.EditTemplate,
            PlatformPermissions.MasterData.DocumentTypes.Default,
            PlatformPermissions.MasterData.DocumentTypes.Create,
            PlatformPermissions.MasterData.DocumentTypes.Update,
            PlatformPermissions.MasterData.DocumentTypes.Delete,
            PlatformPermissions.MasterData.NumberSequences.Default,
            PlatformPermissions.MasterData.NumberSequences.Create,
            PlatformPermissions.MasterData.NumberSequences.Update,
            PlatformPermissions.MasterData.NumberSequences.Delete,
        ];

        /* Host-only permissions cannot be granted inside a tenant (ABP rejects the side mismatch),
         * so they are seeded for the host admin only. */
        string[] hostOnlyPermissions =
        [
            PlatformPermissions.Administration.Maintenance.Caches,
            PlatformPermissions.Administration.Maintenance.CachesClear,
            PlatformPermissions.Administration.Maintenance.Databases,
            PlatformPermissions.Administration.Maintenance.Health,
            PlatformPermissions.Administration.Maintenance.BackgroundJobs,
            PlatformPermissions.Administration.Maintenance.BackgroundJobsManage,
            /* The Tenants and Editions apps (docs/designs/tenants-and-editions.md). */
            PlatformPermissions.Tenants.Default,
            PlatformPermissions.Tenants.Create,
            PlatformPermissions.Tenants.Update,
            PlatformPermissions.Tenants.Delete,
            PlatformPermissions.Tenants.Impersonation,
            PlatformPermissions.Editions.Default,
            PlatformPermissions.Editions.Create,
            PlatformPermissions.Editions.Update,
            PlatformPermissions.Editions.Delete,
            PlatformPermissions.Editions.ManageFeatures,
        ];

        foreach (var permission in permissions)
        {
            await _permissionManager.SetAsync(permission, RoleProviderName, AdminRoleName, true);
        }

        if (context.TenantId == null)
        {
            foreach (var permission in hostOnlyPermissions)
            {
                await _permissionManager.SetAsync(permission, RoleProviderName, AdminRoleName, true);
            }
        }
    }
}
