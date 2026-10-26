using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Volo.Abp.Authorization;
using Volo.Abp.Security.Claims;

namespace OpenTms.Security;

/// <summary>
/// The tests' authorization: the fake admin of <see cref="FakeCurrentPrincipalAccessor"/> holds every permission (as
/// ABP's always-allow authorization did), but a caller without a user is refused wherever production refuses one. The
/// always-allow services granted the anonymous outbox handlers and derived-value jobs too, which hid the browser run's
/// stalled outbox (design 56 build notes): a published reader with <c>[Authorize]</c> passed every test.
/// </summary>
public static class SignedInUserAuthorization
{
    public static IServiceCollection AddSignedInUserAuthorization(this IServiceCollection services)
    {
        services.AddAlwaysAllowAuthorization(); // the permission checker stays always-allow
        services.Replace(ServiceDescriptor.Singleton<IAuthorizationService, SignedInUserAuthorizationService>());
        services.Replace(ServiceDescriptor.Singleton<IAbpAuthorizationService, SignedInUserAuthorizationService>());
        return services.Replace(ServiceDescriptor.Singleton<IMethodInvocationAuthorizationService, SignedInUserMethodInvocationAuthorizationService>());
    }

    /// <summary>ABP's notion of a signed-in user: the principal carries a user id.</summary>
    public static bool IsSignedIn(ClaimsPrincipal? principal) => principal?.FindUserId() != null;
}

/// <summary>Grants every policy to a signed-in principal and none to an anonymous one.</summary>
public class SignedInUserAuthorizationService : IAbpAuthorizationService
{
    private readonly ICurrentPrincipalAccessor _principalAccessor;

    public SignedInUserAuthorizationService(IServiceProvider serviceProvider, ICurrentPrincipalAccessor principalAccessor)
    {
        ServiceProvider = serviceProvider;
        _principalAccessor = principalAccessor;
    }

    public IServiceProvider ServiceProvider { get; }

    public ClaimsPrincipal CurrentPrincipal => _principalAccessor.Principal;

    public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements)
        => Task.FromResult(Result(user));

    public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName)
        => Task.FromResult(Result(user));

    private static AuthorizationResult Result(ClaimsPrincipal? user)
        => SignedInUserAuthorization.IsSignedIn(user)
            ? AuthorizationResult.Success()
            : AuthorizationResult.Failed(AuthorizationFailure.Failed([new DenyAnonymousAuthorizationRequirement()]));
}

/// <summary>
/// ABP's attribute check (<c>MethodInvocationAuthorizationService</c>) with the policy evaluation reduced to "signed in":
/// <c>[AllowAnonymous]</c> on the method skips the check, otherwise any <c>[Authorize]</c> on the method or (for a
/// public method) its class requires a user.
/// </summary>
public class SignedInUserMethodInvocationAuthorizationService : IMethodInvocationAuthorizationService
{
    private readonly ICurrentPrincipalAccessor _principalAccessor;

    public SignedInUserMethodInvocationAuthorizationService(ICurrentPrincipalAccessor principalAccessor)
    {
        _principalAccessor = principalAccessor;
    }

    public Task CheckAsync(MethodInvocationAuthorizationContext context)
    {
        var method = context.Method;
        if (method.GetCustomAttributes(true).OfType<IAllowAnonymous>().Any() || !RequiresAuthorization(method))
        {
            return Task.CompletedTask;
        }

        if (!SignedInUserAuthorization.IsSignedIn(_principalAccessor.Principal))
        {
            throw new AbpAuthorizationException($"{method.DeclaringType?.FullName}.{method.Name} requires a signed-in user; the caller has none.");
        }

        return Task.CompletedTask;
    }

    private static bool RequiresAuthorization(MethodInfo method)
        => method.GetCustomAttributes(true).OfType<IAuthorizeData>().Any()
           || (method.IsPublic && method.DeclaringType != null && method.DeclaringType.GetCustomAttributes(true).OfType<IAuthorizeData>().Any());
}
