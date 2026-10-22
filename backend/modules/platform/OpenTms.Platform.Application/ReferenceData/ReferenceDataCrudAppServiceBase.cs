using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenTms.Platform.Grid;
using OpenTms.Platform.Localization;
using OpenTms.Platform.NumberSequences;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Repositories;

namespace OpenTms.Platform.ReferenceData;

/// <summary>
/// The template behind every reference-data application service of Orders, Financial and Planning &amp; Execution
/// (design 50: design 41's contract, lifted into Platform; Master Data keeps its own copy for now): paged and sorted
/// lists with one wildcard search over a defined column set, lookups bounded by MaxLookupTake, explicit
/// mapping, uniqueness and in-use checks before the database enforces them, and a bulk delete over a grid
/// selection. The concrete service declares the public methods with their permission attributes and
/// delegates here, so the architecture test sees an [Authorize] on every public method.
/// </summary>
public abstract class ReferenceDataCrudAppServiceBase<TEntity, TRowDto, TDto, TCreateDto, TUpdateDto, TListRequest> : ApplicationService
    where TEntity : ReferenceDataAggregateRoot
    where TListRequest : ReferenceDataListRequestDto
    where TUpdateDto : TCreateDto, IReferenceDataUpdateDto
{
    /// <summary>A bulk delete resolves at most this many rows; beyond it the request is refused (TooManyRows).</summary>
    protected const int MaxBulkDeleteRows = ReferenceDataConsts.MaxBulkDeleteRows;

    protected IRepository<TEntity, Guid> Repository { get; }

    protected ReferenceDataCrudAppServiceBase(IRepository<TEntity, Guid> repository)
    {
        Repository = repository;
    }

    /// <summary>The language the list sorts and resolves names for ("nl", "en").</summary>
    protected static string UiLanguage => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();

    // ── hooks ──────────────────────────────────────────────────────────────────

    /// <summary>The entity filters of the list request (not the search): archived, active, country.</summary>
    protected virtual IQueryable<TEntity> ApplyFilters(IQueryable<TEntity> query, TListRequest request) => query;

    /// <summary>The wildcard search over the entity's defined column set; <paramref name="term"/> is lower-cased.</summary>
    protected abstract IQueryable<TEntity> ApplySearch(IQueryable<TEntity> query, string term);

    /// <summary>Sorts on a row column id; null is the default order. Unknown ids throw UnknownSortColumn.</summary>
    protected abstract IOrderedQueryable<TEntity> ApplySort(IQueryable<TEntity> query, string? sortBy, bool descending);

    /// <summary>Lookups (pickers) show the usable rows only: active currencies, non-archived products.</summary>
    protected virtual IQueryable<TEntity> ApplyLookupFilter(IQueryable<TEntity> query) => query;

    protected abstract Task<List<TRowDto>> ToRowsAsync(List<TEntity> entities);

    protected abstract Task<TDto> ToDtoAsync(TEntity entity);

    protected abstract ReferenceDataLookupDto ToLookup(TEntity entity);

    protected abstract string DisplayNameOf(TEntity entity);

    protected abstract Task<TEntity> CreateEntityAsync(TCreateDto input);

    protected abstract Task ApplyUpdateAsync(TEntity entity, TUpdateDto input);

    /// <summary>Runs after the entity is saved on create and update: relations that need the row to exist.</summary>
    protected virtual Task AfterWriteAsync(TEntity entity, TCreateDto input) => Task.CompletedTask;

    /// <summary>A localized reason when the row may not be deleted (in use, default), else null.</summary>
    protected virtual Task<string?> GetDeleteBlockerAsync(TEntity entity) => Task.FromResult<string?>(null);

    /// <summary>Runs before a soft delete: removes join rows and children the soft delete would leave behind.</summary>
    protected virtual Task BeforeDeleteAsync(TEntity entity) => Task.CompletedTask;

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

        var entities = await AsyncExecuter.ToListAsync(page);

        return new GridResponse<TRowDto>
        {
            Rows = await ToRowsAsync(entities),
            TotalCount = totalCount,
            FilteredCount = filteredCount
        };
    }

    protected async Task<TDto> GetInternalAsync(Guid id)
    {
        var entity = await Repository.GetAsync(id, includeDetails: true);
        return await ToDtoAsync(entity);
    }

    protected async Task<List<ReferenceDataLookupDto>> GetLookupInternalAsync(string? term, int take)
    {
        var all = await Repository.GetQueryableAsync();
        var query = ApplyLookupFilter(all);

        var normalized = NormalizeTerm(term);
        if (normalized != null)
        {
            query = ApplySearch(query, normalized);
        }

        var bounded = Math.Clamp(take, 1, ReferenceDataConsts.MaxLookupTake);
        var entities = await AsyncExecuter.ToListAsync(ApplySort(query, null, false).Take(bounded));
        return entities.Select(ToLookup).ToList();
    }

    protected async Task<TDto> CreateInternalAsync(TCreateDto input)
    {
        var entity = await CreateEntityAsync(input);
        await Repository.InsertAsync(entity, autoSave: true);
        await AfterWriteAsync(entity, input);
        await FlushAsync();
        return await ToDtoAsync(await Repository.GetAsync(entity.Id, includeDetails: true));
    }

    protected async Task<TDto> UpdateInternalAsync(Guid id, TUpdateDto input)
    {
        var entity = await Repository.GetAsync(id, includeDetails: true);

        /* ABP compares this stamp with the stored one on save and throws AbpDbConcurrencyException (409). */
        entity.ConcurrencyStamp = input.ConcurrencyStamp;

        await ApplyUpdateAsync(entity, input);
        await Repository.UpdateAsync(entity, autoSave: true);
        await AfterWriteAsync(entity, input);
        await FlushAsync();
        return await ToDtoAsync(await Repository.GetAsync(entity.Id, includeDetails: true));
    }

    protected async Task DeleteInternalAsync(Guid id)
    {
        var entity = await Repository.GetAsync(id, includeDetails: true);

        var blocker = await GetDeleteBlockerAsync(entity);
        if (blocker != null)
        {
            throw new BusinessException(ReferenceDataErrorCodes.InUse).WithData("reason", blocker);
        }

        await BeforeDeleteAsync(entity);
        await Repository.DeleteAsync(entity, autoSave: true);
    }

    protected async Task<ReferenceDataBulkDeleteResultDto> BulkDeleteInternalAsync(
        GridSelectionDto selection,
        Func<IQueryable<TEntity>, IQueryable<TEntity>> applyFilters)
    {
        List<TEntity> targets;
        if (string.Equals(selection.Mode, "FilterBased", StringComparison.OrdinalIgnoreCase))
        {
            /* With details: BeforeDeleteAsync clears the children a soft delete would leave behind. */
            var all = await Repository.WithDetailsAsync();
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
                throw new BusinessException(ReferenceDataErrorCodes.TooManyRows).WithData("max", MaxBulkDeleteRows);
            }

            targets = ids.Count == 0 ? [] : await Repository.GetListAsync(e => ids.Contains(e.Id), includeDetails: true);
        }

        if (targets.Count > MaxBulkDeleteRows)
        {
            throw new BusinessException(ReferenceDataErrorCodes.TooManyRows).WithData("max", MaxBulkDeleteRows);
        }

        var result = new ReferenceDataBulkDeleteResultDto();
        foreach (var entity in targets)
        {
            var blocker = await GetDeleteBlockerAsync(entity);
            if (blocker != null)
            {
                result.SkippedRows.Add(new ReferenceDataSkippedRowDto { Id = entity.Id, Name = DisplayNameOf(entity), Reason = blocker });
                continue;
            }

            await BeforeDeleteAsync(entity);
            await Repository.DeleteAsync(entity);
            result.DeletedCount++;
        }

        Logger.LogInformation("Bulk delete of {Entity}: {Deleted} deleted, {Skipped} skipped.", typeof(TEntity).Name, result.DeletedCount, result.SkippedRows.Count);
        return result;
    }

    // ── helpers for the concrete services ──────────────────────────────────────

    /// <summary>Writes the pending relation rows (incompatibility pairs) so the response can read them back.</summary>
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

    /// <summary>
    /// Orders by a column of another row through a LEFT JOIN. A correlated subquery in ORDER BY
    /// (<c>Order(q, e =&gt; others.Where(…).Select(…).FirstOrDefault())</c>) runs once per row before the page is cut:
    /// 1,017 ms for 250,000 product relation codes sorted by product name (design 48, volume test); the join is one
    /// hash join and a top-N sort.
    /// </summary>
    protected static IOrderedQueryable<TEntity> OrderByJoined<TOther, TKey>(
        IQueryable<TEntity> query,
        IQueryable<TOther> others,
        Expression<Func<TEntity, Guid?>> foreignKey,
        Expression<Func<TOther, Guid?>> otherKey,
        Expression<Func<JoinedRow<TOther>, TKey>> key,
        bool descending)
        where TOther : class
    {
        var joined = query
            .GroupJoin(others, foreignKey, otherKey, (entity, matches) => new { entity, matches })
            .SelectMany(x => x.matches.DefaultIfEmpty(), (x, other) => new JoinedRow<TOther> { Entity = x.entity, Other = other });
        var ordered = descending ? joined.OrderByDescending(key) : joined.OrderBy(key);

        /* EF Core's queryables are IOrderedQueryable, and the ORDER BY survives the projection. */
        return (IOrderedQueryable<TEntity>)ordered.Select(row => row.Entity);
    }

    /// <summary>A row of <see cref="OrderByJoined{TOther,TKey}"/>: the entity and its joined row, null when none.</summary>
    protected sealed class JoinedRow<TOther>
    {
        public TEntity Entity { get; init; } = default!;

        public TOther? Other { get; init; }
    }

    protected static BusinessException UnknownSortColumn(string sortBy)
        => new BusinessException(ReferenceDataErrorCodes.UnknownSortColumn).WithData("sortBy", sortBy);

    /// <summary>The friendly uniqueness check; the unique index remains the truth (assessment §3.2).</summary>
    protected async Task EnsureUniqueAsync(Expression<Func<TEntity, bool>> conflict, string field, string value)
    {
        if (await Repository.AnyAsync(conflict))
        {
            throw new BusinessException(ReferenceDataErrorCodes.DuplicateKey).WithData("field", field).WithData("value", value);
        }
    }

    /// <summary>Every referenced id must be a visible row of this tenant.</summary>
    protected async Task EnsureAllExistAsync(IReadOnlyCollection<Guid> ids, string field)
    {
        var distinct = ids.Distinct().ToList();
        if (distinct.Count == 0)
        {
            return;
        }

        var found = await Repository.CountAsync(e => distinct.Contains(e.Id));
        if (found != distinct.Count)
        {
            throw new BusinessException(ReferenceDataErrorCodes.ReferenceNotFound).WithData("field", field);
        }
    }

    protected LocalizedName ToLocalizedName(LocalizedNameDto? names)
    {
        var defaultText = names?.GetValueOrDefault(LocalizedName.DefaultCulture)?.Trim();
        if (names == null || string.IsNullOrEmpty(defaultText))
        {
            throw new BusinessException(ReferenceDataErrorCodes.NameRequired).WithData("culture", LocalizedName.DefaultCulture);
        }

        try
        {
            return LocalizedName.Create(names);
        }
        catch (ArgumentException)
        {
            throw new BusinessException(ReferenceDataErrorCodes.NameRequired).WithData("culture", LocalizedName.DefaultCulture);
        }
    }

    protected static LocalizedNameDto ToNamesDto(LocalizedName name)
    {
        var dto = new LocalizedNameDto();
        foreach (var (culture, text) in name.Texts)
        {
            dto[culture] = text;
        }

        return dto;
    }

    protected static string Resolve(LocalizedName name) => name.Resolve(CultureInfo.CurrentUICulture.Name);

    protected static void MapAudit(TEntity entity, ReferenceDataDetailDtoBase dto)
    {
        dto.Id = entity.Id;
        dto.Description = entity.Description;
        dto.CreationTime = entity.CreationTime;
        dto.CreatorId = entity.CreatorId;
        dto.LastModificationTime = entity.LastModificationTime;
        dto.LastModifierId = entity.LastModifierId;
        dto.ConcurrencyStamp = entity.ConcurrencyStamp;
    }

    // ── defaults (assessment §3.8: one enforced default per scope) ──────────────

    /// <summary>
    /// Makes <paramref name="id"/> the default of its scope (the tenant, or the rows matching <paramref name="scope"/>):
    /// the old default is cleared and saved first, so the unique partial index never sees two.
    /// </summary>
    protected async Task SetDefaultInternalAsync(Guid id, Func<TEntity, Expression<Func<TEntity, bool>>>? scope = null)
    {
        var entity = await Repository.GetAsync(id);
        if (entity is not IHasReferenceDataDefault target)
        {
            throw new InvalidOperationException($"{typeof(TEntity).Name} has no default.");
        }

        if (target.IsArchived)
        {
            throw new BusinessException(ReferenceDataErrorCodes.DefaultRequired).WithData("reason", "archived");
        }

        if (target.IsDefault)
        {
            return;
        }

        var inScope = scope?.Invoke(entity);
        var current = await Repository.GetListAsync(e => e.Id != id);
        foreach (var other in inScope == null ? current : current.Where(inScope.Compile()).ToList())
        {
            if (other is IHasReferenceDataDefault { IsDefault: true } previous)
            {
                previous.SetDefault(false);
                await Repository.UpdateAsync(other);
            }
        }

        await FlushAsync();
        target.SetDefault(true);
        await Repository.UpdateAsync(entity, autoSave: true);
    }

    /// <summary>A default row cannot be archived or deleted (assessment §3.8): make another row the default first.</summary>
    protected static void EnsureNotDefault(TEntity entity, string action)
    {
        if (entity is IHasReferenceDataDefault { IsDefault: true })
        {
            throw new BusinessException(ReferenceDataErrorCodes.DefaultRequired).WithData("reason", action);
        }
    }

    // ── numbering (design 50) ──────────────────────────────────────────────────

    protected INumberSequenceGenerator NumberSequences => LazyServiceProvider.LazyGetRequiredService<INumberSequenceGenerator>();

    protected static NumberSequenceFormat ToNumberSequenceFormat(NumberSequenceFormatDto? dto)
    {
        if (dto == null)
        {
            throw new BusinessException(ReferenceDataErrorCodes.InvalidNumberSequence).WithData("field", "numbering").WithData("value", "");
        }

        return NumberSequenceFormat.Create(dto.Prefix, dto.IncludeYear, dto.IncludeMonth, dto.Separator, dto.Digits);
    }

    protected static NumberSequenceFormatDto ToNumberingDto(NumberSequenceFormat format) => new()
    {
        Prefix = format.Prefix,
        IncludeYear = format.IncludeYear,
        IncludeMonth = format.IncludeMonth,
        Separator = format.Separator,
        Digits = format.Digits
    };

    /// <summary>The example the list shows: the format applied to counter 1 today.</summary>
    protected NumberSequenceExample NumberingExampleOf(NumberSequenceFormat format)
        => new(format.Format(1, DateOnly.FromDateTime(Clock.Now)));

    /// <summary>Continues numbering at <paramref name="nextNumber"/> when given and higher than the current next number.</summary>
    protected async Task ApplyNextNumberAsync(string sequenceKey, NumberSequenceFormat format, long? nextNumber)
    {
        if (nextNumber == null)
        {
            return;
        }

        var current = await NumberSequences.PeekNextValueAsync(sequenceKey, format);
        if (nextNumber.Value != current)
        {
            await NumberSequences.SetNextValueAsync(sequenceKey, format, nextNumber.Value);
        }
    }

    protected readonly record struct NumberSequenceExample(string Value);
}
