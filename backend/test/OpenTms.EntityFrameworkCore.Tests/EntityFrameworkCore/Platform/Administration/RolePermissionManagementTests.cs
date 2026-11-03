using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenTms.Orders.Permissions;
using OpenTms.Platform;
using OpenTms.Platform.Administration;
using OpenTms.Platform.Administration.Permissions;
using OpenTms.Platform.Permissions;
using OpenTms.Platform.PredefinedRoles;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Data;
using Volo.Abp.Identity;
using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.PermissionManagement;
using Volo.Abp.Security.Claims;
using Volo.Abp.TenantManagement;
using Xunit;

namespace OpenTms.EntityFrameworkCore.Platform.Administration;

/// <summary>The EF Core test application with ABP's real authorization: the test base allows everything.</summary>
[DependsOn(typeof(OpenTmsEntityFrameworkCoreTestModule))]
public class RealAuthorizationPermissionManagementTestModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.Replace(ServiceDescriptor.Transient<IAuthorizationService, AbpAuthorizationService>());
        context.Services.Replace(ServiceDescriptor.Transient<IAbpAuthorizationService, AbpAuthorizationService>());
        context.Services.Replace(ServiceDescriptor.Transient<IMethodInvocationAuthorizationService, MethodInvocationAuthorizationService>());
        context.Services.Replace(ServiceDescriptor.Transient<IPermissionChecker, PermissionChecker>());
    }
}

