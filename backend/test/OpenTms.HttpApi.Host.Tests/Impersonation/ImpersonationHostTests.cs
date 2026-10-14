using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using OpenTms.Pages.Account;
using OpenTms.Platform.Impersonation;
using Shouldly;
using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.OpenIddict;
using Volo.Abp.Security.Claims;
using Volo.Abp.UI.Navigation.Urls;
using Xunit;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace OpenTms.Impersonation;

/// <summary>
/// The auth server's half of "Log in as a tenant user" (docs/designs/tenants-and-editions.md, D14): the ticket protector,
/// the Impersonate page, the claims handler that carries the impersonator into the tokens, and the cookie refresh that
/// keeps it. Plain unit tests: no ABP application, every port faked, the production classes as they are.
/// Risk class: Critical — authentication (a ticket redeemed for the wrong session, or claims that vanish or appear,
/// would log a user in as somebody else or hide who acted).
/// </summary>
public class DataProtectionImpersonationTicketProtectorTests
{
    private static readonly ImpersonationTicket Ticket = new(false, Guid.NewGuid(), null, Guid.NewGuid(), Guid.NewGuid());

    [Fact]
    public void A_ticket_round_trips()
    {
        var protector = new DataProtectionImpersonationTicketProtector(new EphemeralDataProtectionProvider());
        var protectedTicket = protector.Protect(Ticket);

        protectedTicket.ShouldNotContain(Ticket.TargetUserId.ToString(), Case.Insensitive, "the ticket is encrypted, not just signed");
        protector.Unprotect(protectedTicket).ShouldBe(Ticket);

        var back = Ticket with { IsReturn = true, ExpectedTenantId = Guid.NewGuid(), TargetTenantId = null };
        protector.Unprotect(protector.Protect(back)).ShouldBe(back);
    }

    [Fact]
    public void A_tampered_ticket_is_null()
    {
        var protector = new DataProtectionImpersonationTicketProtector(new EphemeralDataProtectionProvider());
        var protectedTicket = protector.Protect(Ticket);

        var bytes = WebEncoders.Base64UrlDecode(protectedTicket);
        bytes[^5] ^= 0x5A;
        protector.Unprotect(WebEncoders.Base64UrlEncode(bytes)).ShouldBeNull();
        protector.Unprotect(protectedTicket[..^4]).ShouldBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("%%%not-base64%%%")]
    [InlineData("eyJJc1JldHVybiI6dHJ1ZX0")]
    public void Garbage_is_null(string value)
    {
        new DataProtectionImpersonationTicketProtector(new EphemeralDataProtectionProvider()).Unprotect(value).ShouldBeNull();
    }

    [Fact]
    public void A_ticket_of_another_key_ring_or_purpose_is_null()
    {
        var ours = new DataProtectionImpersonationTicketProtector(new EphemeralDataProtectionProvider());
        var theirs = new DataProtectionImpersonationTicketProtector(new EphemeralDataProtectionProvider());
        ours.Unprotect(theirs.Protect(Ticket)).ShouldBeNull();

        var provider = new EphemeralDataProtectionProvider();
        var otherPurpose = provider.CreateProtector("Something.Else").ToTimeLimitedDataProtector()
            .Protect(JsonSerializer.Serialize(Ticket), TimeSpan.FromMinutes(5));
        new DataProtectionImpersonationTicketProtector(provider).Unprotect(otherPurpose).ShouldBeNull();
    }

    [Fact]
    public void A_validly_protected_payload_that_is_not_a_ticket_is_null()
    {
        var provider = new EphemeralDataProtectionProvider();
        var notJson = provider.CreateProtector("OpenTms.Impersonation.Ticket.v1").ToTimeLimitedDataProtector()
            .Protect("not json", TimeSpan.FromMinutes(5));
        new DataProtectionImpersonationTicketProtector(provider).Unprotect(notJson).ShouldBeNull();
    }

