using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Guids;
using Volo.Abp.Identity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.PermissionManagement;
using Volo.Abp.Uow;

namespace OpenTms.Platform.PredefinedRoles;

/// <summary>
/// Seeds the predefined roles of design 71 (PO 2026-11-02): tenant roles in every tenant, host roles in the host. Runs in
/// the DbMigrator for every database and in the tenant-provisioning job for a new tenant.
/// <para>
/// "Seed and add new": a role is created static (it cannot be renamed or deleted) with every permission of its definition.
/// The role remembers which permissions the seed granted (<see cref="SeededPermissionsProperty"/>); a later run grants
/// only permissions that are new to the definition, so a grant a tenant administrator removed stays removed.
/// A role of the same name that the seed did not create is left alone, so the seed never widens a tenant's own role.
/// </para>
/// </summary>
public class PredefinedRoleDataSeedContributor : IDataSeedContributor, ITransientDependency
{
    /// <summary>Marks a role the seed created (extra property on <see cref="IdentityRole"/>).</summary>
    public const string PredefinedProperty = "OpenTms.PredefinedRole";

    /// <summary>The permissions the seed granted to the role so far, comma-separated (extra property on <see cref="IdentityRole"/>).</summary>
    public const string SeededPermissionsProperty = "OpenTms.SeededPermissions";

    // "R" is the ABP role permission value provider name (RolePermissionValueProvider.ProviderName).
    private const string RoleProviderName = "R";

    private readonly IdentityRoleManager _roleManager;
    private readonly IPermissionManager _permissionManager;
    private readonly IGuidGenerator _guidGenerator;
    private readonly ICurrentTenant _currentTenant;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly PredefinedRoleOptions _options;
    private readonly ILogger<PredefinedRoleDataSeedContributor> _logger;

    public PredefinedRoleDataSeedContributor(
        IdentityRoleManager roleManager,
        IPermissionManager permissionManager,
        IGuidGenerator guidGenerator,
        ICurrentTenant currentTenant,
        IUnitOfWorkManager unitOfWorkManager,
        IOptions<PredefinedRoleOptions> options,
        ILogger<PredefinedRoleDataSeedContributor> logger)
    {
        _roleManager = roleManager;
        _permissionManager = permissionManager;
        _guidGenerator = guidGenerator;
        _currentTenant = currentTenant;
        _unitOfWorkManager = unitOfWorkManager;
        _options = options.Value;
        _logger = logger;
    }

    [UnitOfWork]
    public virtual async Task SeedAsync(DataSeedContext context)
    {
        var isHost = context.TenantId == null;
        using (_currentTenant.Change(context.TenantId))
        {
            foreach (var definition in _options.Roles.Where(r => r.IsHostRole == isHost && r.Permissions.Count > 0))
            {
                await SeedRoleAsync(definition, context.TenantId);
            }
        }
    }

    private async Task SeedRoleAsync(PredefinedRoleDefinition definition, Guid? tenantId)
    {
        var role = await _roleManager.FindByNameAsync(definition.Name);
        if (role == null)
        {
            role = new IdentityRole(_guidGenerator.Create(), definition.Name, tenantId) { IsStatic = true };
            role.SetProperty(PredefinedProperty, true);
            (await _roleManager.CreateAsync(role)).CheckErrors();
        }
        else if (!role.GetProperty(PredefinedProperty, false))
        {
            _logger.LogWarning("Predefined role {RoleName} skipped for tenant {TenantId}: a role of that name already exists.", definition.Name, tenantId);
            return;
        }

        var seeded = (role.GetProperty<string?>(SeededPermissionsProperty) ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.Ordinal);
        var toGrant = definition.Permissions.Where(p => !seeded.Contains(p)).ToList();
        if (toGrant.Count == 0)
        {
            return;
        }

        /* As the admin seed: an unsaved grant of another contributor in this unit of work would be added twice. */
        if (_unitOfWorkManager.Current != null)
        {
            await _unitOfWorkManager.Current.SaveChangesAsync();
        }

        foreach (var permission in toGrant)
        {
            await _permissionManager.SetAsync(permission, RoleProviderName, definition.Name, true);
        }

        role.SetProperty(SeededPermissionsProperty, string.Join(',', seeded.Concat(toGrant).Order(StringComparer.Ordinal)));
        (await _roleManager.UpdateAsync(role)).CheckErrors();
        _logger.LogInformation("Predefined role {RoleName} for tenant {TenantId}: {Count} permissions granted.", definition.Name, tenantId, toGrant.Count);
    }
}
