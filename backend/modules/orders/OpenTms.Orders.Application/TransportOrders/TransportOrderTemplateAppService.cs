using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using OpenTms.MasterData.Published;
using OpenTms.Orders.Permissions;
using OpenTms.Platform.Features;
using OpenTms.Platform.Grid;
using OpenTms.Platform.ReferenceData;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Features;

namespace OpenTms.Orders.TransportOrders;

/// <summary>
/// Templates and recurrence (design 58): CRUD on the template, "new from template" (materialise), "copy order" through
/// a transient template (Assumption 5) and "template from order". Materialisation is idempotent on template + date +
/// copy index: an order that exists is skipped, the unique index remains the truth.
/// </summary>
[RequiresFeature(AppFeatures.Transport)]
public class TransportOrderTemplateAppService : OrdersAppServiceBase, ITransportOrderTemplateAppService
{
    private readonly IRepository<TransportOrderTemplate, Guid> _templates;
    private readonly IRepository<TransportOrder, Guid> _orders;
    private readonly IRepository<TransportOrderType, Guid> _orderTypes;
    private readonly TemplateMaterialisationRunner _runner;
    private readonly OrdersReferences _references;
    private readonly TransportOrderDtoMapper _mapper;
    private readonly OrdersClock _clock;

    public TransportOrderTemplateAppService(
        IRepository<TransportOrderTemplate, Guid> templates,
        IRepository<TransportOrder, Guid> orders,
        IRepository<TransportOrderType, Guid> orderTypes,
        TemplateMaterialisationRunner runner,
        OrdersReferences references,
        TransportOrderDtoMapper mapper,
        OrdersClock clock)
    {
        _templates = templates;
        _orders = orders;
        _orderTypes = orderTypes;
        _runner = runner;
        _references = references;
        _mapper = mapper;
        _clock = clock;
    }

    [RequiresFeature(AppFeatures.TransportApp.Orders, AppFeatures.TransportApp.OrderTemplates, RequiresAll = true)]
    [Authorize(OrdersPermissions.TransportOrderTemplates.Default)]
    public virtual async Task<GridResponse<TransportOrderTemplateRowDto>> GetListAsync(TransportOrderTemplateListRequestDto input)
    {
        var filtered = ApplyFilters(await _templates.GetQueryableAsync(), input.IncludeInactive, input.CustomerRelationId);
        var term = input.WildcardSearch?.Trim().ToLowerInvariant();
        var searched = string.IsNullOrEmpty(term) ? filtered : ApplySearch(filtered, term);
        var totalCount = await AsyncExecuter.LongCountAsync(filtered);
        var filteredCount = string.IsNullOrEmpty(term) ? totalCount : await AsyncExecuter.LongCountAsync(searched);
        var page = await AsyncExecuter.ToListAsync(ApplySort(searched, input.SortBy, input.Descending).Skip(input.StartRow).Take(input.EndRow - input.StartRow));

        var typeIds = page.Select(t => t.TransportOrderTypeId).Distinct().ToList();
        var types = typeIds.Count == 0 ? new Dictionary<Guid, string>() : (await _orderTypes.GetListAsync(t => typeIds.Contains(t.Id))).ToDictionary(t => t.Id, t => t.Name.Resolve(CultureInfo.CurrentUICulture.Name));
        var pageIds = page.Select(t => t.Id).ToList();
        var lineCounts = pageIds.Count == 0
            ? new Dictionary<Guid, int>()
            : (await LazyServiceProvider.LazyGetRequiredService<IRepository<TransportOrderTemplateLine, Guid>>().GetListAsync(l => pageIds.Contains(l.TemplateId)))
                .GroupBy(l => l.TemplateId).ToDictionary(g => g.Key, g => g.Count());

        return new GridResponse<TransportOrderTemplateRowDto>
        {
            Rows = page.Select(t => new TransportOrderTemplateRowDto
            {
                Id = t.Id,
                Name = t.Name,
                CustomerRelationId = t.CustomerRelationId,
                CustomerName = t.CustomerName,
                TransportOrderTypeName = types.GetValueOrDefault(t.TransportOrderTypeId, L["Orders:NotFound"]),
                Recurrence = t.Schedule.Recurrence,
                Interval = t.Schedule.Interval,
                DaysOfWeek = t.Schedule.DaysOfWeek,
                DayOfMonth = t.Schedule.DayOfMonth,
                HorizonDays = t.Schedule.HorizonDays,
                ValidFrom = t.Schedule.ValidFrom,
                ValidTill = t.Schedule.ValidTill,
                LastMaterialisedDate = t.LastMaterialisedDate,
                LineCount = lineCounts.GetValueOrDefault(t.Id),
                HasTripRequest = t.TripRequest != null,
                IsActive = t.IsActive,
                Description = t.Description
            }).ToList(),
            TotalCount = totalCount,
            FilteredCount = filteredCount
        };
    }

