using System;
using System.Collections.Generic;
using System.Linq;
using OpenTms.Platform.ReferenceData;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace OpenTms.Orders.TransportOrders;

/// <summary>
/// A transport order template (design 58 "Templates and recurrence"): the order payload with its stops as day offsets
/// from the order date, a trip request with times of day, and a schedule the hourly job materialises. A template
/// made from an order ("copy order", Assumption 5) is the same class, transient or stored.
/// </summary>
public class TransportOrderTemplate : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }

    public string Name { get; private set; } = default!;

    /// <summary>Upper case: the unique key per tenant.</summary>
    public string NormalizedName { get; private set; } = default!;

    public string? Description { get; private set; }

    public Guid TransportOrderTypeId { get; private set; }

    public Guid CustomerRelationId { get; private set; }

    public Guid? CustomerContactPersonId { get; private set; }

    public string CustomerName { get; private set; } = string.Empty;

    public string? CustomerReference { get; private set; }

    public Guid? OrganizationUnitId { get; private set; }

    public string? InternalRemarks { get; private set; }

    public string? InvoicingRemarks { get; private set; }

    public Dictionary<string, string> Attributes { get; private set; } = new();

    public TransportOrderFinancialTerms FinancialTerms { get; private set; } = default!;

    public TransportOrderTemplateTripRequest? TripRequest { get; private set; }

    public TemplateSchedule Schedule { get; private set; } = default!;

    public DateOnly? LastMaterialisedDate { get; private set; }

    public bool IsActive { get; private set; } = true;

    /// <summary>The order this template was made from, when any.</summary>
    public Guid? SourceOrderId { get; private set; }

    public virtual ICollection<TransportOrderTemplateLine> Lines { get; private set; } = new List<TransportOrderTemplateLine>();

    public virtual ICollection<TransportOrderTemplateDefaultStop> DefaultStops { get; private set; } = new List<TransportOrderTemplateDefaultStop>();

    public virtual ICollection<TransportOrderTemplateReference> References { get; private set; } = new List<TransportOrderTemplateReference>();

    public virtual ICollection<TransportOrderTemplatePriceList> PriceLists { get; private set; } = new List<TransportOrderTemplatePriceList>();

    public virtual ICollection<TransportOrderTemplateDiscountOrSurcharge> DiscountsOrSurcharges { get; private set; } = new List<TransportOrderTemplateDiscountOrSurcharge>();

    protected TransportOrderTemplate()
    {
    }

    public TransportOrderTemplate(Guid id, Guid? tenantId, string name, OrderHeaderInput header, FinancialTermsInput terms, TemplateSchedule schedule, Func<Guid> newId)
        : base(id)
    {
        TenantId = tenantId;
        SetName(name);
        SetHeader(header);
        SetFinancialTerms(terms, newId);
        SetSchedule(schedule);
    }

    public void SetName(string name)
    {
        Name = OrderValues.RequiredText(name, OrdersConsts.MaxTemplateNameLength, "name");
        NormalizedName = Name.ToUpperInvariant();
    }

    public void SetDescription(string? description) => Description = OrderValues.Text(description, ReferenceDataConsts.MaxDescriptionLength, "description");

    /// <summary>The header fields; the order date of the input is ignored (a template has none).</summary>
    public void SetHeader(OrderHeaderInput header)
    {
        Check.NotNull(header, nameof(header));
        if (header.TransportOrderTypeId == Guid.Empty)
        {
            throw new BusinessException(ReferenceDataErrorCodes.ReferenceNotFound).WithData("field", "transportOrderTypeId");
        }

        if (header.CustomerRelationId == Guid.Empty)
        {
            throw new BusinessException(ReferenceDataErrorCodes.ReferenceNotFound).WithData("field", "customerRelationId");
        }

        TransportOrderTypeId = header.TransportOrderTypeId;
        CustomerRelationId = header.CustomerRelationId;
        CustomerContactPersonId = header.CustomerContactPersonId;
        CustomerName = OrderValues.Text(header.CustomerName, OrdersConsts.MaxStopNameLength, "customerName") ?? string.Empty;
        CustomerReference = OrderValues.Text(header.CustomerReference, OrdersConsts.MaxCustomerReferenceLength, "customerReference");
        OrganizationUnitId = header.OrganizationUnitId;
        InternalRemarks = OrderValues.Text(header.InternalRemarks, OrdersConsts.MaxRemarksLength, "internalRemarks");
        InvoicingRemarks = OrderValues.Text(header.InvoicingRemarks, OrdersConsts.MaxRemarksLength, "invoicingRemarks");
    }

    public OrderHeaderInput HeaderFor(DateOnly orderDate)
        => new(TransportOrderTypeId, CustomerRelationId, CustomerContactPersonId, CustomerName, orderDate, CustomerReference, OrganizationUnitId, InternalRemarks, InvoicingRemarks);

    public void SetAttributes(IReadOnlyDictionary<string, string>? attributes) => Attributes = OrderValues.Attributes(attributes);

    public void SetSchedule(TemplateSchedule schedule) => Schedule = Check.NotNull(schedule, nameof(schedule));

    public void SetActive(bool isActive) => IsActive = isActive;

    public void SetSourceOrder(Guid? orderId) => SourceOrderId = orderId;

    public void RecordMaterialised(DateOnly date)
    {
        if (LastMaterialisedDate == null || date > LastMaterialisedDate)
        {
            LastMaterialisedDate = date;
        }
    }

    public void SetFinancialTerms(FinancialTermsInput input, Func<Guid> newId)
    {
        FinancialTerms = TransportOrderFinancialTerms.From(input);
        PriceLists.Clear();
        foreach (var (id, i) in input.PriceListIds.Select((id, i) => (id, i)))
        {
            PriceLists.Add(new TransportOrderTemplatePriceList(newId(), TenantId, Id, id, i + 1));
        }

        DiscountsOrSurcharges.Clear();
        foreach (var (item, i) in input.DiscountsOrSurcharges.Select((item, i) => (item, i)))
        {
            DiscountsOrSurcharges.Add(new TransportOrderTemplateDiscountOrSurcharge(newId(), TenantId, Id, item.DiscountOrSurchargeId, OrderValues.Defined(item.Source, "source"), i + 1));
        }
    }

    public FinancialTermsInput FinancialTermsInput() => new(
        FinancialTerms.CurrencyCode, FinancialTerms.VatPercentageId, FinancialTerms.PaymentTermDays,
        PriceLists.OrderBy(p => p.Position).Select(p => p.PriceListId).ToList(), FinancialTerms.MultipleMatches,
        DiscountsOrSurcharges.OrderBy(d => d.Position).Select(d => new DiscountOrSurchargeInput(d.DiscountOrSurchargeId, d.Source)).ToList(),
        FinancialTerms.FixedPriceAmount, FinancialTerms.SendFreightDocumentsWithInvoice, FinancialTerms.ChargedBases.ToList());

    /// <summary>Replaces the trip request; null removes it. Its freight items must be lines of this template (call after <see cref="SetLines"/>).</summary>
    public void SetTripRequest(TemplateTripRequestInput? input)
    {
        if (input == null)
        {
            TripRequest = null;
            return;
        }

        var request = TransportOrderTemplateTripRequest.From(input);
        TripRequestLines.EnsureKnown(request.LineIds, Lines.Select(l => l.Id));
        TripRequest = request;
    }

    public void SetDefaultStops(IReadOnlyList<DefaultStopInput> stops)
    {
        Check.NotNull(stops, nameof(stops));
        if (stops.Count > OrdersConsts.MaxStopsPerLine)
        {
            throw OrderValues.TooMany("defaultStops", OrdersConsts.MaxStopsPerLine);
        }

        DefaultStops.Clear();
        var position = 1;
        foreach (var s in stops)
        {
            var details = StopDetails.Create(s.WaypointTypeId, s.RelationId, s.RelationAddressId, s.ContactPersonId, s.TimeSlotId, s.WindowFrom, s.WindowTill, null, null, s.PlannedDurationMin, position);
            DefaultStops.Add(new TransportOrderTemplateDefaultStop(TenantId, Id, position++, details, s.DayOffset));
        }
    }

    /// <summary>Replaces the lines (they get new ids); returns the new ids in input order, so a trip request can be remapped to them.</summary>
    public IReadOnlyList<Guid> SetLines(IReadOnlyList<TemplateLineInput> lines, Func<Guid> newId)
    {
        Check.NotNull(lines, nameof(lines));
        if (lines.Count > OrdersConsts.MaxLines)
        {
            throw OrderValues.TooMany("lines", OrdersConsts.MaxLines);
        }

        Lines.Clear();
        var ids = new List<Guid>();
        var position = 1;
        foreach (var line in lines)
        {
            var added = new TransportOrderTemplateLine(newId(), TenantId, Id, position++, line);
            Lines.Add(added);
            ids.Add(added.Id);
        }

        return ids;
    }

    public void SetReferences(IReadOnlyList<ReferenceInput> references, Func<Guid> newId)
    {
        Check.NotNull(references, nameof(references));
        if (references.Count > OrdersConsts.MaxReferences)
        {
            throw OrderValues.TooMany("references", OrdersConsts.MaxReferences);
        }

        References.Clear();
        var position = 1;
        foreach (var reference in references)
        {
            References.Add(new TransportOrderTemplateReference(newId(), TenantId, Id, position++, reference));
        }
    }

    /// <summary>
    /// A template with the content of an order (Assumption 5: "copy order" goes through a transient template). The
    /// stops become day offsets from the order date; the trip request keeps its times of day in <paramref name="zone"/>.
    /// </summary>
    public static TransportOrderTemplate FromOrder(Guid id, TransportOrder order, string name, TimeZoneInfo zone, Func<Guid> newId)
    {
        Check.NotNull(order, nameof(order));
        var template = new TransportOrderTemplate(id, order.TenantId, name, order.HeaderFor(), order.FinancialTermsInput(), TemplateSchedule.None(), newId);
        template.SetSourceOrder(order.Id);
        template.SetAttributes(order.Attributes);
        template.SetDefaultStops(order.DefaultStops.OrderBy(s => s.Position).Select(s => new DefaultStopInput(
            s.Details.WaypointTypeId, s.Details.RelationId, s.Details.RelationAddressId, s.Details.ContactPersonId, s.DayOffset,
            s.Details.TimeSlotId, s.Details.WindowFrom, s.Details.WindowTill, s.Details.PlannedDurationMin)).ToList());
        var orderLines = order.Lines.OrderBy(l => l.Position).ToList();
        var templateLineIds = template.SetLines(orderLines.Select(l => new TemplateLineInput(
            l.Freight.ToInput(),
            l.Measurements.Select(m => new MeasurementInput(m.Kind, m.Position, m.Value)).ToList(),
            l.RequiredTransportCharacteristics.Select(c => c.TransportCharacteristicId).ToList(),
            l.Stops.OrderBy(s => s.Position).Select(s => new TemplateStopInput(
                s.Details.WaypointTypeId, s.Details.RelationId!.Value, s.Details.RelationAddressId!.Value, s.Details.ContactPersonId,
                s.Date.DayNumber - order.OrderDate.DayNumber, s.Details.TimeSlotId, s.Details.WindowFrom, s.Details.WindowTill,
                s.Details.Reference, s.Details.Remarks, s.Details.PlannedDurationMin)).ToList(),
            l.Attributes)).ToList(), newId);
        template.SetReferences(order.References.OrderBy(r => r.Position).Select(r => new ReferenceInput(r.Type, r.Value, r.Description)).ToList(), newId);
        if (order.TripRequest is { } trip)
        {
            var departure = TimeZoneInfo.ConvertTime(trip.PlannedDeparture, zone);
            var arrival = trip.PlannedArrival == null ? (DateTimeOffset?)null : TimeZoneInfo.ConvertTime(trip.PlannedArrival.Value, zone);
            template.SetTripRequest(new TemplateTripRequestInput(
                trip.TransportTripTypeId, trip.DepartureRelationId, trip.DepartureAddressId, trip.ArrivalRelationId, trip.ArrivalAddressId,
                DateOnly.FromDateTime(departure.DateTime).DayNumber - order.OrderDate.DayNumber, TimeOnly.FromDateTime(departure.DateTime),
                arrival == null ? null : DateOnly.FromDateTime(arrival.Value.DateTime).DayNumber - order.OrderDate.DayNumber,
                arrival == null ? null : TimeOnly.FromDateTime(arrival.Value.DateTime),
                trip.CarrierRelationId, trip.CarrierContactPersonId, trip.VehicleCombinationId, trip.VehicleIds, trip.DriverIds,
                trip.AgreedPurchaseAmount, trip.AgreedPurchaseCurrencyCode,
                TripRequestLines.Remap(trip.LineIds, orderLines.Select(l => (Guid?)l.Id).ToList(), templateLineIds)));
        }

        return template;
    }
}

