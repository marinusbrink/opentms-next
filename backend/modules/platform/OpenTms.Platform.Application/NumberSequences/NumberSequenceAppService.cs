using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using OpenTms.Platform.Features;
using OpenTms.Platform.Grid;
using OpenTms.Platform.Permissions;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Features;

namespace OpenTms.Platform.NumberSequences;

/* Pickers need only the app feature; the list's own screen needs its group and node (PO 2026-10-14, design 49).
 * The numbers themselves are drawn through INumberGenerator, which has no endpoint. */
[RequiresFeature(AppFeatures.MasterData.Default)]
public class NumberSequenceAppService
    : PlatformCrudAppServiceBase<NumberSequence, NumberSequenceRowDto, NumberSequenceDto, NumberSequenceCreateDto, NumberSequenceUpdateDto, PlatformMasterDataListRequestDto>,
      INumberSequenceAppService
{
    private readonly INumberSequenceCounterRepository _counters;

    public NumberSequenceAppService(IRepository<NumberSequence, Guid> repository, INumberSequenceCounterRepository counters)
        : base(repository)
    {
        _counters = counters;
    }

    [RequiresFeature(AppFeatures.MasterData.Platform.Default, AppFeatures.MasterData.Platform.NumberSequences, RequiresAll = true)]
    [Authorize(PlatformPermissions.MasterData.NumberSequences.Default)]
    public Task<GridResponse<NumberSequenceRowDto>> GetListAsync(PlatformMasterDataListRequestDto input) => GetListInternalAsync(input);

    [RequiresFeature(AppFeatures.MasterData.Platform.Default, AppFeatures.MasterData.Platform.NumberSequences, RequiresAll = true)]
    [Authorize(PlatformPermissions.MasterData.NumberSequences.Default)]
    public Task<NumberSequenceDto> GetAsync(Guid id) => GetInternalAsync(id);

    [Authorize(PlatformPermissions.MasterData.NumberSequences.Default)]
    public Task<List<PlatformMasterDataLookupDto>> GetLookupAsync(string? term = null, int take = PlatformMasterDataConsts.MaxLookupTake) => GetLookupInternalAsync(term, take);

    [RequiresFeature(AppFeatures.MasterData.Platform.Default, AppFeatures.MasterData.Platform.NumberSequences, RequiresAll = true)]
    [Authorize(PlatformPermissions.MasterData.NumberSequences.Create)]
    public Task<NumberSequenceDto> CreateAsync(NumberSequenceCreateDto input) => CreateInternalAsync(input);

    [RequiresFeature(AppFeatures.MasterData.Platform.Default, AppFeatures.MasterData.Platform.NumberSequences, RequiresAll = true)]
    [Authorize(PlatformPermissions.MasterData.NumberSequences.Update)]
    public Task<NumberSequenceDto> UpdateAsync(Guid id, NumberSequenceUpdateDto input) => UpdateInternalAsync(id, input);

    [RequiresFeature(AppFeatures.MasterData.Platform.Default, AppFeatures.MasterData.Platform.NumberSequences, RequiresAll = true)]
    [Authorize(PlatformPermissions.MasterData.NumberSequences.Delete)]
    public Task DeleteAsync(Guid id) => DeleteInternalAsync(id);

    [RequiresFeature(AppFeatures.MasterData.Platform.Default, AppFeatures.MasterData.Platform.NumberSequences, RequiresAll = true)]
    [Authorize(PlatformPermissions.MasterData.NumberSequences.Delete)]
    public Task<PlatformMasterDataBulkDeleteResultDto> BulkDeleteAsync(PlatformMasterDataBulkDeleteRequestDto input)
        => BulkDeleteInternalAsync(input.Selection, q => q);

    // ── list hooks ────────────────────────────────────────────────────────────

    protected override IQueryable<NumberSequence> ApplySearch(IQueryable<NumberSequence> query, string term)
        => query.Where(s => s.Name.ToLower().Contains(term) || (s.Description != null && s.Description.ToLower().Contains(term)));

    protected override IOrderedQueryable<NumberSequence> ApplySort(IQueryable<NumberSequence> query, string? sortBy, bool descending)
        => (sortBy?.ToLowerInvariant()) switch
        {
            null or "" or "name" => Order(query, s => s.Name, descending),
            "scheme" => Order(query, s => s.Scheme, descending),
            _ => throw UnknownSortColumn(sortBy)
        };

    protected override async Task<List<NumberSequenceRowDto>> ToRowsAsync(List<NumberSequence> entities)
    {
        var inUse = await InUseAsync(entities.Select(s => s.Id).ToList());
        return entities.Select(s => new NumberSequenceRowDto
        {
            Id = s.Id,
            Name = s.Name,
            Scheme = s.Scheme,
            ExampleNumber = ExampleOf(s),
            IsInUse = inUse.Contains(s.Id)
        }).ToList();
    }

    protected override async Task<NumberSequenceDto> ToDtoAsync(NumberSequence s)
    {
        var dto = new NumberSequenceDto
        {
            Name = s.Name,
            Scheme = s.Scheme,
            Prefix = s.Prefix,
            YearPrefix = s.YearPrefix,
            YearSuffix = s.YearSuffix,
            MonthPrefix = s.MonthPrefix,
            MonthSuffix = s.MonthSuffix,
            MinimumDigits = s.MinimumDigits,
            ExampleNumber = ExampleOf(s),
            IsInUse = await IsInUseAsync(s.Id)
        };
        MapAudit(s, dto);
        return dto;
    }

    protected override PlatformMasterDataLookupDto ToLookup(NumberSequence s) => new() { Id = s.Id, Name = s.Name };

    protected override string DisplayNameOf(NumberSequence s) => s.Name;

    // ── write hooks ───────────────────────────────────────────────────────────

    protected override async Task<NumberSequence> CreateEntityAsync(NumberSequenceCreateDto input)
    {
        var normalized = NumberSequence.NormalizedNameOf(input.Name);
        await EnsureUniqueAsync(s => s.NormalizedName == normalized, "name", input.Name.Trim());

        var sequence = new NumberSequence(GuidGenerator.Create(), CurrentTenant.Id, input.Name, FormatOf(input));
        sequence.SetDescription(input.Description);
        return sequence;
    }

    /// <summary>Name and description stay editable; the numbering fields are frozen once a number has been drawn.</summary>
    protected override async Task ApplyUpdateAsync(NumberSequence sequence, NumberSequenceUpdateDto input)
    {
        var normalized = NumberSequence.NormalizedNameOf(input.Name);
        if (normalized != sequence.NormalizedName)
        {
            await EnsureUniqueAsync(s => s.NormalizedName == normalized && s.Id != sequence.Id, "name", input.Name.Trim());
        }

        sequence.SetName(input.Name);
        /* Exclusive lock before the in-use check: a first draw running now commits its counter row before the check reads
         * (design 49, "Guards"); otherwise both commit and the next draw repeats a number in the new format. */
        await _counters.LockSequencesAsync(CurrentTenant.Id, [sequence.Id], exclusive: true);
        sequence.ChangeFormat(FormatOf(input), await IsInUseAsync(sequence.Id));
        sequence.SetDescription(input.Description);
    }

    /// <summary>The in-use set of the bulk delete under way, read once under lock (PrepareBulkDeleteAsync).</summary>
    private HashSet<Guid>? _bulkInUse;

    protected override async Task PrepareBulkDeleteAsync(List<NumberSequence> targets)
    {
        var ids = targets.Select(t => t.Id).ToList();
        await _counters.LockSequencesAsync(CurrentTenant.Id, ids, exclusive: true);
        _bulkInUse = await InUseAsync(ids);
    }

    protected override async Task<string?> GetDeleteBlockerAsync(NumberSequence sequence)
    {
        bool inUse;
        if (_bulkInUse != null)
        {
            inUse = _bulkInUse.Contains(sequence.Id);
        }
        else
        {
            /* A single delete: the same lock-then-check as an edit. */
            await _counters.LockSequencesAsync(CurrentTenant.Id, [sequence.Id], exclusive: true);
            inUse = await IsInUseAsync(sequence.Id);
        }

        return inUse ? L["Platform:InUseByNumbers"].Value : null;
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static NumberSequenceFormat FormatOf(NumberSequenceCreateDto input)
        => new(input.Scheme, input.Prefix, input.YearPrefix, input.YearSuffix, input.MonthPrefix, input.MonthSuffix, input.MinimumDigits);

    /// <summary>The first number of today's period (counter 1).</summary>
    private string ExampleOf(NumberSequence sequence)
        => NumberSequenceFormatter.Format(sequence.Format, DateOnly.FromDateTime(Clock.Now), 1);

    private Task<bool> IsInUseAsync(Guid sequenceId) => _counters.AnyAsync(c => c.NumberSequenceId == sequenceId);

    private async Task<HashSet<Guid>> InUseAsync(List<Guid> sequenceIds)
    {
        if (sequenceIds.Count == 0)
        {
            return [];
        }

        var query = (await _counters.GetQueryableAsync())
            .Where(c => sequenceIds.Contains(c.NumberSequenceId))
            .Select(c => c.NumberSequenceId)
            .Distinct();
        return (await AsyncExecuter.ToListAsync(query)).ToHashSet();
    }
}
