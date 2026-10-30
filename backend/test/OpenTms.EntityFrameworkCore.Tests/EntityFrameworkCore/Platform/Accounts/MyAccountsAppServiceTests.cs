using System;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.EntityFrameworkCore.Platform.Impersonation;
using OpenTms.Platform;
using OpenTms.Platform.Accounts;
using OpenTms.Platform.SignIn;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Security.Claims;
using Volo.Abp.TenantManagement;
using Xunit;

namespace OpenTms.EntityFrameworkCore.Platform.Accounts;

/// <summary>
/// The user menu's other accounts and the switch between them (docs/designs/63-user-menu.md), the application service
/// through the real container on SQLite: which accounts are listed, every refusal of a switch, the password, the
/// session's password places and the "Remember password" links (both directions, stale after a password change,
/// forgotten both ways, removed with a deleted user or tenant), and negative tenant isolation: another e-mail address's
/// accounts are never listed or entered. The database-per-tenant crossing is covered by AccountSwitchPostgreSqlTests.
/// Risk class: Critical — authentication and cross-tenant access (a wrong switch signs a user into another person's
/// account or a tenant that is not theirs).
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class MyAccountsAppServiceTests : OpenTmsTestBase<AccountsTestModule>
{
    private const string Password = "1q2w3E*";
    private const string OtherPassword = "4r5t6Y&";

    private readonly IMyAccountsAppService _service;
    private readonly ICurrentTenant _currentTenant;
    private readonly ICurrentPrincipalAccessor _principal;
    private readonly RecordingAccountSwitchTicketProtector _tickets;
    private readonly IUserAccountLinkStore _links;
    private readonly IRepository<UserAccountLink, Guid> _linkRows;
    private readonly RecordingLoggerProvider _logs;

    public MyAccountsAppServiceTests()
    {
        _service = GetRequiredService<IMyAccountsAppService>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
        _principal = GetRequiredService<ICurrentPrincipalAccessor>();
        _tickets = GetRequiredService<RecordingAccountSwitchTicketProtector>();
        _links = GetRequiredService<IUserAccountLinkStore>();
        _linkRows = GetRequiredService<IRepository<UserAccountLink, Guid>>();
        _logs = GetRequiredService<RecordingLoggerProvider>();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private Task<Guid> CreateTenantAsync(string name) =>
        WithUnitOfWorkAsync(async () =>
        {
            var tenant = await GetRequiredService<ITenantManager>().CreateAsync(name);
            await GetRequiredService<ITenantRepository>().InsertAsync(tenant, autoSave: true);
            return tenant.Id;
        });

    private Task<Guid> CreateUserAsync(Guid? tenantId, string email, string password = Password, bool active = true, bool mustChangePassword = false) =>
        WithUnitOfWorkAsync(async () =>
        {
            using (_currentTenant.Change(tenantId))
            {
                var user = new IdentityUser(Guid.NewGuid(), "u-" + Guid.NewGuid().ToString("N")[..10], email, tenantId);
                user.SetIsActive(active);
                user.SetShouldChangePasswordOnNextLogin(mustChangePassword);
                (await GetRequiredService<IdentityUserManager>().CreateAsync(user, password)).Succeeded.ShouldBeTrue();
                return user.Id;
            }
        });

    private Task<IdentityUser> ReadUserAsync(Guid? tenantId, Guid userId) =>
        WithUnitOfWorkAsync(async () =>
        {
            using (_currentTenant.Change(tenantId))
            {
                return await GetRequiredService<IdentityUserManager>().GetByIdAsync(userId);
            }
        });

    private Task ChangePasswordAsync(Guid? tenantId, Guid userId, string newPassword) =>
        WithUnitOfWorkAsync(async () =>
        {
            using (_currentTenant.Change(tenantId))
            {
                var manager = GetRequiredService<IdentityUserManager>();
                var user = await manager.GetByIdAsync(userId);
                (await manager.RemovePasswordAsync(user)).Succeeded.ShouldBeTrue();
                (await manager.AddPasswordAsync(user, newPassword)).Succeeded.ShouldBeTrue();
            }
        });

    /// <summary>Runs <paramref name="action"/> as the given user, signed in to their tenant.</summary>
    private async Task<T> AsAsync<T>(Guid userId, Guid? tenantId, Func<Task<T>> action, string? passwordTenants = null, Guid? impersonatorId = null)
    {
        using (_principal.Change(AccountPrincipals.For(userId, tenantId, passwordTenants, impersonatorId)))
        using (_currentTenant.Change(tenantId))
        {
            return await action();
        }
    }

    private Task AsAsync(Guid userId, Guid? tenantId, Func<Task> action, string? passwordTenants = null, Guid? impersonatorId = null) =>
        AsAsync<bool>(userId, tenantId, async () =>
        {
            await action();
            return true;
        }, passwordTenants, impersonatorId);

    private static async Task ShouldFailAsync(Func<Task> action, string code)
    {
        var ex = await Should.ThrowAsync<BusinessException>(action);
        ex.Code.ShouldBe(code);
    }

    private static string Email() => $"jan-{Guid.NewGuid().ToString("N")[..12]}@example.test";

    private static SwitchAccountDto To(Guid? tenantId, string? password = null, bool remember = false) =>
        new() { TenantId = tenantId, Password = password, RememberPassword = remember };

    private static string N(Guid id) => id.ToString("N");

    /// <summary>The password-places claim value for the given accounts, each with the hash of its current security stamp.</summary>
    private async Task<string> PlacesAsync(params (Guid? Tenant, Guid User)[] accounts)
    {
        var places = new System.Collections.Generic.List<PasswordPlace>();
        foreach (var (tenant, user) in accounts)
        {
            places.Add(new PasswordPlace(tenant, user, AccountSwitchClaims.StampHash((await ReadUserAsync(tenant, user)).SecurityStamp)));
        }

        return AccountSwitchClaims.Format(places);
    }

    // ── The list ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_list_holds_the_addresss_accounts_in_the_host_and_other_active_tenants_host_first_and_not_the_current_tenant()
    {
        var email = Email();
        var acme = await CreateTenantAsync("acme");
        var zulu = await CreateTenantAsync("zulu-transport");
        var beta = await CreateTenantAsync("beta-logistics");
        var dormant = await CreateTenantAsync("dormant");
        var current = await CreateUserAsync(acme, email);
        await CreateUserAsync(null, email);
        await CreateUserAsync(zulu, email);
        await CreateUserAsync(beta, email);
        await CreateUserAsync(dormant, email);
        GetRequiredService<InactiveTenants>().Deactivate(dormant);

        var list = await AsAsync(current, acme, () => _service.GetListAsync());

        list.Select(a => (a.TenantId, a.TenantName)).ShouldBe(
        [
            ((Guid?)null, (string?)null),
            (beta, "beta-logistics"),
            (zulu, "zulu-transport"),
        ]);
        list.ShouldAllBe(a => a.NeedsPassword && !a.IsRemembered);
    }

    [Fact]
    public async Task A_lookup_row_of_a_deleted_tenant_is_not_listed()
    {
        var email = Email();
        var acme = await CreateTenantAsync("acme");
        var current = await CreateUserAsync(acme, email);
        await GetRequiredService<IUserSignInLookupStore>().SyncUserAsync(Guid.NewGuid(), Guid.NewGuid(), UserSignInLookupHash.Of(email.ToUpperInvariant()));

        (await AsAsync(current, acme, () => _service.GetListAsync())).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_sessions_password_places_need_no_password_only_for_the_same_user()
    {
        var email = Email();
        var acme = await CreateTenantAsync("acme");
        var beta = await CreateTenantAsync("beta");
        var gamma = await CreateTenantAsync("gamma");
        var current = await CreateUserAsync(acme, email);
        var inHost = await CreateUserAsync(null, email);
        var inBeta = await CreateUserAsync(beta, email);
        await CreateUserAsync(gamma, email);

        var claim = await PlacesAsync((null, inHost), (beta, inBeta), (acme, current));
        /* A place for gamma with another user id: the list shows gamma as still needing a password. */
        claim = AccountSwitchClaims.Format([.. AccountSwitchClaims.Parse(claim), new PasswordPlace(gamma, Guid.NewGuid(), "0123456789abcdef")]);

        var list = await AsAsync(current, acme, () => _service.GetListAsync(), passwordTenants: claim);

        list.Single(a => a.TenantId == null).NeedsPassword.ShouldBeFalse();
        list.Single(a => a.TenantId == beta).NeedsPassword.ShouldBeFalse();
        list.Single(a => a.TenantId == gamma).NeedsPassword.ShouldBeTrue();
        list.ShouldAllBe(a => !a.IsRemembered);
    }

    [Fact]
    public async Task While_impersonating_the_list_is_empty_and_a_switch_is_refused()
    {
        var email = Email();
        var acme = await CreateTenantAsync("acme");
        var beta = await CreateTenantAsync("beta");
        var current = await CreateUserAsync(acme, email);
        await CreateUserAsync(beta, email);
        var hostAdmin = Guid.NewGuid();

        (await AsAsync(current, acme, () => _service.GetListAsync(), impersonatorId: hostAdmin)).ShouldBeEmpty();
        await ShouldFailAsync(
            () => AsAsync(current, acme, () => _service.SwitchAsync(To(beta, Password, remember: true)), passwordTenants: N(beta), impersonatorId: hostAdmin),
            PlatformErrorCodes.AccountSwitchWhileImpersonating);

        _tickets.Protected.ShouldBeEmpty();
        (await _links.GetLinkedAsync(acme, current)).ShouldBeEmpty();
    }

    // ── Negative tenant isolation: another address ────────────────────────────

    [Fact]
    public async Task Another_addresss_accounts_are_never_listed()
    {
        // constitution rule 1: Jan sees his own accounts only, never Piet's tenants.
        var jan = Email();
        var piet = Email();
        var acme = await CreateTenantAsync("acme");
        var beta = await CreateTenantAsync("beta");
        var janInAcme = await CreateUserAsync(acme, jan);
        var pietInAcme = await CreateUserAsync(acme, piet);
        await CreateUserAsync(beta, piet);
        await CreateUserAsync(null, piet);

        (await AsAsync(janInAcme, acme, () => _service.GetListAsync())).ShouldBeEmpty();
        (await AsAsync(pietInAcme, acme, () => _service.GetListAsync())).Select(a => a.TenantId).ShouldBe([null, beta], ignoreOrder: true);
    }

    [Fact]
    public async Task A_switch_to_a_tenant_where_the_address_has_no_account_is_refused_even_with_the_other_users_password()
    {
        // constitution rule 1: beta holds Piet only; Jan cannot reach Piet's account by naming beta, even with Piet's password.
        var jan = Email();
        var acme = await CreateTenantAsync("acme");
        var beta = await CreateTenantAsync("beta");
        var janInAcme = await CreateUserAsync(acme, jan);
        var pietInBeta = await CreateUserAsync(beta, Email(), OtherPassword);

        await ShouldFailAsync(() => AsAsync(janInAcme, acme, () => _service.SwitchAsync(To(beta, OtherPassword))), PlatformErrorCodes.AccountNotAvailable);
        await ShouldFailAsync(() => AsAsync(janInAcme, acme, () => _service.SwitchAsync(To(beta)), passwordTenants: N(beta)), PlatformErrorCodes.AccountNotAvailable);
        await ShouldFailAsync(() => AsAsync(janInAcme, acme, () => _service.SwitchAsync(To(null, OtherPassword))), PlatformErrorCodes.AccountNotAvailable);
        await ShouldFailAsync(() => AsAsync(janInAcme, acme, () => _service.SwitchAsync(To(Guid.NewGuid(), OtherPassword))), PlatformErrorCodes.AccountNotAvailable);

        _tickets.Protected.ShouldBeEmpty();
        (await ReadUserAsync(beta, pietInBeta)).AccessFailedCount.ShouldBe(0, "Piet's account was never touched");
    }

    [Fact]
    public async Task A_lookup_row_that_names_another_addresss_user_does_not_open_that_account()
    {
        /* Defence in depth: should the host's lookup ever name Piet's account under Jan's address (a stale row after an
         * address change), the switch reads the account in its tenant and compares the address itself. */
        var jan = Email();
        var acme = await CreateTenantAsync("acme");
        var beta = await CreateTenantAsync("beta");
        var janInAcme = await CreateUserAsync(acme, jan);
        var pietInBeta = await CreateUserAsync(beta, Email(), Password);
        await GetRequiredService<IUserSignInLookupStore>().SyncUserAsync(beta, pietInBeta, UserSignInLookupHash.Of(jan.ToUpperInvariant()));

        await ShouldFailAsync(() => AsAsync(janInAcme, acme, () => _service.SwitchAsync(To(beta, Password))), PlatformErrorCodes.AccountNotAvailable);
        await ShouldFailAsync(() => AsAsync(janInAcme, acme, () => _service.SwitchAsync(To(beta)), passwordTenants: N(beta)), PlatformErrorCodes.AccountNotAvailable);

        _tickets.Protected.ShouldBeEmpty();
        (await ReadUserAsync(beta, pietInBeta)).AccessFailedCount.ShouldBe(0);
    }

    [Fact]
    public async Task A_switch_to_the_current_tenant_is_refused()
    {
        var email = Email();
        var acme = await CreateTenantAsync("acme");
        var current = await CreateUserAsync(acme, email);

        await ShouldFailAsync(() => AsAsync(current, acme, () => _service.SwitchAsync(To(acme, Password))), PlatformErrorCodes.AccountNotAvailable);
        _tickets.Protected.ShouldBeEmpty();
    }

    // ── Refusals of the target ────────────────────────────────────────────────

    [Fact]
    public async Task A_switch_to_an_inactive_tenant_an_inactive_user_or_one_who_must_change_the_password_is_refused()
    {
        var email = Email();
        var acme = await CreateTenantAsync("acme");
        var dormant = await CreateTenantAsync("dormant");
        var beta = await CreateTenantAsync("beta");
        var gamma = await CreateTenantAsync("gamma");
        var current = await CreateUserAsync(acme, email);
        await CreateUserAsync(dormant, email);
        await CreateUserAsync(beta, email, active: false);
        await CreateUserAsync(gamma, email, mustChangePassword: true);
        GetRequiredService<InactiveTenants>().Deactivate(dormant);

        foreach (var target in new[] { dormant, beta, gamma })
        {
            await ShouldFailAsync(() => AsAsync(current, acme, () => _service.SwitchAsync(To(target, Password))), PlatformErrorCodes.AccountNotAvailable);
            await ShouldFailAsync(() => AsAsync(current, acme, () => _service.SwitchAsync(To(target)), passwordTenants: N(target)), PlatformErrorCodes.AccountNotAvailable);
        }

        _tickets.Protected.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_locked_out_target_is_refused_even_with_the_right_password_or_a_password_place()
    {
        var email = Email();
        var acme = await CreateTenantAsync("acme");
        var beta = await CreateTenantAsync("beta");
        var current = await CreateUserAsync(acme, email);
        var target = await CreateUserAsync(beta, email);
        await WithUnitOfWorkAsync(async () =>
        {
            using (_currentTenant.Change(beta))
            {
                var manager = GetRequiredService<IdentityUserManager>();
                var user = await manager.GetByIdAsync(target);
                (await manager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddHours(1))).Succeeded.ShouldBeTrue();
            }
        });

        await ShouldFailAsync(() => AsAsync(current, acme, () => _service.SwitchAsync(To(beta, Password))), PlatformErrorCodes.AccountLockedOut);
        await ShouldFailAsync(() => AsAsync(current, acme, () => _service.SwitchAsync(To(beta)), passwordTenants: N(beta)), PlatformErrorCodes.AccountLockedOut);
        _tickets.Protected.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_wrong_password_is_refused_and_counted_in_the_target_account_only_until_it_locks()
    {
        var email = Email();
        var acme = await CreateTenantAsync("acme");
        var beta = await CreateTenantAsync("beta");
        var current = await CreateUserAsync(acme, email);
        var target = await CreateUserAsync(beta, email, OtherPassword);

        await ShouldFailAsync(() => AsAsync(current, acme, () => _service.SwitchAsync(To(beta, Password, remember: true))), PlatformErrorCodes.AccountPasswordIncorrect);

        (await ReadUserAsync(beta, target)).AccessFailedCount.ShouldBe(1, "the failed attempt is kept although the request fails");
        (await ReadUserAsync(acme, current)).AccessFailedCount.ShouldBe(0, "the current account's lockout does not move");
        (await _links.GetLinkedAsync(acme, current)).ShouldBeEmpty();

        /* Keep failing: the attempt that locks the account says so, and from then on even the right password is refused. */
        string? last = null;
        for (var attempt = 0; attempt < 10 && last != PlatformErrorCodes.AccountLockedOut; attempt++)
        {
            var ex = await Should.ThrowAsync<BusinessException>(() => AsAsync(current, acme, () => _service.SwitchAsync(To(beta, "wrong-" + attempt))));
            last = ex.Code;
        }

        last.ShouldBe(PlatformErrorCodes.AccountLockedOut);
        await ShouldFailAsync(() => AsAsync(current, acme, () => _service.SwitchAsync(To(beta, OtherPassword))), PlatformErrorCodes.AccountLockedOut);
        _tickets.Protected.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_right_password_resets_the_failed_count()
    {
        var email = Email();
        var acme = await CreateTenantAsync("acme");
        var beta = await CreateTenantAsync("beta");
        var current = await CreateUserAsync(acme, email);
        var target = await CreateUserAsync(beta, email, OtherPassword);

        await ShouldFailAsync(() => AsAsync(current, acme, () => _service.SwitchAsync(To(beta, "wrong"))), PlatformErrorCodes.AccountPasswordIncorrect);
        await AsAsync(current, acme, () => _service.SwitchAsync(To(beta, OtherPassword)));

        (await ReadUserAsync(beta, target)).AccessFailedCount.ShouldBe(0);
    }

    [Fact]
    public async Task Without_a_password_a_password_place_or_a_link_the_password_is_required()
    {
        var email = Email();
        var acme = await CreateTenantAsync("acme");
        var beta = await CreateTenantAsync("beta");
        var current = await CreateUserAsync(acme, email);
        var target = await CreateUserAsync(beta, email);

        await ShouldFailAsync(() => AsAsync(current, acme, () => _service.SwitchAsync(To(beta))), PlatformErrorCodes.AccountPasswordRequired);
        await ShouldFailAsync(() => AsAsync(current, acme, () => _service.SwitchAsync(To(beta, ""))), PlatformErrorCodes.AccountPasswordRequired);
        await ShouldFailAsync(() => AsAsync(current, acme, () => _service.SwitchAsync(To(beta)), passwordTenants: $"host {N(acme)} garbage"),
            PlatformErrorCodes.AccountPasswordRequired);

        _tickets.Protected.ShouldBeEmpty();
        (await ReadUserAsync(beta, target)).AccessFailedCount.ShouldBe(0, "no password, no attempt");
    }

    // ── Successful switches and their tickets ─────────────────────────────────

    [Fact]
    public async Task With_the_password_the_ticket_names_this_session_the_target_and_adds_the_targets_place()
    {
        var email = Email();
        var acme = await CreateTenantAsync("acme");
        var beta = await CreateTenantAsync("beta");
        var current = await CreateUserAsync(acme, email);
        var target = await CreateUserAsync(beta, email, OtherPassword);
        var claim = await PlacesAsync((acme, current));

        var redirect = await AsAsync(current, acme, () => _service.SwitchAsync(To(beta, OtherPassword)), passwordTenants: claim);

        var ticket = _tickets.Protected.ShouldHaveSingleItem();
        ticket.Nonce.ShouldNotBe(Guid.Empty);
        ticket.ShouldBe(new AccountSwitchTicket(
            ExpectedUserId: current, ExpectedTenantId: acme, TargetUserId: target, TargetTenantId: beta,
            PasswordTenants: await PlacesAsync((acme, current), (beta, target)), Nonce: ticket.Nonce));
        redirect.Url.ShouldBe($"{ImpersonationTestModule.AuthServerUrl}/Account/SwitchAccount?ticket={Uri.EscapeDataString("switch/1+x=y")}");
        (await _links.GetLinkedAsync(acme, current)).ShouldBeEmpty("no remember, no link");
        _currentTenant.Id.ShouldBeNull("the call leaves the caller's context as it was");
    }

    [Fact]
    public async Task Every_ticket_has_its_own_nonce()
    {
        var email = Email();
        var acme = await CreateTenantAsync("acme");
        var current = await CreateUserAsync(acme, email);
        await CreateUserAsync(null, email, OtherPassword);

        await AsAsync(current, acme, () => _service.SwitchAsync(To(null, OtherPassword)));
        await AsAsync(current, acme, () => _service.SwitchAsync(To(null, OtherPassword)));

        _tickets.Protected.Select(t => t.Nonce).Distinct().Count().ShouldBe(2);
    }

    [Fact]
    public async Task A_password_place_for_the_target_user_with_its_current_stamp_switches_without_a_password()
    {
        var email = Email();
        var acme = await CreateTenantAsync("acme");
        var current = await CreateUserAsync(acme, email);
        var hostUser = await CreateUserAsync(null, email);
        var claim = await PlacesAsync((acme, current), (null, hostUser));

        await AsAsync(current, acme, () => _service.SwitchAsync(To(null)), passwordTenants: claim);

        var ticket = _tickets.Protected.ShouldHaveSingleItem();
        ticket.ShouldBe(new AccountSwitchTicket(current, acme, hostUser, null, claim, ticket.Nonce), "the places are carried on unchanged");
    }

    [Fact]
    public async Task A_password_place_for_another_user_id_in_the_target_tenant_does_not_hold()
    {
        var email = Email();
        var acme = await CreateTenantAsync("acme");
        var beta = await CreateTenantAsync("beta");
        var current = await CreateUserAsync(acme, email);
        var target = await CreateUserAsync(beta, email);
        var stampHash = AccountSwitchClaims.StampHash((await ReadUserAsync(beta, target)).SecurityStamp);

        await ShouldFailAsync(
            () => AsAsync(current, acme, () => _service.SwitchAsync(To(beta)),
                passwordTenants: AccountSwitchClaims.Format([new PasswordPlace(beta, Guid.NewGuid(), stampHash)])),
            PlatformErrorCodes.AccountPasswordRequired);
        _tickets.Protected.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_password_place_stops_holding_after_the_targets_password_changes()
    {
        var email = Email();
        var acme = await CreateTenantAsync("acme");
        var beta = await CreateTenantAsync("beta");
        var current = await CreateUserAsync(acme, email);
        var target = await CreateUserAsync(beta, email, OtherPassword);
        var claim = await PlacesAsync((beta, target));

        await ChangePasswordAsync(beta, target, "Brand-new1!");

        await ShouldFailAsync(() => AsAsync(current, acme, () => _service.SwitchAsync(To(beta)), passwordTenants: claim), PlatformErrorCodes.AccountPasswordRequired);
        _tickets.Protected.ShouldBeEmpty();
    }

    [Fact]
    public async Task B1_an_address_changed_to_the_victims_does_not_turn_a_known_password_place_into_the_victims_account()
    {
        // Review blocker B1. Mallory signed in with a password valid in acme and beta (her own accounts). Her acme address is
        // then changed to Victor's (directly on the user, as an administrator could), so the lookup now lists Victor's beta
        // account as "hers". Her session's beta place names her own beta user, not Victor: the switch must ask for Victor's
        // password.
        var mallory = Email();
        var victor = Email();
        var acme = await CreateTenantAsync("acme");
        var beta = await CreateTenantAsync("beta");
        var malloryInAcme = await CreateUserAsync(acme, mallory);
        var malloryInBeta = await CreateUserAsync(beta, mallory);
        var victorInBeta = await CreateUserAsync(beta, victor, OtherPassword);
        var claim = await PlacesAsync((acme, malloryInAcme), (beta, malloryInBeta));

        await WithUnitOfWorkAsync(async () =>
        {
            using (_currentTenant.Change(acme))
            {
                var manager = GetRequiredService<IdentityUserManager>();
                (await manager.SetEmailAsync(await manager.GetByIdAsync(malloryInAcme), victor)).Succeeded.ShouldBeTrue();
            }
        });
        /* The lookup follows the address change (design 62), so Victor's beta account is now listed for her. */
        var listed = await AsAsync(malloryInAcme, acme, () => _service.GetListAsync(), passwordTenants: claim);
        listed.ShouldHaveSingleItem().TenantId.ShouldBe(beta);
        listed[0].NeedsPassword.ShouldBeTrue("the beta place is Mallory's own user, not Victor");

        await ShouldFailAsync(() => AsAsync(malloryInAcme, acme, () => _service.SwitchAsync(To(beta)), passwordTenants: claim),
            PlatformErrorCodes.AccountPasswordRequired);
        _tickets.Protected.ShouldBeEmpty();
        (await ReadUserAsync(beta, victorInBeta)).AccessFailedCount.ShouldBe(0);
    }

    [Fact]
    public async Task Remember_writes_the_link_both_ways_and_the_link_then_switches_without_a_password_in_both_directions()
    {
        var email = Email();
        var acme = await CreateTenantAsync("acme");
        var beta = await CreateTenantAsync("beta");
        var inAcme = await CreateUserAsync(acme, email);
        var inBeta = await CreateUserAsync(beta, email, OtherPassword);

        await AsAsync(inAcme, acme, () => _service.SwitchAsync(To(beta, OtherPassword, remember: true)));

        var acmeStamp = (await ReadUserAsync(acme, inAcme)).SecurityStamp;
        var betaStamp = (await ReadUserAsync(beta, inBeta)).SecurityStamp;
        (await _links.GetLinkedAsync(acme, inAcme)).ShouldBe([new AccountLink(new LinkedAccount(beta, inBeta, betaStamp), acmeStamp)]);
        (await _links.GetLinkedAsync(beta, inBeta)).ShouldBe([new AccountLink(new LinkedAccount(acme, inAcme, acmeStamp), betaStamp)]);

        var fromAcme = await AsAsync(inAcme, acme, () => _service.GetListAsync());
        fromAcme.ShouldHaveSingleItem().ShouldBe(new MyAccountDto { TenantId = beta, TenantName = "beta", NeedsPassword = false, IsRemembered = true });
        var fromBeta = await AsAsync(inBeta, beta, () => _service.GetListAsync());
        fromBeta.ShouldHaveSingleItem().ShouldBe(new MyAccountDto { TenantId = acme, TenantName = "acme", NeedsPassword = false, IsRemembered = true });

        _tickets.Protected.Clear();
        await AsAsync(inAcme, acme, () => _service.SwitchAsync(To(beta)));
        await AsAsync(inBeta, beta, () => _service.SwitchAsync(To(acme)));
        _tickets.Protected.Select(t => t with { Nonce = Guid.Empty }).ShouldBe(
        [
            new AccountSwitchTicket(inAcme, acme, inBeta, beta, "", Guid.Empty),
            new AccountSwitchTicket(inBeta, beta, inAcme, acme, "", Guid.Empty),
        ]);
    }

    [Fact]
    public async Task Remembering_again_renews_the_link_instead_of_adding_one()
    {
        var email = Email();
        var acme = await CreateTenantAsync("acme");
        var beta = await CreateTenantAsync("beta");
        var inAcme = await CreateUserAsync(acme, email);
        await CreateUserAsync(beta, email, OtherPassword);

        await AsAsync(inAcme, acme, () => _service.SwitchAsync(To(beta, OtherPassword, remember: true)));
        await AsAsync(inAcme, acme, () => _service.SwitchAsync(To(beta, OtherPassword, remember: true)));

        (await WithUnitOfWorkAsync(() => _linkRows.GetCountAsync())).ShouldBe(2, "one row per direction");
    }

    [Fact]
    public async Task A_link_goes_stale_when_the_target_changes_its_password()
    {
        var email = Email();
        var acme = await CreateTenantAsync("acme");
        var beta = await CreateTenantAsync("beta");
        var inAcme = await CreateUserAsync(acme, email);
        var inBeta = await CreateUserAsync(beta, email, OtherPassword);
        await AsAsync(inAcme, acme, () => _service.SwitchAsync(To(beta, OtherPassword, remember: true)));
        _tickets.Protected.Clear();

        await ChangePasswordAsync(beta, inBeta, "Brand-new1!");

        await ShouldFailAsync(() => AsAsync(inAcme, acme, () => _service.SwitchAsync(To(beta))), PlatformErrorCodes.AccountPasswordRequired);
        _tickets.Protected.ShouldBeEmpty();

        /* As documented on MyAccountDto.NeedsPassword: the list does not read the other tenant, so it still shows the
         * stale link as remembered; the switch itself asks for the password. */
        var listed = (await AsAsync(inAcme, acme, () => _service.GetListAsync())).ShouldHaveSingleItem();
        listed.IsRemembered.ShouldBeTrue();

        /* The new password renews the link. */
        await AsAsync(inAcme, acme, () => _service.SwitchAsync(To(beta, "Brand-new1!", remember: true)));
        await AsAsync(inAcme, acme, () => _service.SwitchAsync(To(beta)));
    }

    [Fact]
    public async Task The_reverse_link_goes_stale_when_the_first_account_changes_its_password()
    {
        var email = Email();
        var acme = await CreateTenantAsync("acme");
        var beta = await CreateTenantAsync("beta");
        var inAcme = await CreateUserAsync(acme, email);
        var inBeta = await CreateUserAsync(beta, email, OtherPassword);
        await AsAsync(inAcme, acme, () => _service.SwitchAsync(To(beta, OtherPassword, remember: true)));

        await ChangePasswordAsync(acme, inAcme, "Brand-new1!");

        await ShouldFailAsync(() => AsAsync(inBeta, beta, () => _service.SwitchAsync(To(acme))), PlatformErrorCodes.AccountPasswordRequired);
    }

    [Fact]
    public async Task B2_the_link_ends_when_the_switch_from_account_changes_its_password()
    {
        // Review blocker B2: a link holds only while BOTH stamps are unchanged. The account switched FROM changes its
        // password (it may have been compromised): its remembered switch to beta must ask for beta's password again.
        var email = Email();
        var acme = await CreateTenantAsync("acme");
        var beta = await CreateTenantAsync("beta");
        var inAcme = await CreateUserAsync(acme, email);
        await CreateUserAsync(beta, email, OtherPassword);
        await AsAsync(inAcme, acme, () => _service.SwitchAsync(To(beta, OtherPassword, remember: true)));
        await AsAsync(inAcme, acme, () => _service.SwitchAsync(To(beta)));
        _tickets.Protected.Clear();

        await ChangePasswordAsync(acme, inAcme, "Brand-new1!");

        await ShouldFailAsync(() => AsAsync(inAcme, acme, () => _service.SwitchAsync(To(beta))), PlatformErrorCodes.AccountPasswordRequired);
        _tickets.Protected.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_link_to_another_user_id_in_the_target_tenant_does_not_hold()
    {
        var email = Email();
        var acme = await CreateTenantAsync("acme");
        var beta = await CreateTenantAsync("beta");
        var inAcme = await CreateUserAsync(acme, email);
        var inBeta = await CreateUserAsync(beta, email);
        var stamp = (await ReadUserAsync(beta, inBeta)).SecurityStamp;
        var ownStamp = (await ReadUserAsync(acme, inAcme)).SecurityStamp;
        await _links.RememberAsync(new LinkedAccount(acme, inAcme, ownStamp), new LinkedAccount(beta, Guid.NewGuid(), stamp));

        await ShouldFailAsync(() => AsAsync(inAcme, acme, () => _service.SwitchAsync(To(beta))), PlatformErrorCodes.AccountPasswordRequired);
    }

    [Fact]
    public async Task Another_users_link_never_opens_this_users_switch()
    {
        // constitution rule 1: a link belongs to the account that made it. Piet's link from acme to beta does nothing for Jan.
        var jan = Email();
        var piet = Email();
        var acme = await CreateTenantAsync("acme");
        var beta = await CreateTenantAsync("beta");
        var janInAcme = await CreateUserAsync(acme, jan);
        var janInBeta = await CreateUserAsync(beta, jan);
        var pietInAcme = await CreateUserAsync(acme, piet);
        var janStamp = (await ReadUserAsync(beta, janInBeta)).SecurityStamp;
        var pietStamp = (await ReadUserAsync(acme, pietInAcme)).SecurityStamp;
        await _links.RememberAsync(new LinkedAccount(acme, pietInAcme, pietStamp), new LinkedAccount(beta, janInBeta, janStamp));

        await ShouldFailAsync(() => AsAsync(janInAcme, acme, () => _service.SwitchAsync(To(beta))), PlatformErrorCodes.AccountPasswordRequired);
        (await AsAsync(janInAcme, acme, () => _service.GetListAsync())).ShouldHaveSingleItem().IsRemembered.ShouldBeFalse();
        /* And Piet, whose address has no account in beta, cannot use the link at all. */
        await ShouldFailAsync(() => AsAsync(pietInAcme, acme, () => _service.SwitchAsync(To(beta))), PlatformErrorCodes.AccountNotAvailable);
    }

    // ── Forget ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Forget_removes_the_link_both_ways_and_leaves_other_links_alone()
    {
        var jan = Email();
        var piet = Email();
        var acme = await CreateTenantAsync("acme");
        var beta = await CreateTenantAsync("beta");
        var janInAcme = await CreateUserAsync(acme, jan);
        await CreateUserAsync(beta, jan, OtherPassword);
        await CreateUserAsync(null, jan, OtherPassword);
        var pietInAcme = await CreateUserAsync(acme, piet);
        var pietInBeta = await CreateUserAsync(beta, piet, OtherPassword);
        await AsAsync(janInAcme, acme, () => _service.SwitchAsync(To(beta, OtherPassword, remember: true)));
        await AsAsync(janInAcme, acme, () => _service.SwitchAsync(To(null, OtherPassword, remember: true)));
        await AsAsync(pietInAcme, acme, () => _service.SwitchAsync(To(beta, OtherPassword, remember: true)));
        (await WithUnitOfWorkAsync(() => _linkRows.GetCountAsync())).ShouldBe(6);

        await AsAsync(janInAcme, acme, () => _service.ForgetAsync(beta));

        (await _links.GetLinkedAsync(acme, janInAcme)).Select(l => l.Linked.TenantId).ShouldBe([null], "the host link stays");
        (await WithUnitOfWorkAsync(async () => (await _linkRows.GetListAsync(l => l.UserTenantId == beta && l.LinkedUserId == janInAcme)).Count)).ShouldBe(0, "the reverse row is gone");
        (await _links.GetLinkedAsync(acme, pietInAcme)).ShouldHaveSingleItem().Linked.UserId.ShouldBe(pietInBeta);
        (await _links.GetLinkedAsync(beta, pietInBeta)).ShouldHaveSingleItem().Linked.UserId.ShouldBe(pietInAcme);
        await ShouldFailAsync(() => AsAsync(janInAcme, acme, () => _service.SwitchAsync(To(beta))), PlatformErrorCodes.AccountPasswordRequired);

        /* Forgetting what is not remembered changes nothing. */
        await AsAsync(janInAcme, acme, () => _service.ForgetAsync(Guid.NewGuid()));
        (await WithUnitOfWorkAsync(() => _linkRows.GetCountAsync())).ShouldBe(4);
    }

    [Fact]
    public async Task Forget_is_refused_while_impersonating_and_the_link_stays()
    {
        var email = Email();
        var acme = await CreateTenantAsync("acme");
        var beta = await CreateTenantAsync("beta");
        var inAcme = await CreateUserAsync(acme, email);
        await CreateUserAsync(beta, email, OtherPassword);
        await AsAsync(inAcme, acme, () => _service.SwitchAsync(To(beta, OtherPassword, remember: true)));

        await ShouldFailAsync(() => AsAsync(inAcme, acme, () => _service.ForgetAsync(beta), impersonatorId: Guid.NewGuid()),
            PlatformErrorCodes.AccountSwitchWhileImpersonating);

        (await WithUnitOfWorkAsync(() => _linkRows.GetCountAsync())).ShouldBe(2);
    }

    [Fact]
    public async Task Forget_by_a_user_with_the_same_id_in_another_tenant_removes_nothing()
    {
        // constitution rule 1: the link is keyed on tenant and user; the same user id in another tenant is someone else.
        var email = Email();
        var acme = await CreateTenantAsync("acme");
        var beta = await CreateTenantAsync("beta");
        var inAcme = await CreateUserAsync(acme, email);
        await CreateUserAsync(beta, email, OtherPassword);
        await AsAsync(inAcme, acme, () => _service.SwitchAsync(To(beta, OtherPassword, remember: true)));

        var gamma = await CreateTenantAsync("gamma");
        await AsAsync(inAcme, gamma, () => _service.ForgetAsync(beta));

        (await WithUnitOfWorkAsync(() => _linkRows.GetCountAsync())).ShouldBe(2);
    }

    // ── Cleanup on deletion ───────────────────────────────────────────────────

    [Fact]
    public async Task Deleting_a_user_removes_its_links_in_both_directions_only()
    {
        var jan = Email();
        var piet = Email();
        var acme = await CreateTenantAsync("acme");
        var beta = await CreateTenantAsync("beta");
        var janInAcme = await CreateUserAsync(acme, jan);
        var janInBeta = await CreateUserAsync(beta, jan, OtherPassword);
        var pietInAcme = await CreateUserAsync(acme, piet);
        await CreateUserAsync(beta, piet, OtherPassword);
        await AsAsync(janInAcme, acme, () => _service.SwitchAsync(To(beta, OtherPassword, remember: true)));
        await AsAsync(pietInAcme, acme, () => _service.SwitchAsync(To(beta, OtherPassword, remember: true)));

        await WithUnitOfWorkAsync(async () =>
        {
            using (_currentTenant.Change(beta))
            {
                var manager = GetRequiredService<IdentityUserManager>();
                (await manager.DeleteAsync(await manager.GetByIdAsync(janInBeta))).Succeeded.ShouldBeTrue();
            }
        });

        (await _links.GetLinkedAsync(acme, janInAcme)).ShouldBeEmpty();
        (await _links.GetLinkedAsync(beta, janInBeta)).ShouldBeEmpty();
        (await _links.GetLinkedAsync(acme, pietInAcme)).Count.ShouldBe(1, "Piet's links stay");
        (await WithUnitOfWorkAsync(() => _linkRows.GetCountAsync())).ShouldBe(2);
    }

    [Fact]
    public async Task Deleting_a_tenant_removes_every_link_to_or_from_it_only()
    {
        var jan = Email();
        var acme = await CreateTenantAsync("acme");
        var beta = await CreateTenantAsync("beta");
        var janInAcme = await CreateUserAsync(acme, jan);
        await CreateUserAsync(beta, jan, OtherPassword);
        var janInHost = await CreateUserAsync(null, jan, OtherPassword);
        await AsAsync(janInAcme, acme, () => _service.SwitchAsync(To(beta, OtherPassword, remember: true)));
        await AsAsync(janInAcme, acme, () => _service.SwitchAsync(To(null, OtherPassword, remember: true)));
        (await WithUnitOfWorkAsync(() => _linkRows.GetCountAsync())).ShouldBe(4);

        await WithUnitOfWorkAsync(() => GetRequiredService<ITenantRepository>().DeleteAsync(beta));

        (await _links.GetLinkedAsync(acme, janInAcme)).Select(l => l.Linked.TenantId).ShouldBe([null]);
        (await _links.GetLinkedAsync(null, janInHost)).Select(l => l.Linked.TenantId).ShouldBe([(Guid?)acme]);
        (await WithUnitOfWorkAsync(async () => (await _linkRows.GetListAsync(l => l.UserTenantId == beta || l.LinkedTenantId == beta)).Count)).ShouldBe(0);
    }

    // ── Logs: ids only (constitution rule 5) ──────────────────────────────────

    [Fact]
    public async Task Switches_log_ids_and_never_the_address_the_tenant_name_or_the_password()
    {
        var email = Email();
        var acme = await CreateTenantAsync("acme-logistics");
        var beta = await CreateTenantAsync("beta-transport");
        var current = await CreateUserAsync(acme, email);
        var target = await CreateUserAsync(beta, email, OtherPassword);
        while (_logs.Entries.TryDequeue(out _))
        {
        }

        await ShouldFailAsync(() => AsAsync(current, acme, () => _service.SwitchAsync(To(beta, "Secret-wrong9"))), PlatformErrorCodes.AccountPasswordIncorrect);
        await AsAsync(current, acme, () => _service.SwitchAsync(To(beta, OtherPassword, remember: true)));
        await AsAsync(current, acme, () => _service.ForgetAsync(beta));

        var lines = _logs.Entries.Where(e => e.Category.Contains("Accounts")).ToList();
        lines.Count.ShouldBeGreaterThanOrEqualTo(3);
        lines.ShouldContain(l => l.Message.Contains(current.ToString()) && l.Message.Contains(target.ToString()));
        foreach (var line in lines)
        {
            foreach (var personal in new[] { email, "@", "acme-logistics", "beta-transport", "Secret-wrong9", OtherPassword })
            {
                line.Message.ShouldNotContain(personal, Case.Insensitive, $"log line: {line.Message}");
                line.Values.ShouldNotContain(personal, Case.Insensitive, $"log values: {line.Values}");
            }
        }
    }
}
