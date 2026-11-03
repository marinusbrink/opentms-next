using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenTms.Integrations.Emailing.Postmark;
using OpenTms.Platform.Accounts;
using OpenTms.Platform.Administration;
using OpenTms.Platform.Emailing;
using OpenTms.Platform.Features;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.FeatureManagement;
using Volo.Abp.Features;
using Volo.Abp.Identity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;
using Xunit;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace OpenTms.HostIntegration;

/// <summary>
/// Password recovery end to end through the real auth server pages (docs/designs/62-sign-in-tenant-detection.md,
/// "Password recovery"): "Forgot password?", the links in the mails (forgot password, invitation), the reset-password page
/// and where the browser lands afterwards, and the sign-in page sending a user who must change the password to choose
/// one. A browser with its own cookies drives the pages (anti-forgery tokens, redirects not followed, so every landing
/// place is asserted); the mails are read from the send log of the tenant they were sent in. The mail service is
/// "configured" with a token and an address that reaches nothing; no job runs, so no mail leaves the test.
/// These flows passed every earlier test and still locked users out: the tests called Identity directly and never the
/// pages, which sent the browser to Swagger, looked tenant users up in the host and kept a forced password change in place.
/// Risk class: Critical — authentication, account access and tenant isolation.
/// </summary>
[Collection(HostIntegrationCollection.Name)]
public class PasswordRecoveryHostTests : IAsyncLifetime
{
    private const string OldPassword = "Old-P@ssw0rd1";
    private const string NewPassword = "New-P@ssw0rd2";

    /// <summary>The SPA's root (the first of App:RedirectAllowedUrls in the host's appsettings.json).</summary>
    private const string SpaRoot = "http://localhost:5173/";

    private const string SignInRequest = "/connect/authorize?client_id=OpenTms_App&response_type=code&state=host-test";

    private readonly HostIntegrationFixture _host;
    private readonly PostmarkOptions _postmark;
    private readonly (string? Token, string From, string BaseUrl) _postmarkBefore;

    public PasswordRecoveryHostTests(HostIntegrationFixture host)
    {
        _host = host;
        _postmark = host.Services.GetRequiredService<IOptions<PostmarkOptions>>().Value;
        _postmarkBefore = (_postmark.ServerToken, _postmark.FromAddress, _postmark.BaseUrl);
    }

    private Guid TenantA => _host.TenantAId;

    private Guid TenantB => _host.TenantBId;

    public async Task InitializeAsync()
    {
        _postmark.ServerToken = "host-test-token";
        _postmark.FromAddress = "noreply@host-tests.example";
        _postmark.BaseUrl = "http://127.0.0.1:9/";
        await SetEmailDeliveryAsync("true");
    }

    public async Task DisposeAsync()
    {
        (_postmark.ServerToken, _postmark.FromAddress, _postmark.BaseUrl) = _postmarkBefore;
        await SetEmailDeliveryAsync(null);
    }

    /* ── Forgot password ───────────────────────────────────────────────────────────────────── */

    [Fact]
    public async Task A_tenant_user_who_forgot_the_password_gets_a_link_that_works_in_any_browser_and_lands_in_the_app()
    {
        var email = NewEmail();
        var userId = await CreateUserAsync(TenantA, email);

        var asking = new TestBrowser(_host);
        var page = await asking.GetAsync("/Account/ForgotPassword?returnUrl=" + Uri.EscapeDataString(SignInRequest) + "&__tenant=" + TenantB);
        page.Status.ShouldBe(HttpStatusCode.OK);
        page.Html.ShouldNotContain("AbpTenantSwitchLink", customMessage: "no tenant switch: the page finds the account itself");

        var sent = await asking.PostAsync(page, new() { ["Email"] = email.ToUpperInvariant() });
        sent.Status.ShouldBe(HttpStatusCode.Redirect);
        sent.Location.ShouldStartWith("/Account/PasswordResetLinkSent");

        var mail = (await MailsAsync(TenantA, email)).ShouldHaveSingleItem();
        mail.HtmlBody!.ShouldContain(HostIntegrationFixture.TenantAName, customMessage: "the mail names the organisation");
        (await MailsAsync(null, email)).ShouldBeEmpty();
        (await MailsAsync(TenantB, email)).ShouldBeEmpty();

        /* Opened on another device: no cookie, no sign-in request of the browser that asked. */
        var link = LinkFrom(mail);
        link.ShouldNotContain("returnUrl");
        var landed = await ChoosePasswordAsync(new TestBrowser(_host), link, NewPassword);

        landed.ShouldBe(SpaRoot, "the app, never the auth server's root (Swagger UI)");
        (await PasswordWorksAsync(TenantA, userId, NewPassword)).ShouldBeTrue();
        (await PasswordWorksAsync(TenantA, userId, OldPassword)).ShouldBeFalse();
    }

