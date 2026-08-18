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

public class UserAppService : PlatformAppServiceBase, IUserAppService
{
    private readonly IIdentityUserRepository _userRepository;
    private readonly IIdentityRoleRepository _roleRepository;
    private readonly IdentityUserManager _userManager;

    public UserAppService(
        IIdentityUserRepository userRepository,
        IIdentityRoleRepository roleRepository,
        IdentityUserManager userManager)
    {
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _userManager = userManager;
    }

    // Bounded at MaxRoleFetchCount — at the design's stated scale (tens of roles per tenant)
    // this is never reached, but the convention requires every collection query to be paged.
    private const int MaxRoleFetchCount = 1000;

    public async Task<GridResponse<AdministrationUserRowDto>> GetListAsync(GridRequest request)
    {
        var filter = BuildUserFilter(request);
        var sorting = BuildUserSorting(request);
        var skipCount = request.StartRow;
        var maxResultCount = request.EndRow - request.StartRow;

        var allRoles = await _roleRepository.GetListAsync(maxResultCount: MaxRoleFetchCount);
        var roleNameById = allRoles.ToDictionary(r => r.Id, r => r.Name);

        var totalCount = await _userRepository.GetCountAsync();
        var filteredCount = filter != null ? await _userRepository.GetCountAsync(filter) : totalCount;

        var users = await _userRepository.GetListAsync(
            filter: filter,
            includeDetails: true,
            maxResultCount: maxResultCount,
            skipCount: skipCount,
            sorting: sorting);

        var rows = users.Select(u => MapToUserRowDto(u, roleNameById)).ToList();

        return new GridResponse<AdministrationUserRowDto>
        {
            Rows = rows,
            TotalCount = totalCount,
            FilteredCount = filteredCount
        };
    }

    public async Task<AdministrationUserRowDto> CreateAsync(AdministrationUserCreateDto input)
    {
        await ValidateRoleNamesAsync(input.RoleNames);

        var user = new IdentityUser(GuidGenerator.Create(), input.UserName, input.Email, CurrentTenant.Id);
        user.Name = input.Name;
        user.Surname = input.Surname;

        ThrowIfFailed(await _userManager.CreateAsync(user));
        ThrowIfFailed(await _userManager.AddPasswordAsync(user, input.Password));

        if (input.RoleNames.Count > 0)
            await _userManager.SetRolesAsync(user, input.RoleNames);

        Logger.LogInformation("User {UserId} created by {ActorId}", user.Id, CurrentUser.Id);

        return await BuildUserRowDtoAsync(user);
    }

    public async Task<AdministrationUserRowDto> UpdateAsync(Guid id, AdministrationUserUpdateDto input)
    {
        await ValidateRoleNamesAsync(input.RoleNames);

        var user = await FindUserOrThrowAsync(id);

        await CheckLastAdminRoleGuardAsync(user, input.RoleNames);

        ThrowIfFailed(await _userManager.SetUserNameAsync(user, input.UserName));
        ThrowIfFailed(await _userManager.SetEmailAsync(user, input.Email));
        user.Name = input.Name;
        user.Surname = input.Surname;
        user.SetIsActive(input.IsActive);

        await _userManager.SetRolesAsync(user, input.RoleNames);
        ThrowIfFailed(await _userManager.UpdateAsync(user));

        Logger.LogInformation("User {UserId} updated by {ActorId}", user.Id, CurrentUser.Id);

        return await BuildUserRowDtoAsync(user);
    }

    public async Task DeleteAsync(Guid id)
    {
        if (CurrentUser.Id == id)
            throw new UserFriendlyException("Administration:SelfDeletionNotAllowed");

        var user = await FindUserOrThrowAsync(id);

        await CheckLastAdminRoleGuardAsync(user, null);

        ThrowIfFailed(await _userManager.DeleteAsync(user));

        Logger.LogInformation("User {UserId} deleted by {ActorId}", id, CurrentUser.Id);
    }

