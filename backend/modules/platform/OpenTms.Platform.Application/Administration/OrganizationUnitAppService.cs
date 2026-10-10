using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenTms.Platform.Grid;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Identity;

namespace OpenTms.Platform.Administration;

/* Organization units are ABP Identity's tree of units with user memberships. The tree of a
 * tenant is small (tens of units), so one bounded query loads it and the hierarchy, the
 * paths, the child flags and the search run in memory; the member counts come from one
 * bounded users query, as the roles list does. Every repository call runs under the tenant
 * data filter: a unit or user of another tenant is simply not found (constitution rule 1). */
[RemoteService(IsEnabled = false)]
public class OrganizationUnitAppService : PlatformAppServiceBase, IOrganizationUnitAppService
{
    // Bounded like the roles list (design #8 §Cost & SLO: tens of units and users per tenant).
    private const int MaxUnitCount = 10_000;
    private const int MaxUserCountForMemberCounts = 10_000;
    private const int MaxRoleFetchCount = 1_000;
    private const int MaxSelectionCount = 10_000;

    private readonly IOrganizationUnitRepository _organizationUnitRepository;
    private readonly IIdentityUserRepository _userRepository;
    private readonly IIdentityRoleRepository _roleRepository;
    private readonly OrganizationUnitManager _organizationUnitManager;
    private readonly IdentityUserManager _userManager;

    public OrganizationUnitAppService(
        IOrganizationUnitRepository organizationUnitRepository,
        IIdentityUserRepository userRepository,
        IIdentityRoleRepository roleRepository,
        OrganizationUnitManager organizationUnitManager,
        IdentityUserManager userManager)
    {
        _organizationUnitRepository = organizationUnitRepository;
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _organizationUnitManager = organizationUnitManager;
        _userManager = userManager;
    }

    // ── Units ─────────────────────────────────────────────────────────────────