    /// <summary>The time-limited payload: 8 bytes big-endian expiry (UTC ticks), then the ticket's JSON.</summary>
    private static (DateTimeOffset Expiry, byte[] Payload) Read(string protectedTicket)
    {
        var bytes = WebEncoders.Base64UrlDecode(protectedTicket);
        return (new DateTimeOffset(BinaryPrimitives.ReadInt64BigEndian(bytes), TimeSpan.Zero), bytes);
    }

    [Fact]
    public void A_ticket_lives_five_minutes()
    {
        var protector = new DataProtectionImpersonationTicketProtector(new PassThroughDataProtectionProvider());
        var before = DateTimeOffset.UtcNow;
        var (expiry, payload) = Read(protector.Protect(Ticket));

        expiry.ShouldBeInRange(before.AddMinutes(5).AddSeconds(-1), DateTimeOffset.UtcNow.AddMinutes(5).AddSeconds(1));
        Encoding.UTF8.GetString(payload, 8, payload.Length - 8).ShouldContain(Ticket.TargetUserId.ToString());
    }

    [Fact]
    public void An_expired_ticket_is_null()
    {
        /* Five minutes later, simulated by rewriting the expiry of the (pass-through protected) payload. */
        var protector = new DataProtectionImpersonationTicketProtector(new PassThroughDataProtectionProvider());
        var (_, payload) = Read(protector.Protect(Ticket));

        BinaryPrimitives.WriteInt64BigEndian(payload, DateTimeOffset.UtcNow.AddSeconds(-1).UtcTicks);
        protector.Unprotect(WebEncoders.Base64UrlEncode(payload)).ShouldBeNull();

        BinaryPrimitives.WriteInt64BigEndian(payload, DateTimeOffset.UtcNow.AddMinutes(4).UtcTicks);
        protector.Unprotect(WebEncoders.Base64UrlEncode(payload)).ShouldBe(Ticket);
    }
}

public class ImpersonatePageTests
{
    private const string Spa = "http://localhost:5173";
    private static readonly Guid HostAdmin = Guid.NewGuid();
    private static readonly Guid Acme = Guid.NewGuid();
    private static readonly Guid Planner = Guid.NewGuid();

    private readonly ICurrentTenant _currentTenant = new CurrentTenant(AsyncLocalCurrentTenantAccessor.Instance);
    private readonly FakeAuthenticationService _auth = new();
    private readonly Dictionary<string, ImpersonationTicket> _tickets = new();
    private readonly FakeUserManager _users;
    private readonly FakeSignInManager _signIn;
    private readonly FakeTenantRepository _tenants;
    private readonly ImpersonateModel _page;

    private sealed class DictionaryTicketProtector(Dictionary<string, ImpersonationTicket> tickets) : IImpersonationTicketProtector
    {
        public string Protect(ImpersonationTicket ticket) => throw new NotSupportedException();

        public ImpersonationTicket? Unprotect(string protectedTicket) => tickets.GetValueOrDefault(protectedTicket);
    }

