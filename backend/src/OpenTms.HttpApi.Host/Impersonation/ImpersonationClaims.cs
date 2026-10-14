using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Principal;
using Volo.Abp.Security.Claims;

namespace OpenTms.Impersonation;

/// <summary>ABP's impersonator claims: who is really acting while a host user is logged in as a tenant user.</summary>
public static class ImpersonationClaims
{
    public static IReadOnlyList<string> Types =>
    [
        AbpClaimTypes.ImpersonatorUserId,
        AbpClaimTypes.ImpersonatorTenantId,
        AbpClaimTypes.ImpersonatorUserName,
        AbpClaimTypes.ImpersonatorTenantName
    ];

    public static IReadOnlyList<Claim> From(ClaimsPrincipal? principal) =>
        principal == null ? [] : Types.SelectMany(type => principal.FindAll(type)).ToList();
}