    public async Task<GridResponse<AdministrationOrganizationUnitRowDto>> GetListAsync(GridRequest request, Guid? parentId)
    {
        var tree = await LoadTreeAsync();
        var searchTerm = BuildSearchTerm(request);

        IEnumerable<OrganizationUnit> scope = searchTerm != null
            // A search covers the whole tree and answers with a flat list (the path column tells where a match sits).
            ? tree.Units.Where(u => u.DisplayName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
            : tree.Units.Where(u => u.ParentId == parentId);

        var rows = scope
            .Select(u => MapToRowDto(u, tree, hasChildren: searchTerm == null && tree.ChildCountById.ContainsKey(u.Id)))
            .ToList();
        rows = SortRows(rows, request);

        var totalCount = searchTerm != null ? tree.Units.Count : rows.Count;
        var page = rows.Skip(request.StartRow).Take(request.EndRow - request.StartRow).ToList();

        return new GridResponse<AdministrationOrganizationUnitRowDto>
        {
            Rows = page,
            TotalCount = totalCount,
            FilteredCount = rows.Count
        };
    }

    public async Task<List<AdministrationOrganizationUnitRowDto>> GetAllAsync()
    {
        var tree = await LoadTreeAsync();
        return tree.Units
            .Select(u => MapToRowDto(u, tree, hasChildren: tree.ChildCountById.ContainsKey(u.Id)))
            .ToList();
    }

    public async Task<AdministrationOrganizationUnitRowDto> CreateAsync(AdministrationOrganizationUnitCreateDto input)
    {
        if (input.ParentId != null)
            await FindUnitOrThrowAsync(input.ParentId.Value);

        var unit = new OrganizationUnit(GuidGenerator.Create(), input.DisplayName.Trim(), input.ParentId, CurrentTenant.Id);
        await _organizationUnitManager.CreateAsync(unit);

        Logger.LogInformation("Organization unit {OrganizationUnitId} created by {ActorId}", unit.Id, CurrentUser.Id);

        return await BuildRowDtoAsync(unit);
    }

    public async Task<AdministrationOrganizationUnitRowDto> UpdateAsync(Guid id, AdministrationOrganizationUnitUpdateDto input)
    {
        var unit = await FindUnitOrThrowAsync(id);

        if (input.ParentId != unit.ParentId)
        {
            if (input.ParentId != null)
            {
                var parent = await FindUnitOrThrowAsync(input.ParentId.Value);
                // The parent may not be the unit itself or anything below it (codes are hierarchical).
                if (parent.Id == unit.Id || parent.Code.StartsWith(unit.Code + ".", StringComparison.Ordinal))
                    throw new OrganizationUnitMoveException(L["Administration:OrganizationUnitCannotMoveIntoItself"]);
            }

            // The manager recodes the unit and its whole subtree under the new parent.
            await _organizationUnitManager.MoveAsync(id, input.ParentId);
            unit = await FindUnitOrThrowAsync(id);
            Logger.LogInformation("Organization unit {OrganizationUnitId} moved under {ParentId} by {ActorId}", id, input.ParentId, CurrentUser.Id);
        }

        if (!string.Equals(unit.DisplayName, input.DisplayName.Trim(), StringComparison.Ordinal))
        {
            unit.DisplayName = input.DisplayName.Trim();
            await _organizationUnitManager.UpdateAsync(unit);
        }

        Logger.LogInformation("Organization unit {OrganizationUnitId} updated by {ActorId}", id, CurrentUser.Id);

        return await BuildRowDtoAsync(unit);
    }

    public async Task DeleteAsync(Guid id, bool force = false)
    {
        var unit = await FindUnitOrThrowAsync(id);

        if (!force)
        {
            var children = await _organizationUnitManager.FindChildrenAsync(id, recursive: true);
            var memberCount = await _organizationUnitRepository.GetMembersCountAsync(unit);
            if (children.Count > 0 || memberCount > 0)
                throw new OrganizationUnitInUseException(unit.DisplayName, children.Count, memberCount);
        }

        // The manager removes the sub-units and every membership before the unit itself.
        await _organizationUnitManager.DeleteAsync(id);

        Logger.LogInformation("Organization unit {OrganizationUnitId} deleted by {ActorId} (force={Force})", id, CurrentUser.Id, force);
    }

    // ── Members ───────────────────────────────────────────────────────────────

    public async Task<GridResponse<AdministrationUserRowDto>> GetMembersAsync(Guid id, GridRequest request)
    {
        var unit = await FindUnitOrThrowAsync(id);
        var filter = BuildUserFilter(request);
        var roleNameById = await LoadRoleNamesAsync();

        var totalCount = await _organizationUnitRepository.GetMembersCountAsync(unit);
        var filteredCount = filter != null ? await _organizationUnitRepository.GetMembersCountAsync(unit, filter) : totalCount;
        var users = await _organizationUnitRepository.GetMembersAsync(
            unit,
            sorting: BuildUserSorting(request),
            maxResultCount: request.EndRow - request.StartRow,
            skipCount: request.StartRow,
            filter: filter,
            includeDetails: true);

        return new GridResponse<AdministrationUserRowDto>
        {
            Rows = users.Select(u => AdministrationUserRowMapper.Map(u, roleNameById, Clock.Now)).ToList(),
            TotalCount = totalCount,
            FilteredCount = filteredCount
        };
    }

    public async Task<GridResponse<AdministrationUserRowDto>> GetUnaddedUsersAsync(Guid id, GridRequest request)
    {
        var unit = await FindUnitOrThrowAsync(id);
        var filter = BuildUserFilter(request);
        var roleNameById = await LoadRoleNamesAsync();

        var totalCount = await _organizationUnitRepository.GetUnaddedUsersCountAsync(unit);
        var filteredCount = filter != null ? await _organizationUnitRepository.GetUnaddedUsersCountAsync(unit, filter) : totalCount;
        var users = await _organizationUnitRepository.GetUnaddedUsersAsync(
            unit,
            sorting: BuildUserSorting(request),
            maxResultCount: request.EndRow - request.StartRow,
            skipCount: request.StartRow,
            filter: filter,
            includeDetails: true);

        return new GridResponse<AdministrationUserRowDto>
        {
            Rows = users.Select(u => AdministrationUserRowMapper.Map(u, roleNameById, Clock.Now)).ToList(),
            TotalCount = totalCount,
            FilteredCount = filteredCount
        };
    }

    public async Task<OrganizationUnitMembersResponseDto> AddMembersAsync(Guid id, OrganizationUnitMembersRequestDto input)
    {
        var unit = await FindUnitOrThrowAsync(id);
        var userIds = await ResolveSelectionAsync(unit, input.Selection, unadded: true);

        var affected = 0;
        foreach (var userId in userIds)
        {
            // A user of another tenant, or one deleted meanwhile, is not found: nothing to add (idempotent, rule 3).
            var user = await _userRepository.FindAsync(userId, includeDetails: true);
            if (user == null || user.IsInOrganizationUnit(unit.Id))
                continue;

            await _userManager.AddToOrganizationUnitAsync(user, unit);
            affected++;
            Logger.LogInformation("User {UserId} added to organization unit {OrganizationUnitId} by {ActorId}", userId, id, CurrentUser.Id);
        }

        return new OrganizationUnitMembersResponseDto { AffectedCount = affected };
    }

    public async Task<OrganizationUnitMembersResponseDto> RemoveMembersAsync(Guid id, OrganizationUnitMembersRequestDto input)
    {
        var unit = await FindUnitOrThrowAsync(id);
        var userIds = await ResolveSelectionAsync(unit, input.Selection, unadded: false);

        var affected = 0;
        foreach (var userId in userIds)
        {
            var user = await _userRepository.FindAsync(userId, includeDetails: true);
            if (user == null || !user.IsInOrganizationUnit(unit.Id))
                continue;

            await _userManager.RemoveFromOrganizationUnitAsync(user, unit);
            affected++;
            Logger.LogInformation("User {UserId} removed from organization unit {OrganizationUnitId} by {ActorId}", userId, id, CurrentUser.Id);
        }

        return new OrganizationUnitMembersResponseDto { AffectedCount = affected };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private sealed record UnitTree(
        List<OrganizationUnit> Units,
        Dictionary<Guid, OrganizationUnit> UnitById,
        Dictionary<Guid, int> ChildCountById,
        Dictionary<Guid, long> MemberCountById);

    private async Task<UnitTree> LoadTreeAsync()
    {
        var units = await _organizationUnitRepository.GetListAsync(sorting: nameof(OrganizationUnit.Code), maxResultCount: MaxUnitCount);
        var unitById = units.ToDictionary(u => u.Id);
        var childCountById = units
            .Where(u => u.ParentId != null)
            .GroupBy(u => u.ParentId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());

        // ABP's user repository has no count-by-unit query; group one bounded users query in memory.
        var users = await _userRepository.GetListAsync(includeDetails: true, maxResultCount: MaxUserCountForMemberCounts);
        var memberCountById = users
            .SelectMany(u => u.OrganizationUnits.Select(m => m.OrganizationUnitId))
            .GroupBy(ouId => ouId)
            .ToDictionary(g => g.Key, g => (long)g.Count());

        return new UnitTree(units, unitById, childCountById, memberCountById);
    }

    private static AdministrationOrganizationUnitRowDto MapToRowDto(OrganizationUnit unit, UnitTree tree, bool hasChildren)
    {
        var parent = unit.ParentId != null && tree.UnitById.TryGetValue(unit.ParentId.Value, out var p) ? p : null;
        return new AdministrationOrganizationUnitRowDto
        {
            Id = unit.Id,
            DisplayName = unit.DisplayName,
            Code = unit.Code,
            ParentId = unit.ParentId,
            ParentDisplayName = parent?.DisplayName,
            Path = BuildPath(unit, tree.UnitById),
            HasChildren = hasChildren,
            MemberCount = tree.MemberCountById.TryGetValue(unit.Id, out var c) ? c : 0,
            CreationTime = unit.CreationTime,
            LastModificationTime = unit.LastModificationTime
        };
    }

    private static string BuildPath(OrganizationUnit unit, Dictionary<Guid, OrganizationUnit> unitById)
    {
        var names = new List<string>();
        var current = unit;
        var guard = 0;
        while (current != null && guard++ < 64)
        {
            names.Add(current.DisplayName);
            current = current.ParentId != null && unitById.TryGetValue(current.ParentId.Value, out var parent) ? parent : null;
        }
        names.Reverse();
        return string.Join(" / ", names);
    }

    /* Maps the entity just written. It is not re-read: inside the unit of work the insert is
     * not flushed yet, so a query by id would not find it. The tree supplies the parent name
     * and the counts. */
    private async Task<AdministrationOrganizationUnitRowDto> BuildRowDtoAsync(OrganizationUnit unit)
    {
        var tree = await LoadTreeAsync();
        return MapToRowDto(unit, tree, hasChildren: tree.ChildCountById.ContainsKey(unit.Id));
    }

    private async Task<OrganizationUnit> FindUnitOrThrowAsync(Guid id)
    {
        var unit = await _organizationUnitRepository.FindAsync(id);
        if (unit == null)
            throw new EntityNotFoundException(typeof(OrganizationUnit), id);
        return unit;
    }

    private async Task<Dictionary<Guid, string>> LoadRoleNamesAsync()
    {
        var roles = await _roleRepository.GetListAsync(maxResultCount: MaxRoleFetchCount);
        return roles.ToDictionary(r => r.Id, r => r.Name);
    }

    private async Task<List<Guid>> ResolveSelectionAsync(OrganizationUnit unit, GridSelectionDto selection, bool unadded)
    {
        if (selection.Mode == "Explicit")
            return selection.ExplicitIds.Except(selection.ExcludedIds).Distinct().ToList();

        // FilterBased: re-run the filter server-side over the list the selection was made in.
        if (selection.FilterRequest == null)
            return new List<Guid>();

        var filter = BuildUserFilter(selection.FilterRequest);
        var users = unadded
            ? await _organizationUnitRepository.GetUnaddedUsersAsync(unit, maxResultCount: MaxSelectionCount, filter: filter)
            : await _organizationUnitRepository.GetMembersAsync(unit, maxResultCount: MaxSelectionCount, filter: filter);
        return users.Select(u => u.Id).Except(selection.ExcludedIds).ToList();
    }

    private static List<AdministrationOrganizationUnitRowDto> SortRows(List<AdministrationOrganizationUnitRowDto> rows, GridRequest request)
    {
        if (request.SortModels.Count == 0) return rows;

        var s = request.SortModels[0];
        var desc = string.Equals(s.Sort, "desc", StringComparison.OrdinalIgnoreCase);
        Func<AdministrationOrganizationUnitRowDto, object>? key = s.ColId switch
        {
            "displayName" => r => r.DisplayName,
            "memberCount" => r => r.MemberCount,
            "creationTime" => r => r.CreationTime,
            "path" => r => r.Path,
            _ => null
        };
        if (key == null) return rows;

        var comparer = s.ColId is "displayName" or "path" ? (IComparer<object>)StringObjectComparer.Instance : Comparer<object>.Default;
        return (desc ? rows.OrderByDescending(key, comparer) : rows.OrderBy(key, comparer)).ToList();
    }

    private sealed class StringObjectComparer : IComparer<object>
    {
        public static readonly StringObjectComparer Instance = new();
        public int Compare(object? x, object? y) =>
            string.Compare(x as string, y as string, StringComparison.CurrentCultureIgnoreCase);
    }

    private static string? BuildSearchTerm(GridRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.WildcardSearch))
            return request.WildcardSearch.Trim();

        if (request.ColumnFilters.TryGetValue("displayName", out var f) && !string.IsNullOrWhiteSpace(f.Filter))
            return f.Filter.Trim();

        return null;
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
