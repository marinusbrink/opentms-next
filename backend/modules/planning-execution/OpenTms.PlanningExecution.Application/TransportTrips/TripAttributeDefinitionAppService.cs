using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using OpenTms.PlanningExecution.Localization;
using OpenTms.PlanningExecution.Permissions;
using OpenTms.Platform.Features;
using OpenTms.Platform.Grid;
using OpenTms.Platform.Localization;
using OpenTms.Platform.ReferenceData;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Features;

namespace OpenTms.PlanningExecution.TransportTrips;

/// <summary>Design 56 "Attributes": the tenant's trip attribute definitions (design-50 shape).</summary>
[RequiresFeature(AppFeatures.MasterData.Default)]
public class TripAttributeDefinitionAppService
    : ReferenceDataCrudAppServiceBase<TripAttributeDefinition, TripAttributeDefinitionRowDto, TripAttributeDefinitionDto, TripAttributeDefinitionCreateDto, TripAttributeDefinitionUpdateDto, ArchivableListRequestDto>,
      ITripAttributeDefinitionAppService
{
    public TripAttributeDefinitionAppService(IRepository<TripAttributeDefinition, Guid> repository)
        : base(repository)
    {
        LocalizationResource = typeof(PlanningExecutionResource);
    }

    [RequiresFeature(AppFeatures.MasterData.PlanningExecution.Default, AppFeatures.MasterData.PlanningExecution.Trips.Default, AppFeatures.MasterData.PlanningExecution.Trips.TripAttributeDefinitions, RequiresAll = true)]
    [Authorize(PlanningExecutionPermissions.TripAttributeDefinitions.Default)]
    public Task<GridResponse<TripAttributeDefinitionRowDto>> GetListAsync(ArchivableListRequestDto input) => GetListInternalAsync(input);

    [RequiresFeature(AppFeatures.MasterData.PlanningExecution.Default, AppFeatures.MasterData.PlanningExecution.Trips.Default, AppFeatures.MasterData.PlanningExecution.Trips.TripAttributeDefinitions, RequiresAll = true)]
    [Authorize(PlanningExecutionPermissions.TripAttributeDefinitions.Default)]
    public Task<TripAttributeDefinitionDto> GetAsync(Guid id) => GetInternalAsync(id);

    /// <summary>The trip editor reads the definitions through the lookup: the Transport app needs no Master data node for it.</summary>
    [Authorize(PlanningExecutionPermissions.TripAttributeDefinitions.Default)]
    public Task<List<ReferenceDataLookupDto>> GetLookupAsync(string? term = null, int take = ReferenceDataConsts.MaxLookupTake) => GetLookupInternalAsync(term, take);

    [RequiresFeature(AppFeatures.MasterData.PlanningExecution.Default, AppFeatures.MasterData.PlanningExecution.Trips.Default, AppFeatures.MasterData.PlanningExecution.Trips.TripAttributeDefinitions, RequiresAll = true)]
    [Authorize(PlanningExecutionPermissions.TripAttributeDefinitions.Create)]
    public Task<TripAttributeDefinitionDto> CreateAsync(TripAttributeDefinitionCreateDto input) => CreateInternalAsync(input);

    [RequiresFeature(AppFeatures.MasterData.PlanningExecution.Default, AppFeatures.MasterData.PlanningExecution.Trips.Default, AppFeatures.MasterData.PlanningExecution.Trips.TripAttributeDefinitions, RequiresAll = true)]
    [Authorize(PlanningExecutionPermissions.TripAttributeDefinitions.Update)]
    public Task<TripAttributeDefinitionDto> UpdateAsync(Guid id, TripAttributeDefinitionUpdateDto input) => UpdateInternalAsync(id, input);

    [RequiresFeature(AppFeatures.MasterData.PlanningExecution.Default, AppFeatures.MasterData.PlanningExecution.Trips.Default, AppFeatures.MasterData.PlanningExecution.Trips.TripAttributeDefinitions, RequiresAll = true)]
    [Authorize(PlanningExecutionPermissions.TripAttributeDefinitions.Delete)]
    public Task DeleteAsync(Guid id) => DeleteInternalAsync(id);

    [RequiresFeature(AppFeatures.MasterData.PlanningExecution.Default, AppFeatures.MasterData.PlanningExecution.Trips.Default, AppFeatures.MasterData.PlanningExecution.Trips.TripAttributeDefinitions, RequiresAll = true)]
    [Authorize(PlanningExecutionPermissions.TripAttributeDefinitions.Delete)]
    public Task<ReferenceDataBulkDeleteResultDto> BulkDeleteAsync(ArchivableBulkDeleteRequestDto input)
        => BulkDeleteInternalAsync(input.Selection, q => input.IncludeArchived ? q : q.Where(d => !d.IsArchived));

    protected override IQueryable<TripAttributeDefinition> ApplyFilters(IQueryable<TripAttributeDefinition> query, ArchivableListRequestDto request)
        => request.IncludeArchived ? query : query.Where(d => !d.IsArchived);

    protected override IQueryable<TripAttributeDefinition> ApplySearch(IQueryable<TripAttributeDefinition> query, string term)
    {
        var language = UiLanguage;
        return query.Where(d =>
            d.Key.ToLower().Contains(term) ||
            (LocalizedNameQuery.Text(d.Name, language) ?? "").ToLower().Contains(term) ||
            (LocalizedNameQuery.Text(d.Name, LocalizedName.DefaultCulture) ?? "").ToLower().Contains(term));
    }

    protected override IOrderedQueryable<TripAttributeDefinition> ApplySort(IQueryable<TripAttributeDefinition> query, string? sortBy, bool descending)
    {
        var language = UiLanguage;
        return (sortBy?.ToLowerInvariant()) switch
        {
            null or "" or "position" => descending ? query.OrderByDescending(d => d.Position).ThenByDescending(d => d.Key) : query.OrderBy(d => d.Position).ThenBy(d => d.Key),
            "key" => Order(query, d => d.Key, descending),
            "name" => Order(query, d => LocalizedNameQuery.Text(d.Name, language) ?? LocalizedNameQuery.Text(d.Name, LocalizedName.DefaultCulture), descending),
            "isarchived" => Order(query, d => d.IsArchived, descending),
            _ => throw UnknownSortColumn(sortBy)
        };
    }

    protected override IQueryable<TripAttributeDefinition> ApplyLookupFilter(IQueryable<TripAttributeDefinition> query) => query.Where(d => !d.IsArchived).OrderBy(d => d.Position);

    protected override Task<List<TripAttributeDefinitionRowDto>> ToRowsAsync(List<TripAttributeDefinition> entities)
        => Task.FromResult(entities.Select(d => new TripAttributeDefinitionRowDto
        {
            Id = d.Id,
            Key = d.Key,
            Name = Resolve(d.Name),
            Position = d.Position,
            IsArchived = d.IsArchived,
            Description = d.Description
        }).ToList());

    protected override Task<TripAttributeDefinitionDto> ToDtoAsync(TripAttributeDefinition d)
    {
        var dto = new TripAttributeDefinitionDto
        {
            Key = d.Key,
            Name = Resolve(d.Name),
            Names = ToNamesDto(d.Name),
            Position = d.Position,
            IsArchived = d.IsArchived
        };
        MapAudit(d, dto);
        return Task.FromResult(dto);
    }

    protected override ReferenceDataLookupDto ToLookup(TripAttributeDefinition d) => new() { Id = d.Id, Name = Resolve(d.Name), Code = d.Key };

    protected override string DisplayNameOf(TripAttributeDefinition d) => Resolve(d.Name);

    protected override async Task<TripAttributeDefinition> CreateEntityAsync(TripAttributeDefinitionCreateDto input)
    {
        var key = TripAttributeDefinition.NormalizeKey(input.Key);
        await EnsureUniqueAsync(d => d.Key == key, "key", key);
        var definition = new TripAttributeDefinition(GuidGenerator.Create(), CurrentTenant.Id, key, ToLocalizedName(input.Names));
        Apply(definition, input);
        return definition;
    }

    protected override async Task ApplyUpdateAsync(TripAttributeDefinition definition, TripAttributeDefinitionUpdateDto input)
    {
        var key = TripAttributeDefinition.NormalizeKey(input.Key);
        if (key != definition.Key)
        {
            await EnsureUniqueAsync(d => d.Key == key && d.Id != definition.Id, "key", key);
            definition.SetKey(key);
        }

        definition.SetName(ToLocalizedName(input.Names));
        Apply(definition, input);
    }

    private static void Apply(TripAttributeDefinition definition, TripAttributeDefinitionCreateDto input)
    {
        definition.SetPosition(input.Position);
        definition.SetDescription(input.Description);
        definition.SetArchived(input.IsArchived);
    }
}