/// <summary>A stop of a template line, dated as an offset from the order date; key (line, position).</summary>
public class TransportOrderTemplateStop : Entity, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid LineId { get; private set; }
    public int Position { get; private set; }
    public StopDetails Details { get; private set; } = default!;
    public int DayOffset { get; private set; }

    protected TransportOrderTemplateStop()
    {
    }

    public TransportOrderTemplateStop(Guid? tenantId, Guid lineId, int position, StopDetails details, int dayOffset)
    {
        TenantId = tenantId;
        LineId = lineId;
        Position = position;
        Details = Check.NotNull(details, nameof(details));
        DayOffset = dayOffset;
    }

    public override object[] GetKeys() => [LineId, Position];
}

/// <summary>A stop of the template's pattern (the waypoint strip a new line starts with); key (template, position).</summary>
public class TransportOrderTemplateDefaultStop : Entity, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid TemplateId { get; private set; }
    public int Position { get; private set; }
    public StopDetails Details { get; private set; } = default!;
    public int DayOffset { get; private set; }

    protected TransportOrderTemplateDefaultStop()
    {
    }

    public TransportOrderTemplateDefaultStop(Guid? tenantId, Guid templateId, int position, StopDetails details, int dayOffset)
    {
        TenantId = tenantId;
        TemplateId = templateId;
        Position = position;
        Details = Check.NotNull(details, nameof(details));
        DayOffset = dayOffset;
    }

    public override object[] GetKeys() => [TemplateId, Position];
}