/// <summary>
/// Changing a role's grants and a user's direct grants through ABP's permission-management service
/// (docs/designs/73-role-permissions.md), with ABP's real authorization: the two new permissions guard providers "R" and
/// "U", the admin role is read-only, the side is enforced, and a tenant's change never reaches another tenant or the host.
/// Risk class: Critical (Platform, authorization in every tenant).
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class RolePermissionManagementTests : OpenTmsTestBase<RealAuthorizationPermissionManagementTestModule>
{
    private const string R = RolePermissionValueProvider.ProviderName;
    private const string U = UserPermissionValueProvider.ProviderName;
    private const string Manager = "permission-manager";
    private const string Planner = PlatformRoleNames.Planner;

    private static readonly string TenantPermission = OrdersPermissions.TransportOrders.Default;
    private static readonly string TenantChildPermission = OrdersPermissions.TransportOrders.Create;
    private static readonly string HostOnlyPermission = PlatformPermissions.Tenants.Default;

    /// <summary>What the caller role holds: both ManagePermissions with their parents, and the permissions the tests grant.</summary>
    private static readonly string[] ManagerGrants =
    [
        PlatformPermissions.Administration.Roles.Default,
        PlatformPermissions.Administration.Roles.ManagePermissions,
        PlatformPermissions.Administration.Users.Default,
        PlatformPermissions.Administration.Users.ManagePermissions,
        TenantPermission,
        TenantChildPermission,
    ];

    private readonly IPermissionAppService _permissions;
    private readonly ICurrentTenant _currentTenant;
    private readonly ICurrentPrincipalAccessor _principal;

    public RolePermissionManagementTests()
    {
        _permissions = GetRequiredService<IPermissionAppService>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
        _principal = GetRequiredService<ICurrentPrincipalAccessor>();
    }

    private Task<Guid> CreateTenantAsync(string name) =>
        WithUnitOfWorkAsync(async () =>
        {
            var tenant = await GetRequiredService<ITenantManager>().CreateAsync(name);
            await GetRequiredService<ITenantRepository>().InsertAsync(tenant, autoSave: true);
            return tenant.Id;
        });

    private Task CreateRoleAsync(Guid? tenantId, string name, params string[] permissions) =>
        WithUnitOfWorkAsync(async () =>
        {
            using (_currentTenant.Change(tenantId))
            {
                (await GetRequiredService<IdentityRoleManager>().CreateAsync(new IdentityRole(Guid.NewGuid(), name, tenantId))).Succeeded.ShouldBeTrue();
                foreach (var permission in permissions)
                {
                    await GetRequiredService<IPermissionManager>().SetAsync(permission, R, name, true);
                }
            }
        });

    private Task<Guid> CreateUserAsync(Guid? tenantId, params string[] roles) =>
        WithUnitOfWorkAsync(async () =>
        {
            using (_currentTenant.Change(tenantId))
            {
                var userName = "u-" + Guid.NewGuid().ToString("N")[..10];
                var user = new IdentityUser(Guid.NewGuid(), userName, userName + "@example.test", tenantId);
                var users = GetRequiredService<IdentityUserManager>();
                (await users.CreateAsync(user, "1q2w3E*")).Succeeded.ShouldBeTrue();
                if (roles.Length > 0)
                {
                    (await users.SetRolesAsync(user, roles)).Succeeded.ShouldBeTrue();
                }

                return user.Id;
            }
        });

    /// <summary>A side (host or tenant) with the caller role and a Planner role holding <see cref="TenantPermission"/> (tenants only).</summary>
    private async Task PrepareSideAsync(Guid? tenantId)
    {
        await CreateRoleAsync(tenantId, Manager, tenantId == null ? ManagerGrants.Except([TenantPermission, TenantChildPermission]).ToArray() : ManagerGrants);
        if (tenantId != null)
        {
            await CreateRoleAsync(tenantId, Planner, TenantPermission);
        }
    }

    private IDisposable As(Guid? tenantId, params string[] roles)
    {
        var claims = new List<Claim>
        {
            new(AbpClaimTypes.UserId, Guid.NewGuid().ToString()),
            new(AbpClaimTypes.UserName, "caller"),
        };
        if (tenantId != null)
        {
            claims.Add(new Claim(AbpClaimTypes.TenantId, tenantId.Value.ToString()));
        }

        claims.AddRange(roles.Select(role => new Claim(AbpClaimTypes.Role, role)));
        var principal = _principal.Change(new ClaimsPrincipal(new ClaimsIdentity(claims, "test")));
        var tenant = _currentTenant.Change(tenantId);
        return new CompositeDisposable(tenant, principal);
    }

    private Task<bool> IsGrantedInDbAsync(Guid? tenantId, string permission, string providerName, string providerKey) =>
        WithUnitOfWorkAsync(async () =>
        {
            using (_currentTenant.Change(tenantId))
            {
                return await GetRequiredService<IPermissionGrantRepository>().FindAsync(permission, providerName, providerKey) != null;
            }
        });

    private static UpdatePermissionsDto Change(string permission, bool granted) =>
        new() { Permissions = [new UpdatePermissionDto { Name = permission, IsGranted = granted }] };

    private static IEnumerable<PermissionGrantInfoDto> All(GetPermissionListResultDto result) =>
        result.Groups.SelectMany(g => g.Permissions);

    // ── Definitions and seed ──────────────────────────────────────────────────

    [Fact]
    public async Task Both_ManagePermissions_exist_on_both_sides_under_their_parents()
    {
        var manager = GetRequiredService<IPermissionDefinitionManager>();

        var roles = await manager.GetAsync(PlatformPermissions.Administration.Roles.ManagePermissions);
        roles.Name.ShouldBe("Platform.Administration.Roles.ManagePermissions");
        roles.Parent!.Name.ShouldBe(PlatformPermissions.Administration.Roles.Default);
        roles.MultiTenancySide.ShouldBe(MultiTenancySides.Both);

        var users = await manager.GetAsync(PlatformPermissions.Administration.Users.ManagePermissions);
        users.Name.ShouldBe("Platform.Administration.Users.ManagePermissions");
        users.Parent!.Name.ShouldBe(PlatformPermissions.Administration.Users.Default);
        users.MultiTenancySide.ShouldBe(MultiTenancySides.Both);
    }

    [Fact]
    public async Task The_seed_grants_both_to_admin_on_the_host_and_in_a_tenant()
    {
        var tenantId = await CreateTenantAsync("perm-seed");
        var contributor = GetRequiredService<AdministrationPermissionDataSeedContributor>();
        await WithUnitOfWorkAsync(() => contributor.SeedAsync(new DataSeedContext(null)));
        await WithUnitOfWorkAsync(async () =>
        {
            using (_currentTenant.Change(tenantId))
            {
                await contributor.SeedAsync(new DataSeedContext(tenantId));
            }
        });

        foreach (var side in new Guid?[] { null, tenantId })
        {
            (await IsGrantedInDbAsync(side, PlatformPermissions.Administration.Roles.ManagePermissions, R, OpenTmsPermissionAppService.AdminRoleName)).ShouldBeTrue();
            (await IsGrantedInDbAsync(side, PlatformPermissions.Administration.Users.ManagePermissions, R, OpenTmsPermissionAppService.AdminRoleName)).ShouldBeTrue();
        }
    }

    [Fact]
    public void No_predefined_role_holds_either_ManagePermissions()
    {
        var options = GetRequiredService<Microsoft.Extensions.Options.IOptions<PredefinedRoleOptions>>().Value;
        foreach (var role in options.Roles)
        {
            role.Permissions.ShouldNotContain(PlatformPermissions.Administration.Roles.ManagePermissions, role.Name);
            role.Permissions.ShouldNotContain(PlatformPermissions.Administration.Users.ManagePermissions, role.Name);
        }
    }

    [Fact]
    public void The_provider_policies_point_at_the_new_permissions()
    {
        var options = GetRequiredService<Microsoft.Extensions.Options.IOptions<PermissionManagementOptions>>().Value;
        options.ProviderPolicies[R].ShouldBe(PlatformPermissions.Administration.Roles.ManagePermissions);
        options.ProviderPolicies[U].ShouldBe(PlatformPermissions.Administration.Users.ManagePermissions);
    }

    // ── The guard ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Without_Roles_ManagePermissions_a_roles_grants_are_neither_read_nor_changed_also_with_ABPs_own_permission()
    {
        var tenantId = await CreateTenantAsync("perm-guard-r");
        await PrepareSideAsync(tenantId);
        await CreateRoleAsync(tenantId, "abp-style", "AbpIdentity.Roles", "AbpIdentity.Roles.ManagePermissions", PlatformPermissions.Administration.Roles.Default);

        using (As(tenantId, "abp-style"))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => _permissions.GetAsync(R, Planner));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _permissions.UpdateAsync(R, Planner, Change(TenantPermission, false)));
        }

        (await IsGrantedInDbAsync(tenantId, TenantPermission, R, Planner)).ShouldBeTrue();
    }

    [Fact]
    public async Task Without_Users_ManagePermissions_a_users_direct_grants_are_neither_read_nor_changed()
    {
        var tenantId = await CreateTenantAsync("perm-guard-u");
        await PrepareSideAsync(tenantId);
        await CreateRoleAsync(tenantId, "roles-only", PlatformPermissions.Administration.Roles.Default, PlatformPermissions.Administration.Roles.ManagePermissions, TenantPermission);
        var userId = await CreateUserAsync(tenantId);

        using (As(tenantId, "roles-only"))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => _permissions.GetAsync(U, userId.ToString()));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _permissions.UpdateAsync(U, userId.ToString(), Change(TenantPermission, true)));
        }

        (await IsGrantedInDbAsync(tenantId, TenantPermission, U, userId.ToString())).ShouldBeFalse();
    }

    // ── Changing grants ───────────────────────────────────────────────────────

    [Fact]
    public async Task A_roles_grant_is_removed_and_added_back()
    {
        var tenantId = await CreateTenantAsync("perm-role");
        await PrepareSideAsync(tenantId);

        using (As(tenantId, Manager))
        {
            All(await _permissions.GetAsync(R, Planner)).Single(p => p.Name == TenantPermission).IsGranted.ShouldBeTrue();

            await _permissions.UpdateAsync(R, Planner, Change(TenantPermission, false));
        }

        (await IsGrantedInDbAsync(tenantId, TenantPermission, R, Planner)).ShouldBeFalse();

        using (As(tenantId, Manager))
        {
            await _permissions.UpdateAsync(R, Planner, Change(TenantChildPermission, true));
            All(await _permissions.GetAsync(R, Planner)).Single(p => p.Name == TenantChildPermission).IsGranted.ShouldBeTrue();
        }

        (await IsGrantedInDbAsync(tenantId, TenantChildPermission, R, Planner)).ShouldBeTrue();
    }

    [Fact]
    public async Task A_users_direct_grant_is_added_and_removed_and_a_grant_through_a_role_is_reported_with_the_role()
    {
        var tenantId = await CreateTenantAsync("perm-user");
        await PrepareSideAsync(tenantId);
        var userId = await CreateUserAsync(tenantId, Planner);

        using (As(tenantId, Manager))
        {
            var throughRole = All(await _permissions.GetAsync(U, userId.ToString())).Single(p => p.Name == TenantPermission);
            throughRole.GrantedProviders.ShouldContain(g => g.ProviderName == R && g.ProviderKey == Planner);
            throughRole.GrantedProviders.ShouldNotContain(g => g.ProviderName == U);

            await _permissions.UpdateAsync(U, userId.ToString(), Change(TenantChildPermission, true));
        }

        (await IsGrantedInDbAsync(tenantId, TenantChildPermission, U, userId.ToString())).ShouldBeTrue();

        using (As(tenantId, Manager))
        {
            await _permissions.UpdateAsync(U, userId.ToString(), Change(TenantChildPermission, false));
        }

        (await IsGrantedInDbAsync(tenantId, TenantChildPermission, U, userId.ToString())).ShouldBeFalse();
    }

    [Fact]
    public async Task The_admin_roles_grants_cannot_be_changed()
    {
        var tenantId = await CreateTenantAsync("perm-admin");
        await PrepareSideAsync(tenantId);
        await CreateRoleAsync(tenantId, OpenTmsPermissionAppService.AdminRoleName, TenantPermission);

        using (As(tenantId, Manager))
        {
            var error = await Should.ThrowAsync<BusinessException>(
                () => _permissions.UpdateAsync(R, OpenTmsPermissionAppService.AdminRoleName, Change(TenantPermission, false)));
            error.Code.ShouldBe(PlatformErrorCodes.AdminRolePermissionsReadOnly);
            await Should.ThrowAsync<BusinessException>(() => _permissions.UpdateAsync(R, "Admin", Change(TenantPermission, false)));
        }

        (await IsGrantedInDbAsync(tenantId, TenantPermission, R, OpenTmsPermissionAppService.AdminRoleName)).ShouldBeTrue();
    }

    [Fact]
    public async Task A_caller_outside_the_admin_role_cannot_grant_a_permission_they_do_not_hold()
    {
        var tenantId = await CreateTenantAsync("perm-escalate");
        await PrepareSideAsync(tenantId);
        const string notHeld = PlatformPermissions.Administration.OrganizationUnits.Default;

        using (As(tenantId, Manager))
        {
            All(await _permissions.GetAsync(R, Planner)).Single(p => p.Name == notHeld).IsEditable.ShouldBeFalse();
            try
            {
                await _permissions.UpdateAsync(R, Planner, Change(notHeld, true));
            }
            catch (Exception)
            {
                /* ABP refuses or skips it; either way nothing may be stored (checked below). */
            }
        }

        (await IsGrantedInDbAsync(tenantId, notHeld, R, Planner)).ShouldBeFalse();
    }

    // ── The side ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Inside_a_tenant_no_host_only_permission_is_listed_or_granted()
    {
        var tenantId = await CreateTenantAsync("perm-side");
        await PrepareSideAsync(tenantId);

        using (As(tenantId, Manager))
        {
            var names = All(await _permissions.GetAsync(R, Planner)).Select(p => p.Name).ToList();
            names.ShouldContain(TenantPermission);
            names.ShouldNotContain(HostOnlyPermission);
            names.ShouldNotContain(PlatformPermissions.Tenants.Impersonation);
        }

        /* An admin caller is not limited to what they hold, so this reaches ABP's side check, which refuses it. */
        using (As(tenantId, Manager, OpenTmsPermissionAppService.AdminRoleName))
        {
            await Should.ThrowAsync<Exception>(() => _permissions.UpdateAsync(R, Planner, Change(HostOnlyPermission, true)));
        }

        (await IsGrantedInDbAsync(tenantId, HostOnlyPermission, R, Planner)).ShouldBeFalse();
    }

    [Fact]
    public async Task On_the_host_the_host_only_permissions_are_listed()
    {
        await PrepareSideAsync(null);
        await CreateRoleAsync(null, "host-role");

        using (As(null, Manager))
        {
            All(await _permissions.GetAsync(R, "host-role")).Select(p => p.Name).ShouldContain(HostOnlyPermission);
        }
    }

    // ── Tenant isolation (negative) ───────────────────────────────────────────

    [Fact]
    public async Task Tenant_As_change_to_its_Planner_role_never_touches_tenant_Bs_Planner_or_the_host()
    {
        var tenantA = await CreateTenantAsync("perm-iso-a");
        var tenantB = await CreateTenantAsync("perm-iso-b");
        await PrepareSideAsync(tenantA);
        await PrepareSideAsync(tenantB);
        await CreateRoleAsync(null, Planner, PlatformPermissions.Administration.Roles.Default);

        using (As(tenantA, Manager))
        {
            await _permissions.UpdateAsync(R, Planner, Change(TenantPermission, false));
            await _permissions.UpdateAsync(R, Planner, Change(TenantChildPermission, false));
        }

        (await IsGrantedInDbAsync(tenantA, TenantPermission, R, Planner)).ShouldBeFalse();
        (await IsGrantedInDbAsync(tenantB, TenantPermission, R, Planner)).ShouldBeTrue("tenant B's Planner keeps its grant");
        (await IsGrantedInDbAsync(null, PlatformPermissions.Administration.Roles.Default, R, Planner)).ShouldBeTrue("the host's Planner keeps its grant");

        /* Tenant A asks for "Planner": it reads its own role, which no longer has the grant, never tenant B's. */
        using (As(tenantA, Manager))
        {
            All(await _permissions.GetAsync(R, Planner)).Single(p => p.Name == TenantPermission).IsGranted.ShouldBeFalse();
        }

        /* And the other way around: tenant B's grant added by tenant A's caller would have to go through tenant A. */
        using (As(tenantA, Manager))
        {
            await _permissions.UpdateAsync(R, Planner, Change(TenantChildPermission, true));
        }

        (await IsGrantedInDbAsync(tenantB, TenantChildPermission, R, Planner)).ShouldBeFalse("tenant A's grant lands in tenant A only");
        (await IsGrantedInDbAsync(tenantA, TenantChildPermission, R, Planner)).ShouldBeTrue();
    }

    private sealed class CompositeDisposable(params IDisposable[] items) : IDisposable
    {
        public void Dispose()
        {
            foreach (var item in items.Reverse())
            {
                item.Dispose();
            }
        }
    }
}
