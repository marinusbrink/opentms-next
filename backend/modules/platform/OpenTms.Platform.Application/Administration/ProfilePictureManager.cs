using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Identity;
using System.Threading.Tasks;
using Microsoft.Extensions.Localization;
using OpenTms.Platform.Localization;
using Volo.Abp;
using Volo.Abp.BlobStoring;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Identity;

namespace OpenTms.Platform.Administration;

/// <summary>
/// Reads and writes a user's profile picture: the bytes in <see cref="ProfilePictureContainer"/> under the user id, the
/// content type as an extra property on the user. Shared by the Administration app's user dialog and the user's own
/// "Change profile picture" (docs/designs/63-user-menu.md), so both apply the same rules.
/// </summary>
public class ProfilePictureManager : ITransientDependency
{
    /// <summary>Extra property on the user that holds the picture's content type; its presence means "has a picture".</summary>
    public const string ContentTypeProperty = "ProfilePictureContentType";

    private readonly IBlobContainer<ProfilePictureContainer> _container;
    private readonly IdentityUserManager _userManager;
    private readonly IStringLocalizer<PlatformResource> _localizer;

    public ProfilePictureManager(
        IBlobContainer<ProfilePictureContainer> container,
        IdentityUserManager userManager,
        IStringLocalizer<PlatformResource> localizer)
    {
        _container = container;
        _userManager = userManager;
        _localizer = localizer;
    }

    public static bool HasPicture(IdentityUser user) => !string.IsNullOrEmpty(user.GetProperty<string?>(ContentTypeProperty));

    public async Task<ProfilePictureDto?> GetAsync(IdentityUser user)
    {
        var contentType = user.GetProperty<string?>(ContentTypeProperty);
        if (string.IsNullOrEmpty(contentType))
            return null;

        var bytes = await _container.GetAllBytesOrNullAsync(BlobName(user.Id));
        return bytes == null ? null : new ProfilePictureDto { Content = bytes, ContentType = contentType };
    }

    public async Task SetAsync(IdentityUser user, ProfilePictureDto input)
    {
        if (!ProfilePictureDto.AllowedContentTypes.Contains(input.ContentType, StringComparer.OrdinalIgnoreCase))
            throw new UserFriendlyException(_localizer["Administration:PictureTypeNotAllowed"]);
        if (input.Content.Length == 0 || input.Content.Length > ProfilePictureDto.MaxBytes)
            throw new UserFriendlyException(_localizer["Administration:PictureTooLarge"]);

        await _container.SaveAsync(BlobName(user.Id), input.Content, overrideExisting: true);
        user.SetProperty(ContentTypeProperty, input.ContentType.ToLowerInvariant());
        ThrowIfFailed(await _userManager.UpdateAsync(user));
    }

    public async Task DeleteAsync(IdentityUser user)
    {
        await _container.DeleteAsync(BlobName(user.Id));
        if (user.HasProperty(ContentTypeProperty))
        {
            user.RemoveProperty(ContentTypeProperty);
            ThrowIfFailed(await _userManager.UpdateAsync(user));
        }
    }

    /// <summary>Removes the bytes only, for a user that is being deleted.</summary>
    public Task DeleteBlobAsync(Guid userId) => _container.DeleteAsync(BlobName(userId));

    private static string BlobName(Guid userId) => userId.ToString("N");

    private static void ThrowIfFailed(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new UserFriendlyException(result.Errors.Select(e => e.Description).JoinAsString("; "));
    }
}
