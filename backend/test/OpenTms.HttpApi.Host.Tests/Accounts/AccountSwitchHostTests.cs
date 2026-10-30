using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using OpenTms.Impersonation;
using OpenTms.Pages.Account;
using OpenTms.Platform.Accounts;
using OpenTms.Platform.Impersonation;
using OpenTms.SignIn;
using Shouldly;
using Volo.Abp.Account.Web;
using Volo.Abp.Account.Web.Pages.Account;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Identity;
using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.OpenIddict;
using Volo.Abp.Options;
using Volo.Abp.Security.Claims;
using Volo.Abp.SecurityLog;
using Volo.Abp.UI.Navigation.Urls;
using Volo.Abp.Users;
using Xunit;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace OpenTms.Accounts;

// ── Fakes for the auth server's account-switch pieces ────────────────────────────────────────────────────────────────

/// <summary>
/// An <see cref="IdentityUserManager"/> that knows users by (tenant, id) and a lockout set. Created without running a
/// constructor (<see cref="Create"/>): ABP's manager needs twenty services the pages under test never use.
/// </summary>
public class SwitchUserManager : IdentityUserManager
{
    private List<IdentityUser>? _users;
    private HashSet<Guid>? _locked;
    private List<(string Id, Guid? Tenant)>? _lookups;

#pragma warning disable CS8625
    private SwitchUserManager()
        : base(null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null)
    {
    }
#pragma warning restore CS8625

    public static SwitchUserManager Create(ICurrentTenant currentTenant)
    {
        var manager = (SwitchUserManager)RuntimeHelpers.GetUninitializedObject(typeof(SwitchUserManager));
        manager.Tenant = currentTenant;
        return manager;
    }

    public ICurrentTenant Tenant { get; private set; } = default!;

    public List<IdentityUser> AllUsers => _users ??= new();

    public HashSet<Guid> Locked => _locked ??= new();

    public List<(string Id, Guid? Tenant)> Lookups => _lookups ??= new();

    public override Task<IdentityUser?> FindByIdAsync(string userId)
    {
        Lookups.Add((userId, Tenant.Id));
        return Task.FromResult(AllUsers.FirstOrDefault(u => u.Id.ToString() == userId && u.TenantId == Tenant.Id));
    }

    public override Task<IdentityUser?> FindByNameAsync(string userName) => Task.FromResult<IdentityUser?>(null);

    public override Task<bool> IsLockedOutAsync(IdentityUser user) => Task.FromResult(Locked.Contains(user.Id));
}

public sealed class NoClaimsFactory : IUserClaimsPrincipalFactory<IdentityUser>
{
    public Task<ClaimsPrincipal> CreateAsync(IdentityUser user) => throw new NotSupportedException();
}

/// <summary>Records sign-out and sign-in (user, claims, persistence, tenant at the time) instead of writing a cookie.</summary>
public sealed class SwitchSignInManager : SignInManager<IdentityUser>
{
    private readonly ICurrentTenant _currentTenant;

    public SwitchSignInManager(SwitchUserManager users, ICurrentTenant currentTenant)
        : base(users, new HttpContextAccessor(), new NoClaimsFactory(), Microsoft.Extensions.Options.Options.Create(new IdentityOptions()),
            NullLogger<SignInManager<IdentityUser>>.Instance, null!, null!)
    {
        _currentTenant = currentTenant;
    }

    public List<string> Calls { get; } = new();

    public HashSet<Guid> NotAllowed { get; } = new();

    public IdentityUser? SignedInUser { get; private set; }

    public List<Claim> SignedInClaims { get; } = new();

    public Guid? TenantAtSignIn { get; private set; }

    public bool? IsPersistent { get; private set; }

    public override Task<bool> CanSignInAsync(IdentityUser user) => Task.FromResult(!NotAllowed.Contains(user.Id));

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

/// <summary>ABP's dynamic identity options (the pages call <c>IdentityOptions.SetAsync()</c>) without the settings behind them.</summary>
public sealed class StaticIdentityOptions : AbpDynamicOptionsManager<IdentityOptions>
{
    public StaticIdentityOptions()
        : base(new OptionsFactory<IdentityOptions>([], []))
    {
    }

    public int Loaded { get; private set; }

    protected override Task OverrideOptionsAsync(string name, IdentityOptions options)
    {
        Loaded++;
        return Task.CompletedTask;
    }
}

public sealed class RecordingSecurityLogManager : ISecurityLogManager
{
    public List<SecurityLogInfo> Saved { get; } = new();