public sealed record TemplateStopInput(
    Guid WaypointTypeId, Guid RelationId, Guid RelationAddressId, Guid? ContactPersonId, int DayOffset, Guid? TimeSlotId,
    TimeOnly? WindowFrom, TimeOnly? WindowTill, string? Reference, string? Remarks, int? PlannedDurationMin);

public sealed record TemplateLineInput(
    FreightInput Freight, IReadOnlyList<MeasurementInput> Measurements, IReadOnlyList<Guid> RequiredTransportCharacteristicIds,
    IReadOnlyList<TemplateStopInput> Stops, IReadOnlyDictionary<string, string>? Attributes);

/// <summary>A line of a template: the freight facts, measurements and characteristics as jsonb, stops as offsets.</summary>
public class TransportOrderTemplateLine : Entity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid TemplateId { get; private set; }
    public int Position { get; private set; }
    public FreightDetails Freight { get; private set; } = default!;
    public Dictionary<string, string> Attributes { get; private set; } = new();
    public List<MeasurementInput> Measurements { get; private set; } = new();
    public List<Guid> RequiredTransportCharacteristicIds { get; private set; } = new();
    public virtual ICollection<TransportOrderTemplateStop> Stops { get; private set; } = new List<TransportOrderTemplateStop>();

    protected TransportOrderTemplateLine()
    {
    }

    public TransportOrderTemplateLine(Guid id, Guid? tenantId, Guid templateId, int position, TemplateLineInput input)
        : base(id)
    {
        TenantId = tenantId;
        TemplateId = templateId;
        Position = position;
        Freight = FreightDetails.From(input.Freight);
        Attributes = OrderValues.Attributes(input.Attributes);
        Measurements = input.Measurements.Select(m => new MeasurementInput(OrderValues.Defined(m.Kind, "measurements.kind"), m.Position, m.Value)).ToList();
        RequiredTransportCharacteristicIds = input.RequiredTransportCharacteristicIds.Where(c => c != Guid.Empty).Distinct().ToList();
        if (input.Stops.Count > OrdersConsts.MaxStopsPerLine)
        {
            throw OrderValues.TooMany("stops", OrdersConsts.MaxStopsPerLine);
        }

        var stopPosition = 1;
        foreach (var s in input.Stops)
        {
            var details = StopDetails.Create(s.WaypointTypeId, s.RelationId, s.RelationAddressId, s.ContactPersonId, s.TimeSlotId, s.WindowFrom, s.WindowTill, s.Reference, s.Remarks, s.PlannedDurationMin, stopPosition);
            Stops.Add(new TransportOrderTemplateStop(tenantId, Id, stopPosition++, details, s.DayOffset));
        }
    }
}