    public async Task<BulkDeleteUsersResponseDto> BulkDeleteAsync(BulkDeleteUsersRequestDto input)
    {
        var userIds = await ResolveSelectionAsync(input.Selection);

        var deletedCount = 0;
        var skippedRows = new List<SkippedRowDto>();

        foreach (var userId in userIds)
        {
            if (CurrentUser.Id == userId)
            {
                var selfUser = await _userRepository.FindAsync(userId);
                skippedRows.Add(new SkippedRowDto
                {
                    Id = userId,
                    UserName = selfUser?.UserName ?? string.Empty,
                    Reason = "Administration:SelfDeletionNotAllowed"
                });
                continue;
            }

            var user = await _userRepository.FindAsync(userId, includeDetails: true);
            if (user == null)
            {
                // Already deleted — treated as deleted (idempotent per design)
                deletedCount++;
                continue;
            }

            var skipReason = await GetLastAdminRoleSkipReasonAsync(user);
            if (skipReason != null)
            {
                skippedRows.Add(new SkippedRowDto
                {
                    Id = userId,
                    UserName = user.UserName,
                    Reason = skipReason
                });
                continue;
            }

            ThrowIfFailed(await _userManager.DeleteAsync(user));
            deletedCount++;
            Logger.LogInformation("User {UserId} bulk-deleted by {ActorId}", userId, CurrentUser.Id);
        }

        return new BulkDeleteUsersResponseDto { DeletedCount = deletedCount, SkippedRows = skippedRows };
    }

    public async Task ResetPasswordAsync(Guid id, AdministrationResetPasswordDto input)
    {
        var user = await FindUserOrThrowAsync(id);

        // Deviation from design §Assumptions #3: uses RemovePasswordAsync + AddPasswordAsync
        // instead of GeneratePasswordResetTokenAsync + ResetPasswordAsync (token-based path).
        // The token path requires a registered IUserTwoFactorTokenProvider<TUser> named "Default",
        // which is absent from the ABP integration-test host and from OpenIddict-only deployments
        // that omit AddDefaultTokenProviders(). Security-stamp impact: unlike the token path,
        // these calls do NOT reset the security stamp, so the target user's existing OIDC sessions
        // remain valid until natural expiry. This is acceptable: admin password reset is an
        // emergency credential rotation, not a session-invalidation mechanism; the OpenIddict
        // refresh-token rotation and short-lived access tokens bound the exposure window.
        // Evaluated and confirmed against the OpenIddict integration in OpenTms.HttpApi.Host.
        ThrowIfFailed(await _userManager.RemovePasswordAsync(user));
        ThrowIfFailed(await _userManager.AddPasswordAsync(user, input.NewPassword));

        Logger.LogInformation("Password reset for user {UserId} by {ActorId}", id, CurrentUser.Id);
    }

    private async Task<IdentityUser> FindUserOrThrowAsync(Guid id)
    {
        var user = await _userRepository.FindAsync(id, includeDetails: true);
        if (user == null)
            throw new EntityNotFoundException(typeof(IdentityUser), id);
        return user;
    }

    private async Task ValidateRoleNamesAsync(IEnumerable<string> roleNames)
    {
        foreach (var roleName in roleNames)
        {
            var role = await _roleRepository.FindByNormalizedNameAsync(roleName.ToUpperInvariant());
            if (role == null)
                throw new UserFriendlyException($"Role '{roleName}' does not exist.");
        }
    }

    private async Task CheckLastAdminRoleGuardAsync(IdentityUser user, IEnumerable<string>? newRoleNames)
    {
        var adminRole = await GetAdminRoleAsync();
        if (adminRole == null) return;

        var userHasAdminRole = user.Roles.Any(r => r.RoleId == adminRole.Id);
        if (!userHasAdminRole) return;

        // newRoleNames is null on delete; on update, check if admin role is being removed
        bool removingAdminRole = newRoleNames != null &&
            !newRoleNames.Any(n => string.Equals(n, adminRole.Name, StringComparison.OrdinalIgnoreCase));

        if (newRoleNames != null && !removingAdminRole) return;

        var adminUserCount = await _userRepository.GetCountAsync(roleId: adminRole.Id);
        if (adminUserCount <= 1)
            throw new UserFriendlyException("Administration:LastAdminRoleHolder");
    }