    public Task SaveAsync(Action<SecurityLogInfo>? saveAction = null)
    {
        var info = new SecurityLogInfo();
        saveAction?.Invoke(info);
        Saved.Add(info);
        return Task.CompletedTask;
    }
}

public sealed class FixedPrincipalAccessor(ClaimsPrincipal principal) : CurrentPrincipalAccessorBase
{
    protected override ClaimsPrincipal GetClaimsPrincipal() => principal;
}

/// <summary>An ABP claims cache that does nothing; created without its constructor, as <see cref="SwitchUserManager"/>.</summary>
public class NoClaimsCache : IdentityDynamicClaimsPrincipalContributorCache
{
#pragma warning disable CS8625
    private NoClaimsCache()
        : base(null, null, null, null, null, null)
    {
    }
#pragma warning restore CS8625

    public static NoClaimsCache Create() => (NoClaimsCache)RuntimeHelpers.GetUninitializedObject(typeof(NoClaimsCache));

    public override Task ClearAsync(Guid userId, Guid? tenantId) => Task.CompletedTask;
}

/// <summary>Answers AuthenticateAsync for the auth cookie with a fixed principal and persistence.</summary>
public sealed class CookieAuthenticationService : IAuthenticationService
{
    public ClaimsPrincipal? Principal { get; set; }

    public bool IsPersistent { get; set; }

    public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) =>
        Task.FromResult(scheme == IdentityConstants.ApplicationScheme && Principal != null
            ? AuthenticateResult.Success(new AuthenticationTicket(Principal, new AuthenticationProperties { IsPersistent = IsPersistent }, scheme))
            : AuthenticateResult.NoResult());

    public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => throw new NotSupportedException();

    public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => throw new NotSupportedException();

    public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties) => throw new NotSupportedException();

    public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => throw new NotSupportedException();
}

/// <summary>Tenants by id with an active flag; every other member is not used.</summary>
public sealed class SwitchTenantStore : ITenantStore
{
    private readonly Dictionary<Guid, TenantConfiguration> _tenants = new();

    public void Add(Guid id, string name, bool isActive = true) => _tenants[id] = new TenantConfiguration(id, name) { IsActive = isActive };

    public void Remove(Guid id) => _tenants.Remove(id);

    public Task<TenantConfiguration?> FindAsync(string normalizedName) => throw new NotSupportedException();

    public Task<TenantConfiguration?> FindAsync(Guid id) => Task.FromResult(_tenants.GetValueOrDefault(id));

    public Task<IReadOnlyList<TenantConfiguration>> GetListAsync(bool includeDetails = false) => throw new NotSupportedException();

    public TenantConfiguration? Find(string normalizedName) => throw new NotSupportedException();

    public TenantConfiguration? Find(Guid id) => _tenants.GetValueOrDefault(id);
}

public static class SwitchFakes
{
    public static IdentitySecurityLogManager SecurityLog(RecordingSecurityLogManager log, SwitchUserManager users)
    {
        var accessor = new FixedPrincipalAccessor(Principals.For(Guid.NewGuid()));
        return new IdentitySecurityLogManager(log, users, accessor, new NoClaimsFactory(), new CurrentUser(accessor));
    }
}

/// <summary>
/// The opentms_password_tenants claim (docs/designs/63-user-menu.md): each entry binds a tenant ("host" for the host) to a
/// user id and a short hash of that user's security stamp. A malformed entry is never a place; one entry per tenant.
/// Risk class: Critical — authentication (a place parsed from garbage, or bound to the wrong user, skips a password).
/// </summary>
public class AccountSwitchClaimsTests
{
    [Fact]
    public void Format_and_parse_round_trip_tenant_user_and_stamp_hash()
    {
        var a = Guid.NewGuid();
        var hostUser = Guid.NewGuid();
        var userA = Guid.NewGuid();
        PasswordPlace[] places = [new(null, hostUser, AccountSwitchClaims.StampHash("s1")), new(a, userA, AccountSwitchClaims.StampHash("s2"))];

        var value = AccountSwitchClaims.Format(places);

        value.ShouldBe($"host|{hostUser:N}|{AccountSwitchClaims.StampHash("s1")} {a:N}|{userA:N}|{AccountSwitchClaims.StampHash("s2")}");
        AccountSwitchClaims.Parse(value).ShouldBe(places);
        AccountSwitchClaims.PasswordTenants.ShouldBe("opentms_password_tenants");
    }

    [Fact]
    public void The_last_entry_per_tenant_wins()
    {
        var a = Guid.NewGuid();
        var first = new PasswordPlace(a, Guid.NewGuid(), "aaaaaaaaaaaaaaaa");
        var host = new PasswordPlace(null, Guid.NewGuid(), "bbbbbbbbbbbbbbbb");
        var second = new PasswordPlace(a, Guid.NewGuid(), "cccccccccccccccc");

        AccountSwitchClaims.Parse(AccountSwitchClaims.Format([first, host, second])).ShouldBe([second, host]);
    }

