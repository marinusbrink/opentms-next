using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenTms.EntityFrameworkCore.Platform.Impersonation;
using OpenTms.Platform.Accounts;
using Volo.Abp.Caching;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.ObjectMapping;
using Volo.Abp.Security.Claims;
using Volo.Abp.TenantManagement;

namespace OpenTms.EntityFrameworkCore.Platform.Accounts;

/// <summary>Records every account-switch ticket instead of encrypting it; the production adapter is tested in OpenTms.HttpApi.Host.Tests.</summary>
public sealed class RecordingAccountSwitchTicketProtector : IAccountSwitchTicketProtector
{
    public List<AccountSwitchTicket> Protected { get; } = new();

    public string Protect(AccountSwitchTicket ticket)
    {
        Protected.Add(ticket);
        return $"switch/{Protected.Count}+x=y";
    }

    public AccountSwitchTicket? Unprotect(string protectedTicket) => null;
}

/// <summary>
/// The tenants a test has deactivated. ABP's <see cref="Tenant"/> has no active flag of its own (the tenant store always
/// answers IsActive = true), so <see cref="DeactivatingTenantStore"/> applies this set on top of the real store.
/// </summary>
public sealed class InactiveTenants
{
    public ConcurrentDictionary<Guid, bool> Ids { get; } = new();

    public void Deactivate(Guid id) => Ids[id] = true;
}

/// <summary>The real tenant store, answering IsActive = false for the tenants in <see cref="InactiveTenants"/>.</summary>
[DisableConventionalRegistration]
public class DeactivatingTenantStore : TenantStore
{
    private readonly InactiveTenants _inactive;

    public DeactivatingTenantStore(
        ITenantRepository tenantRepository,
        IObjectMapper<AbpTenantManagementDomainModule> objectMapper,
        ICurrentTenant currentTenant,
        IDistributedCache<TenantConfigurationCacheItem> cache,
        InactiveTenants inactive)
        : base(tenantRepository, objectMapper, currentTenant, cache)
    {
        _inactive = inactive;
    }

    public override async Task<TenantConfiguration?> FindAsync(Guid id)
    {
        var tenant = await base.FindAsync(id);
        return tenant != null && _inactive.Ids.ContainsKey(id)
            ? new TenantConfiguration(tenant.Id, tenant.Name) { IsActive = false, ConnectionStrings = tenant.ConnectionStrings }
            : tenant;
    }
}

/// <summary>
/// The "Log in as" test application plus what the user menu's account switch needs: a recording ticket protector and a
/// tenant store whose tenants a test can deactivate. The auth server URL comes from <see cref="ImpersonationTestModule"/>.
/// </summary>
[DependsOn(typeof(ImpersonationTestModule))]
public class AccountsTestModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var protector = new RecordingAccountSwitchTicketProtector();
        context.Services.AddSingleton(protector);
        context.Services.Replace(ServiceDescriptor.Singleton<IAccountSwitchTicketProtector>(protector));

        context.Services.AddSingleton<InactiveTenants>();
        context.Services.Replace(ServiceDescriptor.Transient<ITenantStore, DeactivatingTenantStore>());
    }
}

/// <summary>Principals for the account tests: the user, their tenant, the session's password places, impersonation and roles.</summary>
public static class AccountPrincipals
{
    public static ClaimsPrincipal For(
        Guid userId,
        Guid? tenantId,
        string? passwordTenants = null,
        Guid? impersonatorId = null,
        params string[] roles)
    {
        var claims = new List<Claim>
        {
            new(AbpClaimTypes.UserId, userId.ToString()),
            new(AbpClaimTypes.UserName, "user-" + userId.ToString("N")[..6])
        };
        if (tenantId != null)
        {
            claims.Add(new Claim(AbpClaimTypes.TenantId, tenantId.Value.ToString()));
        }

        if (passwordTenants != null)
        {
            claims.Add(new Claim(AccountSwitchClaims.PasswordTenants, passwordTenants));
        }

        if (impersonatorId != null)
        {
            claims.Add(new Claim(AbpClaimTypes.ImpersonatorUserId, impersonatorId.Value.ToString()));
            claims.Add(new Claim(AbpClaimTypes.ImpersonatorUserName, "host.admin"));
        }

        foreach (var role in roles)
        {
            claims.Add(new Claim(AbpClaimTypes.Role, role));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }
}