    [RequiresFeature(AppFeatures.TransportApp.Orders, AppFeatures.TransportApp.OrderTemplates, RequiresAll = true)]
    [Authorize(OrdersPermissions.TransportOrderTemplates.Default)]
    public virtual async Task<TransportOrderTemplateDto> GetAsync(Guid id) => await ToDtoAsync(await _templates.GetAsync(id, includeDetails: true));

    [Authorize(OrdersPermissions.TransportOrderTemplates.Default)]
    public virtual async Task<List<ReferenceDataLookupDto>> GetLookupAsync(string? term = null, int take = ReferenceDataConsts.MaxLookupTake)
    {
        var query = (await _templates.GetQueryableAsync()).Where(t => t.IsActive);
        var normalized = term?.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(normalized))
        {
            query = ApplySearch(query, normalized);
        }

        var rows = await AsyncExecuter.ToListAsync(query.OrderBy(t => t.Name).Take(Math.Clamp(take, 1, ReferenceDataConsts.MaxLookupTake)));
        return rows.Select(t => new ReferenceDataLookupDto { Id = t.Id, Name = t.Name }).ToList();
    }

    [RequiresFeature(AppFeatures.TransportApp.Orders, AppFeatures.TransportApp.OrderTemplates, RequiresAll = true)]
    [Authorize(OrdersPermissions.TransportOrderTemplates.Create)]
    public virtual async Task<TransportOrderTemplateDto> CreateAsync(TransportOrderTemplateCreateDto input)
    {
        await EnsureUniqueNameAsync(input.Name, null);
        var customerName = await ValidateAsync(input);
        var template = new TransportOrderTemplate(GuidGenerator.Create(), CurrentTenant.Id, input.Name,
            TransportOrderInputMapper.Header(ToHeader(input), customerName, _clock.Today()), TransportOrderInputMapper.Terms(input.FinancialTerms),
            TransportOrderInputMapper.Schedule(input.Schedule), GuidGenerator.Create);
        Apply(template, input);
        await _templates.InsertAsync(template, autoSave: true);
        return await ToDtoAsync(await _templates.GetAsync(template.Id, includeDetails: true));
    }

    [RequiresFeature(AppFeatures.TransportApp.Orders, AppFeatures.TransportApp.OrderTemplates, RequiresAll = true)]
    [Authorize(OrdersPermissions.TransportOrderTemplates.Update)]
    public virtual async Task<TransportOrderTemplateDto> UpdateAsync(Guid id, TransportOrderTemplateUpdateDto input)
    {
        var template = await _templates.GetAsync(id, includeDetails: true);
        template.ConcurrencyStamp = input.ConcurrencyStamp;
        await EnsureUniqueNameAsync(input.Name, id);
        var customerName = await ValidateAsync(input);
        template.SetName(input.Name);
        template.SetHeader(TransportOrderInputMapper.Header(ToHeader(input), customerName, _clock.Today()));
        template.SetFinancialTerms(TransportOrderInputMapper.Terms(input.FinancialTerms), GuidGenerator.Create);
        template.SetSchedule(TransportOrderInputMapper.Schedule(input.Schedule));
        Apply(template, input);
        await _templates.UpdateAsync(template, autoSave: true);
        await FlushAsync();
        return await ToDtoAsync(await _templates.GetAsync(id, includeDetails: true));
    }

    [RequiresFeature(AppFeatures.TransportApp.Orders, AppFeatures.TransportApp.OrderTemplates, RequiresAll = true)]
    [Authorize(OrdersPermissions.TransportOrderTemplates.Delete)]
    public virtual async Task DeleteAsync(Guid id) => await _templates.DeleteAsync(id);

    [RequiresFeature(AppFeatures.TransportApp.Orders, AppFeatures.TransportApp.OrderTemplates, RequiresAll = true)]
    [Authorize(OrdersPermissions.TransportOrderTemplates.Delete)]
    public virtual async Task<ReferenceDataBulkDeleteResultDto> BulkDeleteAsync(TransportOrderTemplateBulkDeleteRequestDto input)
    {
        List<TransportOrderTemplate> targets;
        if (string.Equals(input.Selection.Mode, "FilterBased", StringComparison.OrdinalIgnoreCase))
        {
            var query = ApplyFilters(await _templates.GetQueryableAsync(), input.IncludeInactive, null);
            var term = input.Selection.FilterRequest?.WildcardSearch?.Trim().ToLowerInvariant();
            if (!string.IsNullOrEmpty(term))
            {
                query = ApplySearch(query, term);
            }

            var excluded = input.Selection.ExcludedIds;
            targets = await AsyncExecuter.ToListAsync(query.Where(t => !excluded.Contains(t.Id)).OrderBy(t => t.Name).Take(ReferenceDataConsts.MaxBulkDeleteRows + 1));
        }
        else
        {
            var ids = input.Selection.ExplicitIds.Distinct().ToList();
            targets = ids.Count == 0 ? [] : await _templates.GetListAsync(t => ids.Contains(t.Id));
        }

        if (targets.Count > ReferenceDataConsts.MaxBulkDeleteRows)
        {
            throw new BusinessException(ReferenceDataErrorCodes.TooManyRows).WithData("max", ReferenceDataConsts.MaxBulkDeleteRows);
        }

        foreach (var template in targets)
        {
            await _templates.DeleteAsync(template);
        }

        return new ReferenceDataBulkDeleteResultDto { DeletedCount = targets.Count };
    }

    [RequiresFeature(AppFeatures.TransportApp.Orders, AppFeatures.TransportApp.OrderTemplates, RequiresAll = true)]
    [Authorize(OrdersPermissions.TransportOrders.Create)]
    public virtual async Task<MaterialisationResultDto> MaterialiseAsync(Guid id, MaterialiseTemplateDto input)
    {
        var template = await _templates.GetAsync(id, includeDetails: true);
        if (!template.IsActive)
        {
            throw new BusinessException(OrdersErrorCodes.TemplateInactive);
        }

        var dates = Dates(input);
        var details = Details(input, dates, template.Lines.OrderBy(l => l.Position).Select(l => l.Id).ToList());
        var result = await _runner.MaterialiseAsync(template, dates, input.Copies, storedTemplate: true, details);
        await _templates.UpdateAsync(template, autoSave: true);
        return result;
    }

    [RequiresFeature(AppFeatures.TransportApp.Orders, AppFeatures.TransportApp.OrderTemplates, RequiresAll = true)]
    [Authorize(OrdersPermissions.TransportOrderTemplates.Create)]
    public virtual async Task<TransportOrderTemplateDto> CreateFromOrderAsync(Guid orderId)
    {
        var order = await _orders.GetAsync(orderId, includeDetails: true);
        var baseName = L["Orders:TemplateFromOrderName", order.Number ?? order.CustomerName].Value;
        var name = baseName;
        for (var i = 2; await _templates.AnyAsync(t => t.NormalizedName == name.ToUpperInvariant()); i++)
        {
            name = $"{baseName} ({i})";
        }

        var template = TransportOrderTemplate.FromOrder(GuidGenerator.Create(), order, name, _clock.Zone, GuidGenerator.Create);
        await _templates.InsertAsync(template, autoSave: true);
        return await ToDtoAsync(await _templates.GetAsync(template.Id, includeDetails: true));
    }

    [RequiresFeature(AppFeatures.TransportApp.Orders)]
    [Authorize(OrdersPermissions.TransportOrders.Create)]
    public virtual async Task<MaterialisationResultDto> CopyOrderAsync(Guid orderId, MaterialiseTemplateDto input)
    {
        var order = await _orders.GetAsync(orderId, includeDetails: true);
        var dates = Dates(input);
        /* The transient template keeps the order's lines in position order: the wizard names the order's own line ids. */
        var details = Details(input, dates, order.Lines.OrderBy(l => l.Position).Select(l => l.Id).ToList());
        var transient = TransportOrderTemplate.FromOrder(GuidGenerator.Create(), order, "copy", _clock.Zone, GuidGenerator.Create);
        return await _runner.MaterialiseAsync(transient, dates, input.Copies, storedTemplate: false, details);
    }

    /// <summary>
    /// The wizard's per-order details (design 61 "Details"), validated against the request: a date of the request, a copy
    /// index below the copies, each (date, copy) once, each line once and known to the source.
    /// </summary>
    private static Dictionary<(DateOnly Date, int CopyIndex), MaterialisationDetails> Details(MaterialiseTemplateDto input, IReadOnlyList<DateOnly> dates, IReadOnlyList<Guid> lineIds)
    {
        var result = new Dictionary<(DateOnly, int), MaterialisationDetails>();
        if (input.Overrides.Count > OrdersConsts.MaxMaterialisationOverrides)
        {
            throw new BusinessException(OrdersErrorCodes.MaterialisationOverrideInvalid).WithData("date", string.Empty).WithData("copyIndex", string.Empty);
        }

        var indexOf = lineIds.Select((id, index) => (id, index)).ToDictionary(p => p.id, p => p.index);
        foreach (var o in input.Overrides)
        {
            if (!dates.Contains(o.Date) || o.CopyIndex < 0 || o.CopyIndex >= input.Copies || result.ContainsKey((o.Date, o.CopyIndex)))
            {
                throw new BusinessException(OrdersErrorCodes.MaterialisationOverrideInvalid).WithData("date", o.Date.ToString("yyyy-MM-dd")).WithData("copyIndex", o.CopyIndex);
            }

            if (o.CustomerReference is { Length: > OrdersConsts.MaxCustomerReferenceLength })
            {
                throw new BusinessException(OrdersErrorCodes.MaterialisationOverrideInvalid).WithData("date", o.Date.ToString("yyyy-MM-dd")).WithData("copyIndex", o.CopyIndex);
            }

            var lines = new Dictionary<int, MaterialisationLineDetails>();
            foreach (var line in o.Lines)
            {
                if (!indexOf.TryGetValue(line.LineId, out var index))
                {
                    throw new BusinessException(OrdersErrorCodes.MaterialisationLineUnknown).WithData("lineId", line.LineId);
                }

                if (lines.ContainsKey(index) || line.Quantity is < 0 || line.WeightKg is < 0 || (line.WeightKg is { } w && !double.IsFinite(w)))
                {
                    throw new BusinessException(OrdersErrorCodes.MaterialisationOverrideInvalid).WithData("date", o.Date.ToString("yyyy-MM-dd")).WithData("copyIndex", o.CopyIndex);
                }

                lines[index] = new MaterialisationLineDetails(line.Quantity, line.WeightKg);
            }

            result[(o.Date, o.CopyIndex)] = new MaterialisationDetails(o.CustomerReference, lines);
        }

        return result;
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    private static IReadOnlyList<DateOnly> Dates(MaterialiseTemplateDto input)
    {
        var dates = input.Dates.Distinct().OrderBy(d => d).ToList();
        if (dates.Count == 0 || dates.Count > OrdersConsts.MaxMaterialisationDates || input.Copies is < 1 or > OrdersConsts.MaxMaterialisationCopies)
        {
            throw new BusinessException(OrdersErrorCodes.MaterialisationInputInvalid).WithData("max", OrdersConsts.MaxMaterialisationCopies);
        }

        return dates;
    }

    private static TransportOrderHeaderDto ToHeader(TransportOrderTemplateCreateDto input) => new()
    {
        TransportOrderTypeId = input.TransportOrderTypeId,
        CustomerRelationId = input.CustomerRelationId,
        CustomerContactPersonId = input.CustomerContactPersonId,
        CustomerReference = input.CustomerReference,
        OrganizationUnitId = input.OrganizationUnitId,
        InternalRemarks = input.InternalRemarks,
        InvoicingRemarks = input.InvoicingRemarks
    };

    private void Apply(TransportOrderTemplate template, TransportOrderTemplateCreateDto input)
    {
        template.SetDescription(input.Description);
        template.SetAttributes(input.Attributes);
        template.SetDefaultStops(input.DefaultStops.Select(TransportOrderInputMapper.DefaultStop).ToList());
        template.SetLines(input.Lines.Select(TransportOrderInputMapper.TemplateLine).ToList(), GuidGenerator.Create);
        template.SetReferences(input.References.Select(TransportOrderInputMapper.Reference).ToList(), GuidGenerator.Create);
        template.SetTripRequest(TransportOrderInputMapper.TemplateTripRequest(input.TripRequest));
        template.SetActive(input.IsActive);
    }

    private async Task<string> ValidateAsync(TransportOrderTemplateCreateDto input)
    {
        await _references.GetOrderTypeAsync(input.TransportOrderTypeId);
        var customerName = await _references.RelationNameAsync(input.CustomerRelationId, "customerRelationId");
        if (input.CustomerContactPersonId != null)
        {
            await _references.NamesAsync(MasterDataReferenceKind.ContactPerson, [input.CustomerContactPersonId.Value], require: true, "customerContactPersonId");
        }

        await _references.ValidateLinesAsync(input.Lines.Select(l => (
            TransportOrderInputMapper.Freight(l),
            l.Stops.Select(s => (s.WaypointTypeId, (Guid?)s.RelationId, (Guid?)s.RelationAddressId, s.TimeSlotId, s.ContactPersonId)).ToList(),
            (IReadOnlyList<Guid>)l.RequiredTransportCharacteristicIds,
            (IReadOnlyDictionary<string, string>?)l.Attributes)).ToList());
        await _references.ValidatePatternAsync(input.DefaultStops.Select(s => (s.WaypointTypeId, s.RelationId, s.RelationAddressId, s.TimeSlotId)));
        await _references.EnsureFinancialTermsAsync(TransportOrderInputMapper.Terms(input.FinancialTerms));
        await _references.EnsureAttributeKeysAsync(OrderAttributeTarget.Order, [input.Attributes]);
        if (input.TripRequest is { } trip)
        {
            await _references.ValidateTripRequestAsync(trip.DepartureRelationId, trip.DepartureAddressId, trip.ArrivalRelationId, trip.ArrivalAddressId, trip.CarrierRelationId, trip.VehicleCombinationId, trip.VehicleIds, trip.DriverIds);
        }

        return customerName;
    }

    private async Task EnsureUniqueNameAsync(string name, Guid? exceptId)
    {
        var normalized = name.Trim().ToUpperInvariant();
        if (await _templates.AnyAsync(t => t.NormalizedName == normalized && (exceptId == null || t.Id != exceptId.Value)))
        {
            throw new BusinessException(ReferenceDataErrorCodes.DuplicateKey).WithData("field", "name").WithData("value", name.Trim());
        }
    }

    private static IQueryable<TransportOrderTemplate> ApplyFilters(IQueryable<TransportOrderTemplate> query, bool includeInactive, Guid? customerId)
    {
        if (!includeInactive)
        {
            query = query.Where(t => t.IsActive);
        }

        return customerId == null ? query : query.Where(t => t.CustomerRelationId == customerId.Value);
    }

    private static IQueryable<TransportOrderTemplate> ApplySearch(IQueryable<TransportOrderTemplate> query, string term)
        => query.Where(t => t.Name.ToLower().Contains(term) || t.CustomerName.ToLower().Contains(term) || (t.CustomerReference != null && t.CustomerReference.ToLower().Contains(term)));

    private static IOrderedQueryable<TransportOrderTemplate> ApplySort(IQueryable<TransportOrderTemplate> query, string? sortBy, bool descending) => (sortBy?.ToLowerInvariant()) switch
    {
        null or "" or "name" => Order(query, t => t.Name, descending),
        "customername" => Order(query, t => t.CustomerName, descending),
        "recurrence" => Order(query, t => t.Schedule.Recurrence, descending),
        "validfrom" => Order(query, t => t.Schedule.ValidFrom, descending),
        "validtill" => Order(query, t => t.Schedule.ValidTill, descending),
        "lastmaterialiseddate" => Order(query, t => t.LastMaterialisedDate, descending),
        "isactive" => Order(query, t => t.IsActive, descending),
        _ => throw new BusinessException(ReferenceDataErrorCodes.UnknownSortColumn).WithData("sortBy", sortBy)
    };

    private static IOrderedQueryable<TransportOrderTemplate> Order<TKey>(IQueryable<TransportOrderTemplate> query, Expression<Func<TransportOrderTemplate, TKey>> key, bool descending)
        => descending ? query.OrderByDescending(key) : query.OrderBy(key);

    private async Task FlushAsync()
    {
        if (UnitOfWorkManager.Current != null)
        {
            await UnitOfWorkManager.Current.SaveChangesAsync();
        }
    }

    private async Task<TransportOrderTemplateDto> ToDtoAsync(TransportOrderTemplate t)
    {
        var type = await _orderTypes.FindAsync(t.TransportOrderTypeId);
        var details = t.Lines.SelectMany(l => l.Stops).Select(s => s.Details).Concat(t.DefaultStops.Select(s => s.Details)).ToList();
        var relations = await _references.RelationsAsync(details.Select(d => d.RelationId).OfType<Guid>(), require: false);
        var waypointTypes = await _references.WaypointTypesAsync(details.Select(d => d.WaypointTypeId), require: false);
        var units = await _references.NamesAsync(MasterDataReferenceKind.TransportUnit, t.Lines.Select(l => l.Freight.TransportUnitId), require: false, "transportUnitId");
        var products = await _references.NamesAsync(MasterDataReferenceKind.Product, t.Lines.Select(l => l.Freight.ProductId).OfType<Guid>(), require: false, "productId");
        var financial = await _references.FinancialNamesAsync(t.FinancialTermsInput());
        var culture = CultureInfo.CurrentUICulture.Name;
        string NotFound() => L["Orders:NotFound"];
        string RelationName(Guid? id) => id != null && relations.TryGetValue(id.Value, out var r) ? r.Name : NotFound();
        string WaypointName(Guid id) => waypointTypes.TryGetValue(id, out var w) ? w.Name.Resolve(culture) : NotFound();

        var dto = new TransportOrderTemplateDto
        {
            Id = t.Id,
            Name = t.Name,
            Description = t.Description,
            TransportOrderTypeId = t.TransportOrderTypeId,
            TransportOrderTypeName = type?.Name.Resolve(culture) ?? NotFound(),
            CustomerRelationId = t.CustomerRelationId,
            CustomerName = t.CustomerName,
            CustomerContactPersonId = t.CustomerContactPersonId,
            CustomerReference = t.CustomerReference,
            OrganizationUnitId = t.OrganizationUnitId,
            InternalRemarks = t.InternalRemarks,
            InvoicingRemarks = t.InvoicingRemarks,
            Attributes = new Dictionary<string, string>(t.Attributes),
            FinancialTerms = _mapper.TermsDto(t.FinancialTermsInput(), financial),
            DefaultStops = t.DefaultStops.OrderBy(s => s.Position).Select(s => new TransportOrderDefaultStopDto
            {
                Position = s.Position, WaypointTypeId = s.Details.WaypointTypeId, WaypointTypeName = WaypointName(s.Details.WaypointTypeId),
                RelationId = s.Details.RelationId, RelationName = s.Details.RelationId == null ? null : RelationName(s.Details.RelationId),
                RelationAddressId = s.Details.RelationAddressId, ContactPersonId = s.Details.ContactPersonId, DayOffset = s.DayOffset,
                TimeSlotId = s.Details.TimeSlotId, WindowFrom = s.Details.WindowFrom, WindowTill = s.Details.WindowTill, PlannedDurationMin = s.Details.PlannedDurationMin
            }).ToList(),
            Lines = t.Lines.OrderBy(l => l.Position).Select(l =>
            {
                var line = new TemplateLineDto
                {
                    /* The id the wizard's details name (MaterialisationLineOverrideDto.LineId); a template update replaces the lines and their ids. */
                    Id = l.Id,
                    Position = l.Position,
                    TransportUnitName = units.GetValueOrDefault(l.Freight.TransportUnitId, NotFound()),
                    ProductName = l.Freight.ProductId == null ? null : products.GetValueOrDefault(l.Freight.ProductId.Value, NotFound()),
                    Measurements = l.Measurements.Select(m => new TransportOrderMeasurementDto { Kind = m.Kind, Position = m.Position, Value = m.Value }).ToList(),
                    RequiredTransportCharacteristicIds = l.RequiredTransportCharacteristicIds.ToList(),
                    Attributes = new Dictionary<string, string>(l.Attributes),
                    Stops = l.Stops.OrderBy(s => s.Position).Select(s => new TemplateStopDto
                    {
                        Position = s.Position, WaypointTypeId = s.Details.WaypointTypeId, WaypointTypeName = WaypointName(s.Details.WaypointTypeId),
                        RelationId = s.Details.RelationId!.Value, RelationName = RelationName(s.Details.RelationId), RelationAddressId = s.Details.RelationAddressId!.Value,
                        ContactPersonId = s.Details.ContactPersonId, DayOffset = s.DayOffset, TimeSlotId = s.Details.TimeSlotId, WindowFrom = s.Details.WindowFrom,
                        WindowTill = s.Details.WindowTill, Reference = s.Details.Reference, Remarks = s.Details.Remarks, PlannedDurationMin = s.Details.PlannedDurationMin
                    }).ToList()
                };
                TransportOrderDtoMapper.CopyFreight(l.Freight, line);
                return line;
            }).ToList(),
            References = t.References.OrderBy(r => r.Position).Select(r => new TransportOrderReferenceEntryDto { Type = r.Type, Value = r.Value, Description = r.Description }).ToList(),
            TripRequest = t.TripRequest == null ? null : new TemplateTripRequestInputDto
            {
                TransportTripTypeId = t.TripRequest.TransportTripTypeId, DepartureRelationId = t.TripRequest.DepartureRelationId, DepartureAddressId = t.TripRequest.DepartureAddressId,
                ArrivalRelationId = t.TripRequest.ArrivalRelationId, ArrivalAddressId = t.TripRequest.ArrivalAddressId, DepartureDayOffset = t.TripRequest.DepartureDayOffset,
                DepartureTime = t.TripRequest.DepartureTime, ArrivalDayOffset = t.TripRequest.ArrivalDayOffset, ArrivalTime = t.TripRequest.ArrivalTime,
                CarrierRelationId = t.TripRequest.CarrierRelationId, CarrierContactPersonId = t.TripRequest.CarrierContactPersonId, VehicleCombinationId = t.TripRequest.VehicleCombinationId,
                VehicleIds = t.TripRequest.VehicleIds.ToList(), DriverIds = t.TripRequest.DriverIds.ToList(), AgreedPurchaseAmount = t.TripRequest.AgreedPurchaseAmount,
                AgreedPurchaseCurrencyCode = t.TripRequest.AgreedPurchaseCurrencyCode
            },
            Schedule = new TemplateScheduleDto
            {
                Recurrence = t.Schedule.Recurrence, Interval = t.Schedule.Interval, DaysOfWeek = t.Schedule.DaysOfWeek, DayOfMonth = t.Schedule.DayOfMonth,
                HorizonDays = t.Schedule.HorizonDays, ValidFrom = t.Schedule.ValidFrom, ValidTill = t.Schedule.ValidTill
            },
            LastMaterialisedDate = t.LastMaterialisedDate,
            IsActive = t.IsActive,
            SourceOrderId = t.SourceOrderId,
            UpcomingDates = t.IsActive ? t.Schedule.DueDates(_clock.Today()).ToList() : []
        };
        dto.CreationTime = t.CreationTime;
        dto.CreatorId = t.CreatorId;
        dto.LastModificationTime = t.LastModificationTime;
        dto.LastModifierId = t.LastModifierId;
        dto.ConcurrencyStamp = t.ConcurrencyStamp;
        return dto;
    }
}
