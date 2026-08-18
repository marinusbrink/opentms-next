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
            PlatformPermissions.Administration.Roles.Default,
            PlatformPermissions.Administration.Roles.Create,
            PlatformPermissions.Administration.Roles.Update,
            PlatformPermissions.Administration.Roles.Delete,
        ];

        foreach (var permission in permissions)
        {
            await _permissionManager.SetAsync(permission, RoleProviderName, AdminRoleName, true);
        }
    }
}