    public ImpersonatePageTests()
    {
        _users = new FakeUserManager(_currentTenant);
        _signIn = new FakeSignInManager(_users, _currentTenant);
        var (repository, fake) = FakeTenantRepository.Create(_currentTenant);
        _tenants = fake;

        var urls = new AppUrlOptions();
        urls.RedirectAllowedUrls.AddRange(["", "  " + Spa + "/ ", "https://second.example.test"]);

        _page = new ImpersonateModel(new DictionaryTicketProtector(_tickets), _signIn, _currentTenant, repository, Options.Create(urls),
            NullLogger<ImpersonateModel>.Instance);

        var http = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddSingleton<IAuthenticationService>(_auth).BuildServiceProvider()
        };
        /* Whatever the request says, the page must not send the browser there. */
        http.Request.QueryString = new QueryString("?returnUrl=https%3A%2F%2Fevil.example.test%2F&redirect_uri=https%3A%2F%2Fevil.example.test");
        http.Request.Headers.Referer = "https://evil.example.test/";
        _page.PageContext = new PageContext { HttpContext = http };
    }

    private IdentityUser AddUser(Guid id, Guid? tenantId, string userName, bool active = true)
    {
        var user = new IdentityUser(id, userName, userName + "@example.test", tenantId);
        user.SetIsActive(active);
        _users.Users.Add(user);
        return user;
    }

    private void Session(ClaimsPrincipal principal) => _auth.Principals[IdentityConstants.ApplicationScheme] = principal;

    private string Ticket(ImpersonationTicket ticket)
    {
        var key = "t" + _tickets.Count;
        _tickets[key] = ticket;
        return key;
    }

    private static void ShouldBeRefused(IActionResult result) =>
        result.ShouldBeOfType<RedirectResult>().Url.ShouldBe(Spa + "/admin?impersonation=refused");

    private static readonly ImpersonationTicket Start = new(false, HostAdmin, null, Planner, Acme);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown")]
    public async Task No_or_an_invalid_ticket_is_refused_back_to_the_SPA(string? ticket)
    {
        Session(Principals.For(HostAdmin, userName: "admin"));
        ShouldBeRefused(await _page.OnGetAsync(ticket));
        _signIn.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Without_a_session_the_browser_logs_in_first()
    {
        AddUser(Planner, Acme, "acme.planner");
        var result = await _page.OnGetAsync(Ticket(Start));

        result.ShouldBeOfType<ChallengeResult>().AuthenticationSchemes.ShouldBe([IdentityConstants.ApplicationScheme]);
        _signIn.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_ticket_issued_to_another_user_is_refused()
    {
        AddUser(Planner, Acme, "acme.planner");
        Session(Principals.For(Guid.NewGuid(), userName: "other.admin"));

        ShouldBeRefused(await _page.OnGetAsync(Ticket(Start)));
        _signIn.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_ticket_issued_to_the_same_user_id_in_another_tenant_is_refused()
    {
        AddUser(Planner, Acme, "acme.planner");
        Session(Principals.For(HostAdmin, Guid.NewGuid(), "admin"));

        ShouldBeRefused(await _page.OnGetAsync(Ticket(Start)));
        _signIn.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_missing_or_inactive_target_is_refused()
    {
        Session(Principals.For(HostAdmin, userName: "admin"));
        ShouldBeRefused(await _page.OnGetAsync(Ticket(Start)));

        AddUser(Planner, Acme, "acme.planner", active: false);
        ShouldBeRefused(await _page.OnGetAsync(Ticket(Start)));

        _signIn.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_target_id_that_exists_only_in_another_tenant_is_refused()
    {
        /* The page looks the user up in the ticket's target tenant; the same id elsewhere does not count. */
        AddUser(Planner, Guid.NewGuid(), "beta.planner");
        Session(Principals.For(HostAdmin, userName: "admin"));

        ShouldBeRefused(await _page.OnGetAsync(Ticket(Start)));
        _users.Lookups.ShouldContain((Planner.ToString(), (Guid?)Acme));
        _signIn.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Start_signs_in_as_the_target_in_its_tenant_with_the_host_impersonator_and_lands_on_the_SPA_admin()
    {
        var planner = AddUser(Planner, Acme, "acme.planner");
        Session(Principals.For(HostAdmin, userName: "admin"));

        var result = await _page.OnGetAsync(Ticket(Start));

        result.ShouldBeOfType<RedirectResult>().Url.ShouldBe(Spa + "/admin");
        _signIn.Calls.ShouldBe(["SignOut", "SignIn"]);
        _signIn.SignedInUser.ShouldBe(planner);
        _signIn.TenantAtSignIn.ShouldBe(Acme);
        _signIn.IsPersistent.ShouldBe(false);
        _signIn.SignedInClaims.Select(c => (c.Type, c.Value)).ShouldBe(
        [
            (AbpClaimTypes.ImpersonatorUserId, HostAdmin.ToString()),
            (AbpClaimTypes.ImpersonatorUserName, "admin"),
        ], ignoreOrder: true);
        _currentTenant.Id.ShouldBeNull("the tenant switch ends with the request");
    }

    [Fact]
    public async Task Start_by_an_impersonator_in_a_tenant_adds_its_tenant_id_and_name()
    {
        var home = Guid.NewGuid();
        _tenants.Tenants.Add(FakeTenantRepository.NewTenant(home, "home-tenant"));
        AddUser(Planner, Acme, "acme.planner");
        Session(Principals.For(HostAdmin, home, "tenant.admin"));

        var result = await _page.OnGetAsync(Ticket(Start with { ExpectedTenantId = home }));

        result.ShouldBeOfType<RedirectResult>().Url.ShouldBe(Spa + "/admin");
        _signIn.SignedInClaims.Select(c => (c.Type, c.Value)).ShouldBe(
        [
            (AbpClaimTypes.ImpersonatorUserId, HostAdmin.ToString()),
            (AbpClaimTypes.ImpersonatorUserName, "tenant.admin"),
            (AbpClaimTypes.ImpersonatorTenantId, home.ToString()),
            (AbpClaimTypes.ImpersonatorTenantName, "home-tenant"),
        ], ignoreOrder: true);
        _tenants.TenantAtLookup.ShouldBe([(Guid?)null], "the tenant store is the host's");
    }

    [Fact]
    public async Task Return_signs_back_in_as_the_impersonator_without_impersonator_claims()
    {
        var admin = AddUser(HostAdmin, null, "admin");
        Session(Principals.For(Planner, Acme, "acme.planner",
            new Claim(AbpClaimTypes.ImpersonatorUserId, HostAdmin.ToString()),
            new Claim(AbpClaimTypes.ImpersonatorUserName, "admin")));

        var result = await _page.OnGetAsync(Ticket(new ImpersonationTicket(true, Planner, Acme, HostAdmin, null)));

        result.ShouldBeOfType<RedirectResult>().Url.ShouldBe(Spa + "/admin");
        _signIn.SignedInUser.ShouldBe(admin);
        _signIn.TenantAtSignIn.ShouldBeNull();
        _signIn.SignedInClaims.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_return_ticket_whose_target_is_not_the_cookies_impersonator_is_refused()
    {
        AddUser(HostAdmin, null, "admin");
        var someoneElse = AddUser(Guid.NewGuid(), null, "other.admin");

        /* No impersonator claims at all. */
        Session(Principals.For(Planner, Acme, "acme.planner"));
        ShouldBeRefused(await _page.OnGetAsync(Ticket(new ImpersonationTicket(true, Planner, Acme, HostAdmin, null))));

        /* Impersonated by HostAdmin, ticket says back to somebody else. */
        Session(Principals.For(Planner, Acme, "acme.planner", new Claim(AbpClaimTypes.ImpersonatorUserId, HostAdmin.ToString())));
        ShouldBeRefused(await _page.OnGetAsync(Ticket(new ImpersonationTicket(true, Planner, Acme, someoneElse.Id, null))));

        /* Right user, wrong impersonator tenant. */
        ShouldBeRefused(await _page.OnGetAsync(Ticket(new ImpersonationTicket(true, Planner, Acme, HostAdmin, Guid.NewGuid()))));

        _signIn.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_redirect_target_comes_from_configuration_only()
    {
        var urls = new AppUrlOptions();
        var page = new ImpersonateModel(new DictionaryTicketProtector(_tickets), _signIn, _currentTenant,
            FakeTenantRepository.Create(_currentTenant).Repository, Options.Create(urls), NullLogger<ImpersonateModel>.Instance)
        {
            PageContext = _page.PageContext
        };

        /* Without a configured SPA the page stays on its own site. */
        (await page.OnGetAsync("unknown")).ShouldBeOfType<RedirectResult>().Url.ShouldBe("/admin?impersonation=refused");
    }
}

public class ImpersonationClaimsPrincipalHandlerTests
{
    private static readonly Guid Impersonator = Guid.NewGuid();
    private readonly FakeAuthenticationService _auth = new();

    private AbpOpenIddictClaimsPrincipalHandlerContext Context(ClaimsPrincipal principal, string? grantType, bool withHttpContext = true)
    {
        var http = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddSingleton<IAuthenticationService>(_auth).BuildServiceProvider()
        };
        var services = new ServiceCollection()
            .AddSingleton<IHttpContextAccessor>(new HttpContextAccessor { HttpContext = withHttpContext ? http : null })
            .BuildServiceProvider();
        var request = new OpenIddictRequest { GrantType = grantType };
        return new AbpOpenIddictClaimsPrincipalHandlerContext(services, request, principal);
    }

    private static ClaimsPrincipal TokenPrincipal() => new(new ClaimsIdentity([new Claim(OpenIddictConstants.Claims.Subject, Guid.NewGuid().ToString())], "OpenIddict"));

    private static Claim[] ImpersonatorClaims(bool withTenant = false) => withTenant
        ?
        [
            new(AbpClaimTypes.ImpersonatorUserId, Impersonator.ToString()),
            new(AbpClaimTypes.ImpersonatorUserName, "admin"),
            new(AbpClaimTypes.ImpersonatorTenantId, Guid.Empty.ToString()),
            new(AbpClaimTypes.ImpersonatorTenantName, "home"),
        ]
        :
        [
            new(AbpClaimTypes.ImpersonatorUserId, Impersonator.ToString()),
            new(AbpClaimTypes.ImpersonatorUserName, "admin"),
        ];

    private static IEnumerable<Claim> Impersonation(ClaimsPrincipal principal) =>
        principal.Claims.Where(c => ImpersonationClaims.Types.Contains(c.Type));

    [Fact]
    public async Task At_authorize_it_copies_the_claims_from_the_auth_cookie_into_both_tokens()
    {
        _auth.Principals[IdentityConstants.ApplicationScheme] = Principals.For(Guid.NewGuid(), extra: ImpersonatorClaims(withTenant: true));
        var principal = TokenPrincipal();

        await new ImpersonationClaimsPrincipalHandler().HandleAsync(Context(principal, grantType: null));

        Impersonation(principal).Select(c => c.Type).ShouldBe(ImpersonationClaims.Types, ignoreOrder: true);
        principal.FindFirst(AbpClaimTypes.ImpersonatorUserId)!.Value.ShouldBe(Impersonator.ToString());
        foreach (var claim in Impersonation(principal))
        {
            claim.GetDestinations().ShouldBe([OpenIddictConstants.Destinations.AccessToken, OpenIddictConstants.Destinations.IdentityToken], ignoreOrder: true);
        }

        _auth.Asked.ShouldBe([IdentityConstants.ApplicationScheme], "an authorize request never reads the token being redeemed");
    }

    [Theory]
    [InlineData(OpenIddictConstants.GrantTypes.AuthorizationCode)]
    [InlineData(OpenIddictConstants.GrantTypes.RefreshToken)]
    public async Task At_the_token_endpoint_it_copies_the_claims_from_the_redeemed_token(string grantType)
    {
        _auth.Principals[OpenIddictServerAspNetCoreDefaults.AuthenticationScheme] = Principals.For(Guid.NewGuid(), extra: ImpersonatorClaims());
        var principal = TokenPrincipal();

        await new ImpersonationClaimsPrincipalHandler().HandleAsync(Context(principal, grantType));

        principal.FindFirst(AbpClaimTypes.ImpersonatorUserId)!.Value.ShouldBe(Impersonator.ToString());
        principal.FindFirst(AbpClaimTypes.ImpersonatorUserName)!.Value.ShouldBe("admin");
        foreach (var claim in Impersonation(principal))
        {
            claim.GetDestinations().ShouldBe([OpenIddictConstants.Destinations.AccessToken, OpenIddictConstants.Destinations.IdentityToken], ignoreOrder: true);
        }
    }

    [Theory]
    [InlineData(OpenIddictConstants.GrantTypes.AuthorizationCode)]
    [InlineData(OpenIddictConstants.GrantTypes.RefreshToken)]
    public async Task At_the_token_endpoint_the_redeemed_token_decides_not_a_cookie_sent_along(string grantType)
    {
        /* Design D14 step 4: "from the auth cookie (authorize) or the code/refresh token (token endpoint)". A browser
         * session can change between issuing a token and redeeming it (an impersonation started in another tab; a
         * same-origin client such as /swagger sends the cookie along). The token being redeemed is the subject's own
         * record; the cookie's impersonator must not be grafted onto it. */
        _auth.Principals[IdentityConstants.ApplicationScheme] = Principals.For(Guid.NewGuid(), extra: ImpersonatorClaims());
        _auth.Principals[OpenIddictServerAspNetCoreDefaults.AuthenticationScheme] = Principals.For(Guid.NewGuid());
        var principal = TokenPrincipal();

        await new ImpersonationClaimsPrincipalHandler().HandleAsync(Context(principal, grantType));

        Impersonation(principal).ShouldBeEmpty("the redeemed token carries no impersonator; the cookie's must not be added");
    }

    [Fact]
    public async Task At_the_token_endpoint_the_redeemed_tokens_impersonator_wins_over_the_cookies()
    {
        var tokenImpersonator = Guid.NewGuid();
        _auth.Principals[IdentityConstants.ApplicationScheme] = Principals.For(Guid.NewGuid(), extra: ImpersonatorClaims());
        _auth.Principals[OpenIddictServerAspNetCoreDefaults.AuthenticationScheme] = Principals.For(Guid.NewGuid(), extra:
            [new Claim(AbpClaimTypes.ImpersonatorUserId, tokenImpersonator.ToString())]);
        var principal = TokenPrincipal();

        await new ImpersonationClaimsPrincipalHandler().HandleAsync(Context(principal, OpenIddictConstants.GrantTypes.RefreshToken));

        principal.FindAll(AbpClaimTypes.ImpersonatorUserId).Select(c => c.Value).ShouldBe([tokenImpersonator.ToString()]);
    }

    [Fact]
    public async Task It_does_not_duplicate_claims_the_principal_already_has()
    {
        _auth.Principals[IdentityConstants.ApplicationScheme] = Principals.For(Guid.NewGuid(), extra: ImpersonatorClaims());
        var principal = TokenPrincipal();
        ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim(AbpClaimTypes.ImpersonatorUserId, Impersonator.ToString()));

        await new ImpersonationClaimsPrincipalHandler().HandleAsync(Context(principal, grantType: null));

        principal.FindAll(AbpClaimTypes.ImpersonatorUserId).Count().ShouldBe(1);
        principal.FindAll(AbpClaimTypes.ImpersonatorUserName).Count().ShouldBe(1);
    }

    [Fact]
    public async Task Without_impersonator_claims_anywhere_it_changes_nothing()
    {
        _auth.Principals[IdentityConstants.ApplicationScheme] = Principals.For(Guid.NewGuid());
        _auth.Principals[OpenIddictServerAspNetCoreDefaults.AuthenticationScheme] = Principals.For(Guid.NewGuid());
        var principal = TokenPrincipal();
        var before = principal.Claims.Select(c => (c.Type, c.Value)).ToList();

        await new ImpersonationClaimsPrincipalHandler().HandleAsync(Context(principal, OpenIddictConstants.GrantTypes.AuthorizationCode));
        await new ImpersonationClaimsPrincipalHandler().HandleAsync(Context(principal, grantType: null));

        principal.Claims.Select(c => (c.Type, c.Value)).ShouldBe(before);
    }

    [Fact]
    public async Task Without_an_http_context_it_does_nothing()
    {
        var principal = TokenPrincipal();
        await new ImpersonationClaimsPrincipalHandler().HandleAsync(Context(principal, grantType: null, withHttpContext: false));
        Impersonation(principal).ShouldBeEmpty();
    }

    [Fact]
    public void ImpersonationClaims_lists_abps_four_impersonator_claims_and_reads_them_from_a_principal()
    {
        ImpersonationClaims.Types.ShouldBe(
        [
            AbpClaimTypes.ImpersonatorUserId, AbpClaimTypes.ImpersonatorTenantId, AbpClaimTypes.ImpersonatorUserName,
            AbpClaimTypes.ImpersonatorTenantName
        ]);
        ImpersonationClaims.From(null).ShouldBeEmpty();
        ImpersonationClaims.From(Principals.For(Guid.NewGuid(), extra: ImpersonatorClaims(withTenant: true))).Count.ShouldBe(4);
        ImpersonationClaims.From(Principals.For(Guid.NewGuid(), userName: "x")).ShouldBeEmpty();
    }
}

public class ImpersonationHostWiringTests
{
    /// <summary>Runs the host module's ConfigureImpersonation on a fresh service collection.</summary>
    private static ServiceProvider ConfigureImpersonation()
    {
        var services = new ServiceCollection();
        var context = new ServiceConfigurationContext(services);
        var module = new OpenTmsHttpApiHostModule();
        typeof(AbpModule).GetProperty("ServiceConfigurationContext", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!
            .SetValue(module, context);
        typeof(OpenTmsHttpApiHostModule).GetMethod("ConfigureImpersonation", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(module, [context]);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void The_claims_handler_is_registered_with_abps_openiddict_claims_principal_options()
    {
        var options = ConfigureImpersonation().GetRequiredService<IOptions<AbpOpenIddictClaimsPrincipalOptions>>().Value;
        options.ClaimsPrincipalHandlers.ShouldContain(typeof(ImpersonationClaimsPrincipalHandler));
    }

    [Fact]
    public async Task A_refreshed_cookie_keeps_the_impersonator_claims()
    {
        var options = ConfigureImpersonation().GetRequiredService<IOptions<SecurityStampValidatorOptions>>().Value;
        options.OnRefreshingPrincipal.ShouldNotBeNull();

        var current = Principals.For(Guid.NewGuid(), extra:
        [
            new Claim(AbpClaimTypes.ImpersonatorUserId, Guid.NewGuid().ToString()),
            new Claim(AbpClaimTypes.ImpersonatorUserName, "admin"),
        ]);
        var fresh = Principals.For(Guid.NewGuid(), userName: "acme.planner");
        var context = new SecurityStampRefreshingPrincipalContext { CurrentPrincipal = current, NewPrincipal = fresh };

        await options.OnRefreshingPrincipal!(context);

        fresh.FindFirst(AbpClaimTypes.ImpersonatorUserId)!.Value.ShouldBe(current.FindFirst(AbpClaimTypes.ImpersonatorUserId)!.Value);
        fresh.FindFirst(AbpClaimTypes.ImpersonatorUserName)!.Value.ShouldBe("admin");
        fresh.FindAll(AbpClaimTypes.ImpersonatorUserId).Count().ShouldBe(1);
    }

    [Fact]
    public async Task A_refreshed_cookie_without_impersonation_gets_no_impersonator_claims()
    {
        var options = ConfigureImpersonation().GetRequiredService<IOptions<SecurityStampValidatorOptions>>().Value;
        var fresh = Principals.For(Guid.NewGuid());
        var context = new SecurityStampRefreshingPrincipalContext { CurrentPrincipal = Principals.For(Guid.NewGuid()), NewPrincipal = fresh };

        await options.OnRefreshingPrincipal!(context);

        ImpersonationClaims.From(fresh).ShouldBeEmpty();
    }
}
