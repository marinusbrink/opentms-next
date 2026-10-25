using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using OpenTms.Orders.Localization;
using OpenTms.Orders.Permissions;
using OpenTms.Platform.Features;
using OpenTms.Platform.Grid;
using OpenTms.Platform.ReferenceData;
using OpenTms.Platform.ReportDefinitions;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Features;

namespace OpenTms.Orders.ConsignmentNotes;

/// <summary>Design 58: the consignment note profiles, a Master data list under Orders → Transport orders (design-50 shape + set-default).</summary>
[RequiresFeature(AppFeatures.MasterData.Default)]
public class ConsignmentNoteProfileAppService
    : ReferenceDataCrudAppServiceBase<ConsignmentNoteProfile, ConsignmentNoteProfileRowDto, ConsignmentNoteProfileDto, ConsignmentNoteProfileCreateDto, ConsignmentNoteProfileUpdateDto, ArchivableListRequestDto>,
      IConsignmentNoteProfileAppService
{
    private readonly IRepository<ConsignmentNoteFixedDescription, Guid> _fixedDescriptions;
    private readonly IRepository<ReportDefinition, Guid> _reportDefinitions;

    public ConsignmentNoteProfileAppService(
        IRepository<ConsignmentNoteProfile, Guid> repository,
        IRepository<ConsignmentNoteFixedDescription, Guid> fixedDescriptions,
        IRepository<ReportDefinition, Guid> reportDefinitions)
        : base(repository)
    {
        _fixedDescriptions = fixedDescriptions;
        _reportDefinitions = reportDefinitions;
        LocalizationResource = typeof(OrdersResource);
    }

    [RequiresFeature(AppFeatures.MasterData.Orders.Default, AppFeatures.MasterData.Orders.TransportOrders.Default, AppFeatures.MasterData.Orders.TransportOrders.ConsignmentNoteProfiles, RequiresAll = true)]
    [Authorize(OrdersPermissions.ConsignmentNoteProfiles.Default)]
    public Task<GridResponse<ConsignmentNoteProfileRowDto>> GetListAsync(ArchivableListRequestDto input) => GetListInternalAsync(input);

    [RequiresFeature(AppFeatures.MasterData.Orders.Default, AppFeatures.MasterData.Orders.TransportOrders.Default, AppFeatures.MasterData.Orders.TransportOrders.ConsignmentNoteProfiles, RequiresAll = true)]
    [Authorize(OrdersPermissions.ConsignmentNoteProfiles.Default)]
    public Task<ConsignmentNoteProfileDto> GetAsync(Guid id) => GetInternalAsync(id);

    [Authorize(OrdersPermissions.ConsignmentNoteProfiles.Default)]
    public Task<List<ReferenceDataLookupDto>> GetLookupAsync(string? term = null, int take = ReferenceDataConsts.MaxLookupTake) => GetLookupInternalAsync(term, take);

    [RequiresFeature(AppFeatures.MasterData.Orders.Default, AppFeatures.MasterData.Orders.TransportOrders.Default, AppFeatures.MasterData.Orders.TransportOrders.ConsignmentNoteProfiles, RequiresAll = true)]
    [Authorize(OrdersPermissions.ConsignmentNoteProfiles.Create)]
    public Task<ConsignmentNoteProfileDto> CreateAsync(ConsignmentNoteProfileCreateDto input) => CreateInternalAsync(input);

    [RequiresFeature(AppFeatures.MasterData.Orders.Default, AppFeatures.MasterData.Orders.TransportOrders.Default, AppFeatures.MasterData.Orders.TransportOrders.ConsignmentNoteProfiles, RequiresAll = true)]
    [Authorize(OrdersPermissions.ConsignmentNoteProfiles.Update)]
    public Task<ConsignmentNoteProfileDto> UpdateAsync(Guid id, ConsignmentNoteProfileUpdateDto input) => UpdateInternalAsync(id, input);

    [RequiresFeature(AppFeatures.MasterData.Orders.Default, AppFeatures.MasterData.Orders.TransportOrders.Default, AppFeatures.MasterData.Orders.TransportOrders.ConsignmentNoteProfiles, RequiresAll = true)]
    [Authorize(OrdersPermissions.ConsignmentNoteProfiles.Delete)]
    public async Task DeleteAsync(Guid id)
    {
        EnsureNotDefault(await Repository.GetAsync(id), "delete");
        await DeleteInternalAsync(id);
    }

    [RequiresFeature(AppFeatures.MasterData.Orders.Default, AppFeatures.MasterData.Orders.TransportOrders.Default, AppFeatures.MasterData.Orders.TransportOrders.ConsignmentNoteProfiles, RequiresAll = true)]
    [Authorize(OrdersPermissions.ConsignmentNoteProfiles.Delete)]
    public Task<ReferenceDataBulkDeleteResultDto> BulkDeleteAsync(ArchivableBulkDeleteRequestDto input)
        => BulkDeleteInternalAsync(input.Selection, q => input.IncludeArchived ? q : q.Where(p => !p.IsArchived));

    [RequiresFeature(AppFeatures.MasterData.Orders.Default, AppFeatures.MasterData.Orders.TransportOrders.Default, AppFeatures.MasterData.Orders.TransportOrders.ConsignmentNoteProfiles, RequiresAll = true)]
    [Authorize(OrdersPermissions.ConsignmentNoteProfiles.Update)]
    public Task SetDefaultAsync(Guid id) => SetDefaultInternalAsync(id);

    protected override IQueryable<ConsignmentNoteProfile> ApplyFilters(IQueryable<ConsignmentNoteProfile> query, ArchivableListRequestDto request)
        => request.IncludeArchived ? query : query.Where(p => !p.IsArchived);

    protected override IQueryable<ConsignmentNoteProfile> ApplySearch(IQueryable<ConsignmentNoteProfile> query, string term)
        => query.Where(p => p.Name.ToLower().Contains(term));

    protected override IOrderedQueryable<ConsignmentNoteProfile> ApplySort(IQueryable<ConsignmentNoteProfile> query, string? sortBy, bool descending) => (sortBy?.ToLowerInvariant()) switch
    {
        null or "" or "name" => Order(query, p => p.Name, descending),
        "copies" => Order(query, p => p.Copies, descending),
        "isdefault" => Order(query, p => p.IsDefault, descending),
        "isarchived" => Order(query, p => p.IsArchived, descending),
        _ => throw UnknownSortColumn(sortBy)
    };

    protected override IQueryable<ConsignmentNoteProfile> ApplyLookupFilter(IQueryable<ConsignmentNoteProfile> query) => query.Where(p => !p.IsArchived);

    protected override async Task<List<ConsignmentNoteProfileRowDto>> ToRowsAsync(List<ConsignmentNoteProfile> entities)
    {
        var definitions = await DefinitionNamesAsync(entities.Select(p => p.ReportDefinitionId).OfType<Guid>());
        return entities.Select(p => new ConsignmentNoteProfileRowDto
        {
            Id = p.Id, Name = p.Name, Copies = p.Copies, ShowBorders = p.ShowBorders, ShowQrCode = p.ShowQrCode, GroupByLeg = p.GroupByLeg,
            ReportDefinitionName = p.ReportDefinitionId == null ? null : definitions.GetValueOrDefault(p.ReportDefinitionId.Value, L["Orders:NotFound"]),
            IsDefault = p.IsDefault, IsArchived = p.IsArchived, Description = p.Description
        }).ToList();
    }

    protected override async Task<ConsignmentNoteProfileDto> ToDtoAsync(ConsignmentNoteProfile p)
    {
        var definitions = await DefinitionNamesAsync(p.ReportDefinitionId == null ? [] : [p.ReportDefinitionId.Value]);
        var fixedIds = p.FixedDescriptionIds;
        var fixedNames = fixedIds.Count == 0 ? new Dictionary<Guid, string>() : (await _fixedDescriptions.GetListAsync(f => fixedIds.Contains(f.Id))).ToDictionary(f => f.Id, f => f.Name);
        var dto = new ConsignmentNoteProfileDto
        {
            Name = p.Name, Copies = p.Copies, ShowBorders = p.ShowBorders, ShowQrCode = p.ShowQrCode, GroupByLeg = p.GroupByLeg,
            Box1Sender = p.Box1Sender, Box2Consignee = p.Box2Consignee, Box3DeliveryPlace = p.Box3DeliveryPlace, Box4TakingOverPlace = p.Box4TakingOverPlace,
            Box16Carrier = p.Box16Carrier, Box17SuccessiveCarrier = p.Box17SuccessiveCarrier, Box21EstablishedBy = p.Box21EstablishedBy,
            Box6Columns = p.Box6Columns.ToList(), Box13Items = p.Box13Items.ToList(), Box18Items = p.Box18Items.ToList(),
            FixedDescriptionIds = p.FixedDescriptionIds.ToList(),
            FixedDescriptionNames = p.FixedDescriptionIds.Select(id => fixedNames.GetValueOrDefault(id, L["Orders:NotFound"])).ToList(),
            ReportDefinitionId = p.ReportDefinitionId,
            ReportDefinitionName = p.ReportDefinitionId == null ? null : definitions.GetValueOrDefault(p.ReportDefinitionId.Value, L["Orders:NotFound"]),
            IsDefault = p.IsDefault, IsArchived = p.IsArchived
        };
        MapAudit(p, dto);
        return dto;
    }

    protected override ReferenceDataLookupDto ToLookup(ConsignmentNoteProfile p) => new() { Id = p.Id, Name = p.Name };

    protected override string DisplayNameOf(ConsignmentNoteProfile p) => p.Name;

    protected override async Task<ConsignmentNoteProfile> CreateEntityAsync(ConsignmentNoteProfileCreateDto input)
    {
        var normalized = input.Name.Trim().ToUpperInvariant();
        await EnsureUniqueAsync(p => p.NormalizedName == normalized, "name", input.Name.Trim());
        var profile = new ConsignmentNoteProfile(GuidGenerator.Create(), CurrentTenant.Id, input.Name);
        await ApplyAsync(profile, input);
        if (!await Repository.AnyAsync())
        {
            profile.SetDefault(true);
        }

        profile.SetArchived(input.IsArchived);
        return profile;
    }

    protected override async Task ApplyUpdateAsync(ConsignmentNoteProfile profile, ConsignmentNoteProfileUpdateDto input)
    {
        var normalized = input.Name.Trim().ToUpperInvariant();
        if (normalized != profile.NormalizedName)
        {
            await EnsureUniqueAsync(p => p.NormalizedName == normalized && p.Id != profile.Id, "name", input.Name.Trim());
            profile.SetName(input.Name);
        }

        await ApplyAsync(profile, input);
        profile.SetArchived(input.IsArchived);
    }

    private async Task ApplyAsync(ConsignmentNoteProfile profile, ConsignmentNoteProfileCreateDto input)
    {
        profile.SetDescription(input.Description);
        profile.SetLayout(input.Copies, input.ShowBorders, input.ShowQrCode, input.GroupByLeg);
        profile.SetPartySources(input.Box1Sender, input.Box2Consignee, input.Box3DeliveryPlace, input.Box4TakingOverPlace, input.Box16Carrier, input.Box17SuccessiveCarrier, input.Box21EstablishedBy);
        profile.SetItems(input.Box6Columns, input.Box13Items, input.Box18Items);

        var fixedIds = input.FixedDescriptionIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (fixedIds.Count > 0 && await _fixedDescriptions.CountAsync(f => fixedIds.Contains(f.Id)) != fixedIds.Count)
        {
            throw new BusinessException(ReferenceDataErrorCodes.ReferenceNotFound).WithData("field", "fixedDescriptionIds");
        }

        profile.SetFixedDescriptions(fixedIds);

        if (input.ReportDefinitionId != null)
        {
            var definition = await _reportDefinitions.FindAsync(input.ReportDefinitionId.Value);
            if (definition == null || definition.Purpose != ReportPurpose.ConsignmentNote)
            {
                throw new BusinessException(ReferenceDataErrorCodes.ReferenceNotFound).WithData("field", "reportDefinitionId");
            }
        }

        profile.SetReportDefinition(input.ReportDefinitionId);
    }

    protected override Task<string?> GetDeleteBlockerAsync(ConsignmentNoteProfile profile)
        => Task.FromResult(profile.IsDefault ? L["Orders:DefaultProfileCannotBeDeleted"].Value : null);

    private async Task<Dictionary<Guid, string>> DefinitionNamesAsync(IEnumerable<Guid> ids)
    {
        var keys = ids.Distinct().ToList();
        return keys.Count == 0 ? new Dictionary<Guid, string>() : (await _reportDefinitions.GetListAsync(d => keys.Contains(d.Id))).ToDictionary(d => d.Id, d => d.Name);
    }
}