    [Fact]
    public void The_stamp_hash_is_sixteen_lowercase_hex_characters_of_sha256_and_never_the_stamp()
    {
        var stamp = "ABCDEF-stamp-" + Guid.NewGuid();
        var hash = AccountSwitchClaims.StampHash(stamp);

        hash.Length.ShouldBe(16);
        hash.ShouldMatch("^[0-9a-f]{16}$");
        hash.ShouldBe(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(stamp)))[..16]);
        hash.ShouldBe(AccountSwitchClaims.StampHash(stamp), "deterministic");
        AccountSwitchClaims.StampHash(stamp + "x").ShouldNotBe(hash);
        stamp.ShouldNotContain(hash);
        AccountSwitchClaims.StampHash(null).ShouldBe(AccountSwitchClaims.StampHash(string.Empty));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Nothing_parses_to_no_places(string? value) => AccountSwitchClaims.Parse(value).ShouldBeEmpty();

    [Fact]
    public void A_malformed_entry_is_ignored()
    {
        var a = Guid.NewGuid();
        var user = Guid.NewGuid();
        var good = $"{a:N}|{user:N}|0123456789abcdef";

        AccountSwitchClaims.Parse(string.Join(' ',
            "host",                                  // the pre-review format: no user, no hash
            $"{a:N}",                                // tenant only
            $"{a:N}|{user:N}",                       // no hash
            $"{a:N}|{user:N}|",                      // empty hash
            $"{a:N}|not-a-guid|0123456789abcdef",    // bad user id
            $"tenant|{user:N}|0123456789abcdef",     // bad tenant
            $"{a:N}|{user:N}|0123|extra",            // four parts
            good)).ShouldBe([new PasswordPlace(a, user, "0123456789abcdef")]);
    }

    [Fact]
    public void A_place_holds_only_for_the_same_tenant_the_same_user_and_the_current_stamp()
    {
        var a = Guid.NewGuid();
        var user = Guid.NewGuid();
        var place = new PasswordPlace(a, user, AccountSwitchClaims.StampHash("stamp"));

        place.Holds(a, user, "stamp").ShouldBeTrue();
        place.Holds(a, Guid.NewGuid(), "stamp").ShouldBeFalse("another user in the same tenant");
        place.Holds(Guid.NewGuid(), user, "stamp").ShouldBeFalse("another tenant");
        place.Holds(null, user, "stamp").ShouldBeFalse("the host");
        place.Holds(a, user, "renewed-stamp").ShouldBeFalse("the password changed");
    }

    [Fact]
    public void A_link_holds_only_while_both_stamps_are_unchanged()
    {
        var b = Guid.NewGuid();
        var user = Guid.NewGuid();
        var link = new AccountLink(new LinkedAccount(b, user, "target-stamp"), "own-stamp");

        link.Holds("own-stamp", b, user, "target-stamp").ShouldBeTrue();
        link.Holds("own-renewed", b, user, "target-stamp").ShouldBeFalse("the switch-from account changed its password (B2)");
        link.Holds("own-stamp", b, user, "target-renewed").ShouldBeFalse("the target changed its password");
        link.Holds("own-stamp", b, Guid.NewGuid(), "target-stamp").ShouldBeFalse("another user");
        link.Holds("own-stamp", null, user, "target-stamp").ShouldBeFalse("another place");
    }
}

// ── The ticket protector ─────────────────────────────────────────────────────────────────────────────────────────────

public class DataProtectionAccountSwitchTicketProtectorTests
{
    private static readonly AccountSwitchTicket Ticket = new(Guid.NewGuid(), null, Guid.NewGuid(), Guid.NewGuid(), "host", Guid.NewGuid());

    [Fact]
    public void A_ticket_round_trips_encrypted()
    {
        var protector = new DataProtectionAccountSwitchTicketProtector(new EphemeralDataProtectionProvider());
        var protectedTicket = protector.Protect(Ticket);

        protectedTicket.ShouldNotContain(Ticket.TargetUserId.ToString(), Case.Insensitive);
        protector.Unprotect(protectedTicket).ShouldBe(Ticket);

        var other = Ticket with { ExpectedTenantId = Guid.NewGuid(), TargetTenantId = null, PasswordTenants = "" };
        protector.Unprotect(protector.Protect(other)).ShouldBe(other);
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("%%%not-base64%%%")]
    public void Garbage_is_null(string value) =>
        new DataProtectionAccountSwitchTicketProtector(new EphemeralDataProtectionProvider()).Unprotect(value).ShouldBeNull();

    [Fact]
    public void A_tampered_ticket_or_one_of_another_key_ring_is_null()
    {
        var protector = new DataProtectionAccountSwitchTicketProtector(new EphemeralDataProtectionProvider());
        var bytes = WebEncoders.Base64UrlDecode(protector.Protect(Ticket));
        bytes[^5] ^= 0x5A;
        protector.Unprotect(WebEncoders.Base64UrlEncode(bytes)).ShouldBeNull();

        var theirs = new DataProtectionAccountSwitchTicketProtector(new EphemeralDataProtectionProvider());
        protector.Unprotect(theirs.Protect(Ticket)).ShouldBeNull();
    }

