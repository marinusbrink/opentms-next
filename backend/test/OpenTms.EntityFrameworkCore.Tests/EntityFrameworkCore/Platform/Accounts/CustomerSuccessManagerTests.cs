using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenTms.Platform;
using OpenTms.Platform.Administration;
using OpenTms.Platform.Grid;
using OpenTms.Platform.Impersonation;
using OpenTms.Platform.Permissions;
using OpenTms.Platform.Tenants;
using Shouldly;
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

namespace OpenTms.EntityFrameworkCore.Platform.Accounts;

/// <summary>
/// The account test application with ABP's real authorization put back: the test base allows everything
/// (AddAlwaysAllowAuthorization), which would hide what a role is actually granted.
/// </summary>
[DependsOn(typeof(AccountsTestModule))]
public class RealAuthorizationAccountsTestModule : AbpModule
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
/// The tenant list of "Log in as" for the user menu (ImpersonationAppService.GetTenantsAsync) and the static host role
/// "Customer Success Manager" (docs/designs/63-user-menu.md; PO 2026-10-29), with ABP's real authorization: the role holds
/// only Platform.Tenants.Impersonation, not its parent Platform.Tenants, and that must be enough for the tenant list,
/// a tenant's users and the start of "Log in as", and nothing more. Also the seed: static, host only, idempotent.
/// Risk class: Critical — authorization and cross-tenant access (the role sees every tenant).
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class CustomerSuccessManagerTests : OpenTmsTestBase<RealAuthorizationAccountsTestModule>
{
    private readonly IImpersonationAppService _impersonation;
    private readonly ICurrentTenant _currentTenant;
    private readonly ICurrentPrincipalAccessor _principal;

    public CustomerSuccessManagerTests()
    {
        _impersonation = GetRequiredService<IImpersonationAppService>();
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

    private Task<Guid> CreateUserAsync(Guid? tenantId) =>
        WithUnitOfWorkAsync(async () =>
        {
            using (_currentTenant.Change(tenantId))
            {
                var userName = "u-" + Guid.NewGuid().ToString("N")[..10];
                var user = new IdentityUser(Guid.NewGuid(), userName, userName + "@example.test", tenantId);
                (await GetRequiredService<IdentityUserManager>().CreateAsync(user, "1q2w3E*")).Succeeded.ShouldBeTrue();
                return user.Id;
            }
        });

    private Task CreateHostRoleAsync(string name, params string[] permissions) =>
        WithUnitOfWorkAsync(async () =>
        {
            (await GetRequiredService<IdentityRoleManager>().CreateAsync(new IdentityRole(Guid.NewGuid(), name))).Succeeded.ShouldBeTrue();
            foreach (var permission in permissions)
            {
                await GetRequiredService<IPermissionManager>().SetAsync(permission, RolePermissionValueProvider.ProviderName, name, true);
            }
        });

    private IDisposable AsHostUser(params string[] roles) => _principal.Change(AccountPrincipals.For(Guid.NewGuid(), null, roles: roles));

    private static GridRequest Request(string? search = null, int start = 0, int end = 50, string? sort = null) => new()
    {
        StartRow = start,
        EndRow = end,
        WildcardSearch = search,
        SortModels = sort == null ? [] : [new SortModel("name", sort)]
    };

    // ── The role's grant decides (real authorization) ─────────────────────────

    [Fact]
    public async Task The_Customer_Success_Manager_role_alone_opens_the_tenant_list_a_tenants_users_and_log_in_as()
    {
        /* Design assumption: ABP checks the child permission on its own, without its parent Platform.Tenants. */
        var acme = await CreateTenantAsync("acme");
        var planner = await CreateUserAsync(acme);

        using (AsHostUser(PlatformRoleNames.CustomerSuccessManager))
        {
            (await GetRequiredService<IPermissionChecker>().IsGrantedAsync(PlatformPermissions.Tenants.Impersonation)).ShouldBeTrue();
            (await GetRequiredService<IPermissionChecker>().IsGrantedAsync(PlatformPermissions.Tenants.Default)).ShouldBeFalse();

            (await _impersonation.GetTenantsAsync(Request())).Rows.Select(r => r.Name).ShouldContain("acme");
            (await _impersonation.GetTenantUsersAsync(acme, Request())).Rows.Select(r => r.Id).ShouldContain(planner);
            (await _impersonation.StartAsync(acme, planner)).Url.ShouldContain("/Account/Impersonate?ticket=");
        }
    }

    [Fact]
    public async Task The_Customer_Success_Manager_role_does_not_open_the_Tenants_app()
    {
        await CreateTenantAsync("acme");

        using (AsHostUser(PlatformRoleNames.CustomerSuccessManager))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => GetRequiredService<ITenantAdministrationAppService>().GetListAsync(Request()));
        }
    }

    [Fact]
    public async Task Without_the_impersonation_permission_the_tenant_list_is_refused_also_with_the_Tenants_permission()
    {
        await CreateTenantAsync("acme");
        await CreateHostRoleAsync("tenant-viewer", PlatformPermissions.Tenants.Default);

        using (AsHostUser())
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => _impersonation.GetTenantsAsync(Request()));
        }

        using (AsHostUser("tenant-viewer"))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => _impersonation.GetTenantsAsync(Request()));
        }
    }

    [Fact]
    public async Task A_role_of_the_same_name_in_a_tenant_opens_nothing_there()
    {
        // The permission is host-side only: a tenant user whose role carries the name (and even a grant) sees no tenants.
        var acme = await CreateTenantAsync("acme");
        await WithUnitOfWorkAsync(async () =>
        {
            using (_currentTenant.Change(acme))
            {
                (await GetRequiredService<IdentityRoleManager>().CreateAsync(new IdentityRole(Guid.NewGuid(), PlatformRoleNames.CustomerSuccessManager, acme)))
                    .Succeeded.ShouldBeTrue();
            }
        });

        using (_principal.Change(AccountPrincipals.For(Guid.NewGuid(), acme, roles: PlatformRoleNames.CustomerSuccessManager)))
        using (_currentTenant.Change(acme))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => _impersonation.GetTenantsAsync(Request()));
        }
    }

    // ── The tenant list ───────────────────────────────────────────────────────

    [Fact]
    public async Task The_tenant_list_is_paged_searchable_by_name_and_sorted()
    {
        foreach (var name in new[] { "delta", "alpha-transport", "charlie", "bravo-transport" })
        {
            await CreateTenantAsync(name);
        }

        using (AsHostUser(PlatformRoleNames.CustomerSuccessManager))
        {
            var all = await _impersonation.GetTenantsAsync(Request());
            all.Rows.Select(r => r.Name).ShouldBe(["alpha-transport", "bravo-transport", "charlie", "delta"]);
            all.TotalCount.ShouldBe(4);
            all.FilteredCount.ShouldBe(4);

            var page = await _impersonation.GetTenantsAsync(Request(start: 1, end: 3, sort: "desc"));
            page.Rows.Select(r => r.Name).ShouldBe(["charlie", "bravo-transport"]);
            page.TotalCount.ShouldBe(4);

            var found = await _impersonation.GetTenantsAsync(Request(search: " transport "));
            found.Rows.Select(r => r.Name).ShouldBe(["alpha-transport", "bravo-transport"]);
            found.TotalCount.ShouldBe(4);
            found.FilteredCount.ShouldBe(2);
        }
    }

    [Fact]
    public void The_tenant_row_carries_the_id_and_name_only()
    {
        typeof(ImpersonationTenantRowDto).GetProperties().Select(p => p.Name).ShouldBe(["Id", "Name"], ignoreOrder: true);
    }

    [Fact]
    public void The_tenant_list_needs_the_impersonation_permission()
    {
        typeof(ImpersonationAppService).GetMethod(nameof(ImpersonationAppService.GetTenantsAsync))!
            .GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>()
            .ShouldHaveSingleItem().Policy.ShouldBe(PlatformPermissions.Tenants.Impersonation);
    }

    // ── The seed ──────────────────────────────────────────────────────────────

    private Task<IdentityRole?> FindRoleAsync(Guid? tenantId) =>
        WithUnitOfWorkAsync(async () =>
        {
            using (_currentTenant.Change(tenantId))
            {
                return await GetRequiredService<IIdentityRoleRepository>().FindByNormalizedNameAsync(PlatformRoleNames.CustomerSuccessManager.ToUpperInvariant());
            }
        });

    private Task<string[]> GrantsAsync(Guid? tenantId) =>
        WithUnitOfWorkAsync(async () =>
        {
            using (_currentTenant.Change(tenantId))
            {
                return (await GetRequiredService<IPermissionGrantRepository>().GetListAsync(RolePermissionValueProvider.ProviderName, PlatformRoleNames.CustomerSuccessManager))
                    .Select(g => g.Name).ToArray();
            }
        });

    [Fact]
    public async Task The_host_seed_creates_a_static_role_with_the_impersonation_permission_only()
    {
        /* The test application seeds the host on start, as the DbMigrator does. */
        var role = await FindRoleAsync(null);
        role.ShouldNotBeNull();
        role.Name.ShouldBe("Customer Success Manager");
        role.IsStatic.ShouldBeTrue();
        role.TenantId.ShouldBeNull();
        (await GrantsAsync(null)).ShouldBe([PlatformPermissions.Tenants.Impersonation]);
    }

    [Fact]
    public async Task The_seed_is_idempotent()
    {
        var contributor = GetRequiredService<CustomerSuccessManagerRoleDataSeedContributor>();
        await WithUnitOfWorkAsync(() => contributor.SeedAsync(new DataSeedContext(null)));
        await WithUnitOfWorkAsync(() => contributor.SeedAsync(new DataSeedContext(null)));
        await WithUnitOfWorkAsync(() => GetRequiredService<IDataSeeder>().SeedAsync());

        var roles = await WithUnitOfWorkAsync(() => GetRequiredService<IIdentityRoleRepository>().GetListAsync());
        roles.Count(r => r.Name == PlatformRoleNames.CustomerSuccessManager).ShouldBe(1);
        (await GrantsAsync(null)).ShouldBe([PlatformPermissions.Tenants.Impersonation]);
    }

    [Fact]
    public async Task The_seed_makes_an_existing_role_of_that_name_static_again()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var manager = GetRequiredService<IdentityRoleManager>();
            var role = (await manager.FindByNameAsync(PlatformRoleNames.CustomerSuccessManager))!;
            role.IsStatic = false;
            (await manager.UpdateAsync(role)).Succeeded.ShouldBeTrue();
        });

        await WithUnitOfWorkAsync(() => GetRequiredService<CustomerSuccessManagerRoleDataSeedContributor>().SeedAsync(new DataSeedContext(null)));

        (await FindRoleAsync(null))!.IsStatic.ShouldBeTrue();
    }

    [Fact]
    public async Task The_seed_creates_nothing_in_a_tenant()
    {
        var acme = await CreateTenantAsync("acme");

        await WithUnitOfWorkAsync(async () =>
        {
            using (_currentTenant.Change(acme))
            {
                await GetRequiredService<CustomerSuccessManagerRoleDataSeedContributor>().SeedAsync(new DataSeedContext(acme));
                await GetRequiredService<IDataSeeder>().SeedAsync(new DataSeedContext(acme));
            }
        });

        (await FindRoleAsync(acme)).ShouldBeNull();
        (await GrantsAsync(acme)).ShouldBeEmpty();
    }
}