/// <summary>Design 58: the reusable texts of boxes 6 and 13, a Master data list under Orders → Transport orders.</summary>
[RequiresFeature(AppFeatures.MasterData.Default)]
public class ConsignmentNoteFixedDescriptionAppService
    : ReferenceDataCrudAppServiceBase<ConsignmentNoteFixedDescription, ConsignmentNoteFixedDescriptionRowDto, ConsignmentNoteFixedDescriptionDto, ConsignmentNoteFixedDescriptionCreateDto, ConsignmentNoteFixedDescriptionUpdateDto, ArchivableListRequestDto>,
      IConsignmentNoteFixedDescriptionAppService
{
    private readonly IRepository<ConsignmentNoteProfile, Guid> _profiles;

    public ConsignmentNoteFixedDescriptionAppService(IRepository<ConsignmentNoteFixedDescription, Guid> repository, IRepository<ConsignmentNoteProfile, Guid> profiles)
        : base(repository)
    {
        _profiles = profiles;
        LocalizationResource = typeof(OrdersResource);
    }

    [RequiresFeature(AppFeatures.MasterData.Orders.Default, AppFeatures.MasterData.Orders.TransportOrders.Default, AppFeatures.MasterData.Orders.TransportOrders.ConsignmentNoteFixedDescriptions, RequiresAll = true)]
    [Authorize(OrdersPermissions.ConsignmentNoteFixedDescriptions.Default)]
    public Task<GridResponse<ConsignmentNoteFixedDescriptionRowDto>> GetListAsync(ArchivableListRequestDto input) => GetListInternalAsync(input);

    [RequiresFeature(AppFeatures.MasterData.Orders.Default, AppFeatures.MasterData.Orders.TransportOrders.Default, AppFeatures.MasterData.Orders.TransportOrders.ConsignmentNoteFixedDescriptions, RequiresAll = true)]
    [Authorize(OrdersPermissions.ConsignmentNoteFixedDescriptions.Default)]
    public Task<ConsignmentNoteFixedDescriptionDto> GetAsync(Guid id) => GetInternalAsync(id);

    [Authorize(OrdersPermissions.ConsignmentNoteFixedDescriptions.Default)]
    public Task<List<ReferenceDataLookupDto>> GetLookupAsync(string? term = null, int take = ReferenceDataConsts.MaxLookupTake) => GetLookupInternalAsync(term, take);

    [RequiresFeature(AppFeatures.MasterData.Orders.Default, AppFeatures.MasterData.Orders.TransportOrders.Default, AppFeatures.MasterData.Orders.TransportOrders.ConsignmentNoteFixedDescriptions, RequiresAll = true)]
    [Authorize(OrdersPermissions.ConsignmentNoteFixedDescriptions.Create)]
    public Task<ConsignmentNoteFixedDescriptionDto> CreateAsync(ConsignmentNoteFixedDescriptionCreateDto input) => CreateInternalAsync(input);

    [RequiresFeature(AppFeatures.MasterData.Orders.Default, AppFeatures.MasterData.Orders.TransportOrders.Default, AppFeatures.MasterData.Orders.TransportOrders.ConsignmentNoteFixedDescriptions, RequiresAll = true)]
    [Authorize(OrdersPermissions.ConsignmentNoteFixedDescriptions.Update)]
    public Task<ConsignmentNoteFixedDescriptionDto> UpdateAsync(Guid id, ConsignmentNoteFixedDescriptionUpdateDto input) => UpdateInternalAsync(id, input);

    [RequiresFeature(AppFeatures.MasterData.Orders.Default, AppFeatures.MasterData.Orders.TransportOrders.Default, AppFeatures.MasterData.Orders.TransportOrders.ConsignmentNoteFixedDescriptions, RequiresAll = true)]
    [Authorize(OrdersPermissions.ConsignmentNoteFixedDescriptions.Delete)]
    public Task DeleteAsync(Guid id) => DeleteInternalAsync(id);

    [RequiresFeature(AppFeatures.MasterData.Orders.Default, AppFeatures.MasterData.Orders.TransportOrders.Default, AppFeatures.MasterData.Orders.TransportOrders.ConsignmentNoteFixedDescriptions, RequiresAll = true)]
    [Authorize(OrdersPermissions.ConsignmentNoteFixedDescriptions.Delete)]
    public Task<ReferenceDataBulkDeleteResultDto> BulkDeleteAsync(ArchivableBulkDeleteRequestDto input)
        => BulkDeleteInternalAsync(input.Selection, q => input.IncludeArchived ? q : q.Where(f => !f.IsArchived));

    protected override IQueryable<ConsignmentNoteFixedDescription> ApplyFilters(IQueryable<ConsignmentNoteFixedDescription> query, ArchivableListRequestDto request)
        => request.IncludeArchived ? query : query.Where(f => !f.IsArchived);

    protected override IQueryable<ConsignmentNoteFixedDescription> ApplySearch(IQueryable<ConsignmentNoteFixedDescription> query, string term)
        => query.Where(f => f.Name.ToLower().Contains(term) || f.Text.ToLower().Contains(term));

    protected override IOrderedQueryable<ConsignmentNoteFixedDescription> ApplySort(IQueryable<ConsignmentNoteFixedDescription> query, string? sortBy, bool descending) => (sortBy?.ToLowerInvariant()) switch
    {
        null or "" or "name" => Order(query, f => f.Name, descending),
        "text" => Order(query, f => f.Text, descending),
        "isarchived" => Order(query, f => f.IsArchived, descending),
        _ => throw UnknownSortColumn(sortBy)
    };

    protected override IQueryable<ConsignmentNoteFixedDescription> ApplyLookupFilter(IQueryable<ConsignmentNoteFixedDescription> query) => query.Where(f => !f.IsArchived);

    protected override Task<List<ConsignmentNoteFixedDescriptionRowDto>> ToRowsAsync(List<ConsignmentNoteFixedDescription> entities)
        => Task.FromResult(entities.Select(f => new ConsignmentNoteFixedDescriptionRowDto { Id = f.Id, Name = f.Name, Text = f.Text, IsArchived = f.IsArchived, Description = f.Description }).ToList());

    protected override Task<ConsignmentNoteFixedDescriptionDto> ToDtoAsync(ConsignmentNoteFixedDescription f)
    {
        var dto = new ConsignmentNoteFixedDescriptionDto { Name = f.Name, Text = f.Text, IsArchived = f.IsArchived };
        MapAudit(f, dto);
        return Task.FromResult(dto);
    }

    protected override ReferenceDataLookupDto ToLookup(ConsignmentNoteFixedDescription f) => new() { Id = f.Id, Name = f.Name };

    protected override string DisplayNameOf(ConsignmentNoteFixedDescription f) => f.Name;

    protected override async Task<ConsignmentNoteFixedDescription> CreateEntityAsync(ConsignmentNoteFixedDescriptionCreateDto input)
    {
        var normalized = input.Name.Trim().ToUpperInvariant();
        await EnsureUniqueAsync(f => f.NormalizedName == normalized, "name", input.Name.Trim());
        var description = new ConsignmentNoteFixedDescription(GuidGenerator.Create(), CurrentTenant.Id, input.Name, input.Text);
        description.SetDescription(input.Description);
        description.SetArchived(input.IsArchived);
        return description;
    }

    protected override async Task ApplyUpdateAsync(ConsignmentNoteFixedDescription description, ConsignmentNoteFixedDescriptionUpdateDto input)
    {
        var normalized = input.Name.Trim().ToUpperInvariant();
        if (normalized != description.NormalizedName)
        {
            await EnsureUniqueAsync(f => f.NormalizedName == normalized && f.Id != description.Id, "name", input.Name.Trim());
            description.SetName(input.Name);
        }

        description.SetText(input.Text);
        description.SetDescription(input.Description);
        description.SetArchived(input.IsArchived);
    }

    /// <summary>In use by a profile: the profiles of a tenant are few, so the jsonb lists are checked in memory.</summary>
    protected override async Task<string?> GetDeleteBlockerAsync(ConsignmentNoteFixedDescription description)
    {
        var user = (await _profiles.GetListAsync()).FirstOrDefault(p => p.FixedDescriptionIds.Contains(description.Id));
        return user == null ? null : L["Orders:FixedDescriptionUsedByProfile", user.Name].Value;
    }
}
