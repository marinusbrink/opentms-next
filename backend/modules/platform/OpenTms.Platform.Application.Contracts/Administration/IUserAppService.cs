using System;
using System.Threading.Tasks;
using OpenTms.Platform.Grid;
using Volo.Abp;
using Volo.Abp.Application.Services;

namespace OpenTms.Platform.Administration;

[RemoteService(IsEnabled = false)]
public interface IUserAppService : IApplicationService
{
    Task<GridResponse<AdministrationUserRowDto>> GetListAsync(GridRequest request);
    Task<AdministrationUserRowDto> CreateAsync(AdministrationUserCreateDto input);
    Task<AdministrationUserRowDto> UpdateAsync(Guid id, AdministrationUserUpdateDto input);
    Task DeleteAsync(Guid id);
    Task<BulkDeleteUsersResponseDto> BulkDeleteAsync(BulkDeleteUsersRequestDto input);
    Task ResetPasswordAsync(Guid id, AdministrationResetPasswordDto input);

    /// <summary>Ends a lockout early: clears the lockout end and the failed-attempt count.</summary>
    Task UnlockAsync(Guid id);

    /// <summary>The user's profile picture, or null when there is none.</summary>
    Task<ProfilePictureDto?> GetProfilePictureAsync(Guid id);
    Task SetProfilePictureAsync(Guid id, ProfilePictureDto input);
    Task DeleteProfilePictureAsync(Guid id);
}
