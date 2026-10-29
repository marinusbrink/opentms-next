using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using OpenTms.Platform.Administration;
using Volo.Abp.Application.Services;

namespace OpenTms.Platform.Accounts;

/// <summary>The signed-in user's own profile, as "My settings" shows it (docs/designs/63-user-menu.md).</summary>
public record MyProfileDto
{
    public string UserName { get; init; } = default!;
    public string? Email { get; init; }
    public string? Name { get; init; }
    public string? Surname { get; init; }
    public string? PhoneNumber { get; init; }

    /// <summary>The user's own language ("nl", "en"); null when the browser's language applies.</summary>
    public string? Language { get; init; }

    public bool HasProfilePicture { get; init; }
}

/// <summary>What "My settings" changes. The e-mail address and user name are the administrator's.</summary>
public record UpdateMyProfileDto
{
    /* ABP Identity's column lengths (IdentityUserConsts, not referenced by this contracts project). */
    public const int MaxNameLength = 64;
    public const int MaxPhoneNumberLength = 16;

    [StringLength(MaxNameLength)]
    public string? Name { get; init; }

    [StringLength(MaxNameLength)]
    public string? Surname { get; init; }

    [StringLength(MaxPhoneNumberLength)]
    public string? PhoneNumber { get; init; }

    /// <summary>One of the product's languages, or null for the browser's language.</summary>
    [StringLength(10)]
    public string? Language { get; init; }
}

/// <summary>
/// The signed-in user's own settings and profile picture (docs/designs/63-user-menu.md). Every signed-in user may use it
/// for their own account only; the password is changed through ABP's <c>/api/account/my-profile/change-password</c>.
/// </summary>
public interface IMyProfileAppService : IApplicationService
{
    Task<MyProfileDto> GetAsync();

    Task<MyProfileDto> UpdateAsync(UpdateMyProfileDto input);

    /// <summary>The user's own profile picture, or null when there is none.</summary>
    Task<ProfilePictureDto?> GetPictureAsync();

    Task SetPictureAsync(ProfilePictureDto input);

    Task DeletePictureAsync();
}
