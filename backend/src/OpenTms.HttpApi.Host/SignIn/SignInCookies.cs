using System;
using Microsoft.AspNetCore.Http;

namespace OpenTms.SignIn;

/// <summary>The options of the account pages' own cookies: HttpOnly, for <c>/Account</c> only, secure over HTTPS.</summary>
public static class SignInCookies
{
    public static CookieOptions Options(HttpRequest request, SameSiteMode sameSite, DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        Secure = request.IsHttps,
        SameSite = sameSite,
        Path = "/Account",
        Expires = expires,
        IsEssential = true,
    };
}