public class TransportOrderTemplateReference : Entity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid TemplateId { get; private set; }
    public int Position { get; private set; }
    public string Type { get; private set; } = default!;
    public string Value { get; private set; } = default!;
    public string? Description { get; private set; }

    protected TransportOrderTemplateReference()
    {
    }

    public TransportOrderTemplateReference(Guid id, Guid? tenantId, Guid templateId, int position, ReferenceInput input)
        : base(id)
    {
        TenantId = tenantId;
        TemplateId = templateId;
        Position = position;
        Type = OrderValues.RequiredText(input.Type, OrdersConsts.MaxReferenceTypeLength, "type");
        Value = OrderValues.RequiredText(input.Value, OrdersConsts.MaxReferenceValueLength, "value");
        Description = OrderValues.Text(input.Description, OrdersConsts.MaxReferenceDescriptionLength, "description");
    }
}

public class TransportOrderTemplatePriceList : Entity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid TemplateId { get; private set; }
    public Guid PriceListId { get; private set; }
    public int Position { get; private set; }

    protected TransportOrderTemplatePriceList()
    {
    }

    public TransportOrderTemplatePriceList(Guid id, Guid? tenantId, Guid templateId, Guid priceListId, int position)
        : base(id)
    {
        TenantId = tenantId;
        TemplateId = templateId;
        PriceListId = priceListId;
        Position = position;
    }
}

