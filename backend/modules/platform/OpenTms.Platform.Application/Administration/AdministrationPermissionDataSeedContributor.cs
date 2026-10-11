using System.Threading.Tasks;
using OpenTms.Platform.Permissions;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.PermissionManagement;

namespace OpenTms.Platform.Administration;

// Grants Administration permissions to the built-in "admin" role on every seed run.
// Design §Migration strategy assumption 10.
public class AdministrationPermissionDataSeedContributor : IDataSeedContributor, ITransientDependency
{
    // "R" is the ABP role permission value provider name (RolePermissionValueProvider.ProviderName).
    private const string RoleProviderName = "R";
    private const string AdminRoleName = "admin";

    private readonly IPermissionManager _permissionManager;

    public AdministrationPermissionDataSeedContributor(IPermissionManager permissionManager)
    {
        _permissionManager = permissionManager;
    }

    public async Task SeedAsync(DataSeedContext context)
    {
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