    [Fact]
    public void An_impersonation_ticket_is_never_a_switch_ticket_and_the_reverse()
    {
        /* Same key ring, other purpose: "Log in as" and the account switch cannot redeem each other's tickets. */
        var provider = new EphemeralDataProtectionProvider();
        var switchProtector = new DataProtectionAccountSwitchTicketProtector(provider);
        var impersonationProtector = new DataProtectionImpersonationTicketProtector(provider);

        switchProtector.Unprotect(impersonationProtector.Protect(new ImpersonationTicket(false, Ticket.ExpectedUserId, null, Ticket.TargetUserId, Ticket.TargetTenantId)))
            .ShouldBeNull();
        impersonationProtector.Unprotect(switchProtector.Protect(Ticket)).ShouldBeNull();
    }

    [Fact]
    public void A_ticket_lives_five_minutes()
    {
        var protector = new DataProtectionAccountSwitchTicketProtector(new PassThroughDataProtectionProvider());
        var before = DateTimeOffset.UtcNow;
        var payload = WebEncoders.Base64UrlDecode(protector.Protect(Ticket));
        var expiry = new DateTimeOffset(BinaryPrimitives.ReadInt64BigEndian(payload), TimeSpan.Zero);
        expiry.ShouldBeInRange(before.AddMinutes(5).AddSeconds(-1), DateTimeOffset.UtcNow.AddMinutes(5).AddSeconds(1));

        BinaryPrimitives.WriteInt64BigEndian(payload, DateTimeOffset.UtcNow.AddSeconds(-1).UtcTicks);
        protector.Unprotect(WebEncoders.Base64UrlEncode(payload)).ShouldBeNull("expired");
    }

    [Fact]
    public void A_valid_payload_that_is_not_a_ticket_is_null()
    {
        var provider = new EphemeralDataProtectionProvider();
        var notJson = provider.CreateProtector("OpenTms.AccountSwitch.Ticket.v1").ToTimeLimitedDataProtector().Protect("not json", TimeSpan.FromMinutes(5));
        new DataProtectionAccountSwitchTicketProtector(provider).Unprotect(notJson).ShouldBeNull();
    }
}

// ── The SwitchAccount page ───────────────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The auth server's half of the user menu's account switch (docs/designs/63-user-menu.md): the page redeems a ticket only
/// for the session it names, never while impersonating, re-reads the target in its tenant, signs it in with the password
/// places and the session's persistence, and logs the sign-in. Plain unit tests with every port faked.
/// Risk class: Critical — authentication (a ticket redeemed for the wrong session signs a user in as someone else).
/// </summary>
public class SwitchAccountPageTests
{
    private const string Spa = "http://localhost:5173";
    private static readonly Guid Acme = Guid.NewGuid();
    private static readonly Guid Beta = Guid.NewGuid();
    private static readonly Guid JanInAcme = Guid.NewGuid();
    private static readonly Guid JanInBeta = Guid.NewGuid();

    private readonly ICurrentTenant _currentTenant = new CurrentTenant(AsyncLocalCurrentTenantAccessor.Instance);
    private readonly CookieAuthenticationService _auth = new();
    private readonly Dictionary<string, AccountSwitchTicket> _tickets = new();
    private readonly SwitchUserManager _users;
    private readonly SwitchSignInManager _signIn;
    private readonly RecordingSecurityLogManager _securityLog = new();
    private readonly StaticIdentityOptions _identityOptions = new();
    private readonly SwitchTenantStore _tenants = new();
    private readonly IDistributedCache _cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
    private readonly SwitchAccountModel _page;

    private sealed class DictionaryTicketProtector(Dictionary<string, AccountSwitchTicket> tickets) : IAccountSwitchTicketProtector
    {
        public string Protect(AccountSwitchTicket ticket) => throw new NotSupportedException();

        public AccountSwitchTicket? Unprotect(string protectedTicket) => tickets.GetValueOrDefault(protectedTicket);
    }

