using System;
using System.Linq;
using Microsoft.AspNetCore.Identity;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using OpenTms.Platform.Administration;
using Volo.Abp;
using Volo.Abp.Identity;
using Volo.Abp.Localization;
using Volo.Abp.SettingManagement;
using Volo.Abp.Users;

namespace OpenTms.Platform.Accounts;

/* "My settings" and "Change profile picture" of the user menu (docs/designs/63-user-menu.md): the signed-in user's own
 * account only, in the current tenant; no permission beyond being signed in. The language is ABP's DefaultLanguage
 * setting at user level, so it follows the user to every browser; no user value means the browser's language. */
/* Exposed only through its controller (the conventional route is switched off). */
[RemoteService(IsEnabled = false)]
[Authorize]
public class MyProfileAppService : PlatformAppServiceBase, IMyProfileAppService
{
    private readonly IdentityUserManager _userManager;
    private readonly ProfilePictureManager _profilePictures;
    private readonly ISettingManager _settingManager;
    private readonly ILanguageProvider _languageProvider;

    public MyProfileAppService(
        IdentityUserManager userManager,
        ProfilePictureManager profilePictures,
        ISettingManager settingManager,
        ILanguageProvider languageProvider)
    {
        _userManager = userManager;
        _profilePictures = profilePictures;
        _settingManager = settingManager;
        _languageProvider = languageProvider;
    }

    public async Task<MyProfileDto> GetAsync()
    {
        var user = await _userManager.GetByIdAsync(CurrentUser.GetId());
        return ToDto(user, await _settingManager.GetOrNullForCurrentUserAsync(LocalizationSettingNames.DefaultLanguage, fallback: false));
    }

    public async Task<MyProfileDto> UpdateAsync(UpdateMyProfileDto input)
    {
        var language = string.IsNullOrWhiteSpace(input.Language) ? null : input.Language.Trim();
        if (language != null)
        {
            var languages = await _languageProvider.GetLanguagesAsync();
            if (languages.All(l => !string.Equals(l.CultureName, language, StringComparison.OrdinalIgnoreCase)))
            {
                throw new BusinessException(PlatformErrorCodes.UnsupportedCulture).WithData("culture", language);
            }
        }

        var user = await _userManager.GetByIdAsync(CurrentUser.GetId());
        user.Name = Normalize(input.Name);
        user.Surname = Normalize(input.Surname);
        var phoneNumber = Normalize(input.PhoneNumber);
        if (phoneNumber != user.PhoneNumber)
        {
            /* Not UserManager.SetPhoneNumberAsync: that renews the security stamp, which would end the user's remembered
             * account switches for a phone number. */
            user.SetPhoneNumber(phoneNumber, confirmed: false);
        }

        (await _userManager.UpdateAsync(user)).CheckErrors();
        /* forceToSet: a choice equal to the default ("en") is still the user's own and must survive a default change. */
        language = language?.ToLowerInvariant();
        await _settingManager.SetForCurrentUserAsync(LocalizationSettingNames.DefaultLanguage, language, forceToSet: true);

        Logger.LogInformation("User {UserId} changed their own settings", user.Id);
        /* The value just written, not read back: within this unit of work the setting cache still holds the old one. */
        return ToDto(user, language);
    }

    public async Task<ProfilePictureDto?> GetPictureAsync() =>
        await _profilePictures.GetAsync(await _userManager.GetByIdAsync(CurrentUser.GetId()));

    public async Task SetPictureAsync(ProfilePictureDto input)
    {
        var user = await _userManager.GetByIdAsync(CurrentUser.GetId());
        await _profilePictures.SetAsync(user, input);
        Logger.LogInformation("User {UserId} set their own profile picture", user.Id);
    }

    public async Task DeletePictureAsync()
    {
        var user = await _userManager.GetByIdAsync(CurrentUser.GetId());
        await _profilePictures.DeleteAsync(user);
        Logger.LogInformation("User {UserId} removed their own profile picture", user.Id);
    }

    private static MyProfileDto ToDto(IdentityUser user, string? language) => new()
    {
        UserName = user.UserName,
        Email = user.Email,
        Name = user.Name,
        Surname = user.Surname,
        PhoneNumber = user.PhoneNumber,
        Language = language,
        HasProfilePicture = ProfilePictureManager.HasPicture(user)
    };

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
