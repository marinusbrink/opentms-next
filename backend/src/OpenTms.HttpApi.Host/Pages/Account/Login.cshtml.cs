using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTms.Localization;
using OpenTms.SignIn;
using Volo.Abp.Account.Settings;
using Volo.Abp.Account.Web;
using Volo.Abp.Account.Web.Pages.Account;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Identity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.OpenIddict;
using Volo.Abp.Settings;
using Volo.Abp.UI.Navigation.Urls;
using Volo.Abp.Validation;

namespace OpenTms.Pages.Account;

public enum SignInStep
{
    Email,
    Password,
    Tenant,
}

/// <summary>
/// The sign-in page in three steps, without a tenant chosen up front (docs/designs/62-sign-in-tenant-detection.md):
/// 1. the e-mail address (with "Remember me"); an address no active tenant or the host knows goes no further;
/// 2. the password; where it is valid in one place the user is signed in there;
/// 3. where it is valid in several (tenants, or a tenant and the host), part of the tenant's name, of which the server
///    picks the one tenant that contains it.
/// Opened without a return URL, the page sends the browser to the SPA, which starts a proper sign-in.
/// A remembered address starts the page at step 2; the organisation last chosen for an address pre-fills step 3. ABP's tenant switch, its <c>__tenant</c> pre-selection and
/// sign-in by user name are not used here.
/// </summary>
/// <remarks>Exposed as itself: ABP's base class exposes only <see cref="LoginModel"/>, which this page does not ask for.</remarks>
[ExposeServices(typeof(OpenTmsLoginModel))]
public class OpenTmsLoginModel : OpenIddictSupportedLoginModel
{
    private readonly SignInTenantResolver _tenantResolver;
    private readonly SignInEmailProtector _emailProtector;
    private readonly SignInTenantSelectionProtector _selectionProtector;
    private readonly SignInLastTenantProtector _lastTenantProtector;
    private readonly ILookupNormalizer _normalizer;
    private readonly IStringLocalizer<OpenTmsResource> _openTmsLocalizer;
    private readonly IConfiguration _configuration;
    private readonly AppUrlOptions _appUrlOptions;

    public SignInStep Step { get; private set; } = SignInStep.Email;

    [BindProperty]
    public string? Email { get; set; }

    [BindProperty]
    public string? Password { get; set; }

    [BindProperty]
    public bool RememberMe { get; set; }

    [BindProperty]
    public string? TenantQuery { get; set; }

    public OpenTmsLoginModel(
        IAuthenticationSchemeProvider schemeProvider,
        IOptions<AbpAccountOptions> accountOptions,
        IOptions<IdentityOptions> identityOptions,
        IdentityDynamicClaimsPrincipalContributorCache identityDynamicClaimsPrincipalContributorCache,
        AbpOpenIddictRequestHelper openIddictRequestHelper,
        IWebHostEnvironment webHostEnvironment,
        SignInTenantResolver tenantResolver,
        SignInEmailProtector emailProtector,
        SignInTenantSelectionProtector selectionProtector,
        SignInLastTenantProtector lastTenantProtector,
        ILookupNormalizer normalizer,
        IStringLocalizer<OpenTmsResource> openTmsLocalizer,
        IConfiguration configuration,
        IOptions<AppUrlOptions> appUrlOptions)
        : base(schemeProvider, accountOptions, identityOptions, identityDynamicClaimsPrincipalContributorCache, openIddictRequestHelper, webHostEnvironment)
    {
        _tenantResolver = tenantResolver;
        _emailProtector = emailProtector;
        _selectionProtector = selectionProtector;
        _lastTenantProtector = lastTenantProtector;
        _normalizer = normalizer;
        _openTmsLocalizer = openTmsLocalizer;
        _configuration = configuration;
        _appUrlOptions = appUrlOptions.Value;
    }

    private string HostName => _openTmsLocalizer["Shell:TenantHost"];

    /// <summary>True when this sign-in is for the API through Swagger UI rather than the SPA: the page looks different.</summary>
    public bool IsApiSignIn { get; private set; }

