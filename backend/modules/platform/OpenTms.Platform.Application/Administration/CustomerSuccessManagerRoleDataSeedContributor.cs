using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using OpenTms.Platform.Permissions;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Guids;
using Volo.Abp.Identity;
using Volo.Abp.PermissionManagement;
using Volo.Abp.Uow;

namespace OpenTms.Platform.Administration;

/// <summary>
/// Seeds the static host role "Customer Success Manager" with the Impersonation permission on every host seed run
/// (PO 2026-10-29, docs/designs/63-user-menu.md). Static: the Administration app cannot rename or delete it. Idempotent.
/// </summary>
public class CustomerSuccessManagerRoleDataSeedContributor : IDataSeedContributor, ITransientDependency
{
    // "R" is the ABP role permission value provider name (RolePermissionValueProvider.ProviderName).
    private const string RoleProviderName = "R";

    private readonly IdentityRoleManager _roleManager;
    private readonly IPermissionManager _permissionManager;
    private readonly IGuidGenerator _guidGenerator;
    private readonly IUnitOfWorkManager _unitOfWorkManager;

    public CustomerSuccessManagerRoleDataSeedContributor(
        IdentityRoleManager roleManager,
        IPermissionManager permissionManager,
        IGuidGenerator guidGenerator,
        IUnitOfWorkManager unitOfWorkManager)
    {
        _roleManager = roleManager;
        _permissionManager = permissionManager;
        _guidGenerator = guidGenerator;
        _unitOfWorkManager = unitOfWorkManager;
    }

    public async Task SeedAsync(DataSeedContext context)
    {
        /* A host role: a tenant has no tenants to see. */
        if (context.TenantId != null)
        {
            return;
        }

        var role = await _roleManager.FindByNameAsync(PlatformRoleNames.CustomerSuccessManager);
        if (role == null)
        {
            role = new IdentityRole(_guidGenerator.Create(), PlatformRoleNames.CustomerSuccessManager) { IsStatic = true };
            (await _roleManager.CreateAsync(role)).CheckErrors();
        }
        else if (!role.IsStatic)
        {
            role.IsStatic = true;
            (await _roleManager.UpdateAsync(role)).CheckErrors();
        }

        /* As the admin seed: an unsaved grant of another contributor in this unit of work would be added twice. */
        if (_unitOfWorkManager.Current != null)
        {
            await _unitOfWorkManager.Current.SaveChangesAsync();
        }

        await _permissionManager.SetAsync(PlatformPermissions.Tenants.Impersonation, RoleProviderName, PlatformRoleNames.CustomerSuccessManager, true);
    }
}
