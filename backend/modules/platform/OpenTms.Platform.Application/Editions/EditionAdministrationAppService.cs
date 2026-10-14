using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using OpenTms.Platform.Grid;
using OpenTms.Platform.Permissions;
using OpenTms.Platform.Tenants;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;

namespace OpenTms.Platform.Editions;

/* The Editions app (host only): docs/designs/tenants-and-editions.md. A host has a handful of editions,
 * so the list is read whole and paged in memory, like the other host-owned lists. */
[RemoteService(IsEnabled = false)]
[Authorize(PlatformPermissions.Editions.Default)]
public class EditionAdministrationAppService : PlatformAppServiceBase, IEditionAdministrationAppService
{
    private readonly IRepository<Edition, Guid> _editionRepository;
    private readonly ITenantEditionQuery _tenantEditionQuery;

    public EditionAdministrationAppService(
        IRepository<Edition, Guid> editionRepository,
        ITenantEditionQuery tenantEditionQuery)
    {
        _editionRepository = editionRepository;
        _tenantEditionQuery = tenantEditionQuery;
    }

    public async Task<GridResponse<EditionRowDto>> GetListAsync(GridRequest request)
    {
        HostOnlyGuard.Ensure(CurrentTenant);

        var editions = await _editionRepository.GetListAsync();
        var counts = await _tenantEditionQuery.CountTenantsByEditionAsync(editions.Select(e => e.Id).ToList());
        var all = editions.Select(e => Map(e, counts)).ToList();

        var search = request.WildcardSearch?.Trim();
        if (string.IsNullOrEmpty(search) && request.ColumnFilters.TryGetValue("displayName", out var nameFilter))
        {
            search = nameFilter.Filter?.Trim();
        }

        IEnumerable<EditionRowDto> filtered = string.IsNullOrEmpty(search)
            ? all
            : all.Where(e => e.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase));

        var sort = request.SortModels.FirstOrDefault();
        var descending = string.Equals(sort?.Sort, "desc", StringComparison.OrdinalIgnoreCase);
        filtered = sort?.ColId switch
        {
            "tenantCount" => descending ? filtered.OrderByDescending(e => e.TenantCount) : filtered.OrderBy(e => e.TenantCount),
            "creationTime" => descending ? filtered.OrderByDescending(e => e.CreationTime) : filtered.OrderBy(e => e.CreationTime),
            _ => descending
                ? filtered.OrderByDescending(e => e.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                : filtered.OrderBy(e => e.DisplayName, StringComparer.CurrentCultureIgnoreCase)
        };

        var filteredList = filtered.ToList();
        return new GridResponse<EditionRowDto>
        {
            Rows = filteredList.Skip(request.StartRow).Take(Math.Max(0, request.EndRow - request.StartRow)).ToList(),
            TotalCount = all.Count,
            FilteredCount = filteredList.Count
        };
    }

    public async Task<EditionRowDto> GetAsync(Guid id)
    {
        HostOnlyGuard.Ensure(CurrentTenant);

        var edition = await _editionRepository.GetAsync(id);
        return Map(edition, await _tenantEditionQuery.CountTenantsByEditionAsync(new[] { id }));
    }

    [Authorize(PlatformPermissions.Editions.Create)]
    public async Task<EditionRowDto> CreateAsync(EditionCreateUpdateDto input)
    {
        HostOnlyGuard.Ensure(CurrentTenant);
        await EnsureNameIsFreeAsync(input.DisplayName, null);

        var edition = await _editionRepository.InsertAsync(new Edition(GuidGenerator.Create(), input.DisplayName), autoSave: true);

        Logger.LogInformation("Edition {EditionId} created by {ActorId}", edition.Id, CurrentUser.Id);
        return Map(edition, new Dictionary<Guid, int>());
    }

    [Authorize(PlatformPermissions.Editions.Update)]
    public async Task<EditionRowDto> UpdateAsync(Guid id, EditionCreateUpdateDto input)
    {
        HostOnlyGuard.Ensure(CurrentTenant);

        var edition = await _editionRepository.GetAsync(id);
        await EnsureNameIsFreeAsync(input.DisplayName, id);
        edition.SetDisplayName(input.DisplayName);
        await _editionRepository.UpdateAsync(edition, autoSave: true);

        Logger.LogInformation("Edition {EditionId} updated by {ActorId}", id, CurrentUser.Id);
        return Map(edition, await _tenantEditionQuery.CountTenantsByEditionAsync(new[] { id }));
    }

    [Authorize(PlatformPermissions.Editions.Delete)]
    public async Task DeleteAsync(Guid id)
    {
        HostOnlyGuard.Ensure(CurrentTenant);

        var edition = await _editionRepository.GetAsync(id);
        var counts = await _tenantEditionQuery.CountTenantsByEditionAsync(new[] { id });
        if (counts.TryGetValue(id, out var tenantCount) && tenantCount > 0)
        {
            throw new EditionInUseException(edition.DisplayName, tenantCount);
        }

        await _editionRepository.DeleteAsync(edition);

        Logger.LogInformation("Edition {EditionId} deleted by {ActorId}", id, CurrentUser.Id);
    }

    private async Task EnsureNameIsFreeAsync(string displayName, Guid? exceptId)
    {
        var name = displayName.Trim();
        if (await _editionRepository.AnyAsync(e => e.DisplayName == name && e.Id != exceptId))
        {
            throw new UserFriendlyException(L["Editions:NameTaken", name]);
        }
    }

    private static EditionRowDto Map(Edition edition, IReadOnlyDictionary<Guid, int> counts) => new()
    {
        Id = edition.Id,
        DisplayName = edition.DisplayName,
        TenantCount = counts.TryGetValue(edition.Id, out var count) ? count : 0,
        CreationTime = edition.CreationTime
    };
}