    /// <summary>Before every handler: which client the authorize request in the return URL comes from.</summary>
    public override async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        var request = await OpenIddictRequestHelper.GetFromReturnUrlAsync(ReturnUrl);
        IsApiSignIn = SignInClient.IsApi(request?.ClientId, _configuration["AuthServer:SwaggerClientId"]);
        if (IsApiSignIn)
        {
            ViewData[SignInClient.ViewDataKey] = SignInClient.Api;
        }

        await base.OnPageHandlerExecutionAsync(context, next);
    }

    /// <summary>Step 1, or step 2 for an address carried over from step 1 or remembered from an earlier visit.</summary>
    public override async Task<IActionResult> OnGetAsync()
    {
        /* Opened without a sign-in request to return to (a bookmark, after signing out of the auth server): signing in
         * here would land on the auth server, not in the app. The SPA starts a proper sign-in instead. */
        if (!HasLocalReturnUrl)
        {
            return Redirect(SpaUrl.Root(_appUrlOptions));
        }

        await LoadLoginOptionsAsync();
        if (IsExternalLoginOnly)
        {
            return await OnPostExternalLogin(ExternalProviders.First().AuthenticationScheme);
        }

        Response.Cookies.Delete(SignInTenantSelectionProtector.CookieName, CookieOptions(SameSiteMode.Strict, null));
        Response.Cookies.Delete(TenantResolverConsts.DefaultTenantKey);

        var email = ReadEmail();
        if (email != null && await _tenantResolver.IsKnownEmailAsync(email.Email))
        {
            Email = email.Email;
            RememberMe = email.RememberMe;
            Step = SignInStep.Password;
            return Page();
        }

        if (email != null)
        {
            ForgetEmail();
        }

        var request = await OpenIddictRequestHelper.GetFromReturnUrlAsync(ReturnUrl);
        Email = request?.LoginHint;
        return Page();
    }

    /// <summary>"Another e-mail address": forgets the address (a remembered one too) and starts over at step 1.</summary>
    public IActionResult OnGetChangeEmail()
    {
        ForgetEmail();
        return RedirectToPage("./Login", new { ReturnUrl, ReturnUrlHash });
    }

    /// <summary>Step 1: an address some active tenant or the host knows goes on to the password, any other stays here.</summary>
    public async Task<IActionResult> OnPostEmailAsync()
    {
        await CheckLocalLoginAsync();
        await LoadLoginOptionsAsync();
        ModelState.Clear();

        var email = Email?.Trim() ?? string.Empty;
        if (!ValidationHelper.IsValidEmailAddress(email))
        {
            Alerts.Danger(_openTmsLocalizer["SignIn:EmailInvalid"]);
            return Page();
        }

        if (!await _tenantResolver.IsKnownEmailAsync(email))
        {
            Alerts.Danger(_openTmsLocalizer["SignIn:EmailUnknown"]);
            return Page();
        }

        RememberEmail(new SignInEmail(email, RememberMe));
        return RedirectToPage("./Login", new { ReturnUrl, ReturnUrlHash });
    }

    /// <summary>Step 2: the password for the address of step 1.</summary>
    public async Task<IActionResult> OnPostPasswordAsync()
    {
        await CheckLocalLoginAsync();
        await LoadLoginOptionsAsync();
        ModelState.Clear();

        var email = ReadEmail();
        if (email == null)
        {
            return RedirectToPage("./Login", new { ReturnUrl, ReturnUrlHash });
        }

        Email = email.Email;
        RememberMe = email.RememberMe;
        Step = SignInStep.Password;
        if (string.IsNullOrEmpty(Password))
        {
            Alerts.Danger(_openTmsLocalizer["SignIn:PasswordRequired"]);
            return Page();
        }

        var resolution = await _tenantResolver.ResolveAsync(email.Email, Password);
        switch (resolution.Outcome)
        {
            case SignInResolutionOutcome.Single:
                return await SignInAsync(resolution.Choices[0], email.RememberMe);

            case SignInResolutionOutcome.Multiple:
                Response.Cookies.Append(
                    SignInTenantSelectionProtector.CookieName,
                    _selectionProtector.Protect(new SignInTenantSelection(resolution.Choices, email.RememberMe)),
                    CookieOptions(SameSiteMode.Strict, DateTimeOffset.UtcNow.Add(SignInTenantSelectionProtector.Lifetime)));
                Step = SignInStep.Tenant;
                TenantQuery = SignInLastTenant.PrefillFor(ReadLastTenant(), EmailHash(email.Email), resolution.Choices, HostName);
                return Page();

            case SignInResolutionOutcome.LockedOut:
                await SaveFailedSecurityLogAsync(IdentitySecurityLogActionConsts.LoginLockedout);
                Alerts.Warning(L["UserLockedOutMessage"]);
                return Page();

            case SignInResolutionOutcome.NotAllowed:
                await SaveFailedSecurityLogAsync(IdentitySecurityLogActionConsts.LoginNotAllowed);
                Alerts.Warning(L["LoginIsNotAllowed"]);
                return Page();

            case SignInResolutionOutcome.Unavailable:
                await SaveFailedSecurityLogAsync(IdentitySecurityLogActionConsts.LoginFailed);
                Alerts.Warning(_openTmsLocalizer["SignIn:Unavailable"]);
                return Page();

            default:
                await SaveFailedSecurityLogAsync(IdentitySecurityLogActionConsts.LoginInvalidUserNameOrPassword);
                Alerts.Danger(_openTmsLocalizer["SignIn:PasswordInvalid"]);
                return Page();
        }
    }

    /// <summary>
    /// Step 3's live hint: the one tenant whose name contains the typed text, or only that there is none or more than
    /// one. Answers only within the sealed selection of the tenants where the password was valid.
    /// </summary>
    public IActionResult OnPostFindTenant(string? query)
    {
        var selection = ReadSelection();
        if (selection == null)
        {
            return new JsonResult(new { status = "expired" });
        }

        var match = SignInTenantMatch.Find(selection.Choices, query, HostName);
        return new JsonResult(new
        {
            status = match.Status switch
            {
                SignInTenantMatchStatus.One => "one",
                SignInTenantMatchStatus.Ambiguous => "ambiguous",
                SignInTenantMatchStatus.None => "none",
                _ => "short",
            },
            name = match.Choice == null ? null : SignInTenantMatch.DisplayName(match.Choice, HostName),
        });
    }

    /// <summary>Step 3: signs in to the one tenant the typed text picks.</summary>
    public async Task<IActionResult> OnPostSelectTenantAsync()
    {
        await LoadLoginOptionsAsync();
        ModelState.Clear();

        var selection = ReadSelection();
        if (selection == null)
        {
            Alerts.Warning(_openTmsLocalizer["SignIn:TenantSelectionExpired"]);
            return BackToPassword();
        }

        var match = SignInTenantMatch.Find(selection.Choices, TenantQuery, HostName);
        if (match.Choice == null)
        {
            Alerts.Warning(_openTmsLocalizer[match.Status == SignInTenantMatchStatus.Ambiguous ? "SignIn:TenantAmbiguous" : "SignIn:TenantNotFound"]);
            Step = SignInStep.Tenant;
            return Page();
        }

        var email = ReadEmail();
        if (email != null)
        {
            Response.Cookies.Append(
                SignInLastTenantProtector.CookieName,
                _lastTenantProtector.Protect(new SignInLastTenant(EmailHash(email.Email), match.Choice.TenantId)),
                CookieOptions(SameSiteMode.Strict, DateTimeOffset.UtcNow.Add(SignInLastTenantProtector.RememberFor)));
        }

        return await SignInAsync(match.Choice, selection.RememberMe);
    }

    /// <summary>
    /// Signs in to the chosen place. The user is read again there: one deactivated or locked out since the password
    /// was checked (up to five minutes ago, for step 3) is refused.
    /// </summary>
    private async Task<IActionResult> SignInAsync(SignInChoice choice, bool rememberMe)
    {
        using (CurrentTenant.Change(choice.TenantId))
        {
            await IdentityOptions.SetAsync();
            var user = await UserManager.FindByIdAsync(choice.UserId.ToString());
            if (user == null || !user.IsActive || await UserManager.IsLockedOutAsync(user) || !await SignInManager.CanSignInAsync(user))
            {
                Response.Cookies.Delete(SignInTenantSelectionProtector.CookieName, CookieOptions(SameSiteMode.Strict, null));
                Alerts.Warning(L["LoginIsNotAllowed"]);
                return BackToPassword();
            }

            await SignInManager.SignInAsync(user, rememberMe);
            await IdentitySecurityLogManager.SaveAsync(new IdentitySecurityLogContext
            {
                Identity = IdentitySecurityLogIdentityConsts.Identity,
                Action = IdentitySecurityLogActionConsts.LoginSucceeded,
                UserName = user.UserName,
            });
            await IdentityDynamicClaimsPrincipalContributorCache.ClearAsync(user.Id, user.TenantId);
        }

        Logger.LogInformation("Signed in user {UserId} (tenant {TenantId})", choice.UserId, choice.TenantId);
        Response.Cookies.Delete(SignInTenantSelectionProtector.CookieName, CookieOptions(SameSiteMode.Strict, null));
        Response.Cookies.Delete(TenantResolverConsts.DefaultTenantKey);
        if (!rememberMe)
        {
            ForgetEmail();
        }

        return HasLocalReturnUrl
            ? await RedirectSafelyAsync(ReturnUrl, ReturnUrlHash)
            : Redirect(SpaUrl.Root(_appUrlOptions));
    }

    /// <summary>Whether the page has a sign-in request to return to (the OpenID Connect authorize request, or another page of the auth server).</summary>
    private bool HasLocalReturnUrl => !string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl);

    /// <summary>Step 2 again for the address of step 1 (alerts do not survive a redirect), or step 1 without one.</summary>
    private IActionResult BackToPassword()
    {
        var email = ReadEmail();
        if (email != null)
        {
            Email = email.Email;
            RememberMe = email.RememberMe;
            Step = SignInStep.Password;
        }

        return Page();
    }

    private async Task LoadLoginOptionsAsync()
    {
        ExternalProviders = await GetExternalProviders();
        EnableLocalLogin = await SettingProvider.IsTrueAsync(AccountSettingNames.EnableLocalLogin);
    }

    /// <summary>A failed sign-in has no tenant yet: ABP's security log entry is written in the host, as ABP does without a tenant.</summary>
    private Task SaveFailedSecurityLogAsync(string action) =>
        IdentitySecurityLogManager.SaveAsync(new IdentitySecurityLogContext
        {
            Identity = IdentitySecurityLogIdentityConsts.Identity,
            Action = action,
            UserName = Email,
        });

    private SignInEmail? ReadEmail() => _emailProtector.Unprotect(Request.Cookies[SignInEmailProtector.CookieName]);

    /// <summary>
    /// Keeps the address for step 2: for the browser session, or for 30 days with "Remember me". Lax, not Strict: the
    /// SPA sends the browser here from its own site, and the remembered address must come along.
    /// </summary>
    private void RememberEmail(SignInEmail email) =>
        Response.Cookies.Append(
            SignInEmailProtector.CookieName,
            _emailProtector.Protect(email),
            CookieOptions(SameSiteMode.Lax, email.RememberMe ? DateTimeOffset.UtcNow.Add(SignInEmailProtector.RememberFor) : null));

    private void ForgetEmail() =>
        Response.Cookies.Delete(SignInEmailProtector.CookieName, CookieOptions(SameSiteMode.Lax, null));

    /// <summary>The address as the lookup knows it: the remembered organisation is kept per address, never in readable form.</summary>
    private string EmailHash(string email) => Platform.SignIn.UserSignInLookupHash.Of(_normalizer.NormalizeEmail(email.Trim()));

    private SignInLastTenant? ReadLastTenant() =>
        _lastTenantProtector.Unprotect(Request.Cookies[SignInLastTenantProtector.CookieName]);

    private SignInTenantSelection? ReadSelection() =>
        _selectionProtector.Unprotect(Request.Cookies[SignInTenantSelectionProtector.CookieName]);

    private CookieOptions CookieOptions(SameSiteMode sameSite, DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        Secure = Request.IsHttps,
        SameSite = sameSite,
        Path = "/Account",
        Expires = expires,
        IsEssential = true,
    };
}
