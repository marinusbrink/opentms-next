using System;
using System.Threading.Tasks;
using OpenTms.Platform.Grid;
using Volo.Abp;
using Volo.Abp.Application.Services;

namespace OpenTms.Platform.Administration;

[RemoteService(IsEnabled = false)]
public interface IUserRoleAppService : IApplicationService
{
    Task<GridResponse<AdministrationRoleRowDto>> GetListAsync(GridRequest request);
    Task<AdministrationRoleRowDto> CreateAsync(AdministrationRoleCreateUpdateDto input);
    Task<AdministrationRoleRowDto> UpdateAsync(Guid id, AdministrationRoleCreateUpdateDto input);
    Task DeleteAsync(Guid id, bool force = false);
    Task<BulkDeleteRolesResponseDto> BulkDeleteAsync(BulkDeleteRolesRequestDto input);
}