    public SwitchAccountPageTests()
    {
        _users = SwitchUserManager.Create(_currentTenant);
        _signIn = new SwitchSignInManager(_users, _currentTenant);
        var urls = new AppUrlOptions();
        urls.RedirectAllowedUrls.AddRange([Spa]);

        _page = new SwitchAccountModel(new DictionaryTicketProtector(_tickets), _signIn, _currentTenant, _identityOptions,
            SwitchFakes.SecurityLog(_securityLog, _users), _tenants, _cache, Options.Create(urls), NullLogger<SwitchAccountModel>.Instance);
        _tenants.Add(Acme, "acme");
        _tenants.Add(Beta, "beta");
        /* The session's own account: the page compares its address with the target's. */
        AddUser(JanInAcme, Acme);

        var http = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddSingleton<IAuthenticationService>(_auth).BuildServiceProvider()
        };
        http.Request.QueryString = new QueryString("?returnUrl=https%3A%2F%2Fevil.example.test%2F");
        _page.PageContext = new PageContext { HttpContext = http };
    }

    private IdentityUser AddUser(Guid id, Guid? tenantId, bool active = true, string email = "jan@example.test")
    {
        var user = new IdentityUser(id, "u-" + id.ToString("N")[..6], email, tenantId);
        user.SetIsActive(active);
        _users.AllUsers.Add(user);
        return user;
    }

    /// <summary>A fresh ticket (a new nonce each time, as the switch issues them) unless <paramref name="sameNonce"/>.</summary>
    private string Ticket(AccountSwitchTicket ticket, bool sameNonce = false)
    {
        var key = "t" + _tickets.Count;
        _tickets[key] = sameNonce ? ticket : ticket with { Nonce = Guid.NewGuid() };
        return key;
    }

    private static readonly AccountSwitchTicket ToBeta = new(JanInAcme, Acme, JanInBeta, Beta, $"{Acme:N}|{JanInAcme:N}|0123456789abcdef", Guid.NewGuid());

    private void ShouldBeRefused(IActionResult result)
    {
        result.ShouldBeOfType<RedirectResult>().Url.ShouldBe(Spa + "/admin?impersonation=refused");
        _signIn.Calls.ShouldBeEmpty();
        _securityLog.Saved.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown")]
    public async Task No_or_an_invalid_ticket_is_refused(string? ticket)
    {
        AddUser(JanInBeta, Beta);
        _auth.Principal = Principals.For(JanInAcme, Acme);
        ShouldBeRefused(await _page.OnGetAsync(ticket));
    }

    [Fact]
    public async Task Without_a_session_the_browser_logs_in_first()
    {
        AddUser(JanInBeta, Beta);
        (await _page.OnGetAsync(Ticket(ToBeta))).ShouldBeOfType<ChallengeResult>().AuthenticationSchemes.ShouldBe([IdentityConstants.ApplicationScheme]);
        _signIn.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_ticket_issued_to_another_session_is_refused()
    {
        AddUser(JanInBeta, Beta);

        _auth.Principal = Principals.For(Guid.NewGuid(), Acme);
        ShouldBeRefused(await _page.OnGetAsync(Ticket(ToBeta)));

        /* The same user id in another tenant (or the host) is another session. */
        _auth.Principal = Principals.For(JanInAcme, Guid.NewGuid());
        ShouldBeRefused(await _page.OnGetAsync(Ticket(ToBeta)));
        _auth.Principal = Principals.For(JanInAcme);
        ShouldBeRefused(await _page.OnGetAsync(Ticket(ToBeta)));
    }

    [Fact]
    public async Task An_impersonating_session_is_refused()
    {
        AddUser(JanInBeta, Beta);
        _auth.Principal = Principals.For(JanInAcme, Acme, extra: new Claim(AbpClaimTypes.ImpersonatorUserId, Guid.NewGuid().ToString()));

        ShouldBeRefused(await _page.OnGetAsync(Ticket(ToBeta)));
    }

    [Fact]
    public async Task A_missing_inactive_locked_out_or_not_allowed_target_is_refused()
    {
        _auth.Principal = Principals.For(JanInAcme, Acme);
        ShouldBeRefused(await _page.OnGetAsync(Ticket(ToBeta)));

        var target = AddUser(JanInBeta, Beta, active: false);
        ShouldBeRefused(await _page.OnGetAsync(Ticket(ToBeta)));

        target.SetIsActive(true);
        _users.Locked.Add(JanInBeta);
        ShouldBeRefused(await _page.OnGetAsync(Ticket(ToBeta)));

        _users.Locked.Clear();
        _signIn.NotAllowed.Add(JanInBeta);
        ShouldBeRefused(await _page.OnGetAsync(Ticket(ToBeta)));
    }

    [Fact]
    public async Task A_target_id_that_exists_only_in_another_tenant_is_refused()
    {
        AddUser(JanInBeta, Guid.NewGuid());
        _auth.Principal = Principals.For(JanInAcme, Acme);

        ShouldBeRefused(await _page.OnGetAsync(Ticket(ToBeta)));
        _users.Lookups.ShouldContain((JanInBeta.ToString(), (Guid?)Beta));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_switch_signs_in_the_target_in_its_tenant_with_the_tickets_password_places_and_keeps_persistence(bool persistent)
    {
        var target = AddUser(JanInBeta, Beta);
        _auth.Principal = Principals.For(JanInAcme, Acme, extra: new Claim(AccountSwitchClaims.PasswordTenants, "stale-old-value"));
        _auth.IsPersistent = persistent;

        var result = await _page.OnGetAsync(Ticket(ToBeta));

        result.ShouldBeOfType<RedirectResult>().Url.ShouldBe(Spa + "/admin");
        _signIn.Calls.ShouldBe(["SignOut", "SignIn"]);
        _signIn.SignedInUser.ShouldBe(target);
        _signIn.TenantAtSignIn.ShouldBe(Beta);
        _signIn.IsPersistent.ShouldBe(persistent);
        _signIn.SignedInClaims.Select(c => (c.Type, c.Value)).ShouldBe([(AccountSwitchClaims.PasswordTenants, ToBeta.PasswordTenants)],
            "the ticket's places, not the old cookie's; and no impersonator claims");
        _identityOptions.Loaded.ShouldBe(1, "the target tenant's identity settings are loaded");
        _securityLog.Saved.ShouldHaveSingleItem().Action.ShouldBe(IdentitySecurityLogActionConsts.LoginSucceeded);
        _currentTenant.Id.ShouldBeNull("the tenant switch ends with the request");
    }

    [Fact]
    public async Task The_switch_to_the_host_signs_in_without_a_tenant()
    {
        var hostUser = AddUser(JanInBeta, null);
        _auth.Principal = Principals.For(JanInAcme, Acme);

        var result = await _page.OnGetAsync(Ticket(new AccountSwitchTicket(JanInAcme, Acme, JanInBeta, null, "", Guid.Empty)));

        result.ShouldBeOfType<RedirectResult>().Url.ShouldBe(Spa + "/admin");
        _signIn.SignedInUser.ShouldBe(hostUser);
        _signIn.TenantAtSignIn.ShouldBeNull();
        _signIn.SignedInClaims.ShouldHaveSingleItem().Value.ShouldBe("");
    }
    [Fact]
    public async Task A_ticket_is_redeemed_once()
    {
        AddUser(JanInBeta, Beta);
        _auth.Principal = Principals.For(JanInAcme, Acme);
        var ticket = Ticket(ToBeta);

        (await _page.OnGetAsync(ticket)).ShouldBeOfType<RedirectResult>().Url.ShouldBe(Spa + "/admin");
        _signIn.Calls.Clear();
        _securityLog.Saved.Clear();

        ShouldBeRefused(await _page.OnGetAsync(ticket));
        /* Another ticket string carrying the same nonce is the same ticket. */
        ShouldBeRefused(await _page.OnGetAsync(Ticket(_tickets[ticket], sameNonce: true)));
    }

    [Fact]
    public async Task A_target_tenant_that_is_inactive_or_missing_is_refused()
    {
        AddUser(JanInBeta, Beta);
        _auth.Principal = Principals.For(JanInAcme, Acme);

        _tenants.Add(Beta, "beta", isActive: false);
        ShouldBeRefused(await _page.OnGetAsync(Ticket(ToBeta)));

        _tenants.Remove(Beta);
        ShouldBeRefused(await _page.OnGetAsync(Ticket(ToBeta)));
    }

    [Fact]
    public async Task A_target_of_another_address_is_refused()
    {
        // The address may have changed in the five minutes since the switch checked it, or the ticket names someone else.
        AddUser(JanInBeta, Beta, email: "victor@example.test");
        _auth.Principal = Principals.For(JanInAcme, Acme);

        ShouldBeRefused(await _page.OnGetAsync(Ticket(ToBeta)));
    }

    [Fact]
    public async Task A_session_user_that_no_longer_exists_is_refused()
    {
        AddUser(JanInBeta, Beta);
        _users.AllUsers.RemoveAll(u => u.Id == JanInAcme);
        _auth.Principal = Principals.For(JanInAcme, Acme);

        ShouldBeRefused(await _page.OnGetAsync(Ticket(ToBeta)));
    }

    [Fact]
    public async Task A_target_that_must_change_its_password_is_refused()
    {
        AddUser(JanInBeta, Beta).SetShouldChangePasswordOnNextLogin(true);
        _auth.Principal = Principals.For(JanInAcme, Acme);

        ShouldBeRefused(await _page.OnGetAsync(Ticket(ToBeta)));
    }
}

/// <summary>
/// The sign-in page's last step (docs/designs/63-user-menu.md): the session gets the opentms_password_tenants claim with
/// every account the password was valid in, each as tenant|userId|hash of that user's current stamp, read in its own
/// tenant. The page's private sign-in step is invoked directly with every port faked; the steps before it are covered by
/// SignInHostTests. Risk class: Critical — authentication.
/// </summary>
public class LoginPagePasswordPlacesTests
{
    private static readonly Guid Acme = Guid.NewGuid();
    private static readonly Guid Beta = Guid.NewGuid();
    private static readonly Guid UserInAcme = Guid.NewGuid();
    private static readonly Guid UserInBeta = Guid.NewGuid();
    private static readonly Guid UserInHost = Guid.NewGuid();

    private readonly ICurrentTenant _currentTenant = new CurrentTenant(AsyncLocalCurrentTenantAccessor.Instance);
    private readonly SwitchUserManager _users;
    private readonly SwitchSignInManager _signIn;
    private readonly RecordingSecurityLogManager _securityLog = new();
    private readonly OpenTmsLoginModel _page;

    public LoginPagePasswordPlacesTests()
    {
        _users = SwitchUserManager.Create(_currentTenant);
        _signIn = new SwitchSignInManager(_users, _currentTenant);
        var securityLog = SwitchFakes.SecurityLog(_securityLog, _users);

        var services = new ServiceCollection()
            .AddSingleton(_currentTenant)
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddSingleton<IdentityUserManager>(_users)
            .AddSingleton<SignInManager<IdentityUser>>(_signIn)
            .AddSingleton(securityLog)
            .BuildServiceProvider();

        _page = new OpenTmsLoginModel(null!, Options.Create(new AbpAccountOptions()), new StaticIdentityOptions(), NoClaimsCache.Create(), null!, null!,
            null!, null!, null!, null!, null!, null!, null!, Options.Create(new AppUrlOptions()))
        {
            LazyServiceProvider = new AbpLazyServiceProvider(services),
            PageContext = new PageContext { HttpContext = new DefaultHttpContext { RequestServices = services } }
        };
        foreach (var (name, value) in new (string, object)[] { ("SignInManager", _signIn), ("UserManager", _users), ("IdentitySecurityLogManager", securityLog) })
        {
            var property = typeof(AccountPageModel).GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
            if (property.CanWrite)
            {
                property.SetValue(_page, value);
            }
        }
    }

    private IdentityUser AddUser(Guid id, Guid? tenantId)
    {
        var user = new IdentityUser(id, "jan-" + id.ToString("N")[..6], "jan@example.test", tenantId);
        _users.AllUsers.Add(user);
        return user;
    }

    private Task<IActionResult> SignInAsync(SignInChoice choice, params SignInChoice[] places)
    {
        var method = typeof(OpenTmsLoginModel).GetMethod("SignInAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (Task<IActionResult>)method.Invoke(_page, [choice, true, places])!;
    }

    [Fact]
    public async Task The_session_gets_every_place_bound_to_its_user_and_stamp_hash()
    {
        var inAcme = AddUser(UserInAcme, Acme);
        var inBeta = AddUser(UserInBeta, Beta);
        var inHost = AddUser(UserInHost, null);
        var acme = new SignInChoice(Acme, "acme", UserInAcme);

        await SignInAsync(acme, new SignInChoice(null, null, UserInHost), acme, new SignInChoice(Beta, "beta", UserInBeta));

        _signIn.SignedInUser.ShouldBe(inAcme);
        _signIn.TenantAtSignIn.ShouldBe(Acme);
        _signIn.IsPersistent.ShouldBe(true);
        var claim = _signIn.SignedInClaims.ShouldHaveSingleItem();
        claim.Type.ShouldBe(AccountSwitchClaims.PasswordTenants);
        AccountSwitchClaims.Parse(claim.Value).ShouldBe(
        [
            new PasswordPlace(null, UserInHost, AccountSwitchClaims.StampHash(inHost.SecurityStamp)),
            new PasswordPlace(Acme, UserInAcme, AccountSwitchClaims.StampHash(inAcme.SecurityStamp)),
            new PasswordPlace(Beta, UserInBeta, AccountSwitchClaims.StampHash(inBeta.SecurityStamp)),
        ]);
        claim.Value.ShouldNotContain(inAcme.SecurityStamp, Case.Insensitive, "a hash, never the stamp");
        _users.Lookups.ShouldContain((UserInBeta.ToString(), (Guid?)Beta), "each place is read in its own tenant");
        _users.Lookups.ShouldContain((UserInHost.ToString(), (Guid?)null));
    }

    [Fact]
    public async Task A_place_whose_user_cannot_be_read_is_left_out()
    {
        var inAcme = AddUser(UserInAcme, Acme);
        var acme = new SignInChoice(Acme, "acme", UserInAcme);

        await SignInAsync(acme, acme, new SignInChoice(Beta, "beta", Guid.NewGuid()));

        _signIn.SignedInClaims.ShouldHaveSingleItem().Value.ShouldBe($"{Acme:N}|{UserInAcme:N}|{AccountSwitchClaims.StampHash(inAcme.SecurityStamp)}");
    }
}

// ── Carrying the claim into tokens and through cookie refresh ────────────────────────────────────────────────────────

public class PasswordPlacesClaimCarryTests
{
    private readonly FakeAuthenticationService _auth = new();

    private AbpOpenIddictClaimsPrincipalHandlerContext Context(ClaimsPrincipal principal, string? grantType)
    {
        var http = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddSingleton<IAuthenticationService>(_auth).BuildServiceProvider()
        };
        var services = new ServiceCollection().AddSingleton<IHttpContextAccessor>(new HttpContextAccessor { HttpContext = http }).BuildServiceProvider();
        return new AbpOpenIddictClaimsPrincipalHandlerContext(services, new OpenIddictRequest { GrantType = grantType }, principal);
    }

    private static ClaimsPrincipal TokenPrincipal() => new(new ClaimsIdentity([new Claim(OpenIddictConstants.Claims.Subject, Guid.NewGuid().ToString())], "OpenIddict"));

    [Fact]
    public void The_carried_claims_are_the_four_impersonator_claims_and_the_password_places()
    {
        CarriedSessionClaims.Types.ShouldBe([.. ImpersonationClaims.Types, AccountSwitchClaims.PasswordTenants]);
        CarriedSessionClaims.From(null).ShouldBeEmpty();
        CarriedSessionClaims.From(Principals.For(Guid.NewGuid(), extra: new Claim(AccountSwitchClaims.PasswordTenants, "host")))
            .ShouldHaveSingleItem().Value.ShouldBe("host");
    }

    [Fact]
    public async Task At_authorize_the_password_places_go_from_the_cookie_into_both_tokens()
    {
        _auth.Principals[IdentityConstants.ApplicationScheme] = Principals.For(Guid.NewGuid(), extra: new Claim(AccountSwitchClaims.PasswordTenants, "host"));
        var principal = TokenPrincipal();

        await new ImpersonationClaimsPrincipalHandler().HandleAsync(Context(principal, grantType: null));

        var claim = principal.FindAll(AccountSwitchClaims.PasswordTenants).ShouldHaveSingleItem();
        claim.Value.ShouldBe("host");
        claim.GetDestinations().ShouldBe([OpenIddictConstants.Destinations.AccessToken, OpenIddictConstants.Destinations.IdentityToken], ignoreOrder: true);
    }

    [Theory]
    [InlineData(OpenIddictConstants.GrantTypes.AuthorizationCode)]
    [InlineData(OpenIddictConstants.GrantTypes.RefreshToken)]
    public async Task At_the_token_endpoint_only_the_redeemed_tokens_places_count(string grantType)
    {
        /* A cookie sent along (another tab switched meanwhile) must not add places to a token it did not issue. */
        _auth.Principals[IdentityConstants.ApplicationScheme] = Principals.For(Guid.NewGuid(), extra: new Claim(AccountSwitchClaims.PasswordTenants, "host"));
        _auth.Principals[OpenIddictServerAspNetCoreDefaults.AuthenticationScheme] = Principals.For(Guid.NewGuid());
        var principal = TokenPrincipal();

        await new ImpersonationClaimsPrincipalHandler().HandleAsync(Context(principal, grantType));
        principal.FindAll(AccountSwitchClaims.PasswordTenants).ShouldBeEmpty();

        var tenant = Guid.NewGuid();
        _auth.Principals[OpenIddictServerAspNetCoreDefaults.AuthenticationScheme] = Principals.For(Guid.NewGuid(), extra: new Claim(AccountSwitchClaims.PasswordTenants, $"{tenant:N}"));
        await new ImpersonationClaimsPrincipalHandler().HandleAsync(Context(principal, grantType));
        principal.FindAll(AccountSwitchClaims.PasswordTenants).ShouldHaveSingleItem().Value.ShouldBe($"{tenant:N}");
    }

    [Fact]
    public async Task A_refreshed_cookie_keeps_the_password_places_once()
    {
        var services = new ServiceCollection();
        var context = new ServiceConfigurationContext(services);
        var module = new OpenTmsHttpApiHostModule();
        typeof(AbpModule).GetProperty("ServiceConfigurationContext", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.SetValue(module, context);
        typeof(OpenTmsHttpApiHostModule).GetMethod("ConfigureImpersonation", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(module, [context]);
        var options = services.BuildServiceProvider().GetRequiredService<IOptions<SecurityStampValidatorOptions>>().Value;

        var current = Principals.For(Guid.NewGuid(), extra: new Claim(AccountSwitchClaims.PasswordTenants, "host"));
        var fresh = Principals.For(Guid.NewGuid());
        await options.OnRefreshingPrincipal!(new SecurityStampRefreshingPrincipalContext { CurrentPrincipal = current, NewPrincipal = fresh });

        fresh.FindAll(AccountSwitchClaims.PasswordTenants).ShouldHaveSingleItem().Value.ShouldBe("host");
    }
}
