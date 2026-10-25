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

/* Design 53. Pickers (the lookup) need only the app feature; the list's own screen needs its node (PO 2026-10-14). */
[RequiresFeature(AppFeatures.MasterData.Default)]
public class TransportTripTypeAppService
    : ReferenceDataCrudAppServiceBase<TransportTripType, TransportTripTypeRowDto, TransportTripTypeDto, TransportTripTypeCreateDto, TransportTripTypeUpdateDto, ArchivableListRequestDto>,
      ITransportTripTypeAppService
{
    public TransportTripTypeAppService(IRepository<TransportTripType, Guid> repository)
        : base(repository)
    {
        LocalizationResource = typeof(PlanningExecutionResource);
    }

    [RequiresFeature(AppFeatures.MasterData.PlanningExecution.Default, AppFeatures.MasterData.PlanningExecution.Trips.Default, AppFeatures.MasterData.PlanningExecution.Trips.TransportTripTypes, RequiresAll = true)]
    [Authorize(PlanningExecutionPermissions.TransportTripTypes.Default)]
    public Task<GridResponse<TransportTripTypeRowDto>> GetListAsync(ArchivableListRequestDto input) => GetListInternalAsync(input);

    [RequiresFeature(AppFeatures.MasterData.PlanningExecution.Default, AppFeatures.MasterData.PlanningExecution.Trips.Default, AppFeatures.MasterData.PlanningExecution.Trips.TransportTripTypes, RequiresAll = true)]
    [Authorize(PlanningExecutionPermissions.TransportTripTypes.Default)]
    public Task<TransportTripTypeDto> GetAsync(Guid id) => GetInternalAsync(id);

    [Authorize(PlanningExecutionPermissions.TransportTripTypes.Default)]
    public Task<List<ReferenceDataLookupDto>> GetLookupAsync(string? term = null, int take = ReferenceDataConsts.MaxLookupTake) => GetLookupInternalAsync(term, take);

    [RequiresFeature(AppFeatures.MasterData.PlanningExecution.Default, AppFeatures.MasterData.PlanningExecution.Trips.Default, AppFeatures.MasterData.PlanningExecution.Trips.TransportTripTypes, RequiresAll = true)]
    [Authorize(PlanningExecutionPermissions.TransportTripTypes.Create)]
    public Task<TransportTripTypeDto> CreateAsync(TransportTripTypeCreateDto input) => CreateInternalAsync(input);

    [RequiresFeature(AppFeatures.MasterData.PlanningExecution.Default, AppFeatures.MasterData.PlanningExecution.Trips.Default, AppFeatures.MasterData.PlanningExecution.Trips.TransportTripTypes, RequiresAll = true)]
    [Authorize(PlanningExecutionPermissions.TransportTripTypes.Update)]
    public Task<TransportTripTypeDto> UpdateAsync(Guid id, TransportTripTypeUpdateDto input) => UpdateInternalAsync(id, input);

    [RequiresFeature(AppFeatures.MasterData.PlanningExecution.Default, AppFeatures.MasterData.PlanningExecution.Trips.Default, AppFeatures.MasterData.PlanningExecution.Trips.TransportTripTypes, RequiresAll = true)]
    [Authorize(PlanningExecutionPermissions.TransportTripTypes.Delete)]
    public async Task DeleteAsync(Guid id)
    {
        EnsureNotDefault(await Repository.GetAsync(id), "delete");
        await DeleteInternalAsync(id);
    }

    [RequiresFeature(AppFeatures.MasterData.PlanningExecution.Default, AppFeatures.MasterData.PlanningExecution.Trips.Default, AppFeatures.MasterData.PlanningExecution.Trips.TransportTripTypes, RequiresAll = true)]
    [Authorize(PlanningExecutionPermissions.TransportTripTypes.Delete)]
    public Task<ReferenceDataBulkDeleteResultDto> BulkDeleteAsync(ArchivableBulkDeleteRequestDto input)
        => BulkDeleteInternalAsync(input.Selection, q => input.IncludeArchived ? q : q.Where(t => !t.IsArchived));

    [RequiresFeature(AppFeatures.MasterData.PlanningExecution.Default, AppFeatures.MasterData.PlanningExecution.Trips.Default, AppFeatures.MasterData.PlanningExecution.Trips.TransportTripTypes, RequiresAll = true)]
    [Authorize(PlanningExecutionPermissions.TransportTripTypes.Update)]
    public Task SetDefaultAsync(Guid id) => SetDefaultInternalAsync(id);

    protected override IQueryable<TransportTripType> ApplyFilters(IQueryable<TransportTripType> query, ArchivableListRequestDto request)
        => request.IncludeArchived ? query : query.Where(t => !t.IsArchived);

    protected override IQueryable<TransportTripType> ApplySearch(IQueryable<TransportTripType> query, string term)
    {
        var language = UiLanguage;
        return query.Where(t =>
            t.Code.ToLower().Contains(term) ||
            (LocalizedNameQuery.Text(t.Name, language) ?? "").ToLower().Contains(term) ||
            (LocalizedNameQuery.Text(t.Name, LocalizedName.DefaultCulture) ?? "").ToLower().Contains(term));
    }

    protected override IOrderedQueryable<TransportTripType> ApplySort(IQueryable<TransportTripType> query, string? sortBy, bool descending)
    {
        var language = UiLanguage;
        return (sortBy?.ToLowerInvariant()) switch
        {
            null or "" or "code" => Order(query, t => t.Code, descending),
            "name" => Order(query, t => LocalizedNameQuery.Text(t.Name, language) ?? LocalizedNameQuery.Text(t.Name, LocalizedName.DefaultCulture), descending),
            "defaultexecution" => Order(query, t => t.DefaultExecution, descending),
            "isdefault" => Order(query, t => t.IsDefault, descending),
            "isarchived" => Order(query, t => t.IsArchived, descending),
            _ => throw UnknownSortColumn(sortBy)
        };
    }

    protected override IQueryable<TransportTripType> ApplyLookupFilter(IQueryable<TransportTripType> query) => query.Where(t => !t.IsArchived);

    protected override Task<List<TransportTripTypeRowDto>> ToRowsAsync(List<TransportTripType> entities)
        => Task.FromResult(entities.Select(t => new TransportTripTypeRowDto
        {
            Id = t.Id,
            Code = t.Code,
            Name = Resolve(t.Name),
            DefaultExecution = t.DefaultExecution,
            SendToFleetManagementSystem = t.SendToFleetManagementSystem,
            NumberingExample = NumberingExampleOf(t.Numbering).Value,
            IsDefault = t.IsDefault,
            IsArchived = t.IsArchived,
            Description = t.Description
        }).ToList());

    protected override async Task<TransportTripTypeDto> ToDtoAsync(TransportTripType t)
    {
        var dto = new TransportTripTypeDto
        {
            Code = t.Code,
            Name = Resolve(t.Name),
            Names = ToNamesDto(t.Name),
            DefaultExecution = t.DefaultExecution,
            SendToFleetManagementSystem = t.SendToFleetManagementSystem,
            Numbering = ToNumberingDto(t.Numbering),
            NextNumber = await NumberSequences.PeekNextValueAsync(t.SequenceKey, t.Numbering),
            NextNumberPreview = await NumberSequences.PeekNextAsync(t.SequenceKey, t.Numbering),
            IsDefault = t.IsDefault,
            IsArchived = t.IsArchived
        };
        MapAudit(t, dto);
        return dto;
    }

    protected override ReferenceDataLookupDto ToLookup(TransportTripType t) => new() { Id = t.Id, Name = Resolve(t.Name), Code = t.Code };

    protected override string DisplayNameOf(TransportTripType t) => Resolve(t.Name);

    protected override async Task<TransportTripType> CreateEntityAsync(TransportTripTypeCreateDto input)
    {
        var code = ReferenceDataAggregateRoot.NormalizeCode(input.Code);
        await EnsureUniqueAsync(t => t.Code == code, "code", code);
        var type = new TransportTripType(GuidGenerator.Create(), CurrentTenant.Id, code, ToLocalizedName(input.Names), ToNumberSequenceFormat(input.Numbering));
        Apply(type, input);

        /* A tenant's first type becomes the default (design 53). */
        if (!await Repository.AnyAsync())
        {
            type.SetDefault(true);
        }

        type.SetArchived(input.IsArchived);
        return type;
    }

    protected override async Task ApplyUpdateAsync(TransportTripType type, TransportTripTypeUpdateDto input)
    {
        var code = ReferenceDataAggregateRoot.NormalizeCode(input.Code);
        if (code != type.Code)
        {
            await EnsureUniqueAsync(t => t.Code == code && t.Id != type.Id, "code", code);
            type.SetCode(code);
        }

        type.SetName(ToLocalizedName(input.Names));
        type.SetNumbering(ToNumberSequenceFormat(input.Numbering));
        Apply(type, input);
        type.SetArchived(input.IsArchived);
    }

    private static void Apply(TransportTripType type, TransportTripTypeCreateDto input)
    {
        type.SetDescription(input.Description);
        type.SetDefaultExecution(input.DefaultExecution);
        type.SendToFleetManagementSystem = input.SendToFleetManagementSystem;
    }

    protected override Task AfterWriteAsync(TransportTripType type, TransportTripTypeCreateDto input)
        => ApplyNextNumberAsync(type.SequenceKey, type.Numbering, input.NextNumber);

    protected override Task<string?> GetDeleteBlockerAsync(TransportTripType type)
        => Task.FromResult(type.IsDefault ? L["PlanningExecution:DefaultTripTypeCannotBeDeleted"].Value : null);
}
