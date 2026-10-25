using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using OpenTms.Orders.Localization;
using OpenTms.Orders.Permissions;
using OpenTms.Platform.Features;
using OpenTms.Platform.Grid;
using OpenTms.Platform.Localization;
using OpenTms.Platform.ReferenceData;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Features;

namespace OpenTms.Orders.TransportOrders;

/* Design 51. Pickers (the lookup) need only the app feature; the list's own screen needs its node (PO 2026-10-14). */
[RequiresFeature(AppFeatures.MasterData.Default)]
public class TimeSlotAppService
    : ReferenceDataCrudAppServiceBase<TimeSlot, TimeSlotRowDto, TimeSlotDto, TimeSlotCreateDto, TimeSlotUpdateDto, ArchivableListRequestDto>,
      ITimeSlotAppService
{
    public TimeSlotAppService(IRepository<TimeSlot, Guid> repository)
        : base(repository)
    {
        LocalizationResource = typeof(OrdersResource);
    }

    [RequiresFeature(AppFeatures.MasterData.Orders.Default, AppFeatures.MasterData.Orders.TransportOrders.Default, AppFeatures.MasterData.Orders.TransportOrders.TimeSlots, RequiresAll = true)]
    [Authorize(OrdersPermissions.TimeSlots.Default)]
    public Task<GridResponse<TimeSlotRowDto>> GetListAsync(ArchivableListRequestDto input) => GetListInternalAsync(input);

    [RequiresFeature(AppFeatures.MasterData.Orders.Default, AppFeatures.MasterData.Orders.TransportOrders.Default, AppFeatures.MasterData.Orders.TransportOrders.TimeSlots, RequiresAll = true)]
    [Authorize(OrdersPermissions.TimeSlots.Default)]
    public Task<TimeSlotDto> GetAsync(Guid id) => GetInternalAsync(id);

    [Authorize(OrdersPermissions.TimeSlots.Default)]
    public Task<List<ReferenceDataLookupDto>> GetLookupAsync(string? term = null, int take = ReferenceDataConsts.MaxLookupTake) => GetLookupInternalAsync(term, take);

    [RequiresFeature(AppFeatures.MasterData.Orders.Default, AppFeatures.MasterData.Orders.TransportOrders.Default, AppFeatures.MasterData.Orders.TransportOrders.TimeSlots, RequiresAll = true)]
    [Authorize(OrdersPermissions.TimeSlots.Create)]
    public Task<TimeSlotDto> CreateAsync(TimeSlotCreateDto input) => CreateInternalAsync(input);

    [RequiresFeature(AppFeatures.MasterData.Orders.Default, AppFeatures.MasterData.Orders.TransportOrders.Default, AppFeatures.MasterData.Orders.TransportOrders.TimeSlots, RequiresAll = true)]
    [Authorize(OrdersPermissions.TimeSlots.Update)]
    public Task<TimeSlotDto> UpdateAsync(Guid id, TimeSlotUpdateDto input) => UpdateInternalAsync(id, input);

    [RequiresFeature(AppFeatures.MasterData.Orders.Default, AppFeatures.MasterData.Orders.TransportOrders.Default, AppFeatures.MasterData.Orders.TransportOrders.TimeSlots, RequiresAll = true)]
    [Authorize(OrdersPermissions.TimeSlots.Delete)]
    public Task DeleteAsync(Guid id) => DeleteInternalAsync(id);

    [RequiresFeature(AppFeatures.MasterData.Orders.Default, AppFeatures.MasterData.Orders.TransportOrders.Default, AppFeatures.MasterData.Orders.TransportOrders.TimeSlots, RequiresAll = true)]
    [Authorize(OrdersPermissions.TimeSlots.Delete)]
    public Task<ReferenceDataBulkDeleteResultDto> BulkDeleteAsync(ArchivableBulkDeleteRequestDto input)
        => BulkDeleteInternalAsync(input.Selection, q => input.IncludeArchived ? q : q.Where(t => !t.IsArchived));

    protected override IQueryable<TimeSlot> ApplyFilters(IQueryable<TimeSlot> query, ArchivableListRequestDto request)
        => request.IncludeArchived ? query : query.Where(t => !t.IsArchived);

    protected override IQueryable<TimeSlot> ApplySearch(IQueryable<TimeSlot> query, string term)
    {
        var language = UiLanguage;
        return query.Where(t =>
            t.Code.ToLower().Contains(term) ||
            (LocalizedNameQuery.Text(t.Name, language) ?? "").ToLower().Contains(term) ||
            (LocalizedNameQuery.Text(t.Name, LocalizedName.DefaultCulture) ?? "").ToLower().Contains(term));
    }

    protected override IOrderedQueryable<TimeSlot> ApplySort(IQueryable<TimeSlot> query, string? sortBy, bool descending)
    {
        var language = UiLanguage;
        return (sortBy?.ToLowerInvariant()) switch
        {
            null or "" or "code" => Order(query, t => t.Code, descending),
            "name" => Order(query, t => LocalizedNameQuery.Text(t.Name, language) ?? LocalizedNameQuery.Text(t.Name, LocalizedName.DefaultCulture), descending),
            "starttime" => Order(query, t => t.StartTime, descending),
            "endtime" => Order(query, t => t.EndTime, descending),
            "isovernight" => Order(query, t => t.EndTime < t.StartTime, descending),
            "isarchived" => Order(query, t => t.IsArchived, descending),
            _ => throw UnknownSortColumn(sortBy)
        };
    }

    protected override IQueryable<TimeSlot> ApplyLookupFilter(IQueryable<TimeSlot> query) => query.Where(t => !t.IsArchived);

    protected override Task<List<TimeSlotRowDto>> ToRowsAsync(List<TimeSlot> entities)
        => Task.FromResult(entities.Select(t => new TimeSlotRowDto
        {
            Id = t.Id,
            Code = t.Code,
            Name = Resolve(t.Name),
            StartTime = t.StartTime,
            EndTime = t.EndTime,
            IsOvernight = t.IsOvernight,
            IsArchived = t.IsArchived,
            Description = t.Description
        }).ToList());

    protected override Task<TimeSlotDto> ToDtoAsync(TimeSlot t)
    {
        var dto = new TimeSlotDto
        {
            Code = t.Code,
            Name = Resolve(t.Name),
            Names = ToNamesDto(t.Name),
            StartTime = t.StartTime,
            EndTime = t.EndTime,
            IsOvernight = t.IsOvernight,
            IsArchived = t.IsArchived
        };
        MapAudit(t, dto);
        return Task.FromResult(dto);
    }

    protected override ReferenceDataLookupDto ToLookup(TimeSlot t) => new() { Id = t.Id, Name = Resolve(t.Name), Code = t.Code };

    protected override string DisplayNameOf(TimeSlot t) => Resolve(t.Name);

    protected override async Task<TimeSlot> CreateEntityAsync(TimeSlotCreateDto input)
    {
        var code = ReferenceDataAggregateRoot.NormalizeCode(input.Code);
        await EnsureUniqueAsync(t => t.Code == code, "code", code);
        var slot = new TimeSlot(GuidGenerator.Create(), CurrentTenant.Id, code, ToLocalizedName(input.Names), input.StartTime, input.EndTime);
        slot.SetDescription(input.Description);
        slot.SetArchived(input.IsArchived);
        return slot;
    }

    protected override async Task ApplyUpdateAsync(TimeSlot slot, TimeSlotUpdateDto input)
    {
        var code = ReferenceDataAggregateRoot.NormalizeCode(input.Code);
        if (code != slot.Code)
        {
            await EnsureUniqueAsync(t => t.Code == code && t.Id != slot.Id, "code", code);
            slot.SetCode(code);
        }

        slot.SetName(ToLocalizedName(input.Names));
        slot.SetTimes(input.StartTime, input.EndTime);
        slot.SetDescription(input.Description);
        slot.SetArchived(input.IsArchived);
    }
}
