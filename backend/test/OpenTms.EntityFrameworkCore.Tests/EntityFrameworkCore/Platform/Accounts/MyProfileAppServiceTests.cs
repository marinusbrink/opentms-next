using System;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using OpenTms.Controllers.Platform;
using OpenTms.Platform;
using OpenTms.Platform.Accounts;
using OpenTms.Platform.Administration;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Identity;
using Volo.Abp.Localization;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Security.Claims;
using Volo.Abp.SettingManagement;
using Volo.Abp.Settings;
using Volo.Abp.TenantManagement;
using Xunit;

namespace OpenTms.EntityFrameworkCore.Platform.Accounts;

/// <summary>
/// "My settings" and "Change profile picture" of the user menu (docs/designs/63-user-menu.md), the application service
/// through the real container on SQLite: the signed-in user's own account only (another user's profile, language and
/// picture stay untouched, also with the same user id in another tenant), the language validated against the product's
/// languages, and a phone or name change that leaves the security stamp alone (a renewed stamp would end every
/// remembered account switch). Risk class: Critical — Platform, authentication-adjacent (security stamp) and tenant
/// isolation.
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class MyProfileAppServiceTests : OpenTmsTestBase<AccountsTestModule>
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];

    private readonly IMyProfileAppService _service;
    private readonly ICurrentTenant _currentTenant;
    private readonly ICurrentPrincipalAccessor _principal;

    public MyProfileAppServiceTests()
    {
        _service = GetRequiredService<IMyProfileAppService>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
        _principal = GetRequiredService<ICurrentPrincipalAccessor>();
    }

    private Task<Guid> CreateTenantAsync(string name) =>
        WithUnitOfWorkAsync(async () =>
        {
            var tenant = await GetRequiredService<ITenantManager>().CreateAsync(name);
            await GetRequiredService<ITenantRepository>().InsertAsync(tenant, autoSave: true);
            return tenant.Id;
        });

    private Task<IdentityUser> CreateUserAsync(Guid? tenantId, Guid? id = null, string? name = null, string? phone = null) =>
        WithUnitOfWorkAsync(async () =>
        {
            using (_currentTenant.Change(tenantId))
            {
                var userName = "u-" + Guid.NewGuid().ToString("N")[..10];
                var user = new IdentityUser(id ?? Guid.NewGuid(), userName, userName + "@example.test", tenantId) { Name = name };
                user.SetPhoneNumber(phone, false);
                (await GetRequiredService<IdentityUserManager>().CreateAsync(user, "1q2w3E*")).Succeeded.ShouldBeTrue();
                return user;
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

    private async Task<T> AsAsync<T>(Guid userId, Guid? tenantId, Func<Task<T>> action)
    {
        using (_principal.Change(AccountPrincipals.For(userId, tenantId)))
        using (_currentTenant.Change(tenantId))
        {
            return await action();
        }
    }

    private Task AsAsync(Guid userId, Guid? tenantId, Func<Task> action) =>
        AsAsync<bool>(userId, tenantId, async () =>
        {
            await action();
            return true;
        });

    private Task<string?> UserLanguageAsync(Guid? tenantId, Guid userId) =>
        WithUnitOfWorkAsync(async () =>
        {
            using (_currentTenant.Change(tenantId))
            {
                return await GetRequiredService<ISettingManager>().GetOrNullForUserAsync(LocalizationSettingNames.DefaultLanguage, userId, fallback: false);
            }
        });

    // ── Get and update ────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_returns_the_signed_in_users_own_profile_without_a_language_until_one_is_chosen()
    {
        var acme = await CreateTenantAsync("acme");
        var user = await CreateUserAsync(acme, name: "Jan", phone: "0612345678");

        var profile = await AsAsync(user.Id, acme, () => _service.GetAsync());

        profile.ShouldBe(new MyProfileDto
        {
            UserName = user.UserName, Email = user.Email, Name = "Jan", Surname = null, PhoneNumber = "0612345678",
            Language = null, HasProfilePicture = false
        });
    }

    [Fact]
    public async Task Update_sets_name_surname_phone_and_language_trimmed_and_blank_becomes_empty()
    {
        var acme = await CreateTenantAsync("acme");
        var user = await CreateUserAsync(acme, name: "Old", phone: "0600000000");

        var updated = await AsAsync(user.Id, acme, () => _service.UpdateAsync(new UpdateMyProfileDto
        {
            Name = "  Jan ", Surname = " de Vries ", PhoneNumber = " 0612345678 ", Language = " NL "
        }));

        updated.Name.ShouldBe("Jan");
        updated.Surname.ShouldBe("de Vries");
        updated.PhoneNumber.ShouldBe("0612345678");
        updated.Language.ShouldBe("nl");
        (await AsAsync(user.Id, acme, () => _service.GetAsync())).ShouldBe(updated);
        var stored = await ReadUserAsync(acme, user.Id);
        stored.PhoneNumberConfirmed.ShouldBeFalse();
        stored.UserName.ShouldBe(user.UserName, "the user name is the administrator's");
        stored.Email.ShouldBe(user.Email, "the e-mail address is the administrator's");

        var cleared = await AsAsync(user.Id, acme, () => _service.UpdateAsync(new UpdateMyProfileDto { Name = " ", Surname = null, PhoneNumber = "" }));
        cleared.Name.ShouldBeNull();
        cleared.Surname.ShouldBeNull();
        cleared.PhoneNumber.ShouldBeNull();
    }

    [Fact]
    public async Task Clearing_the_language_removes_the_users_choice()
    {
        var acme = await CreateTenantAsync("acme");
        var user = await CreateUserAsync(acme);
        await AsAsync(user.Id, acme, () => _service.UpdateAsync(new UpdateMyProfileDto { Language = "nl" }));

        (await AsAsync(user.Id, acme, () => _service.GetAsync())).Language.ShouldBe("nl");
        await AsAsync(user.Id, acme, () => _service.UpdateAsync(new UpdateMyProfileDto { Language = "  " }));

        (await UserLanguageAsync(acme, user.Id)).ShouldBeNull("no language stored: the browser's applies");
        (await AsAsync(user.Id, acme, () => _service.GetAsync())).Language.ShouldBeNull();
    }

    [Fact]
    public async Task The_update_that_clears_the_language_answers_without_a_language()
    {
        /* The dialog shows what the update answers; it must be what was stored. */
        var acme = await CreateTenantAsync("acme");
        var user = await CreateUserAsync(acme);
        await AsAsync(user.Id, acme, () => _service.UpdateAsync(new UpdateMyProfileDto { Language = "nl" }));

        /* As the dialog does: it opens with the profile, then saves. */
        (await AsAsync(user.Id, acme, () => _service.GetAsync())).Language.ShouldBe("nl");

        var cleared = await AsAsync(user.Id, acme, () => _service.UpdateAsync(new UpdateMyProfileDto { Language = null }));

        cleared.Language.ShouldBeNull("the language was cleared");
        (await UserLanguageAsync(acme, user.Id)).ShouldBeNull();
        (await AsAsync(user.Id, acme, () => _service.GetAsync())).Language.ShouldBeNull();
    }

    [Theory]
    [InlineData("de")]
    [InlineData("fr-FR")]
    [InlineData("nl-NL")]
    [InlineData("english")]
    public async Task An_unsupported_language_is_refused_and_nothing_changes(string language)
    {
        var acme = await CreateTenantAsync("acme");
        var user = await CreateUserAsync(acme, name: "Jan", phone: "0600000000");

        var ex = await Should.ThrowAsync<BusinessException>(() =>
            AsAsync(user.Id, acme, () => _service.UpdateAsync(new UpdateMyProfileDto { Name = "Changed", PhoneNumber = "0611111111", Language = language })));

        ex.Code.ShouldBe(PlatformErrorCodes.UnsupportedCulture);
        ex.Data["culture"].ShouldBe(language);
        var stored = await ReadUserAsync(acme, user.Id);
        stored.Name.ShouldBe("Jan");
        stored.PhoneNumber.ShouldBe("0600000000");
        (await UserLanguageAsync(acme, user.Id)).ShouldBeNull();
    }

    [Fact]
    public async Task Choosing_English_is_stored_and_returned()
    {
        var acme = await CreateTenantAsync("acme");
        var user = await CreateUserAsync(acme);

        /* An explicit choice of English must be kept even where English is also the application's default language:
         * otherwise a user with a Dutch browser who chose English gets Dutch. */
        var updated = await AsAsync(user.Id, acme, () => _service.UpdateAsync(new UpdateMyProfileDto { Language = "en" }));

        (await UserLanguageAsync(acme, user.Id)).ShouldBe("en", "the user's choice is stored at user level");
        (await AsAsync(user.Id, acme, () => _service.GetAsync())).Language.ShouldBe("en");
        updated.Language.ShouldBe("en");
    }

    [Fact]
    public async Task A_phone_or_name_change_leaves_the_security_stamp_alone()
    {
        var acme = await CreateTenantAsync("acme");
        var user = await CreateUserAsync(acme, name: "Jan", phone: "0600000000");
        var stampBefore = (await ReadUserAsync(acme, user.Id)).SecurityStamp;

        await AsAsync(user.Id, acme, () => _service.UpdateAsync(new UpdateMyProfileDto { Name = "Piet", Surname = "Jansen", PhoneNumber = "0699999999", Language = "en" }));
        await AsAsync(user.Id, acme, () => _service.UpdateAsync(new UpdateMyProfileDto { Name = "Piet", PhoneNumber = null }));

        var stored = await ReadUserAsync(acme, user.Id);
        stored.PhoneNumber.ShouldBeNull();
        stored.SecurityStamp.ShouldBe(stampBefore);
    }

    [Fact]
    public async Task Update_and_language_touch_only_the_current_user_not_a_colleague_or_a_user_of_another_tenant()
    {
        // constitution rule 1: a colleague and a user of tenant B keep their profile and language. (The SQLite test database is
        // shared, so the same id cannot exist twice here; database-per-tenant keeps ids apart by construction.)
        var acme = await CreateTenantAsync("acme");
        var beta = await CreateTenantAsync("beta");
        var me = await CreateUserAsync(acme, name: "Jan");
        var colleague = await CreateUserAsync(acme, name: "Kees");
        var twin = await CreateUserAsync(beta, name: "Twin");

        await AsAsync(me.Id, acme, () => _service.UpdateAsync(new UpdateMyProfileDto { Name = "Changed", Language = "nl" }));

        (await ReadUserAsync(acme, colleague.Id)).Name.ShouldBe("Kees");
        (await ReadUserAsync(beta, twin.Id)).Name.ShouldBe("Twin");
        (await UserLanguageAsync(acme, colleague.Id)).ShouldBeNull();
        (await UserLanguageAsync(beta, twin.Id)).ShouldBeNull();
        (await AsAsync(twin.Id, beta, () => _service.GetAsync())).Language.ShouldBeNull();
        (await AsAsync(me.Id, acme, () => _service.GetAsync())).Language.ShouldBe("nl");
    }

    [Fact]
    public async Task A_user_id_from_another_tenant_finds_nothing_in_the_current_tenant()
    {
        // constitution rule 1: the profile is read in the current tenant only; a principal of tenant A in tenant B's context
        // reads no user of B.
        var acme = await CreateTenantAsync("acme");
        var beta = await CreateTenantAsync("beta");
        var me = await CreateUserAsync(acme);

        using (_principal.Change(AccountPrincipals.For(me.Id, acme)))
        using (_currentTenant.Change(beta))
        {
            await Should.ThrowAsync<EntityNotFoundException>(() => _service.GetAsync());
            await Should.ThrowAsync<EntityNotFoundException>(() => _service.UpdateAsync(new UpdateMyProfileDto { Name = "x" }));
            await Should.ThrowAsync<EntityNotFoundException>(() => _service.SetPictureAsync(new ProfilePictureDto { Content = Png, ContentType = "image/png" }));
        }
    }

    // ── Picture ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task My_picture_is_set_read_and_removed_for_the_current_user_only()
    {
        var acme = await CreateTenantAsync("acme");
        var beta = await CreateTenantAsync("beta");
        var me = await CreateUserAsync(acme);
        var colleague = await CreateUserAsync(acme);
        var twin = await CreateUserAsync(beta);

        /* The colleague already has a picture of their own. */
        byte[] colleaguePng = [0x89, 0x50, 0x4E, 0x47, 9, 9, 9];
        await AsAsync(colleague.Id, acme, () => _service.SetPictureAsync(new ProfilePictureDto { Content = colleaguePng, ContentType = "image/png" }));

        await AsAsync(me.Id, acme, () => _service.SetPictureAsync(new ProfilePictureDto { Content = Png, ContentType = "IMAGE/PNG" }));

        var mine = await AsAsync(me.Id, acme, () => _service.GetPictureAsync());
        mine.ShouldNotBeNull();
        mine.Content.ShouldBe(Png);
        mine.ContentType.ShouldBe("image/png");
        (await AsAsync(me.Id, acme, () => _service.GetAsync())).HasProfilePicture.ShouldBeTrue();
        (await AsAsync(colleague.Id, acme, () => _service.GetPictureAsync()))!.Content.ShouldBe(colleaguePng);
        (await AsAsync(twin.Id, beta, () => _service.GetAsync())).HasProfilePicture.ShouldBeFalse("another tenant's user is untouched");

        await AsAsync(me.Id, acme, () => _service.DeletePictureAsync());

        (await AsAsync(me.Id, acme, () => _service.GetPictureAsync())).ShouldBeNull();
        (await AsAsync(me.Id, acme, () => _service.GetAsync())).HasProfilePicture.ShouldBeFalse();
        (await AsAsync(colleague.Id, acme, () => _service.GetPictureAsync()))!.Content.ShouldBe(colleaguePng, "the colleague's picture survives");
    }

    [Fact]
    public async Task My_picture_follows_the_same_rules_as_the_administration_dialog()
    {
        var acme = await CreateTenantAsync("acme");
        var me = await CreateUserAsync(acme);

        await Should.ThrowAsync<UserFriendlyException>(() =>
            AsAsync(me.Id, acme, () => _service.SetPictureAsync(new ProfilePictureDto { Content = Png, ContentType = "image/gif" })));
        await Should.ThrowAsync<UserFriendlyException>(() =>
            AsAsync(me.Id, acme, () => _service.SetPictureAsync(new ProfilePictureDto { Content = new byte[ProfilePictureDto.MaxBytes + 1], ContentType = "image/png" })));
        await Should.ThrowAsync<UserFriendlyException>(() =>
            AsAsync(me.Id, acme, () => _service.SetPictureAsync(new ProfilePictureDto { Content = [], ContentType = "image/png" })));

        (await AsAsync(me.Id, acme, () => _service.GetPictureAsync())).ShouldBeNull();
        /* Removing a picture that is not there is fine. */
        await AsAsync(me.Id, acme, () => _service.DeletePictureAsync());
    }

    [Fact]
    public async Task A_picture_set_by_the_administrator_is_my_picture_and_the_reverse()
    {
        /* One ProfilePictureManager behind both: the administration dialog and the user menu see the same picture. */
        var acme = await CreateTenantAsync("acme");
        var me = await CreateUserAsync(acme);

        await AsAsync(me.Id, acme, () => _service.SetPictureAsync(new ProfilePictureDto { Content = Png, ContentType = "image/webp" }));
        using (_currentTenant.Change(acme))
        {
            var viaAdmin = await GetRequiredService<IUserAppService>().GetProfilePictureAsync(me.Id);
            viaAdmin!.Content.ShouldBe(Png);
            viaAdmin.ContentType.ShouldBe("image/webp");
            await GetRequiredService<IUserAppService>().DeleteProfilePictureAsync(me.Id);
        }

        (await AsAsync(me.Id, acme, () => _service.GetPictureAsync())).ShouldBeNull();
    }

    // ── ABP's own profile endpoint ────────────────────────────────────────────

    [Fact]
    public async Task Abps_profile_update_keeps_the_users_email_address()
    {
        /* The address is the sign-in key and names the user's accounts in other tenants (design 63): only an administrator
         * changes it. DisableSelfEmailUpdateSettingDefinitionProvider switches ABP's IsEmailUpdateEnabled off. */
        var acme = await CreateTenantAsync("acme");
        var me = await CreateUserAsync(acme, name: "Jan");

        (await GetRequiredService<Volo.Abp.Settings.ISettingProvider>().IsTrueAsync(Volo.Abp.Identity.Settings.IdentitySettingNames.User.IsEmailUpdateEnabled))
            .ShouldBeFalse();
        await AsAsync(me.Id, acme, () => GetRequiredService<Volo.Abp.Account.IProfileAppService>().UpdateAsync(new Volo.Abp.Account.UpdateProfileDto
        {
            UserName = me.UserName, Email = "victim@example.test", Name = "Jan Changed", ConcurrencyStamp = me.ConcurrencyStamp
        }));

        var stored = await ReadUserAsync(acme, me.Id);
        stored.Email.ShouldBe(me.Email);
        stored.NormalizedEmail.ShouldBe(me.Email!.ToUpperInvariant());
        stored.Name.ShouldBe("Jan Changed", "the rest of the profile is still the user's own");
    }

    // ── Gates ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Every_signed_in_user_may_use_both_services_and_no_permission_is_needed()
    {
        foreach (var type in new[] { typeof(MyProfileAppService), typeof(MyAccountsAppService), typeof(MyProfileController), typeof(MyAccountsController) })
        {
            type.GetCustomAttribute<AuthorizeAttribute>().ShouldNotBeNull($"{type.Name} needs a signed-in user").Policy.ShouldBeNull();
        }

        typeof(MyProfileAppService).GetCustomAttribute<RemoteServiceAttribute>()!.IsEnabled.ShouldBeFalse("only the explicit controller exposes it");
        typeof(MyAccountsAppService).GetCustomAttribute<RemoteServiceAttribute>()!.IsEnabled.ShouldBeFalse("only the explicit controller exposes it");
    }

    [Fact]
    public async Task Without_a_signed_in_user_nothing_is_read()
    {
        using (_principal.Change(new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity())))
        {
            await Should.ThrowAsync<Exception>(() => _service.GetAsync());
            await Should.ThrowAsync<Exception>(() => GetRequiredService<IMyAccountsAppService>().GetListAsync());
        }
    }
}
