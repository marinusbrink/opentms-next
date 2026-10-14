using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Security.Claims;
using Volo.Abp.TenantManagement;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace OpenTms.Impersonation;

/// <summary>Answers AuthenticateAsync per scheme with a fixed principal (or no result); everything else is not used.</summary>
public sealed class FakeAuthenticationService : IAuthenticationService
{
    public Dictionary<string, ClaimsPrincipal> Principals { get; } = new();

    public List<string> Asked { get; } = new();

    public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme)
    {
        Asked.Add(scheme ?? "");
        return Task.FromResult(scheme != null && Principals.TryGetValue(scheme, out var principal)
            ? AuthenticateResult.Success(new AuthenticationTicket(principal, scheme))
            : AuthenticateResult.NoResult());
    }

    public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => throw new NotSupportedException();

    public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => throw new NotSupportedException();

    public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties) => throw new NotSupportedException();

    public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => throw new NotSupportedException();
}

/// <summary>A user store that is never asked anything: the fake user manager answers FindById itself.</summary>
public sealed class NullUserStore : IUserStore<IdentityUser>
{
    public void Dispose()
    {
    }

    public Task<string> GetUserIdAsync(IdentityUser user, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<string?> GetUserNameAsync(IdentityUser user, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task SetUserNameAsync(IdentityUser user, string? userName, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<string?> GetNormalizedUserNameAsync(IdentityUser user, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task SetNormalizedUserNameAsync(IdentityUser user, string? normalizedName, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<IdentityResult> CreateAsync(IdentityUser user, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<IdentityResult> UpdateAsync(IdentityUser user, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<IdentityResult> DeleteAsync(IdentityUser user, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<IdentityUser?> FindByIdAsync(string userId, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<IdentityUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken) => throw new NotSupportedException();
}

/// <summary>Users by (tenant, id); records the tenant that was current when a user was looked up.</summary>
public sealed class FakeUserManager : UserManager<IdentityUser>
{
    private readonly ICurrentTenant _currentTenant;

    public FakeUserManager(ICurrentTenant currentTenant)
        : base(new NullUserStore(), Microsoft.Extensions.Options.Options.Create(new IdentityOptions()), null!, [], [], null!, null!, null!, NullLogger<UserManager<IdentityUser>>.Instance)
    {
        _currentTenant = currentTenant;
    }

    public List<IdentityUser> Users { get; } = new();

    public List<(string Id, Guid? Tenant)> Lookups { get; } = new();

    public override Task<IdentityUser?> FindByIdAsync(string userId)
    {
        Lookups.Add((userId, _currentTenant.Id));
        return Task.FromResult(Users.FirstOrDefault(u => u.Id.ToString() == userId && u.TenantId == _currentTenant.Id));
    }
}

public sealed class NullClaimsFactory : IUserClaimsPrincipalFactory<IdentityUser>
{
    public Task<ClaimsPrincipal> CreateAsync(IdentityUser user) => throw new NotSupportedException();
}

/// <summary>Records sign-out and sign-in (user, claims, tenant at the time) instead of writing a cookie.</summary>
public sealed class FakeSignInManager : SignInManager<IdentityUser>
{
    private readonly ICurrentTenant _currentTenant;

    public FakeSignInManager(FakeUserManager users, ICurrentTenant currentTenant)
        : base(users, new HttpContextAccessor(), new NullClaimsFactory(), Microsoft.Extensions.Options.Options.Create(new IdentityOptions()),
            NullLogger<SignInManager<IdentityUser>>.Instance, null!, null!)
    {
        _currentTenant = currentTenant;
    }

    public List<string> Calls { get; } = new();

    public IdentityUser? SignedInUser { get; private set; }

    public List<Claim> SignedInClaims { get; } = new();

    public Guid? TenantAtSignIn { get; private set; }

    public bool? IsPersistent { get; private set; }

    public override Task SignOutAsync()
    {
        Calls.Add("SignOut");
        return Task.CompletedTask;
    }

    public override Task SignInWithClaimsAsync(IdentityUser user, AuthenticationProperties? authenticationProperties, IEnumerable<Claim> additionalClaims)
    {
        Calls.Add("SignIn");
        SignedInUser = user;
        SignedInClaims.AddRange(additionalClaims);
        TenantAtSignIn = _currentTenant.Id;
        IsPersistent = authenticationProperties?.IsPersistent ?? false;
        return Task.CompletedTask;
    }
}

/// <summary>An <see cref="ITenantRepository"/> that only knows FindAsync(id) over a list; any other member throws.</summary>
public class FakeTenantRepository : DispatchProxy
{
    public List<Tenant> Tenants { get; } = new();

    public List<Guid?> TenantAtLookup { get; } = new();

    public ICurrentTenant? CurrentTenant { get; set; }

    public static (ITenantRepository Repository, FakeTenantRepository Fake) Create(ICurrentTenant currentTenant)
    {
        var repository = DispatchProxy.Create<ITenantRepository, FakeTenantRepository>();
        var fake = (FakeTenantRepository)(object)repository;
        fake.CurrentTenant = currentTenant;
        return (repository, fake);
    }

    public static Tenant NewTenant(Guid id, string name)
    {
        var ctor = typeof(Tenant).GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .OrderByDescending(c => c.GetParameters().Length)
            .First(c => c.GetParameters().FirstOrDefault()?.ParameterType == typeof(Guid));
        var args = ctor.GetParameters().Select((p, i) => i == 0 ? (object?)id : p.Name!.Contains("ormalized") ? name.ToUpperInvariant() : p.ParameterType == typeof(string) ? name : null).ToArray();
        return (Tenant)ctor.Invoke(args);
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod?.Name == "FindAsync" && args?.FirstOrDefault() is Guid id)
        {
            TenantAtLookup.Add(CurrentTenant?.Id);
            return Task.FromResult(Tenants.FirstOrDefault(t => t.Id == id));
        }

        throw new NotSupportedException(targetMethod?.Name);
    }
}

/// <summary>A data-protection provider whose protector does not encrypt, so a test can read and rewrite the payload.</summary>
public sealed class PassThroughDataProtectionProvider : IDataProtectionProvider, IDataProtector
{
    public IDataProtector CreateProtector(string purpose) => this;

    public byte[] Protect(byte[] plaintext) => plaintext.ToArray();

    public byte[] Unprotect(byte[] protectedData) => protectedData.ToArray();
}

public static class Principals
{
    public static ClaimsPrincipal For(Guid userId, Guid? tenantId = null, string? userName = null, params Claim[] extra)
    {
        var claims = new List<Claim> { new(AbpClaimTypes.UserId, userId.ToString()) };
        if (userName != null)
        {
            claims.Add(new Claim(AbpClaimTypes.UserName, userName));
        }

        if (tenantId != null)
        {
            claims.Add(new Claim(AbpClaimTypes.TenantId, tenantId.Value.ToString()));
        }

        claims.AddRange(extra);
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Identity.Application"));
    }
}