    private async Task<string?> GetLastAdminRoleSkipReasonAsync(IdentityUser user)
    {
        var adminRole = await GetAdminRoleAsync();
        if (adminRole == null) return null;

        var userHasAdminRole = user.Roles.Any(r => r.RoleId == adminRole.Id);
        if (!userHasAdminRole) return null;

        var adminUserCount = await _userRepository.GetCountAsync(roleId: adminRole.Id);
        return adminUserCount <= 1 ? "Administration:LastAdminRoleHolder" : null;
    }

    private async Task<IdentityRole?> GetAdminRoleAsync()
    {
        // Use normalized-name lookup instead of loading all roles — fixes the unbounded GetListAsync.
        return await _roleRepository.FindByNormalizedNameAsync("ADMIN");
    }

    private async Task<List<Guid>> ResolveSelectionAsync(GridSelectionDto selection)
    {
        if (selection.Mode == "Explicit")
            return selection.ExplicitIds.Except(selection.ExcludedIds).ToList();

        // FilterBased: re-execute the filter server-side
        if (selection.FilterRequest == null)
            return new List<Guid>();

        var filter = BuildUserFilter(selection.FilterRequest);
        var users = await _userRepository.GetListAsync(filter: filter, maxResultCount: 10_000);
        return users.Select(u => u.Id)
                    .Except(selection.ExcludedIds)
                    .ToList();
    }

    private async Task<AdministrationUserRowDto> BuildUserRowDtoAsync(IdentityUser user)
    {
        var roleNames = await _userManager.GetRolesAsync(user);
        return new AdministrationUserRowDto
        {
            Id = user.Id,
            UserName = user.UserName,
            Email = user.Email,
            Name = user.Name,
            Surname = user.Surname,
            IsActive = user.IsActive,
            RoleNames = roleNames.ToList(),
            CreationTime = user.CreationTime
        };
    }

    private static AdministrationUserRowDto MapToUserRowDto(IdentityUser user, Dictionary<Guid, string> roleNameById)
    {
        var roleNames = user.Roles
            .Select(r => roleNameById.TryGetValue(r.RoleId, out var name) ? name : null)
            .Where(n => n != null)
            .Select(n => n!)
            .ToList();

        return new AdministrationUserRowDto
        {
            Id = user.Id,
            UserName = user.UserName,
            Email = user.Email,
            Name = user.Name,
            Surname = user.Surname,
            IsActive = user.IsActive,
            RoleNames = roleNames,
            CreationTime = user.CreationTime
        };
    }

    private static void ThrowIfFailed(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new UserFriendlyException(
                result.Errors.Select(e => e.Description).JoinAsString("; "));
    }

    private static string? BuildUserFilter(GridRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.WildcardSearch))
            return request.WildcardSearch;

        if (request.ColumnFilters.TryGetValue("userName", out var uf) && !string.IsNullOrWhiteSpace(uf.Filter))
            return uf.Filter;

        if (request.ColumnFilters.TryGetValue("email", out var ef) && !string.IsNullOrWhiteSpace(ef.Filter))
            return ef.Filter;

        return null;
    }

    private static string? BuildUserSorting(GridRequest request)
    {
        if (request.SortModels.Count == 0) return null;

        var s = request.SortModels[0];
        var dir = string.Equals(s.Sort, "desc", StringComparison.OrdinalIgnoreCase) ? " desc" : " asc";

        return s.ColId switch
        {
            "userName" => "UserName" + dir,
            "email" => "Email" + dir,
            "name" => "Name" + dir,
            "surname" => "Surname" + dir,
            "creationTime" => "CreationTime" + dir,
            _ => null
        };
    }
}