    [Fact]
    public async Task An_address_with_accounts_in_several_places_gets_one_mail_per_place_each_in_its_own_tenant()
    {
        var email = NewEmail();
        var inHost = await CreateUserAsync(null, email);
        var inA = await CreateUserAsync(TenantA, email);
        var inB = await CreateUserAsync(TenantB, email);

        var browser = new TestBrowser(_host);
        var sent = await browser.PostAsync(await browser.GetAsync("/Account/ForgotPassword"), new() { ["Email"] = email });
        sent.Status.ShouldBe(HttpStatusCode.Redirect);

        var hostMail = (await MailsAsync(null, email)).ShouldHaveSingleItem();
        var mailA = (await MailsAsync(TenantA, email)).ShouldHaveSingleItem();
        var mailB = (await MailsAsync(TenantB, email)).ShouldHaveSingleItem();
        LinkFrom(hostMail).ShouldContain("userId=" + inHost);
        LinkFrom(hostMail).ShouldNotContain("__tenant=");
        LinkFrom(mailA).ShouldContain("userId=" + inA + "&__tenant=" + TenantA);
        LinkFrom(mailB).ShouldContain("userId=" + inB + "&__tenant=" + TenantB);
        mailB.HtmlBody!.ShouldContain(HostIntegrationFixture.TenantBName);

        /* Each link sets the password of its own account only. */
        await ChoosePasswordAsync(new TestBrowser(_host), LinkFrom(mailB), NewPassword);
        (await PasswordWorksAsync(TenantB, inB, NewPassword)).ShouldBeTrue();
        (await PasswordWorksAsync(TenantA, inA, OldPassword)).ShouldBeTrue();
        (await PasswordWorksAsync(null, inHost, OldPassword)).ShouldBeTrue();
    }

