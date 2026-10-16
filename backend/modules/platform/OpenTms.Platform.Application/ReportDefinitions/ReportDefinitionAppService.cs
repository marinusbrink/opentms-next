using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using OpenTms.Platform.Features;
using OpenTms.Platform.Grid;
using OpenTms.Platform.Permissions;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Features;
using Volo.Abp.Localization;

namespace OpenTms.Platform.ReportDefinitions;

/* Pickers need only the app feature; the list's own screen needs its group and node (PO 2026-10-14, design 49). */
[RequiresFeature(AppFeatures.MasterData.Default)]
public class ReportDefinitionAppService
    : PlatformCrudAppServiceBase<ReportDefinition, ReportDefinitionRowDto, ReportDefinitionDto, ReportDefinitionCreateDto, ReportDefinitionUpdateDto, ReportDefinitionListRequestDto>,
      IReportDefinitionAppService
{
    private readonly IReadOnlyCollection<string> _cultures;

    public ReportDefinitionAppService(IRepository<ReportDefinition, Guid> repository, IOptions<AbpLocalizationOptions> localization)
        : base(repository)
    {
        _cultures = localization.Value.Languages.Select(l => l.CultureName.ToLowerInvariant()).ToList();
    }

    [RequiresFeature(AppFeatures.MasterData.Platform.Default, AppFeatures.MasterData.Platform.ReportDefinitions, RequiresAll = true)]
    [Authorize(PlatformPermissions.MasterData.ReportDefinitions.Default)]
    public Task<GridResponse<ReportDefinitionRowDto>> GetListAsync(ReportDefinitionListRequestDto input) => GetListInternalAsync(input);

    [RequiresFeature(AppFeatures.MasterData.Platform.Default, AppFeatures.MasterData.Platform.ReportDefinitions, RequiresAll = true)]
    [Authorize(PlatformPermissions.MasterData.ReportDefinitions.Default)]
    public Task<ReportDefinitionDto> GetAsync(Guid id) => GetInternalAsync(id);

    [Authorize(PlatformPermissions.MasterData.ReportDefinitions.Default)]
    public Task<List<PlatformMasterDataLookupDto>> GetLookupAsync(string? term = null, int take = PlatformMasterDataConsts.MaxLookupTake) => GetLookupInternalAsync(term, take);

    [RequiresFeature(AppFeatures.MasterData.Platform.Default, AppFeatures.MasterData.Platform.ReportDefinitions, RequiresAll = true)]
    [Authorize(PlatformPermissions.MasterData.ReportDefinitions.Create)]
    public Task<ReportDefinitionDto> CreateAsync(ReportDefinitionCreateDto input) => CreateInternalAsync(input);

    [RequiresFeature(AppFeatures.MasterData.Platform.Default, AppFeatures.MasterData.Platform.ReportDefinitions, RequiresAll = true)]
    [Authorize(PlatformPermissions.MasterData.ReportDefinitions.Update)]
    public Task<ReportDefinitionDto> UpdateAsync(Guid id, ReportDefinitionUpdateDto input) => UpdateInternalAsync(id, input);

    [RequiresFeature(AppFeatures.MasterData.Platform.Default, AppFeatures.MasterData.Platform.ReportDefinitions, RequiresAll = true)]
    [Authorize(PlatformPermissions.MasterData.ReportDefinitions.Delete)]
    public Task DeleteAsync(Guid id) => DeleteInternalAsync(id);

    [RequiresFeature(AppFeatures.MasterData.Platform.Default, AppFeatures.MasterData.Platform.ReportDefinitions, RequiresAll = true)]
    [Authorize(PlatformPermissions.MasterData.ReportDefinitions.Delete)]
    public Task<PlatformMasterDataBulkDeleteResultDto> BulkDeleteAsync(ReportDefinitionBulkDeleteRequestDto input)
        => BulkDeleteInternalAsync(input.Selection, q => ApplyFilters(q, input.Purpose, input.IncludeArchived));

    /// <summary>Hides the definition from pickers and automatic selection; twice is one effect.</summary>
    [RequiresFeature(AppFeatures.MasterData.Platform.Default, AppFeatures.MasterData.Platform.ReportDefinitions, RequiresAll = true)]
    [Authorize(PlatformPermissions.MasterData.ReportDefinitions.Update)]
    public async Task ArchiveAsync(Guid id)
    {
        var definition = await Repository.GetAsync(id);
        definition.Archive();
        await Repository.UpdateAsync(definition, autoSave: true);
    }

    [RequiresFeature(AppFeatures.MasterData.Platform.Default, AppFeatures.MasterData.Platform.ReportDefinitions, RequiresAll = true)]
    [Authorize(PlatformPermissions.MasterData.ReportDefinitions.Update)]
    public async Task UnarchiveAsync(Guid id)
    {
        var definition = await Repository.GetAsync(id);
        definition.Unarchive();
        await Repository.UpdateAsync(definition, autoSave: true);
    }

    // ── list hooks ────────────────────────────────────────────────────────────

    protected override IQueryable<ReportDefinition> ApplyFilters(IQueryable<ReportDefinition> query, ReportDefinitionListRequestDto request)
        => ApplyFilters(query, request.Purpose, request.IncludeArchived);

    private static IQueryable<ReportDefinition> ApplyFilters(IQueryable<ReportDefinition> query, ReportPurpose? purpose, bool includeArchived)
    {
        if (purpose != null)
        {
            query = query.Where(d => d.Purpose == purpose.Value);
        }

        return includeArchived ? query : query.Where(d => !d.IsArchived);
    }

    protected override IQueryable<ReportDefinition> ApplySearch(IQueryable<ReportDefinition> query, string term)
        => query.Where(d => d.Name.ToLower().Contains(term) || (d.Description != null && d.Description.ToLower().Contains(term)));

    protected override IOrderedQueryable<ReportDefinition> ApplySort(IQueryable<ReportDefinition> query, string? sortBy, bool descending)
        => (sortBy?.ToLowerInvariant()) switch
        {
            null or "" or "name" => Order(query, d => d.Name, descending),
            "purpose" => Order(query, d => d.Purpose, descending),
            "culture" => Order(query, d => d.Culture, descending),
            "provider" => Order(query, d => d.Provider, descending),
            "isdefault" => Order(query, d => d.IsDefault, descending),
            "isarchived" => Order(query, d => d.IsArchived, descending),
            _ => throw UnknownSortColumn(sortBy)
        };

    protected override IQueryable<ReportDefinition> ApplyLookupFilter(IQueryable<ReportDefinition> query) => query.Where(d => !d.IsArchived);

    protected override Task<List<ReportDefinitionRowDto>> ToRowsAsync(List<ReportDefinition> entities)
        => Task.FromResult(entities.Select(d => new ReportDefinitionRowDto
        {
            Id = d.Id,
            Name = d.Name,
            Purpose = d.Purpose,
            Culture = d.Culture,
            Provider = d.Provider,
            IsDefault = d.IsDefault,
            IsArchived = d.IsArchived
        }).ToList());

    protected override Task<ReportDefinitionDto> ToDtoAsync(ReportDefinition d)
    {
        var dto = new ReportDefinitionDto
        {
            Name = d.Name,
            Purpose = d.Purpose,
            Culture = d.Culture,
            Provider = d.Provider,
            ProviderTemplateId = d.ProviderTemplateId,
            FileNamePattern = d.FileNamePattern,
            IsDefault = d.IsDefault,
            IsArchived = d.IsArchived,
            AvailableInCustomerPortal = d.AvailableInCustomerPortal
        };
        MapAudit(d, dto);
        return Task.FromResult(dto);
    }

    protected override PlatformMasterDataLookupDto ToLookup(ReportDefinition d) => new() { Id = d.Id, Name = d.Name };

    protected override string DisplayNameOf(ReportDefinition d) => d.Name;

    // ── write hooks ───────────────────────────────────────────────────────────

    protected override async Task<ReportDefinition> CreateEntityAsync(ReportDefinitionCreateDto input)
    {
        ScribanTemplateValidator.EnsureValid(input.FileNamePattern, "fileNamePattern");
        var normalized = ReportDefinition.NormalizedNameOf(input.Name);
        await EnsureUniqueAsync(d => d.NormalizedName == normalized, "name", input.Name.Trim());

        var definition = new ReportDefinition(
            GuidGenerator.Create(), CurrentTenant.Id, input.Name, input.Purpose, input.Culture, _cultures,
            input.Provider, input.ProviderTemplateId, input.FileNamePattern);
        await ApplyAsync(definition, input);
        return definition;
    }

    protected override async Task ApplyUpdateAsync(ReportDefinition definition, ReportDefinitionUpdateDto input)
    {
        ScribanTemplateValidator.EnsureValid(input.FileNamePattern, "fileNamePattern");
        var normalized = ReportDefinition.NormalizedNameOf(input.Name);
        if (normalized != definition.NormalizedName)
        {
            await EnsureUniqueAsync(d => d.NormalizedName == normalized && d.Id != definition.Id, "name", input.Name.Trim());
        }

        definition.SetName(input.Name);
        definition.SetPurpose(input.Purpose, input.Culture, _cultures);
        definition.SetProvider(input.Provider, input.ProviderTemplateId);
        definition.SetFileNamePattern(input.FileNamePattern);
        await ApplyAsync(definition, input);
    }

    private async Task ApplyAsync(ReportDefinition definition, ReportDefinitionCreateDto input)
    {
        definition.SetAvailableInCustomerPortal(input.AvailableInCustomerPortal);
        definition.SetDescription(input.Description);
        if (input.IsArchived)
        {
            definition.Archive();
        }
        else
        {
            definition.Unarchive();
        }

        if (input.IsDefault)
        {
            /* Out of the default index while the previous default is cleared and saved. */
            definition.SetDefault(false);
            await ClearOtherDefaultsAsync(definition);
        }

        definition.SetDefault(input.IsDefault);
    }

    /// <summary>
    /// At most one default per purpose and language: the previous default is cleared and saved first, in the same
    /// transaction, so it has left the partial unique index before this definition enters it.
    /// </summary>
    private async Task ClearOtherDefaultsAsync(ReportDefinition definition)
    {
        var purpose = definition.Purpose;
        var culture = definition.Culture;
        var id = definition.Id;
        var previous = await Repository.GetListAsync(d => d.Purpose == purpose && d.Culture == culture && d.IsDefault && d.Id != id);
        foreach (var other in previous)
        {
            other.SetDefault(false);
            await Repository.UpdateAsync(other);
        }

        if (previous.Count > 0)
        {
            await FlushAsync();
        }
    }
}
