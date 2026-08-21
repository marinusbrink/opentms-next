using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using OpenTms.Platform.Grid;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Identity;

namespace OpenTms.Platform.Administration;

[RemoteService(IsEnabled = false)]
public class UserRoleAppService : PlatformAppServiceBase, IUserRoleAppService
{
    private readonly IIdentityRoleRepository _roleRepository;
    private readonly IIdentityUserRepository _userRepository;
    private readonly IdentityRoleManager _roleManager;

    public UserRoleAppService(
        IIdentityRoleRepository roleRepository,
        IIdentityUserRepository userRepository,
        IdentityRoleManager roleManager)
    {
        _roleRepository = roleRepository;
        _userRepository = userRepository;
        _roleManager = roleManager;
    }

    public async Task<GridResponse<AdministrationRoleRowDto>> GetListAsync(GridRequest request)
    {
        var filter = BuildRoleFilter(request);
        var sorting = BuildRoleSorting(request);
        var skipCount = request.StartRow;
        var maxResultCount = request.EndRow - request.StartRow;

        var totalCount = await _roleRepository.GetCountAsync();
        var filteredCount = filter != null ? await _roleRepository.GetCountAsync(filter) : totalCount;

        var roles = await _roleRepository.GetListAsync(
            filter: filter,
            maxResultCount: maxResultCount,
            skipCount: skipCount,
            sorting: sorting);

        // ABP's IIdentityUserRepository has no bulk-count-by-roleId API (no GROUP BY overload),
        // so we load all users with their role assignments in one query and group in memory.
        // Bounded at MaxUserCountForRoleCounts — design §Cost & SLO states tens of users per tenant.
        const int MaxUserCountForRoleCounts = 10_000;
        var allUsers = await _userRepository.GetListAsync(includeDetails: true, maxResultCount: MaxUserCountForRoleCounts);
        var userCountByRoleId = allUsers
            .SelectMany(u => u.Roles.Select(r => r.RoleId))
            .GroupBy(roleId => roleId)
            .ToDictionary(g => g.Key, g => (long)g.Count());

        var rows = roles
            .Select(r => MapToRoleRowDto(r, userCountByRoleId.TryGetValue(r.Id, out var c) ? c : 0))
            .ToList();

        return new GridResponse<AdministrationRoleRowDto>
        {
            Rows = rows,
            TotalCount = totalCount,
            FilteredCount = filteredCount
        };
    }

    public async Task<AdministrationRoleRowDto> CreateAsync(AdministrationRoleCreateUpdateDto input)
    {
        var role = new IdentityRole(GuidGenerator.Create(), input.Name, CurrentTenant.Id);
        role.IsDefault = input.IsDefault;
        role.IsPublic = input.IsPublic;

        ThrowIfFailed(await _roleManager.CreateAsync(role));

        Logger.LogInformation("Role {RoleId} created by {ActorId}", role.Id, CurrentUser.Id);

        var userCount = await _userRepository.GetCountAsync(roleId: role.Id);
        return MapToRoleRowDto(role, userCount);
    }

    public async Task<AdministrationRoleRowDto> UpdateAsync(Guid id, AdministrationRoleCreateUpdateDto input)
    {
        var role = await FindRoleOrThrowAsync(id);

        ThrowIfFailed(await _roleManager.SetRoleNameAsync(role, input.Name));
        role.IsDefault = input.IsDefault;
        role.IsPublic = input.IsPublic;

        ThrowIfFailed(await _roleManager.UpdateAsync(role));

        Logger.LogInformation("Role {RoleId} updated by {ActorId}", id, CurrentUser.Id);

        var userCount = await _userRepository.GetCountAsync(roleId: role.Id);
        return MapToRoleRowDto(role, userCount);
    }

    public async Task DeleteAsync(Guid id, bool force = false)
    {
        var role = await FindRoleOrThrowAsync(id);

        if (!force)
        {
            var userCount = await _userRepository.GetCountAsync(roleId: role.Id);
            if (userCount > 0)
                throw new RoleHasUsersException(role.Name, userCount);
        }

        ThrowIfFailed(await _roleManager.DeleteAsync(role));

        Logger.LogInformation("Role {RoleId} deleted by {ActorId} (force={Force})", id, CurrentUser.Id, force);
    }

    public async Task<BulkDeleteRolesResponseDto> BulkDeleteAsync(BulkDeleteRolesRequestDto input)
    {
        var roleIds = await ResolveRoleSelectionAsync(input.Selection);

        var deletedCount = 0;
        var skippedRows = new List<SkippedRoleDto>();

        foreach (var roleId in roleIds)
        {
            var role = await _roleRepository.FindAsync(roleId);
            if (role == null)
            {
                // Already deleted — treated as deleted (idempotent per design)
                deletedCount++;
                continue;
            }

            if (role.IsStatic)
            {
                skippedRows.Add(new SkippedRoleDto
                {
                    Id = roleId,
                    Name = role.Name,
                    Reason = "Administration:StaticRole"
                });
                continue;
            }

            ThrowIfFailed(await _roleManager.DeleteAsync(role));
            deletedCount++;
            Logger.LogInformation("Role {RoleId} bulk-deleted by {ActorId}", roleId, CurrentUser.Id);
        }

        return new BulkDeleteRolesResponseDto { DeletedCount = deletedCount, SkippedRows = skippedRows };
    }

    private async Task<List<Guid>> ResolveRoleSelectionAsync(GridSelectionDto selection)
    {
        if (selection.Mode == "Explicit")
            return selection.ExplicitIds.Except(selection.ExcludedIds).ToList();

        // FilterBased: re-execute the filter server-side
        if (selection.FilterRequest == null)
            return new List<Guid>();

        var filter = BuildRoleFilter(selection.FilterRequest);
        var roles = await _roleRepository.GetListAsync(filter: filter, maxResultCount: 10_000);
        return roles.Select(r => r.Id)
                    .Except(selection.ExcludedIds)
                    .ToList();
    }

    private async Task<IdentityRole> FindRoleOrThrowAsync(Guid id)
    {
        var role = await _roleRepository.FindAsync(id);
        if (role == null)
            throw new EntityNotFoundException(typeof(IdentityRole), id);
        return role;
    }

    private static AdministrationRoleRowDto MapToRoleRowDto(IdentityRole role, long userCount) =>
        new()
        {
            Id = role.Id,
            Name = role.Name,
            IsDefault = role.IsDefault,
            IsPublic = role.IsPublic,
            IsStatic = role.IsStatic,
            UserCount = userCount
        };

    private static void ThrowIfFailed(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new UserFriendlyException(
                result.Errors.Select(e => e.Description).JoinAsString("; "));
    }

    private static string? BuildRoleFilter(GridRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.WildcardSearch))
            return request.WildcardSearch;

        if (request.ColumnFilters.TryGetValue("name", out var nf) && !string.IsNullOrWhiteSpace(nf.Filter))
            return nf.Filter;

        return null;
    }

    private static string? BuildRoleSorting(GridRequest request)
    {
        if (request.SortModels.Count == 0) return null;

        var s = request.SortModels[0];
        var dir = string.Equals(s.Sort, "desc", StringComparison.OrdinalIgnoreCase) ? " desc" : " asc";

        return s.ColId switch
        {
            "name" => "Name" + dir,
            "creationTime" => "CreationTime" + dir,
            _ => null
        };
    }
}
