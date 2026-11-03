using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.EntityFrameworkCore.Platform.Emailing;
using OpenTms.Platform.Accounts;
using OpenTms.Platform.Administration;
using OpenTms.Platform.Emailing;
using OpenTms.Platform.Localization;
using Microsoft.Extensions.Localization;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Xunit;

namespace OpenTms.EntityFrameworkCore.Platform.Administration;

/// <summary>
/// "Forgot password?" for one account of the address (docs/designs/62-sign-in-tenant-detection.md, "Password recovery"):
/// the mail goes out in the account's own tenant, names the organisation and carries a reset link for that tenant; an
/// inactive user, an address that changed or mail that is off sends nothing.
/// Risk class: Critical — account access and tenant isolation (a link for a user of one tenant sent in another's name).
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class ForgotPasswordMailerTests : EmailDeliveryTestBase
{
    private const string Email = "j.devries@bakker.example";

    private readonly IUserAppService _users;
    private readonly ForgotPasswordMailer _mailer;

    private IStringLocalizer<PlatformResource> L => GetRequiredService<IStringLocalizer<PlatformResource>>();

    public ForgotPasswordMailerTests()
    {
        _users = GetRequiredService<IUserAppService>();
        _mailer = GetRequiredService<ForgotPasswordMailer>();
    }

    private async Task<Guid> CreateUserAsync(bool isActive = true)
    {
        var created = await _users.CreateAsync(new AdministrationUserCreateDto
        {
            UserName = "j.devries", Email = Email, Name = "Jan", Password = "Typed-P@ss1"
        });
        if (!isActive)
        {
            await WithUnitOfWorkAsync(async () =>
            {
                var userManager = GetRequiredService<IdentityUserManager>();
                var user = await userManager.GetByIdAsync(created.Id);
                user.SetIsActive(false);
                await userManager.UpdateAsync(user);
            });
        }

        return created.Id;
    }

    private Task<List<EmailMessage>> MailsAsync() =>
        WithUnitOfWorkAsync(() => GetRequiredService<IRepository<EmailMessage, Guid>>().GetListAsync());

    [Fact]
    public async Task The_link_goes_out_in_the_accounts_tenant_naming_the_organisation()
    {
        using var _ = DeliveryOn();
        Guid id;
        using (InTenantA())
        {
            id = await CreateUserAsync();
        }

        (await _mailer.SendAsync(TenantA, "Bakker Transport", id, " J.deVries@Bakker.example ")).ShouldBeTrue();

        using (InTenantA())
        {
            var mail = (await MailsAsync()).ShouldHaveSingleItem();
            mail.TenantId.ShouldBe(TenantA);
            mail.To.ShouldBe(Email);
            mail.Subject.ShouldBe(L["Accounts:ForgotPasswordSubject"]);
            mail.HtmlBody!.ShouldContain("Bakker Transport");
            mail.HtmlBody!.ShouldContain("/Account/ResetPassword?userId=" + id + "&__tenant=" + TenantA);
            mail.HtmlBody!.ShouldContain("24 ");
            mail.HtmlBody!.ShouldNotContain("returnUrl", customMessage: "the link must work in any browser, not only the one that asked");
        }

        using (InTenantB())
        {
            (await MailsAsync()).ShouldBeEmpty();
        }

        using (InHost())
        {
            (await MailsAsync()).ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task A_user_of_one_tenant_gets_no_mail_when_another_tenant_is_named()
    {
        using var _ = DeliveryOn();
        Guid id;
        using (InTenantA())
        {
            id = await CreateUserAsync();
        }

        (await _mailer.SendAsync(TenantB, "Other", id, Email)).ShouldBeFalse();

        using (InTenantB())
        {
            (await MailsAsync()).ShouldBeEmpty();
        }

        using (InTenantA())
        {
            (await MailsAsync()).ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task An_inactive_user_or_an_address_the_user_no_longer_has_gets_nothing()
    {
        using var _ = DeliveryOn();
        Guid inactive, active;
        using (InTenantA())
        {
            inactive = await CreateUserAsync(isActive: false);
        }

        using (InTenantB())
        {
            active = await CreateUserAsync();
        }

        (await _mailer.SendAsync(TenantA, "Bakker Transport", inactive, Email)).ShouldBeFalse();
        (await _mailer.SendAsync(TenantB, "Other", active, "someone.else@bakker.example")).ShouldBeFalse();

        using (InTenantA())
        {
            (await MailsAsync()).ShouldBeEmpty();
        }

        using (InTenantB())
        {
            (await MailsAsync()).ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task Without_email_delivery_for_the_tenant_nothing_is_sent_and_the_caller_is_told()
    {
        Guid id;
        using (DeliveryOn())
        using (InTenantA())
        {
            id = await CreateUserAsync();
        }

        using var _ = DeliveryOff();
        (await _mailer.SendAsync(TenantA, "Bakker Transport", id, Email)).ShouldBeFalse();

        using (InTenantA())
        {
            (await MailsAsync()).ShouldBeEmpty();
        }
    }
}