    [Fact]
    public async Task An_unknown_address_is_told_so_and_gets_no_mail()
    {
        var email = NewEmail();
        var browser = new TestBrowser(_host);

        var answer = await browser.PostAsync(await browser.GetAsync("/Account/ForgotPassword"), new() { ["Email"] = email });

        answer.Status.ShouldBe(HttpStatusCode.OK, "the page stays, with the message of the sign-in page's step 1");
        answer.Html.ShouldContain("alert-danger");
        (await MailsAsync(null, email)).ShouldBeEmpty();
        (await MailsAsync(TenantA, email)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Without_email_delivery_for_the_organisation_the_page_says_no_mail_went_out()
    {
        var email = NewEmail();
        await CreateUserAsync(TenantA, email);
        await SetEmailDeliveryAsync("false");

        var browser = new TestBrowser(_host);
        var answer = await browser.PostAsync(await browser.GetAsync("/Account/ForgotPassword"), new() { ["Email"] = email });

        answer.Status.ShouldBe(HttpStatusCode.OK, "not \"sent\": the user would wait for a mail that never comes");
        answer.Html.ShouldContain("alert-warning");
        (await MailsAsync(TenantA, email)).ShouldBeEmpty();
    }

    /* ── The links ─────────────────────────────────────────────────────────────────────────── */

    [Fact]
    public async Task A_link_works_once_and_then_offers_a_new_one()
    {
        var email = NewEmail();
        var userId = await CreateUserAsync(TenantB, email);
        var link = await LinkAsync(TenantB, userId, PasswordLinkKind.Reset);
        await ChoosePasswordAsync(new TestBrowser(_host), link, NewPassword);

        var again = await OpenLinkAsync(new TestBrowser(_host), link);

        again.Status.ShouldBe(HttpStatusCode.OK);
        again.Html.ShouldNotContain("name=\"Password\"");
        again.Html.ShouldContain("/Account/ForgotPassword", customMessage: "the way to a new link");
        (await PasswordWorksAsync(TenantB, userId, NewPassword)).ShouldBeTrue();
    }

    [Fact]
    public async Task An_invitation_sets_the_first_password_through_the_page()
    {
        var email = NewEmail();
        var userId = await CreateUserAsync(TenantA, email);
        await WithUserAsync(TenantA, userId, async (manager, user) =>
        {
            await _host.Services.GetRequiredService<UserPasswordMailSender>().SendInvitationAsync(user);
            (await manager.UpdateAsync(user)).Succeeded.ShouldBeTrue();
        });

        var link = LinkFrom((await MailsAsync(TenantA, email)).ShouldHaveSingleItem());
        (await ChoosePasswordAsync(new TestBrowser(_host), link, NewPassword)).ShouldBe(SpaRoot);

        (await PasswordWorksAsync(TenantA, userId, NewPassword)).ShouldBeTrue();
        (await OpenLinkAsync(new TestBrowser(_host), link)).Html.ShouldNotContain("name=\"Password\"", customMessage: "an invitation is spent once used");
    }

    [Fact]
    public async Task A_link_for_a_user_of_one_tenant_does_nothing_with_another_tenant_in_it()
    {
        var email = NewEmail();
        var userId = await CreateUserAsync(TenantA, email);
        var link = await LinkAsync(TenantA, userId, PasswordLinkKind.Reset);
        var forged = link.Replace("__tenant=" + TenantA, "__tenant=" + TenantB);
        forged.ShouldNotBe(link);

        var browser = new TestBrowser(_host);
        var page = await OpenLinkAsync(browser, forged);
        page.Html.ShouldNotContain("name=\"Password\"");

        /* The invalid link shows no form: the anti-forgery token comes from another page of the same browser. */
        var tokenPage = await browser.GetAsync("/Account/ForgotPassword");
        var posted = await browser.PostAsync(tokenPage with { Url = "/Account/ResetPassword" },
            new() { ["Password"] = NewPassword, ["ConfirmPassword"] = NewPassword });

        posted.Status.ShouldBe(HttpStatusCode.OK, "no confirmation");
        (await PasswordWorksAsync(TenantA, userId, OldPassword)).ShouldBeTrue();
    }

    [Fact]
    public async Task Choosing_the_password_through_a_link_ends_a_lockout()
    {
        var email = NewEmail();
        var userId = await CreateUserAsync(TenantA, email);
        await WithUserAsync(TenantA, userId, async (manager, user) =>
        {
            await manager.SetLockoutEnabledAsync(user, true);
            await manager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddHours(1));
            await manager.AccessFailedAsync(user);
        });

        (await SubmitPasswordAsync(new TestBrowser(_host), email, OldPassword)).Html.ShouldContain("alert-warning", customMessage: "locked out first");
        await ChoosePasswordAsync(new TestBrowser(_host), await LinkAsync(TenantA, userId, PasswordLinkKind.Reset), NewPassword);

        await WithUserAsync(TenantA, userId, async (manager, user) =>
        {
            (await manager.IsLockedOutAsync(user)).ShouldBeFalse("proving the mailbox is what an administrator's unlock asks for");
            user.AccessFailedCount.ShouldBe(0);
        });
        (await SignInAsync(new TestBrowser(_host), email, NewPassword)).ShouldStartWith(SignInRequest);
    }

    /* ── Must change the password ──────────────────────────────────────────────────────────── */

    [Fact]
    public async Task A_user_who_must_change_the_password_chooses_one_at_sign_in_and_is_then_signed_in()
    {
        var email = NewEmail();
        var userId = await CreateUserAsync(TenantA, email, user => user.SetShouldChangePasswordOnNextLogin(true));
        var browser = new TestBrowser(_host);

        var afterPassword = await SubmitPasswordAsync(browser, email, OldPassword);

        afterPassword.Status.ShouldBe(HttpStatusCode.Redirect, "not \"login is not allowed\": that locked the user out for good");
        afterPassword.Location.ShouldBe("/Account/ResetPassword", "the fresh token travels in the page's cookie, never in a URL");
        var landed = await ChoosePasswordAsync(browser, afterPassword.Location!, NewPassword);
        landed.ShouldStartWith("/Account/Login?", Case.Insensitive, "back to the sign-in the user came from");

        var signedIn = await SubmitPasswordAsync(browser, email, NewPassword, startAt: landed);
        signedIn.Status.ShouldBe(HttpStatusCode.Redirect);
        signedIn.Location.ShouldStartWith(SignInRequest);
        await WithUserAsync(TenantA, userId, (_, user) =>
        {
            user.ShouldChangePasswordOnNextLogin.ShouldBeFalse();
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task A_wrong_password_of_a_user_who_must_change_it_is_just_wrong()
    {
        var email = NewEmail();
        await CreateUserAsync(TenantB, email, user => user.SetShouldChangePasswordOnNextLogin(true));

        var answer = await SubmitPasswordAsync(new TestBrowser(_host), email, "Wrong-P@ss9");

        answer.Status.ShouldBe(HttpStatusCode.OK);
        answer.Html.ShouldContain("alert-danger");
        answer.Location.ShouldBeNull();
    }

    /* ── Helpers ───────────────────────────────────────────────────────────────────────────── */

    private ICurrentTenant CurrentTenant => _host.Services.GetRequiredService<ICurrentTenant>();

    private IUnitOfWorkManager UnitOfWorkManager => _host.Services.GetRequiredService<IUnitOfWorkManager>();

    private static string NewEmail() => $"recovery-{Guid.NewGuid():N}@host-tests.example";

    /// <summary>Platform's EmailDelivery feature for tenants A and B: a value, or null for the edition's default again.</summary>
    private async Task SetEmailDeliveryAsync(string? value)
    {
        using var scope = _host.Services.CreateScope();
        var features = scope.ServiceProvider.GetRequiredService<IFeatureManager>();
        var store = scope.ServiceProvider.GetRequiredService<IFeatureManagementStore>();
        foreach (var tenant in new[] { TenantA, TenantB })
        {
            if (value == null)
            {
                await store.DeleteAsync(PlatformFeatures.EmailDelivery, TenantFeatureValueProvider.ProviderName, tenant.ToString());
            }
            else
            {
                await features.SetForTenantAsync(tenant, PlatformFeatures.EmailDelivery, value);
            }
        }
    }

    private async Task<Guid> CreateUserAsync(Guid? tenantId, string email, Action<IdentityUser>? change = null)
    {
        using var scope = _host.Services.CreateScope();
        using (CurrentTenant.Change(tenantId))
        using (var unitOfWork = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: true))
        {
            var manager = scope.ServiceProvider.GetRequiredService<IdentityUserManager>();
            var user = new IdentityUser(Guid.NewGuid(), "u-" + Guid.NewGuid().ToString("N")[..12], email, tenantId);
            (await manager.CreateAsync(user, OldPassword)).Succeeded.ShouldBeTrue();
            if (change != null)
            {
                change(user);
                (await manager.UpdateAsync(user)).Succeeded.ShouldBeTrue();
            }

            await unitOfWork.CompleteAsync();
            return user.Id;
        }
    }

    private async Task WithUserAsync(Guid? tenantId, Guid userId, Func<IdentityUserManager, IdentityUser, Task> action)
    {
        using var scope = _host.Services.CreateScope();
        using (CurrentTenant.Change(tenantId))
        using (var unitOfWork = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: true))
        {
            var manager = scope.ServiceProvider.GetRequiredService<IdentityUserManager>();
            await action(manager, await manager.GetByIdAsync(userId));
            await unitOfWork.CompleteAsync();
        }
    }

    private async Task<bool> PasswordWorksAsync(Guid? tenantId, Guid userId, string password)
    {
        var works = false;
        await WithUserAsync(tenantId, userId, async (manager, user) => works = await manager.CheckPasswordAsync(user, password));
        return works;
    }

    /// <summary>The link an administrator's reset (or an invitation) would mail, made in the user's tenant.</summary>
    private async Task<string> LinkAsync(Guid? tenantId, Guid userId, PasswordLinkKind kind)
    {
        var link = string.Empty;
        await WithUserAsync(tenantId, userId, async (_, user) =>
            link = await _host.Services.GetRequiredService<PasswordLinkBuilder>().BuildAsync(user, kind));
        return new Uri(link).PathAndQuery;
    }

    private async Task<List<EmailMessage>> MailsAsync(Guid? tenantId, string to)
    {
        using var scope = _host.Services.CreateScope();
        using (CurrentTenant.Change(tenantId))
        using (UnitOfWorkManager.Begin(requiresNew: true))
        {
            var mails = await scope.ServiceProvider.GetRequiredService<IRepository<EmailMessage, Guid>>().GetListAsync(m => m.To == to);
            return mails.Where(m => m.TenantId == tenantId).ToList();
        }
    }

    private static string LinkFrom(EmailMessage mail)
    {
        var match = Regex.Match(mail.HtmlBody!, "href=\"([^\"]*/Account/ResetPassword\\?[^\"]+)\"");
        match.Success.ShouldBeTrue("the mail carries a link to the reset-password page");
        return new Uri(WebUtility.HtmlDecode(match.Groups[1].Value)).PathAndQuery;
    }

    /// <summary>Opens the link, chooses the password and follows the confirmation's way on: where the browser lands.</summary>
    private static async Task<string> ChoosePasswordAsync(TestBrowser browser, string link, string password)
    {
        var page = await OpenLinkAsync(browser, link);
        page.Status.ShouldBe(HttpStatusCode.OK);
        page.Html.ShouldContain("name=\"Password\"", customMessage: "the link is valid");
        page.Html.ShouldNotContain("AbpTenantSwitchLink");

        var saved = await browser.PostAsync(page, new() { ["Password"] = password, ["ConfirmPassword"] = password });
        saved.Status.ShouldBe(HttpStatusCode.Redirect, saved.Html);
        saved.Location.ShouldStartWith("/Account/ResetPasswordConfirmation");

        var confirmation = await browser.GetAsync(saved.Location!);
        var continueLink = Regex.Match(confirmation.Html, "id=\"ContinueLink\"[^>]*href=\"([^\"]+)\"");
        continueLink.Success.ShouldBeTrue(confirmation.Html);
        return WebUtility.HtmlDecode(continueLink.Groups[1].Value);
    }

    /// <summary>
    /// Opens a mail link: the page takes the token into its cookie and sends the browser on to its own address without
    /// the token (the address bar, history and later log lines never hold it).
    /// </summary>
    private static async Task<TestBrowser.Answer> OpenLinkAsync(TestBrowser browser, string link)
    {
        var answer = await browser.GetAsync(link);
        if (!link.Contains("resetToken="))
        {
            return answer;
        }

        answer.Status.ShouldBe(HttpStatusCode.Redirect);
        answer.Location.ShouldBe("/Account/ResetPassword");
        return await browser.GetAsync(answer.Location!);
    }

    /// <summary>Steps 1 and 2 of the sign-in page, for the sign-in request <see cref="SignInRequest"/>.</summary>
    private static async Task<TestBrowser.Answer> SubmitPasswordAsync(TestBrowser browser, string email, string password, string? startAt = null)
    {
        var page = await browser.GetAsync(startAt ?? "/Account/Login?ReturnUrl=" + Uri.EscapeDataString(SignInRequest));
        page.Status.ShouldBe(HttpStatusCode.OK);
        if (!page.Html.Contains("name=\"Password\""))
        {
            var next = await browser.PostAsync(page, new() { ["Email"] = email }, action: "/Account/Login?handler=Email");
            next.Status.ShouldBe(HttpStatusCode.Redirect);
            page = await browser.GetAsync(next.Location!);
        }

        return await browser.PostAsync(page, new() { ["Password"] = password }, action: "/Account/Login?handler=Password");
    }

    private static async Task<string> SignInAsync(TestBrowser browser, string email, string password)
    {
        var answer = await SubmitPasswordAsync(browser, email, password);
        answer.Status.ShouldBe(HttpStatusCode.Redirect, answer.Html);
        return answer.Location!;
    }
}

/// <summary>
/// A browser for the in-memory server: keeps cookies, does not follow redirects, posts forms with the page's anti-forgery
/// token and hidden fields.
/// </summary>
internal sealed class TestBrowser
{
    private static readonly Uri BaseAddress = new("http://localhost/");

    private readonly HttpClient _client;
    private readonly CookieContainer _cookies = new();

    public TestBrowser(HostIntegrationFixture host)
    {
        _client = new HttpClient(new CookieHandler(_cookies) { InnerHandler = host.App.GetTestServer().CreateHandler() })
        {
            BaseAddress = BaseAddress
        };
    }

    public sealed record Answer(HttpStatusCode Status, string Html, string? Location, string Url);

    public async Task<Answer> GetAsync(string url) => await ReadAsync(await _client.GetAsync(url), url);

    /// <summary>Posts the page's form: its hidden fields, then <paramref name="fields"/>; to <paramref name="action"/> or the page's own address.</summary>
    public async Task<Answer> PostAsync(Answer page, Dictionary<string, string> fields, string? action = null, Dictionary<string, string>? hidden = null)
    {
        var form = new Dictionary<string, string>();
        foreach (Match input in Regex.Matches(page.Html, "<input[^>]*type=\"hidden\"[^>]*>"))
        {
            var name = Regex.Match(input.Value, "name=\"([^\"]+)\"");
            var value = Regex.Match(input.Value, "value=\"([^\"]*)\"");
            if (name.Success)
            {
                form[WebUtility.HtmlDecode(name.Groups[1].Value)] = value.Success ? WebUtility.HtmlDecode(value.Groups[1].Value) : string.Empty;
            }
        }

        foreach (var (name, value) in hidden ?? [])
        {
            form[name] = value;
        }

        foreach (var (name, value) in fields)
        {
            form[name] = value;
        }

        var url = action ?? page.Url;
        return await ReadAsync(await _client.PostAsync(url, new FormUrlEncodedContent(form)), url);
    }

    private static async Task<Answer> ReadAsync(HttpResponseMessage response, string url)
    {
        var location = response.Headers.Location;
        return new Answer(
            response.StatusCode,
            await response.Content.ReadAsStringAsync(),
            location == null ? null : location.IsAbsoluteUri ? location.ToString() : location.OriginalString,
            url);
    }

    private sealed class CookieHandler(CookieContainer cookies) : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var header = cookies.GetCookieHeader(request.RequestUri!);
            if (!string.IsNullOrEmpty(header))
            {
                request.Headers.Add("Cookie", header);
            }

            var response = await base.SendAsync(request, cancellationToken);
            if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
            {
                foreach (var setCookie in setCookies)
                {
                    cookies.SetCookies(request.RequestUri!, setCookie);
                }
            }

            return response;
        }
    }
}