public class TransportOrderTemplateDiscountOrSurcharge : Entity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid TemplateId { get; private set; }
    public Guid DiscountOrSurchargeId { get; private set; }
    public DiscountOrSurchargeSource Source { get; private set; }
    public int Position { get; private set; }

    protected TransportOrderTemplateDiscountOrSurcharge()
    {
    }

    public TransportOrderTemplateDiscountOrSurcharge(Guid id, Guid? tenantId, Guid templateId, Guid discountOrSurchargeId, DiscountOrSurchargeSource source, int position)
        : base(id)
    {
        TenantId = tenantId;
        TemplateId = templateId;
        DiscountOrSurchargeId = discountOrSurchargeId;
        Source = source;
        Position = position;
    }
}

public sealed record TemplateTripRequestInput(
    Guid TransportTripTypeId, Guid DepartureRelationId, Guid DepartureAddressId, Guid ArrivalRelationId, Guid ArrivalAddressId,
    int DepartureDayOffset, TimeOnly DepartureTime, int? ArrivalDayOffset, TimeOnly? ArrivalTime,
    Guid? CarrierRelationId, Guid? CarrierContactPersonId, Guid? VehicleCombinationId, IReadOnlyList<Guid> VehicleIds, IReadOnlyList<Guid> DriverIds,
    decimal? AgreedPurchaseAmount, string? AgreedPurchaseCurrencyCode, IReadOnlyList<Guid>? LineIds = null);

