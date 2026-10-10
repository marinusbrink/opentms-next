using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using OpenTms.Platform.Grid;
using Volo.Abp;
using Volo.Abp.Application.Services;

namespace OpenTms.Platform.Administration;

[RemoteService(IsEnabled = false)]
public interface IOrganizationUnitAppService : IApplicationService
{
    /// <summary>
    /// One level of the tree: the children of <paramref name="parentId"/> (the roots when null).
    /// With a wildcard search the whole tree is searched and the matches come back as a flat list.
    /// </summary>
    Task<GridResponse<AdministrationOrganizationUnitRowDto>> GetListAsync(GridRequest request, Guid? parentId);

    /// <summary>The whole tree of the tenant as a flat list in tree (code) order: the source of the parent picker.</summary>
    Task<List<AdministrationOrganizationUnitRowDto>> GetAllAsync();

    Task<AdministrationOrganizationUnitRowDto> CreateAsync(AdministrationOrganizationUnitCreateDto input);
    Task<AdministrationOrganizationUnitRowDto> UpdateAsync(Guid id, AdministrationOrganizationUnitUpdateDto input);

    /// <summary>Deletes the unit with its sub-units and memberships. Throws <see cref="OrganizationUnitInUseException"/> when it still has either and <paramref name="force"/> is false.</summary>
    Task DeleteAsync(Guid id, bool force = false);

    Task<GridResponse<AdministrationUserRowDto>> GetMembersAsync(Guid id, GridRequest request);
    Task<GridResponse<AdministrationUserRowDto>> GetUnaddedUsersAsync(Guid id, GridRequest request);
    Task<OrganizationUnitMembersResponseDto> AddMembersAsync(Guid id, OrganizationUnitMembersRequestDto input);
    Task<OrganizationUnitMembersResponseDto> RemoveMembersAsync(Guid id, OrganizationUnitMembersRequestDto input);
}
