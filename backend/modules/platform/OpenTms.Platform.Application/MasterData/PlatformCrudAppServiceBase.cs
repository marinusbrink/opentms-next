using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenTms.Platform.Grid;
using OpenTms.Platform.Localization;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;

namespace OpenTms.Platform;

/// <summary>
/// The template behind every Platform master-data application service (design 49), the Platform counterpart of Master
/// Data's MasterDataCrudAppServiceBase (Platform references no domain module): paged and sorted lists with one wildcard
/// search over a defined column set, lookups bounded by MaxLookupTake, explicit mapping, uniqueness checks before the
/// database enforces them, and a bulk delete over a grid selection. The concrete service declares the public methods
/// with their permission and feature attributes and delegates here, so the architecture test sees an [Authorize] on
/// every public method.
/// </summary>
public abstract class PlatformCrudAppServiceBase<TEntity, TRowDto, TDto, TCreateDto, TUpdateDto, TListRequest> : PlatformAppServiceBase
    where TEntity : PlatformMasterDataAggregateRoot
    where TListRequest : PlatformMasterDataListRequestDto
    where TUpdateDto : TCreateDto, IPlatformMasterDataUpdateDto
{
    /// <summary>A bulk delete resolves at most this many rows; beyond it the request is refused (TooManyRows).</summary>
    protected const int MaxBulkDeleteRows = 1000;

    protected IRepository<TEntity, Guid> Repository { get; }

    protected PlatformCrudAppServiceBase(IRepository<TEntity, Guid> repository)
    {
        Repository = repository;
    }

    /// <summary>The language the list sorts and resolves names for ("nl", "en").</summary>
    protected static string UiLanguage => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();

    // ── hooks ──────────────────────────────────────────────────────────────────

    /// <summary>The entity filters of the list request (not the search): purpose, archived.</summary>
    protected virtual IQueryable<TEntity> ApplyFilters(IQueryable<TEntity> query, TListRequest request) => query;

    /// <summary>The wildcard search over the entity's defined column set; <paramref name="term"/> is lower-cased.</summary>
    protected abstract IQueryable<TEntity> ApplySearch(IQueryable<TEntity> query, string term);

    /// <summary>Sorts on a row column id; null is the default order. Unknown ids throw UnknownSortColumn.</summary>
    protected abstract IOrderedQueryable<TEntity> ApplySort(IQueryable<TEntity> query, string? sortBy, bool descending);

    /// <summary>Lookups (pickers) show the usable rows only: non-archived report definitions and document types.</summary>
    protected virtual IQueryable<TEntity> ApplyLookupFilter(IQueryable<TEntity> query) => query;

    protected abstract Task<List<TRowDto>> ToRowsAsync(List<TEntity> entities);

    /// <summary>
    /// Reads a sorted list page as rows. The default loads the entities and maps them with <see cref="ToRowsAsync"/>; a
    /// service whose entity holds large columns the row does not show (an e-mail template's body) projects instead.
    /// </summary>
    protected virtual async Task<List<TRowDto>> ReadRowsAsync(IQueryable<TEntity> page)
        => await ToRowsAsync(await AsyncExecuter.ToListAsync(page));

    /// <summary>Reads a sorted, bounded lookup query; overridable for the same reason as <see cref="ReadRowsAsync"/>.</summary>
    protected virtual async Task<List<PlatformMasterDataLookupDto>> ReadLookupsAsync(IQueryable<TEntity> query)
        => (await AsyncExecuter.ToListAsync(query)).Select(ToLookup).ToList();

    protected abstract Task<TDto> ToDtoAsync(TEntity entity);

    protected abstract PlatformMasterDataLookupDto ToLookup(TEntity entity);

    protected abstract string DisplayNameOf(TEntity entity);

    protected abstract Task<TEntity> CreateEntityAsync(TCreateDto input);

    protected abstract Task ApplyUpdateAsync(TEntity entity, TUpdateDto input);

    /// <summary>Runs once before a bulk delete checks its rows: locks and batch reads that GetDeleteBlockerAsync then uses.</summary>
    protected virtual Task PrepareBulkDeleteAsync(List<TEntity> targets) => Task.CompletedTask;

    /// <summary>A localized reason when the row may not be deleted (in use), else null.</summary>
    protected virtual Task<string?> GetDeleteBlockerAsync(TEntity entity) => Task.FromResult<string?>(null);

    // ── template methods ───────────────────────────────────────────────────────

    protected async Task<GridResponse<TRowDto>> GetListInternalAsync(TListRequest input)
    {
        var all = await Repository.GetQueryableAsync();
        var filtered = ApplyFilters(all, input);

        var term = NormalizeTerm(input.WildcardSearch);
        var searched = term == null ? filtered : ApplySearch(filtered, term);

        var totalCount = await AsyncExecuter.LongCountAsync(filtered);
        var filteredCount = term == null ? totalCount : await AsyncExecuter.LongCountAsync(searched);

        var page = ApplySort(searched, input.SortBy, input.Descending)
            .Skip(input.StartRow)
            .Take(input.EndRow - input.StartRow);

        return new GridResponse<TRowDto>
        {
            Rows = await ReadRowsAsync(page),
            TotalCount = totalCount,
            FilteredCount = filteredCount
        };
    }

    protected async Task<TDto> GetInternalAsync(Guid id)
    {
        var entity = await Repository.GetAsync(id, includeDetails: true);
        return await ToDtoAsync(entity);
    }

    protected async Task<List<PlatformMasterDataLookupDto>> GetLookupInternalAsync(string? term, int take)
    {
        var all = await Repository.GetQueryableAsync();
        var query = ApplyLookupFilter(all);

        var normalized = NormalizeTerm(term);
        if (normalized != null)
        {
            query = ApplySearch(query, normalized);
        }

        var bounded = Math.Clamp(take, 1, PlatformMasterDataConsts.MaxLookupTake);
        return await ReadLookupsAsync(ApplySort(query, null, false).Take(bounded));
    }

    protected async Task<TDto> CreateInternalAsync(TCreateDto input)
    {
        var entity = await CreateEntityAsync(input);
        await Repository.InsertAsync(entity, autoSave: true);
        return await ToDtoAsync(await Repository.GetAsync(entity.Id, includeDetails: true));
    }

    protected async Task<TDto> UpdateInternalAsync(Guid id, TUpdateDto input)
    {
        var entity = await Repository.GetAsync(id, includeDetails: true);

        /* ABP compares this stamp with the stored one on save and throws AbpDbConcurrencyException (409). */
        entity.ConcurrencyStamp = input.ConcurrencyStamp;

        await ApplyUpdateAsync(entity, input);
        await Repository.UpdateAsync(entity, autoSave: true);
        return await ToDtoAsync(await Repository.GetAsync(entity.Id, includeDetails: true));
    }

    protected async Task DeleteInternalAsync(Guid id)
    {
        var entity = await Repository.GetAsync(id, includeDetails: true);

        var blocker = await GetDeleteBlockerAsync(entity);
        if (blocker != null)
        {
            throw new BusinessException(PlatformErrorCodes.InUse).WithData("reason", blocker);
        }

        await Repository.DeleteAsync(entity, autoSave: true);
    }

    protected async Task<PlatformMasterDataBulkDeleteResultDto> BulkDeleteInternalAsync(
        GridSelectionDto selection,
        Func<IQueryable<TEntity>, IQueryable<TEntity>> applyFilters)
    {
        List<TEntity> targets;
        if (string.Equals(selection.Mode, "FilterBased", StringComparison.OrdinalIgnoreCase))
        {
            /* No details: a soft delete touches the parent only. */
            var all = await Repository.GetQueryableAsync();
            var query = applyFilters(all);
            var term = NormalizeTerm(selection.FilterRequest?.WildcardSearch);
            if (term != null)
            {
                query = ApplySearch(query, term);
            }

            if (selection.ExcludedIds.Count > 0)
            {
                var excluded = selection.ExcludedIds;
                query = query.Where(e => !excluded.Contains(e.Id));
            }

            targets = await AsyncExecuter.ToListAsync(ApplySort(query, null, false).Take(MaxBulkDeleteRows + 1));
        }
        else
        {
            var ids = selection.ExplicitIds.Distinct().ToList();
            if (ids.Count > MaxBulkDeleteRows)
            {
                throw new BusinessException(PlatformErrorCodes.TooManyRows).WithData("max", MaxBulkDeleteRows);
            }

            targets = ids.Count == 0 ? [] : await Repository.GetListAsync(e => ids.Contains(e.Id), includeDetails: false);
        }

        if (targets.Count > MaxBulkDeleteRows)
        {
            throw new BusinessException(PlatformErrorCodes.TooManyRows).WithData("max", MaxBulkDeleteRows);
        }

        await PrepareBulkDeleteAsync(targets);

        var result = new PlatformMasterDataBulkDeleteResultDto();
        foreach (var entity in targets)
        {
            var blocker = await GetDeleteBlockerAsync(entity);
            if (blocker != null)
            {
                result.SkippedRows.Add(new PlatformMasterDataSkippedRowDto { Id = entity.Id, Name = DisplayNameOf(entity), Reason = blocker });
                continue;
            }

            await Repository.DeleteAsync(entity);
            result.DeletedCount++;
        }

        Logger.LogInformation("Bulk delete of {Entity}: {Deleted} deleted, {Skipped} skipped.", typeof(TEntity).Name, result.DeletedCount, result.SkippedRows.Count);
        return result;
    }

    // ── helpers for the concrete services ──────────────────────────────────────

    /// <summary>Writes the pending changes, so a cleared default leaves the partial unique index before the new one enters it.</summary>
    protected async Task FlushAsync()
    {
        if (UnitOfWorkManager.Current != null)
        {
            await UnitOfWorkManager.Current.SaveChangesAsync();
        }
    }

    protected static string? NormalizeTerm(string? term)
    {
        var value = term?.Trim().ToLowerInvariant();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    protected static IOrderedQueryable<TEntity> Order<TKey>(IQueryable<TEntity> query, Expression<Func<TEntity, TKey>> key, bool descending)
        => descending ? query.OrderByDescending(key) : query.OrderBy(key);

    protected static BusinessException UnknownSortColumn(string sortBy)
        => new BusinessException(PlatformErrorCodes.UnknownSortColumn).WithData("sortBy", sortBy);

    /// <summary>The friendly uniqueness check; the unique index remains the truth (assessment §3.2).</summary>
    protected async Task EnsureUniqueAsync(Expression<Func<TEntity, bool>> conflict, string field, string value)
    {
        if (await Repository.AnyAsync(conflict))
        {
            throw new BusinessException(PlatformErrorCodes.DuplicateKey).WithData("field", field).WithData("value", value);
        }
    }

    protected LocalizedName ToLocalizedName(PlatformLocalizedNameDto? names)
    {
        var defaultText = names?.GetValueOrDefault(LocalizedName.DefaultCulture)?.Trim();
        if (names == null || string.IsNullOrEmpty(defaultText))
        {
            throw new BusinessException(PlatformErrorCodes.NameRequired).WithData("culture", LocalizedName.DefaultCulture);
        }

        try
        {
            return LocalizedName.Create(names);
        }
        catch (ArgumentException)
        {
            throw new BusinessException(PlatformErrorCodes.NameRequired).WithData("culture", LocalizedName.DefaultCulture);
        }
    }

    protected static PlatformLocalizedNameDto ToNamesDto(LocalizedName name)
    {
        var dto = new PlatformLocalizedNameDto();
        foreach (var (culture, text) in name.Texts)
        {
            dto[culture] = text;
        }

        return dto;
    }

    protected static string Resolve(LocalizedName name) => name.Resolve(CultureInfo.CurrentUICulture.Name);

    protected static void MapAudit(TEntity entity, PlatformMasterDataDetailDtoBase dto)
    {
        dto.Id = entity.Id;
        dto.Description = entity.Description;
        dto.CreationTime = entity.CreationTime;
        dto.CreatorId = entity.CreatorId;
        dto.LastModificationTime = entity.LastModificationTime;
        dto.LastModifierId = entity.LastModifierId;
        dto.ConcurrencyStamp = entity.ConcurrencyStamp;
    }
}
