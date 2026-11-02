using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using OpenTms.EntityFrameworkCore.Platform.Emailing;
using OpenTms.Platform.Administration;
using OpenTms.Platform.Emailing;
using OpenTms.Platform.Grid;
using OpenTms.Platform.Localization;
using Microsoft.Extensions.Localization;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Xunit;

namespace OpenTms.EntityFrameworkCore.Platform.Administration;

/// <summary>
/// Inviting a new user and sending a reset link (Users in Master data): the mail goes through the real send log of
/// design 64, the link is ABP's reset-password page with a working token, and nothing is created or changed when the
/// mail could not go out.
/// Risk class: Critical — account access (who can set a password for whom) and tenant isolation.
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class UserInvitationTests : EmailDeliveryTestBase
{
    private const string NewPassword = "Chosen-P@ss1";

    private readonly IUserAppService _users;
    private readonly IdentityUserManager _userManager;

    private IStringLocalizer<PlatformResource> L => GetRequiredService<IStringLocalizer<PlatformResource>>();

    public UserInvitationTests()
    {
        _users = GetRequiredService<IUserAppService>();
        _userManager = GetRequiredService<IdentityUserManager>();
    }

    private static AdministrationUserCreateDto Invite(string userName = "j.devries", string email = "j.devries@bakker.example") => new()
    {
        UserName = userName,
        Email = email,
        Name = "Jan",
        Surname = "de Vries",
        SendInvitation = true,
        ShouldChangePasswordOnNextLogin = true
    };

    private Task<List<EmailMessage>> MailsAsync() =>
        WithUnitOfWorkAsync(() => GetRequiredService<IRepository<EmailMessage, Guid>>().GetListAsync());

    private Task<IdentityUser?> FindUserAsync(Guid id) => WithUnitOfWorkAsync(() => _userManager.FindByIdAsync(id.ToString()));

    private Task<AdministrationUserRowDto> RowAsync(Guid id) =>
        WithUnitOfWorkAsync(async () =>
            (await _users.GetListAsync(new GridRequest { StartRow = 0, EndRow = 50 })).Rows.Single(r => r.Id == id));

    private static string TokenFrom(EmailMessage mail)
    {
        var match = Regex.Match(mail.HtmlBody!, "resetToken=([^\"&]+)");
        match.Success.ShouldBeTrue("the mail carries a reset link");
        return WebUtility.UrlDecode(match.Groups[1].Value);
    }

    private Task<bool> PasswordWorksAsync(Guid id, string password) =>
        WithUnitOfWorkAsync(async () => await _userManager.CheckPasswordAsync((await _userManager.GetByIdAsync(id)), password));

    /* ── Invitation on create ──────────────────────────────────────────────────────────────── */

    [Fact]
    public async Task Creating_with_an_invitation_mails_the_user_a_working_one_time_link()
    {
        using var _ = DeliveryOn();
        using var __ = InTenantA();

        var created = await _users.CreateAsync(Invite());

        var mail = (await MailsAsync()).ShouldHaveSingleItem();
        mail.To.ShouldBe("j.devries@bakker.example");
        mail.HtmlBody.ShouldNotBeNull();
        mail.HtmlBody.ShouldContain("https://api.opentms.example.test/Account/ResetPassword");
        mail.HtmlBody.ShouldContain("userId=" + created.Id);
        mail.HtmlBody.ShouldContain("__tenant=" + TenantA);
        mail.HtmlBody.ShouldContain("Jan", customMessage: "the greeting uses the first name");

        /* The link's token sets the password the user chooses. */
        var token = TokenFrom(mail);
        var result = await WithUnitOfWorkAsync(async () =>
            await _userManager.ResetPasswordAsync(await _userManager.GetByIdAsync(created.Id), token, NewPassword));
        result.Succeeded.ShouldBeTrue();
        (await PasswordWorksAsync(created.Id, NewPassword)).ShouldBeTrue();
    }

    [Fact]
    public async Task An_invited_user_is_pending_until_they_choose_a_password_and_is_not_asked_to_change_it_again()
    {
        using var _ = DeliveryOn();
        using var __ = InTenantA();

        var created = await _users.CreateAsync(Invite());

        created.InvitationPending.ShouldBeTrue();
        created.ShouldChangePasswordOnNextLogin.ShouldBeFalse("they choose their own password from the mail");
        (await RowAsync(created.Id)).InvitationPending.ShouldBeTrue();

        var token = TokenFrom((await MailsAsync()).Single());
        await WithUnitOfWorkAsync(async () =>
            (await _userManager.ResetPasswordAsync(await _userManager.GetByIdAsync(created.Id), token, NewPassword)).Succeeded.ShouldBeTrue());

        (await RowAsync(created.Id)).InvitationPending.ShouldBeFalse();
    }

    [Fact]
    public async Task A_password_an_administrator_sets_also_ends_the_pending_invitation()
    {
        using var _ = DeliveryOn();
        using var __ = InTenantA();
        var created = await _users.CreateAsync(Invite());

        await WithUnitOfWorkAsync(() => _users.ResetPasswordAsync(created.Id, new AdministrationResetPasswordDto { NewPassword = NewPassword }));

        (await RowAsync(created.Id)).InvitationPending.ShouldBeFalse();
    }

    [Fact]
    public async Task A_user_created_with_a_typed_password_gets_no_mail_and_is_not_pending()
    {
        using var _ = DeliveryOn();
        using var __ = InTenantA();

        var created = await _users.CreateAsync(new AdministrationUserCreateDto
        {
            UserName = "p.jansen", Email = "p.jansen@bakker.example", Password = "Typed-P@ss1"
        });

        created.InvitationPending.ShouldBeFalse();
        (await MailsAsync()).ShouldBeEmpty();
        (await PasswordWorksAsync(created.Id, "Typed-P@ss1")).ShouldBeTrue();
    }

    [Fact]
    public async Task An_invitation_ignores_a_typed_password()
    {
        using var _ = DeliveryOn();
        using var __ = InTenantA();

        var created = await _users.CreateAsync(Invite() with { Password = "Typed-P@ss1" });

        (await PasswordWorksAsync(created.Id, "Typed-P@ss1")).ShouldBeFalse("nobody but the user knows the password");
    }

    [Fact]
    public async Task Without_email_delivery_an_invitation_is_refused_and_no_user_is_created()
    {
        using var _ = DeliveryOff();
        using var __ = InTenantA();

        var ex = await Should.ThrowAsync<UserFriendlyException>(() => _users.CreateAsync(Invite()));
        ex.Message.ShouldBe(L["Administration:EmailDeliveryNotAvailable"]);

        (await WithUnitOfWorkAsync(() => _userManager.FindByNameAsync("j.devries"))).ShouldBeNull();
        (await MailsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Without_a_configured_mail_service_an_invitation_is_refused()
    {
        using var _ = DeliveryOn();
        using var __ = InTenantA();
        Provider.IsConfigured = false;

        await Should.ThrowAsync<UserFriendlyException>(() => _users.CreateAsync(Invite()));

        (await WithUnitOfWorkAsync(() => _userManager.FindByNameAsync("j.devries"))).ShouldBeNull();
    }

    /* ── Resend invitation ─────────────────────────────────────────────────────────────────── */

    [Fact]
    public async Task Resend_mails_a_new_link_while_the_invitation_is_pending()
    {
        using var _ = DeliveryOn();
        using var __ = InTenantA();
        var created = await _users.CreateAsync(Invite());

        await WithUnitOfWorkAsync(() => _users.ResendInvitationAsync(created.Id));

        var mails = await MailsAsync();
        mails.Count.ShouldBe(2);
        mails.ShouldAllBe(m => m.To == "j.devries@bakker.example");
        (await RowAsync(created.Id)).InvitationPending.ShouldBeTrue();
    }

    [Fact]
    public async Task Resend_is_refused_once_the_user_chose_a_password()
    {
        using var _ = DeliveryOn();
        using var __ = InTenantA();
        var created = await _users.CreateAsync(Invite());
        await WithUnitOfWorkAsync(() => _users.ResetPasswordAsync(created.Id, new AdministrationResetPasswordDto { NewPassword = NewPassword }));

        var ex = await Should.ThrowAsync<UserFriendlyException>(() => WithUnitOfWorkAsync(() => _users.ResendInvitationAsync(created.Id)));
        ex.Message.ShouldBe(L["Administration:InvitationNotPending"]);
        (await MailsAsync()).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Resend_is_refused_for_a_user_who_was_never_invited()
    {
        using var _ = DeliveryOn();
        using var __ = InTenantA();
        var created = await _users.CreateAsync(new AdministrationUserCreateDto
        {
            UserName = "p.jansen", Email = "p.jansen@bakker.example", Password = "Typed-P@ss1"
        });

        await Should.ThrowAsync<UserFriendlyException>(() => WithUnitOfWorkAsync(() => _users.ResendInvitationAsync(created.Id)));
        (await MailsAsync()).ShouldBeEmpty();
    }

    /* ── Reset link ────────────────────────────────────────────────────────────────────────── */

    [Fact]
    public async Task A_reset_link_mails_a_working_link_and_leaves_the_current_password_working_until_used()
    {
        using var _ = DeliveryOn();
        using var __ = InTenantA();
        var created = await _users.CreateAsync(new AdministrationUserCreateDto
        {
            UserName = "p.jansen", Email = "p.jansen@bakker.example", Name = "Petra", Password = "Typed-P@ss1"
        });

        await WithUnitOfWorkAsync(() => _users.SendPasswordResetLinkAsync(created.Id));

        var mail = (await MailsAsync()).ShouldHaveSingleItem();
        mail.To.ShouldBe("p.jansen@bakker.example");
        mail.Subject.ShouldBe(L["Administration:PasswordResetLinkSubject"]);
        (await PasswordWorksAsync(created.Id, "Typed-P@ss1")).ShouldBeTrue("nothing changes until the user uses the link");

        var token = TokenFrom(mail);
        await WithUnitOfWorkAsync(async () =>
            (await _userManager.ResetPasswordAsync(await _userManager.GetByIdAsync(created.Id), token, NewPassword)).Succeeded.ShouldBeTrue());
        (await PasswordWorksAsync(created.Id, NewPassword)).ShouldBeTrue();
        (await PasswordWorksAsync(created.Id, "Typed-P@ss1")).ShouldBeFalse();
    }

    [Fact]
    public async Task A_used_link_cannot_be_used_twice()
    {
        using var _ = DeliveryOn();
        using var __ = InTenantA();
        var created = await _users.CreateAsync(Invite());
        var token = TokenFrom((await MailsAsync()).Single());

        await WithUnitOfWorkAsync(async () =>
            (await _userManager.ResetPasswordAsync(await _userManager.GetByIdAsync(created.Id), token, NewPassword)).Succeeded.ShouldBeTrue());
        var second = await WithUnitOfWorkAsync(async () =>
            await _userManager.ResetPasswordAsync(await _userManager.GetByIdAsync(created.Id), token, "Another-P@ss2"));

        second.Succeeded.ShouldBeFalse();
    }

    [Fact]
    public async Task Without_email_delivery_a_reset_link_is_refused()
    {
        Guid id;
        using (DeliveryOn())
        using (InTenantA())
        {
            id = (await _users.CreateAsync(new AdministrationUserCreateDto
            {
                UserName = "p.jansen", Email = "p.jansen@bakker.example", Password = "Typed-P@ss1"
            })).Id;
        }

        using var _ = DeliveryOff();
        using var __ = InTenantA();
        await Should.ThrowAsync<UserFriendlyException>(() => WithUnitOfWorkAsync(() => _users.SendPasswordResetLinkAsync(id)));
        (await MailsAsync()).ShouldBeEmpty();
    }

    /* ── Tenant isolation (constitution rule 1) ────────────────────────────────────────────── */

    [Fact]
    public async Task Another_tenant_cannot_send_a_reset_link_or_an_invitation_for_a_user_it_does_not_own()
    {
        using var _ = DeliveryOn();
        Guid id;
        using (InTenantA())
        {
            id = (await _users.CreateAsync(Invite())).Id;
        }

        using (InTenantB())
        {
            await Should.ThrowAsync<EntityNotFoundException>(() => WithUnitOfWorkAsync(() => _users.SendPasswordResetLinkAsync(id)));
            await Should.ThrowAsync<EntityNotFoundException>(() => WithUnitOfWorkAsync(() => _users.ResendInvitationAsync(id)));
            (await MailsAsync()).ShouldBeEmpty();
        }

        using (InTenantA())
        {
            (await MailsAsync()).Count.ShouldBe(1, "only the original invitation");
        }
    }

    [Fact]
    public async Task The_invitation_is_queued_in_the_inviting_tenant_only()
    {
        using var _ = DeliveryOn();
        using (InTenantA())
        {
            await _users.CreateAsync(Invite());
            (await MailsAsync()).ShouldHaveSingleItem().TenantId.ShouldBe(TenantA);
        }

        using (InTenantB())
        {
            (await MailsAsync()).ShouldBeEmpty();
        }
    }
}
