using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.Extensions.DependencyInjection;
using OpenTms.Pages.Account;
using OpenTms.Platform.SignIn;
using Shouldly;
using Volo.Abp.Account;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace OpenTms.SignIn;

/// <summary>
/// The sign-in page's tenant detection (docs/designs/62-sign-in-tenant-detection.md): which places a user name and
/// password lead to, how part of a tenant's name picks one, the sealed choice between the two steps, and the page always
/// running in the host. Plain unit tests: no ABP application, every port faked.
/// Risk class: Critical — authentication and tenant isolation (a tenant offered where the password is wrong, or a choice
/// that can be forged, would sign a user into a tenant that is not theirs or tell which tenants a name exists in).
/// </summary>
public class SignInTenantResolverTests
{
    private const string Password = "1q2w3E*";

    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();
    private static readonly Guid UserInHost = Guid.NewGuid();
    private static readonly Guid UserInA = Guid.NewGuid();
    private static readonly Guid UserInB = Guid.NewGuid();

    private readonly FakeLookupStore _lookup = new();
    private readonly FakeCredentialChecker _checker = new();
    private readonly FakeTenantStore _tenants = new();

    private SignInTenantResolver Resolver() => new(_lookup, _checker, _tenants, new UpperInvariantLookupNormalizer());

    public SignInTenantResolverTests()
    {
        _tenants.Add(TenantA, "filogic-opemtms");
        _tenants.Add(TenantB, "acme-transport");
    }

