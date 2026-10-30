using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using OpenTms.MasterData.Published;
using OpenTms.Orders.Published;
using OpenTms.PlanningExecution.Legs;
using OpenTms.PlanningExecution.Localization;
using OpenTms.PlanningExecution.Permissions;
using OpenTms.Platform.Features;
using OpenTms.Platform.Grid;
using OpenTms.Platform.ReferenceData;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Features;

namespace OpenTms.PlanningExecution.ViaDepots;

/// <summary>Design 59 "ViaDepotRule" in the design-50 shape; the Master Data ids are validated and named through the published lookup.</summary>
[RequiresFeature(AppFeatures.MasterData.Default)]
public class ViaDepotRuleAppService
    : ReferenceDataCrudAppServiceBase<ViaDepotRule, ViaDepotRuleRowDto, ViaDepotRuleDto, ViaDepotRuleCreateDto, ViaDepotRuleUpdateDto, ArchivableListRequestDto>,
      IViaDepotRuleAppService
{
    private readonly IMasterDataReferenceLookup _masterData;
    private readonly MasterDataReferences _references;
    private readonly ITransportOrderLookup _orderLookup;

    public ViaDepotRuleAppService(IRepository<ViaDepotRule, Guid> repository, IMasterDataReferenceLookup masterData, MasterDataReferences references, ITransportOrderLookup orderLookup)
        : base(repository)
    {
        _masterData = masterData;
        _references = references;
        _orderLookup = orderLookup;
        LocalizationResource = typeof(PlanningExecutionResource);
    }

    [RequiresFeature(AppFeatures.MasterData.PlanningExecution.Default, AppFeatures.MasterData.PlanningExecution.Trips.Default, AppFeatures.MasterData.PlanningExecution.Trips.ViaDepotRules, RequiresAll = true)]
    [Authorize(PlanningExecutionPermissions.ViaDepotRules.Default)]
    public Task<GridResponse<ViaDepotRuleRowDto>> GetListAsync(ArchivableListRequestDto input) => GetListInternalAsync(input);

    [RequiresFeature(AppFeatures.MasterData.PlanningExecution.Default, AppFeatures.MasterData.PlanningExecution.Trips.Default, AppFeatures.MasterData.PlanningExecution.Trips.ViaDepotRules, RequiresAll = true)]
    [Authorize(PlanningExecutionPermissions.ViaDepotRules.Default)]
    public Task<ViaDepotRuleDto> GetAsync(Guid id) => GetInternalAsync(id);

    [RequiresFeature(AppFeatures.MasterData.PlanningExecution.Default, AppFeatures.MasterData.PlanningExecution.Trips.Default, AppFeatures.MasterData.PlanningExecution.Trips.ViaDepotRules, RequiresAll = true)]
    [Authorize(PlanningExecutionPermissions.ViaDepotRules.Create)]
    public Task<ViaDepotRuleDto> CreateAsync(ViaDepotRuleCreateDto input) => CreateInternalAsync(input);

    [RequiresFeature(AppFeatures.MasterData.PlanningExecution.Default, AppFeatures.MasterData.PlanningExecution.Trips.Default, AppFeatures.MasterData.PlanningExecution.Trips.ViaDepotRules, RequiresAll = true)]
    [Authorize(PlanningExecutionPermissions.ViaDepotRules.Update)]
    public Task<ViaDepotRuleDto> UpdateAsync(Guid id, ViaDepotRuleUpdateDto input) => UpdateInternalAsync(id, input);

    [RequiresFeature(AppFeatures.MasterData.PlanningExecution.Default, AppFeatures.MasterData.PlanningExecution.Trips.Default, AppFeatures.MasterData.PlanningExecution.Trips.ViaDepotRules, RequiresAll = true)]
    [Authorize(PlanningExecutionPermissions.ViaDepotRules.Delete)]
    public Task DeleteAsync(Guid id) => DeleteInternalAsync(id);

    [RequiresFeature(AppFeatures.MasterData.PlanningExecution.Default, AppFeatures.MasterData.PlanningExecution.Trips.Default, AppFeatures.MasterData.PlanningExecution.Trips.ViaDepotRules, RequiresAll = true)]
    [Authorize(PlanningExecutionPermissions.ViaDepotRules.Delete)]
    public Task<ReferenceDataBulkDeleteResultDto> BulkDeleteAsync(ArchivableBulkDeleteRequestDto input)
        => BulkDeleteInternalAsync(input.Selection, q => input.IncludeArchived ? q : q.Where(r => !r.IsArchived));

    protected override IQueryable<ViaDepotRule> ApplyFilters(IQueryable<ViaDepotRule> query, ArchivableListRequestDto request)
        => request.IncludeArchived ? query : query.Where(r => !r.IsArchived);

    protected override IQueryable<ViaDepotRule> ApplySearch(IQueryable<ViaDepotRule> query, string term)
        => query.Where(r => (r.Description ?? "").ToLower().Contains(term));

    protected override IOrderedQueryable<ViaDepotRule> ApplySort(IQueryable<ViaDepotRule> query, string? sortBy, bool descending)
        => (sortBy?.ToLowerInvariant()) switch
        {
            null or "" or "scope" => descending ? query.OrderByDescending(r => r.Scope).ThenByDescending(r => r.Position) : query.OrderBy(r => r.Scope).ThenBy(r => r.Position),
            "position" => Order(query, r => r.Position, descending),
            "overnight" => Order(query, r => r.Overnight, descending),
            "isarchived" => Order(query, r => r.IsArchived, descending),
            _ => throw UnknownSortColumn(sortBy)
        };

    protected override IQueryable<ViaDepotRule> ApplyLookupFilter(IQueryable<ViaDepotRule> query) => query.Where(r => !r.IsArchived);

    protected override async Task<List<ViaDepotRuleRowDto>> ToRowsAsync(List<ViaDepotRule> entities)
    {
        var relations = await _references.RelationNamesAsync(entities.Select(r => r.RelationId).Concat(entities.Select(r => (Guid?)r.DepotRelationId)));
        var regions = (await _masterData.GetGeographicalRegionsAsync(entities.Where(r => r.GeographicalRegionId != null).Select(r => r.GeographicalRegionId!.Value).Distinct().ToList()))
            .ToDictionary(r => r.Id, r => r.Name);
        var addresses = await _references.AddressesAsync(entities.Select(r => (Guid?)r.DepotAddressId));
        return entities.Select(r => new ViaDepotRuleRowDto
        {
            Id = r.Id,
            Scope = r.Scope,
            RelationId = r.RelationId,
            RelationName = r.RelationId != null ? relations.GetValueOrDefault(r.RelationId.Value, NotFound) : null,
            GeographicalRegionId = r.GeographicalRegionId,
            GeographicalRegionName = r.GeographicalRegionId != null ? regions.GetValueOrDefault(r.GeographicalRegionId.Value, NotFound) : null,
            StopWaypointTypeId = r.StopWaypointTypeId,
            DepotRelationId = r.DepotRelationId,
            DepotRelationName = relations.GetValueOrDefault(r.DepotRelationId, NotFound),
            DepotAddressId = r.DepotAddressId,
            DepotAddressCity = addresses.TryGetValue(r.DepotAddressId, out var address) ? address.City : NotFound,
            ArrivalWaypointTypeId = r.ArrivalWaypointTypeId,
            DepartureWaypointTypeId = r.DepartureWaypointTypeId,
            Overnight = r.Overnight,
            Position = r.Position,
            IsArchived = r.IsArchived,
            Description = r.Description
        }).ToList();
    }

    private string NotFound => L["PlanningExecution:NotFound"].Value;

    protected override async Task<ViaDepotRuleDto> ToDtoAsync(ViaDepotRule r)
    {
        var row = (await ToRowsAsync([r])).Single();
        var dto = new ViaDepotRuleDto
        {
            Scope = r.Scope,
            RelationId = r.RelationId,
            RelationName = row.RelationName,
            GeographicalRegionId = r.GeographicalRegionId,
            GeographicalRegionName = row.GeographicalRegionName,
            StopWaypointTypeId = r.StopWaypointTypeId,
            DepotRelationId = r.DepotRelationId,
            DepotRelationName = row.DepotRelationName,
            DepotAddressId = r.DepotAddressId,
            DepotAddressCity = row.DepotAddressCity,
            ArrivalWaypointTypeId = r.ArrivalWaypointTypeId,
            DepartureWaypointTypeId = r.DepartureWaypointTypeId,
            Overnight = r.Overnight,
            Position = r.Position,
            IsArchived = r.IsArchived
        };
        MapAudit(r, dto);
        return dto;
    }

    protected override ReferenceDataLookupDto ToLookup(ViaDepotRule r) => new() { Id = r.Id, Name = DisplayNameOf(r) };

    protected override string DisplayNameOf(ViaDepotRule r) => $"{L["PlanningExecution:ViaDepotScope." + r.Scope].Value} #{r.Position}";

    protected override async Task<ViaDepotRule> CreateEntityAsync(ViaDepotRuleCreateDto input)
    {
        await ValidateReferencesAsync(input);
        var rule = new ViaDepotRule(GuidGenerator.Create(), CurrentTenant.Id, input.Scope, input.RelationId, input.GeographicalRegionId, input.DepotRelationId, input.DepotAddressId, input.ArrivalWaypointTypeId);
        Apply(rule, input);
        return rule;
    }

    protected override async Task ApplyUpdateAsync(ViaDepotRule rule, ViaDepotRuleUpdateDto input)
    {
        await ValidateReferencesAsync(input);
        rule.SetScope(input.Scope, input.RelationId, input.GeographicalRegionId);
        rule.SetDepot(input.DepotRelationId, input.DepotAddressId);
        Apply(rule, input);
    }

    private static void Apply(ViaDepotRule rule, ViaDepotRuleCreateDto input)
    {
        rule.SetStopWaypointType(input.StopWaypointTypeId ?? Guid.Empty);
        rule.SetWaypointTypes(input.ArrivalWaypointTypeId, input.DepartureWaypointTypeId);
        rule.SetOvernight(input.Overnight);
        rule.SetPosition(input.Position);
        rule.SetDescription(input.Description);
        rule.SetArchived(input.IsArchived);
    }

    private async Task ValidateReferencesAsync(ViaDepotRuleCreateDto input)
    {
        await _references.EnsureRelationsExistAsync([input.RelationId, input.DepotRelationId], "relationId");
        if (input.GeographicalRegionId != null && (await _masterData.GetGeographicalRegionsAsync([input.GeographicalRegionId.Value])).Count == 0)
        {
            throw new BusinessException(ReferenceDataErrorCodes.ReferenceNotFound).WithData("field", "geographicalRegionId").WithData("value", input.GeographicalRegionId.Value);
        }

        var addresses = await _references.AddressesAsync([input.DepotAddressId]);
        if (!addresses.TryGetValue(input.DepotAddressId, out var address) || address.RelationId != input.DepotRelationId)
        {
            throw new BusinessException(ReferenceDataErrorCodes.ReferenceNotFound).WithData("field", "depotAddressId").WithData("value", input.DepotAddressId);
        }

        /* The waypoint types are Orders ids, checked through its published lookup (as the split at an address does). */
        var typeFields = new (string Field, Guid? Id)[] { ("stopWaypointTypeId", input.StopWaypointTypeId), ("arrivalWaypointTypeId", input.ArrivalWaypointTypeId), ("departureWaypointTypeId", input.DepartureWaypointTypeId) };
        var known = (await _orderLookup.GetWaypointTypesAsync(typeFields.Where(f => f.Id != null).Select(f => f.Id!.Value).Distinct().ToList())).Select(t => t.Id).ToHashSet();
        foreach (var (field, id) in typeFields)
        {
            if (id != null && !known.Contains(id.Value))
            {
                throw new BusinessException(ReferenceDataErrorCodes.ReferenceNotFound).WithData("field", field).WithData("value", id.Value);
            }
        }
    }
}