/// <summary>The template's trip request: the order's request with times of day instead of instants.</summary>
public class TransportOrderTemplateTripRequest
{
    public Guid TransportTripTypeId { get; private set; }
    public Guid DepartureRelationId { get; private set; }
    public Guid DepartureAddressId { get; private set; }
    public Guid ArrivalRelationId { get; private set; }
    public Guid ArrivalAddressId { get; private set; }
    public int DepartureDayOffset { get; private set; }
    public TimeOnly DepartureTime { get; private set; }
    public int? ArrivalDayOffset { get; private set; }
    public TimeOnly? ArrivalTime { get; private set; }
    public Guid? CarrierRelationId { get; private set; }
    public Guid? CarrierContactPersonId { get; private set; }
    public Guid? VehicleCombinationId { get; private set; }
    public List<Guid> VehicleIds { get; private set; } = new();
    public List<Guid> DriverIds { get; private set; } = new();
    public decimal? AgreedPurchaseAmount { get; private set; }
    public string? AgreedPurchaseCurrencyCode { get; private set; }

    /// <summary>The template lines whose legs go on the trip (as <see cref="TransportOrderTripRequest.LineIds"/>; null = every line).</summary>
    public List<Guid>? LineIds { get; private set; }

    protected TransportOrderTemplateTripRequest()
    {
    }

    public static TransportOrderTemplateTripRequest From(TemplateTripRequestInput input)
    {
        Check.NotNull(input, nameof(input));
        /* The same bounds as the order's request, checked through it on a reference date. */
        var reference = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero).AddDays(input.DepartureDayOffset).Add(input.DepartureTime.ToTimeSpan());
        var arrival = input.ArrivalDayOffset == null || input.ArrivalTime == null
            ? (DateTimeOffset?)null
            : new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero).AddDays(input.ArrivalDayOffset.Value).Add(input.ArrivalTime.Value.ToTimeSpan());
        var checkedRequest = TransportOrderTripRequest.From(new TripRequestInput(
            input.TransportTripTypeId, input.DepartureRelationId, input.DepartureAddressId, input.ArrivalRelationId, input.ArrivalAddressId,
            reference, arrival, input.CarrierRelationId, input.CarrierContactPersonId, input.VehicleCombinationId, input.VehicleIds, input.DriverIds,
            input.AgreedPurchaseAmount, input.AgreedPurchaseCurrencyCode), null);

        return new TransportOrderTemplateTripRequest
        {
            TransportTripTypeId = input.TransportTripTypeId,
            DepartureRelationId = input.DepartureRelationId,
            DepartureAddressId = input.DepartureAddressId,
            ArrivalRelationId = input.ArrivalRelationId,
            ArrivalAddressId = input.ArrivalAddressId,
            DepartureDayOffset = input.DepartureDayOffset,
            DepartureTime = input.DepartureTime,
            ArrivalDayOffset = input.ArrivalDayOffset,
            ArrivalTime = input.ArrivalTime,
            CarrierRelationId = input.CarrierRelationId,
            CarrierContactPersonId = input.CarrierContactPersonId,
            VehicleCombinationId = input.VehicleCombinationId,
            VehicleIds = checkedRequest.VehicleIds,
            DriverIds = checkedRequest.DriverIds,
            AgreedPurchaseAmount = checkedRequest.AgreedPurchaseAmount,
            AgreedPurchaseCurrencyCode = checkedRequest.AgreedPurchaseCurrencyCode,
            LineIds = TripRequestLines.Normalize(input.LineIds)
        };
    }

    public TemplateTripRequestInput ToInput() => new(
        TransportTripTypeId, DepartureRelationId, DepartureAddressId, ArrivalRelationId, ArrivalAddressId, DepartureDayOffset, DepartureTime,
        ArrivalDayOffset, ArrivalTime, CarrierRelationId, CarrierContactPersonId, VehicleCombinationId, VehicleIds.ToList(), DriverIds.ToList(),
        AgreedPurchaseAmount, AgreedPurchaseCurrencyCode, LineIds?.ToList());
}