    [Fact]
    public async Task An_address_nobody_has_is_unknown_and_invalid()
    {
        (await Resolver().IsKnownEmailAsync("nobody@example.test")).ShouldBeFalse();

        var resolution = await Resolver().ResolveAsync("nobody@example.test", Password);
        resolution.Outcome.ShouldBe(SignInResolutionOutcome.Invalid);
        _checker.Checked.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_address_is_known_in_an_active_tenant_or_the_host_only()
    {
        _lookup.Add("jan@example.test", new SignInCandidate(TenantB, UserInB));
        _tenants.Add(TenantB, "acme-transport", isActive: false);
        (await Resolver().IsKnownEmailAsync("jan@example.test")).ShouldBeFalse("a deactivated tenant is no place to sign in");

        _lookup.Add("host@example.test", new SignInCandidate(null, UserInHost));
        (await Resolver().IsKnownEmailAsync(" Host@Example.TEST ")).ShouldBeTrue();
        _checker.Checked.ShouldBeEmpty("step 1 asks no tenant database");
    }

    [Fact]
    public async Task Valid_in_one_tenant_signs_in_there()
    {
        _lookup.Add("jan@example.test", new SignInCandidate(TenantA, UserInA));
        _checker.Outcomes[UserInA] = SignInCredentialOutcome.Succeeded;

        var resolution = await Resolver().ResolveAsync("Jan@Example.test", Password);

        resolution.Outcome.ShouldBe(SignInResolutionOutcome.Single);
        resolution.Choices.ShouldHaveSingleItem().ShouldBe(new SignInChoice(TenantA, "filogic-opemtms", UserInA));
    }

    [Fact]
    public async Task Valid_in_several_places_asks_for_the_tenant_host_first()
    {
        _lookup.Add("admin@example.test", new SignInCandidate(TenantB, UserInB), new SignInCandidate(null, UserInHost), new SignInCandidate(TenantA, UserInA));
        _checker.Outcomes[UserInHost] = SignInCredentialOutcome.Succeeded;
        _checker.Outcomes[UserInA] = SignInCredentialOutcome.Succeeded;
        _checker.Outcomes[UserInB] = SignInCredentialOutcome.Succeeded;

        var resolution = await Resolver().ResolveAsync("admin@example.test", Password);

        resolution.Outcome.ShouldBe(SignInResolutionOutcome.Multiple);
        resolution.Choices.Select(c => c.TenantName).ShouldBe([null, "acme-transport", "filogic-opemtms"]);
    }

    [Fact]
    public async Task A_tenant_where_the_password_is_wrong_is_never_offered_and_its_lockout_does_not_move()
    {
        // constitution rule 1: the same address in another tenant with another password reveals nothing about that tenant,
        // and signing in to tenant A never counts as a failed attempt in tenant B.
        _lookup.Add("jan@example.test", new SignInCandidate(TenantA, UserInA), new SignInCandidate(TenantB, UserInB));
        _checker.Outcomes[UserInA] = SignInCredentialOutcome.Succeeded;
        _checker.Outcomes[UserInB] = SignInCredentialOutcome.Failed;

        var resolution = await Resolver().ResolveAsync("jan@example.test", Password);

        resolution.Outcome.ShouldBe(SignInResolutionOutcome.Single);
        resolution.Choices.ShouldHaveSingleItem().TenantId.ShouldBe(TenantA);
        _checker.FailuresRecorded.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_password_valid_nowhere_counts_towards_lockout_everywhere_it_was_checked()
    {
        _lookup.Add("jan@example.test", new SignInCandidate(TenantA, UserInA), new SignInCandidate(TenantB, UserInB));

        var resolution = await Resolver().ResolveAsync("jan@example.test", "wrong");

        resolution.Outcome.ShouldBe(SignInResolutionOutcome.Invalid);
        _checker.FailuresRecorded.ShouldBe([UserInA, UserInB], ignoreOrder: true);
    }

    [Fact]
    public async Task The_failure_that_locks_an_account_says_so()
    {
        _lookup.Add("jan@example.test", new SignInCandidate(TenantA, UserInA));
        _checker.LocksOnFailure.Add(UserInA);

        (await Resolver().ResolveAsync("jan@example.test", "wrong")).Outcome.ShouldBe(SignInResolutionOutcome.LockedOut);
    }

    [Fact]
    public async Task Every_candidate_is_checked_in_its_own_tenant()
    {
        _lookup.Add("jan@example.test", new SignInCandidate(TenantA, UserInA), new SignInCandidate(TenantB, UserInB));

        await Resolver().ResolveAsync("jan@example.test", Password);

        _checker.Checked.ShouldBe([new SignInCandidate(TenantA, UserInA), new SignInCandidate(TenantB, UserInB)], ignoreOrder: true);
    }

    [Fact]
    public async Task An_inactive_or_deleted_tenant_is_skipped_before_its_password_is_checked()
    {
        var gone = Guid.NewGuid();
        _tenants.Add(TenantB, "acme-transport", isActive: false);
        _lookup.Add("jan@example.test", new SignInCandidate(TenantB, UserInB), new SignInCandidate(gone, Guid.NewGuid()));
        _checker.Outcomes[UserInB] = SignInCredentialOutcome.Succeeded;

        var resolution = await Resolver().ResolveAsync("jan@example.test", Password);

        resolution.Outcome.ShouldBe(SignInResolutionOutcome.Invalid);
        _checker.Checked.ShouldBeEmpty("a failed check would count towards lockout in a tenant nobody can sign in to");
    }

    [Fact]
    public async Task A_tenant_whose_database_fails_is_left_out_and_the_others_still_sign_in()
    {
        _lookup.Add("jan@example.test", new SignInCandidate(TenantA, UserInA), new SignInCandidate(TenantB, UserInB));
        _checker.Throws.Add(UserInB);
        _checker.Outcomes[UserInA] = SignInCredentialOutcome.Succeeded;

        var resolution = await Resolver().ResolveAsync("jan@example.test", Password);

        resolution.Outcome.ShouldBe(SignInResolutionOutcome.Single);
        resolution.Choices.ShouldHaveSingleItem().TenantId.ShouldBe(TenantA);
    }

    [Fact]
    public async Task Valid_nowhere_while_a_tenant_could_not_be_asked_is_unavailable_not_invalid()
    {
        _lookup.Add("jan@example.test", new SignInCandidate(TenantA, UserInA), new SignInCandidate(TenantB, UserInB));
        _checker.Throws.Add(UserInB);

        (await Resolver().ResolveAsync("jan@example.test", Password)).Outcome.ShouldBe(SignInResolutionOutcome.Unavailable);
    }

    [Fact]
    public async Task A_stale_lookup_row_is_ignored()
    {
        _lookup.Add("jan@example.test", new SignInCandidate(TenantA, UserInA));
        _checker.Outcomes[UserInA] = SignInCredentialOutcome.NotFound;

        (await Resolver().ResolveAsync("jan@example.test", Password)).Outcome.ShouldBe(SignInResolutionOutcome.Invalid);
        _checker.FailuresRecorded.ShouldBeEmpty();
    }

    [Fact]
    public async Task Locked_out_and_not_allowed_are_told_only_when_no_place_succeeds()
    {
        _lookup.Add("jan@example.test", new SignInCandidate(TenantA, UserInA), new SignInCandidate(TenantB, UserInB));
        _checker.Outcomes[UserInA] = SignInCredentialOutcome.LockedOut;
        _checker.Outcomes[UserInB] = SignInCredentialOutcome.NotAllowed;
        (await Resolver().ResolveAsync("jan@example.test", Password)).Outcome.ShouldBe(SignInResolutionOutcome.LockedOut);

        _checker.Outcomes[UserInA] = SignInCredentialOutcome.Failed;
        (await Resolver().ResolveAsync("jan@example.test", Password)).Outcome.ShouldBe(SignInResolutionOutcome.NotAllowed);

        _checker.Outcomes[UserInA] = SignInCredentialOutcome.Succeeded;
        (await Resolver().ResolveAsync("jan@example.test", Password)).Outcome.ShouldBe(SignInResolutionOutcome.Single);
    }

    [Fact]
    public async Task The_lookup_is_asked_by_hash_never_by_the_typed_address()
    {
        await Resolver().ResolveAsync("Jan@Example.com", Password);

        _lookup.Asked.ShouldBe([UserSignInLookupHash.Of("JAN@EXAMPLE.COM")]);
        _lookup.Asked.ShouldAllBe(hash => !hash.Contains("JAN", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class FakeLookupStore : IUserSignInLookupStore
    {
        private readonly Dictionary<string, List<SignInCandidate>> _rows = new();

        public List<string> Asked { get; } = new();

        public void Add(string email, params SignInCandidate[] candidates) =>
            _rows[UserSignInLookupHash.Of(email.ToUpperInvariant())] = candidates.ToList();

        public Task<IReadOnlyList<SignInCandidate>> FindAsync(string emailHash)
        {
            Asked.Add(emailHash);
            return Task.FromResult<IReadOnlyList<SignInCandidate>>(_rows.TryGetValue(emailHash, out var rows) ? rows : []);
        }

        public Task SyncUserAsync(Guid? tenantId, Guid userId, string? emailHash) => throw new NotSupportedException();

        public Task ReplaceTenantAsync(Guid? tenantId, IReadOnlyCollection<UserSignInLookupEntry> entries) => throw new NotSupportedException();

        public Task DeleteTenantAsync(Guid tenantId) => throw new NotSupportedException();
    }

    private sealed class FakeCredentialChecker : ISignInCredentialChecker
    {
        public Dictionary<Guid, SignInCredentialOutcome> Outcomes { get; } = new();

        public HashSet<Guid> Throws { get; } = new();

        public HashSet<Guid> LocksOnFailure { get; } = new();

        public List<SignInCandidate> Checked { get; } = new();

        public List<Guid> FailuresRecorded { get; } = new();

        public Task<SignInCredentialOutcome> CheckAsync(SignInCandidate candidate, string normalizedEmail, string password)
        {
            Checked.Add(candidate);
            if (Throws.Contains(candidate.UserId))
            {
                throw new InvalidOperationException("tenant database unreachable");
            }

            return Task.FromResult(Outcomes.GetValueOrDefault(candidate.UserId, SignInCredentialOutcome.Failed));
        }

        public Task<bool> RecordFailureAsync(SignInCandidate candidate)
        {
            FailuresRecorded.Add(candidate.UserId);
            return Task.FromResult(LocksOnFailure.Contains(candidate.UserId));
        }
    }

    private sealed class FakeTenantStore : ITenantStore
    {
        private readonly Dictionary<Guid, TenantConfiguration> _tenants = new();

        public void Add(Guid id, string name, bool isActive = true) =>
            _tenants[id] = new TenantConfiguration(id, name) { IsActive = isActive };

        public Task<TenantConfiguration?> FindAsync(string normalizedName) =>
            Task.FromResult(_tenants.Values.FirstOrDefault(t => t.NormalizedName == normalizedName));

        public Task<TenantConfiguration?> FindAsync(Guid id) => Task.FromResult(_tenants.GetValueOrDefault(id));

        public Task<IReadOnlyList<TenantConfiguration>> GetListAsync(bool includeDetails = false) =>
            Task.FromResult<IReadOnlyList<TenantConfiguration>>(_tenants.Values.ToList());

        public TenantConfiguration? Find(string normalizedName) => FindAsync(normalizedName).Result;

        public TenantConfiguration? Find(Guid id) => FindAsync(id).Result;
    }
}

public class SignInTenantMatchTests
{
    private static readonly SignInChoice Host = new(null, null, Guid.NewGuid());
    private static readonly SignInChoice Filogic = new(Guid.NewGuid(), "filogic-opemtms", Guid.NewGuid());
    private static readonly SignInChoice Acme = new(Guid.NewGuid(), "acme-transport", Guid.NewGuid());
    private static readonly SignInChoice[] Choices = [Host, Filogic, Acme];

    [Theory]
    [InlineData("opem")]
    [InlineData("OPEM")]
    [InlineData("  filogic ")]
    [InlineData("gic-op")]
    public void Part_of_a_name_anywhere_in_it_finds_the_tenant(string query)
    {
        var match = SignInTenantMatch.Find(Choices, query, "Host");

        match.Status.ShouldBe(SignInTenantMatchStatus.One);
        match.Choice.ShouldBe(Filogic);
    }

    [Fact]
    public void The_host_is_found_by_its_localized_name()
    {
        SignInTenantMatch.Find(Choices, "hos", "Host").Choice.ShouldBe(Host);
        SignInTenantMatch.DisplayName(Host, "Host").ShouldBe("Host");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("op")]
    [InlineData("  a ")]
    public void Fewer_than_three_characters_say_nothing(string? query)
    {
        var match = SignInTenantMatch.Find(Choices, query, "Host");

        match.Status.ShouldBe(SignInTenantMatchStatus.TooShort);
        match.Choice.ShouldBeNull();
    }

    [Fact]
    public void Text_in_several_names_gives_no_name()
    {
        var match = SignInTenantMatch.Find([Filogic, Acme with { TenantName = "acme-opemtms" }], "opemtms", "Host");

        match.Status.ShouldBe(SignInTenantMatchStatus.Ambiguous);
        match.Choice.ShouldBeNull();
    }

    [Fact]
    public void The_full_name_always_wins()
    {
        var acme = Acme with { TenantName = "acme" };
        var hostInName = Filogic with { TenantName = "ghost-logistics" };
        var shortName = new SignInChoice(Guid.NewGuid(), "BX", Guid.NewGuid());
        SignInChoice[] choices = [Host, acme, Acme, hostInName, shortName];

        SignInTenantMatch.Find(choices, "ACME", "Host").Choice.ShouldBe(acme, "acme next to acme-transport can still be chosen");
        SignInTenantMatch.Find(choices, "acme", "Host").Status.ShouldBe(SignInTenantMatchStatus.One);
        SignInTenantMatch.Find(choices, "acm", "Host").Status.ShouldBe(SignInTenantMatchStatus.Ambiguous);
        SignInTenantMatch.Find(choices, "host", "Host").Choice.ShouldBe(Host, "the host next to a tenant with 'host' in its name");
        SignInTenantMatch.Find(choices, "bx", "Host").Choice.ShouldBe(shortName, "a name shorter than three characters, typed in full");
    }

    [Fact]
    public void A_tenant_outside_the_choices_is_never_found()
    {
        // Only tenants where the password was valid are searched: another tenant's name finds nothing.
        var match = SignInTenantMatch.Find([Filogic], "acme-transport", "Host");

        match.Status.ShouldBe(SignInTenantMatchStatus.None);
        match.Choice.ShouldBeNull();
    }
}

public class SignInEmailProtectorTests
{
    [Fact]
    public void An_address_round_trips_encrypted()
    {
        var protector = new SignInEmailProtector(new EphemeralDataProtectionProvider());
        var sealedEmail = protector.Protect(new SignInEmail("jan@example.test", RememberMe: true));

        sealedEmail.ShouldNotContain("jan", Case.Insensitive);
        protector.Unprotect(sealedEmail).ShouldBe(new SignInEmail("jan@example.test", true));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("jan@example.test")]
    public void A_missing_or_forged_address_is_null(string? value)
    {
        new SignInEmailProtector(new EphemeralDataProtectionProvider()).Unprotect(value).ShouldBeNull();
    }

    [Fact]
    public void A_tenant_selection_cannot_be_read_as_an_address()
    {
        var provider = new EphemeralDataProtectionProvider();
        var selection = new SignInTenantSelectionProtector(provider).Protect(new SignInTenantSelection([], false));

        new SignInEmailProtector(provider).Unprotect(selection).ShouldBeNull();
    }
}

public class SignInTenantSelectionProtectorTests
{
    private static readonly SignInTenantSelection Selection = new(
        [new SignInChoice(null, null, Guid.NewGuid()), new SignInChoice(Guid.NewGuid(), "filogic-opemtms", Guid.NewGuid())],
        RememberMe: true);

    [Fact]
    public void A_selection_round_trips_encrypted()
    {
        var protector = new SignInTenantSelectionProtector(new EphemeralDataProtectionProvider());
        var sealedSelection = protector.Protect(Selection);

        sealedSelection.ShouldNotContain("filogic", Case.Insensitive, "the tenant names are encrypted, not just signed");
        var back = protector.Unprotect(sealedSelection).ShouldNotBeNull();
        back.RememberMe.ShouldBeTrue();
        back.Choices.ShouldBe(Selection.Choices);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-selection")]
    public void A_missing_or_forged_selection_is_null(string? value)
    {
        new SignInTenantSelectionProtector(new EphemeralDataProtectionProvider()).Unprotect(value).ShouldBeNull();
    }

    [Fact]
    public void A_tampered_selection_is_null()
    {
        var protector = new SignInTenantSelectionProtector(new EphemeralDataProtectionProvider());
        var sealedSelection = protector.Protect(Selection);
        var tampered = sealedSelection[..^4] + (sealedSelection.EndsWith("AAAA") ? "BBBB" : "AAAA");

        protector.Unprotect(tampered).ShouldBeNull();
    }

    [Fact]
    public void An_expired_selection_is_null()
    {
        var provider = new EphemeralDataProtectionProvider();
        var expired = provider.CreateProtector("OpenTms.SignIn.TenantSelection").ToTimeLimitedDataProtector()
            .Protect(JsonSerializer.Serialize(Selection), DateTimeOffset.UtcNow.AddMinutes(-1));

        new SignInTenantSelectionProtector(provider).Unprotect(expired).ShouldBeNull();
    }

    [Fact]
    public void Another_purpose_cannot_be_read_as_a_selection()
    {
        var provider = new EphemeralDataProtectionProvider();
        var other = provider.CreateProtector("OpenTms.Impersonation").ToTimeLimitedDataProtector()
            .Protect(JsonSerializer.Serialize(Selection), DateTimeOffset.UtcNow.AddMinutes(5));

        new SignInTenantSelectionProtector(provider).Unprotect(other).ShouldBeNull();
    }
}

public class SignInPageTenantResolveContributorTests
{
    [Theory]
    [InlineData("/Account/Login", true)]
    [InlineData("/account/login", true)]
    [InlineData("/Account/ForgotPassword", false)]
    [InlineData("/Account/ResetPassword", false)]
    [InlineData("/api/abp/application-configuration", false)]
    public async Task Only_the_sign_in_page_is_pinned_to_the_host(string path, bool handled)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = path;
        httpContext.Request.QueryString = new QueryString("?__tenant=" + Guid.NewGuid());
        var services = new ServiceCollection()
            .AddSingleton<IHttpContextAccessor>(new HttpContextAccessor { HttpContext = httpContext })
            .BuildServiceProvider();
        var context = new ResolveContext(services);

        await new SignInPageTenantResolveContributor().ResolveAsync(context);

        context.Handled.ShouldBe(handled);
        context.TenantIdOrName.ShouldBeNull("the sign-in page runs in the host; a pre-selected __tenant is ignored");
    }

    private sealed class ResolveContext(IServiceProvider serviceProvider) : ITenantResolveContext
    {
        public IServiceProvider ServiceProvider { get; } = serviceProvider;

        public string? TenantIdOrName { get; set; }

        public bool Handled { get; set; }
    }
}

public class SelfRegistrationRemovalTests
{
    [Fact]
    public void The_register_page_answers_404()
    {
        var page = new RegisterModel();

        page.OnGet().ShouldBeOfType<NotFoundResult>();
        page.OnPost().ShouldBeOfType<NotFoundResult>();
    }

    [Fact]
    public void The_register_endpoint_is_removed_and_the_rest_of_the_account_api_stays()
    {
        var controller = new ControllerModel(typeof(AccountController).GetTypeInfo(), []);
        foreach (var method in typeof(AccountController).GetMethods().Where(m => m.DeclaringType == typeof(AccountController)))
        {
            controller.Actions.Add(new ActionModel(method, []) { Controller = controller });
        }
        var application = new ApplicationModel();
        application.Controllers.Add(controller);

        new RemoveSelfRegistrationEndpointConvention().Apply(application);

        controller.Actions.Select(a => a.ActionMethod.Name).ShouldNotContain(nameof(AccountController.RegisterAsync));
        controller.Actions.Select(a => a.ActionMethod.Name).ShouldContain(nameof(AccountController.ResetPasswordAsync));
    }
}

public class SignInLastTenantTests
{
    private static readonly string JanHash = UserSignInLookupHash.Of("JAN@EXAMPLE.TEST");
    private static readonly SignInChoice Host = new(null, null, Guid.NewGuid());
    private static readonly SignInChoice Filogic = new(Guid.NewGuid(), "filogic-opemtms", Guid.NewGuid());
    private static readonly SignInChoice[] Choices = [Host, Filogic];

    [Fact]
    public void The_organisation_chosen_last_time_is_filled_in()
    {
        SignInLastTenant.PrefillFor(new SignInLastTenant(JanHash, Filogic.TenantId), JanHash, Choices, "Host").ShouldBe("filogic-opemtms");
        SignInLastTenant.PrefillFor(new SignInLastTenant(JanHash, null), JanHash, Choices, "Host").ShouldBe("Host");
    }

    [Fact]
    public void Nothing_is_filled_in_for_another_address_or_an_organisation_that_is_no_choice_now()
    {
        // The name of one user's organisation never shows for another address on the same browser.
        SignInLastTenant.PrefillFor(new SignInLastTenant(UserSignInLookupHash.Of("PIET@EXAMPLE.TEST"), Filogic.TenantId), JanHash, Choices, "Host")
            .ShouldBeNull();
        SignInLastTenant.PrefillFor(new SignInLastTenant(JanHash, Guid.NewGuid()), JanHash, Choices, "Host")
            .ShouldBeNull("only a place where the password is valid now");
        SignInLastTenant.PrefillFor(null, JanHash, Choices, "Host").ShouldBeNull();
    }

    [Fact]
    public void The_remembered_organisation_round_trips_encrypted_and_a_forged_one_is_null()
    {
        var provider = new EphemeralDataProtectionProvider();
        var protector = new SignInLastTenantProtector(provider);
        var remembered = new SignInLastTenant(JanHash, Filogic.TenantId);

        var sealedValue = protector.Protect(remembered);
        sealedValue.ShouldNotContain(Filogic.TenantId!.Value.ToString(), Case.Insensitive);
        protector.Unprotect(sealedValue).ShouldBe(remembered);

        protector.Unprotect("forged").ShouldBeNull();
        protector.Unprotect(new SignInEmailProtector(provider).Protect(new SignInEmail("jan@example.test", true))).ShouldBeNull();
    }
}

public class SignInClientTests
{
    [Theory]
    [InlineData("OpenTms_Swagger", "OpenTms_Swagger", true)]
    [InlineData("OpenTms_App", "OpenTms_Swagger", false)]
    [InlineData("opentms_swagger", "OpenTms_Swagger", false)]
    [InlineData(null, "OpenTms_Swagger", false)]
    [InlineData("OpenTms_Swagger", null, false)]
    [InlineData("", "", false)]
    public void Only_the_configured_swagger_client_is_an_api_sign_in(string? requestClientId, string? swaggerClientId, bool isApi)
    {
        SignInClient.IsApi(requestClientId, swaggerClientId).ShouldBe(isApi);
    }
}

public class SpaUrlTests
{
    [Theory]
    [InlineData(new[] { "http://localhost:5173" }, "http://localhost:5173/")]
    [InlineData(new[] { " https://app.opentms.nl/ ", "http://localhost:5173" }, "https://app.opentms.nl/")]
    [InlineData(new[] { "", "  ", "https://app.opentms.nl" }, "https://app.opentms.nl/")]
    [InlineData(new string[0], "/")]
    public void The_spa_is_the_first_allowed_redirect_url(string[] allowed, string expected)
    {
        var options = new Volo.Abp.UI.Navigation.Urls.AppUrlOptions();
        foreach (var url in allowed)
        {
            options.RedirectAllowedUrls.Add(url);
        }

        SpaUrl.Root(options).ShouldBe(expected);
    }
}
