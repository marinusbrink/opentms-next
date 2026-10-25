using System;
using System.Collections.Generic;
using System.Linq;
using OpenTms.Platform.NumberSequences;
using OpenTms.Platform.ReferenceData;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace OpenTms.Orders.TransportOrders;

/// <summary>
/// The transport order (design 58; OpenTMS TransportOrder): the customer's request, its lines with their requested
/// stops, the references, the financial terms copied from the customer, the optional trip request (Q9) and the stored
/// derived values the order summary calculator writes (design 56, flow 2). The status machine lives here as commands
/// with the rules of design 56; the application service adds the checks that need Planning's legs (locks, cancel,
/// delete) before it calls them. Cross-module ids carry no database constraint (rule 1); the service validates them
/// through the published lookups.
/// </summary>
public class TransportOrder : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }

    /// <summary>Drawn on Confirm from the type's sequence; null while Draft.</summary>
    public string? Number { get; private set; }

    public TransportOrderStatus Status { get; private set; } = TransportOrderStatus.Draft;

    public Guid TransportOrderTypeId { get; private set; }

    public Guid CustomerRelationId { get; private set; }

    public Guid? CustomerContactPersonId { get; private set; }

    /// <summary>The customer's name as known at the last save, so the list searches and sorts without a cross-module join.</summary>
    public string CustomerName { get; private set; } = string.Empty;

    public DateOnly OrderDate { get; private set; }

    public string? CustomerReference { get; private set; }

    public Guid? OrganizationUnitId { get; private set; }

    public string? InternalRemarks { get; private set; }

    public string? InvoicingRemarks { get; private set; }

    public Dictionary<string, string> Attributes { get; private set; } = new();

    public OrderSource Source { get; private set; } = OrderSource.Screen;

    /// <summary>A boundary agent's confidence in its Draft (0..1); null for orders people entered.</summary>
    public decimal? DraftConfidence { get; private set; }

    public Guid? SourceTemplateId { get; private set; }

    /// <summary>The copy index of a materialised order (0 for the first copy): with the template and the date the natural key.</summary>
    public int CopyIndex { get; private set; }

    public TransportOrderFinancialTerms FinancialTerms { get; private set; } = default!;

    public TransportOrderTripRequest? TripRequest { get; private set; }

    public virtual ICollection<TransportOrderLine> Lines { get; private set; } = new List<TransportOrderLine>();

    public virtual ICollection<TransportOrderDefaultStop> DefaultStops { get; private set; } = new List<TransportOrderDefaultStop>();

    public virtual ICollection<TransportOrderReference> References { get; private set; } = new List<TransportOrderReference>();

    public virtual ICollection<TransportOrderPriceList> PriceLists { get; private set; } = new List<TransportOrderPriceList>();

    public virtual ICollection<TransportOrderDiscountOrSurcharge> DiscountsOrSurcharges { get; private set; } = new List<TransportOrderDiscountOrSurcharge>();

    public virtual ICollection<TransportOrderRemark> Remarks { get; private set; } = new List<TransportOrderRemark>();

    /* Stored derived values (design 56, flow 2): written by OrderSummaryCalculator only. */

    public decimal? RevenueAmount { get; private set; }

    public decimal? CostOfRevenueAmount { get; private set; }

    public decimal? MarginAmount { get; private set; }

    public decimal? Co2Kg { get; private set; }

    public ExecutionProgress ExecutionProgress { get; private set; } = ExecutionProgress.Unplanned;

    public int LineCount { get; private set; }

    public int StopCount { get; private set; }

    public string? FirstStopName { get; private set; }

    public string? FirstStopCity { get; private set; }

    public DateOnly? FirstStopDate { get; private set; }

    public string? LastStopName { get; private set; }

    public string? LastStopCity { get; private set; }

    public DateOnly? LastStopDate { get; private set; }

    public string? TripNumbers { get; private set; }

    public DateTime? CalculatedAt { get; private set; }

    protected TransportOrder()
    {
    }

    public TransportOrder(Guid id, Guid? tenantId, OrderHeaderInput header, FinancialTermsInput terms, OrderSource source, Func<Guid> newId)
        : base(id)
    {
        TenantId = tenantId;
        Source = OrderValues.Defined(source, "source");
        SetHeader(header, null);
        SetFinancialTerms(terms, newId);
    }

    // ── header ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// The header; <paramref name="numbering"/> is the type's format when the order is numbered, so an order date that
    /// leaves the period its number was drawn in is refused (OpenTMS rule, orders.md scenario 8).
    /// </summary>
    public void SetHeader(OrderHeaderInput header, NumberSequenceFormat? numbering)
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

        if (Number != null && numbering != null && numbering.Period(header.OrderDate) != numbering.Period(OrderDate))
        {
            throw new BusinessException(OrdersErrorCodes.OrderDatePeriodLocked).WithData("orderDate", OrderDate).WithData("newOrderDate", header.OrderDate);
        }

        TransportOrderTypeId = header.TransportOrderTypeId;
        CustomerRelationId = header.CustomerRelationId;
        CustomerContactPersonId = header.CustomerContactPersonId;
        CustomerName = OrderValues.Text(header.CustomerName, OrdersConsts.MaxStopNameLength, "customerName") ?? string.Empty;
        OrderDate = header.OrderDate;
        CustomerReference = OrderValues.Text(header.CustomerReference, OrdersConsts.MaxCustomerReferenceLength, "customerReference");
        OrganizationUnitId = header.OrganizationUnitId;
        InternalRemarks = OrderValues.Text(header.InternalRemarks, OrdersConsts.MaxRemarksLength, "internalRemarks");
        InvoicingRemarks = OrderValues.Text(header.InvoicingRemarks, OrdersConsts.MaxRemarksLength, "invoicingRemarks");
    }

    public OrderHeaderInput HeaderFor()
        => new(TransportOrderTypeId, CustomerRelationId, CustomerContactPersonId, CustomerName, OrderDate, CustomerReference, OrganizationUnitId, InternalRemarks, InvoicingRemarks);

    public void SetCustomerName(string name) => CustomerName = OrderValues.Text(name, OrdersConsts.MaxStopNameLength, "customerName") ?? string.Empty;

    public void SetDraftConfidence(decimal? confidence)
    {
        if (confidence is < 0 or > 1)
        {
            throw new BusinessException(ReferenceDataErrorCodes.InvalidCode).WithData("field", "draftConfidence").WithData("value", confidence.Value);
        }

        DraftConfidence = confidence;
    }

    public void SetSourceTemplate(Guid? templateId, int copyIndex)
    {
        if (copyIndex < 0)
        {
            throw new BusinessException(ReferenceDataErrorCodes.InvalidCode).WithData("field", "copyIndex").WithData("value", copyIndex);
        }

        SourceTemplateId = templateId;
        CopyIndex = templateId == null ? 0 : copyIndex;
    }

    /// <summary>The attributes, keys already checked against the definitions by the service (Q12).</summary>
    public void SetAttributes(IReadOnlyDictionary<string, string>? attributes) => Attributes = OrderValues.Attributes(attributes);

    // ── financial terms ─────────────────────────────────────────────────────────

    public void SetFinancialTerms(FinancialTermsInput input, Func<Guid> newId)
    {
        FinancialTerms = TransportOrderFinancialTerms.From(input);
        SyncPositioned(PriceLists, input.PriceListIds.Select((id, i) => (id, i + 1)).ToList(),
            p => p.PriceListId, (id, position) => new TransportOrderPriceList(newId(), TenantId, Id, id, position), (p, position) => p.SetPosition(position));

        var wanted = input.DiscountsOrSurcharges;
        foreach (var stale in DiscountsOrSurcharges.Where(d => wanted.All(w => w.DiscountOrSurchargeId != d.DiscountOrSurchargeId)).ToList())
        {
            DiscountsOrSurcharges.Remove(stale);
        }

        foreach (var (item, i) in wanted.Select((item, i) => (item, i)))
        {
            var existing = DiscountsOrSurcharges.FirstOrDefault(d => d.DiscountOrSurchargeId == item.DiscountOrSurchargeId);
            if (existing == null)
            {
                DiscountsOrSurcharges.Add(new TransportOrderDiscountOrSurcharge(newId(), TenantId, Id, item.DiscountOrSurchargeId, OrderValues.Defined(item.Source, "source"), i + 1));
            }
            else
            {
                existing.Set(OrderValues.Defined(item.Source, "source"), i + 1);
            }
        }
    }

    public FinancialTermsInput FinancialTermsInput() => new(
        FinancialTerms.CurrencyCode, FinancialTerms.VatPercentageId, FinancialTerms.PaymentTermDays,
        PriceLists.OrderBy(p => p.Position).Select(p => p.PriceListId).ToList(), FinancialTerms.MultipleMatches,
        DiscountsOrSurcharges.OrderBy(d => d.Position).Select(d => new DiscountOrSurchargeInput(d.DiscountOrSurchargeId, d.Source)).ToList(),
        FinancialTerms.FixedPriceAmount, FinancialTerms.SendFreightDocumentsWithInvoice);

    // ── trip request ────────────────────────────────────────────────────────────

    /// <summary>Replaces the trip request; null removes it. The trip id Planning reported survives a change.</summary>
    public void SetTripRequest(TripRequestInput? input)
        => TripRequest = input == null ? null : TransportOrderTripRequest.From(input, TripRequest?.TripId);

    /// <summary>Planning's TransportTripCreated handler: idempotent, and a request that was removed meanwhile is left alone.</summary>
    public bool RecordTrip(Guid tripId)
    {
        if (TripRequest == null || TripRequest.TripId == tripId)
        {
            return false;
        }

        TripRequest.SetTrip(tripId);
        return true;
    }

    // ── default stops (the pattern) ─────────────────────────────────────────────

    public void SetDefaultStops(IReadOnlyList<DefaultStopInput> stops)
    {
        Check.NotNull(stops, nameof(stops));
        if (stops.Count > OrdersConsts.MaxStopsPerLine)
        {
            throw OrderValues.TooMany("defaultStops", OrdersConsts.MaxStopsPerLine);
        }

        foreach (var stale in DefaultStops.Where(s => s.Position > stops.Count).ToList())
        {
            DefaultStops.Remove(stale);
        }

        for (var i = 0; i < stops.Count; i++)
        {
            var position = i + 1;
            var input = stops[i];
            var details = StopDetails.Create(input.WaypointTypeId, input.RelationId, input.RelationAddressId, input.ContactPersonId, input.TimeSlotId, input.WindowFrom, input.WindowTill, null, null, input.PlannedDurationMin, position);
            var existing = DefaultStops.FirstOrDefault(s => s.Position == position);
            if (existing == null)
            {
                DefaultStops.Add(new TransportOrderDefaultStop(TenantId, Id, position, details, input.DayOffset));
            }
            else
            {
                existing.Set(details, input.DayOffset);
            }
        }
    }

    /// <summary>The stops a new line starts with: the pattern dated from the order date (Assumption 1).</summary>
    public IReadOnlyList<StopInput> StopsFromPattern()
        => DefaultStops.OrderBy(s => s.Position)
            .Where(s => s.Details.RelationId != null && s.Details.RelationAddressId != null)
            .Select(s => new StopInput(null, s.Details.WaypointTypeId, s.Details.RelationId!.Value, s.Details.RelationAddressId!.Value, s.Details.ContactPersonId,
                OrderDate.AddDays(s.DayOffset), s.Details.TimeSlotId, s.Details.WindowFrom, s.Details.WindowTill, null, null, s.Details.PlannedDurationMin))
            .ToList();

    // ── lines ───────────────────────────────────────────────────────────────────

    /// <summary>Replaces the lines (matched on id; new lines without id); returns the ids whose stops changed.</summary>
    public LineChanges SetLines(IReadOnlyList<LineInput> inputs, Func<Guid> newId)
    {
        Check.NotNull(inputs, nameof(inputs));
        if (inputs.Count > OrdersConsts.MaxLines)
        {
            throw OrderValues.TooMany("lines", OrdersConsts.MaxLines);
        }

        var changes = new LineChanges();
        var keep = new HashSet<Guid>();
        for (var i = 0; i < inputs.Count; i++)
        {
            var input = inputs[i];
            var existing = input.Id == null ? null : Lines.FirstOrDefault(l => l.Id == input.Id.Value);
            if (existing == null)
            {
                var line = new TransportOrderLine(newId(), TenantId, Id, i + 1, input, newId);
                Lines.Add(line);
                keep.Add(line.Id);
                changes.LineIds.Add(line.Id);
                changes.StopChangedLineIds.Add(line.Id);
            }
            else
            {
                var stopsChanged = existing.Set(i + 1, input, newId);
                keep.Add(existing.Id);
                changes.LineIds.Add(existing.Id);
                if (stopsChanged)
                {
                    changes.StopChangedLineIds.Add(existing.Id);
                }
            }
        }

        foreach (var stale in Lines.Where(l => !keep.Contains(l.Id)).ToList())
        {
            Lines.Remove(stale);
            changes.RemovedLineIds.Add(stale.Id);
        }

        return changes;
    }

    public TransportOrderLine AddLine(LineInput input, Func<Guid> newId)
    {
        if (Lines.Count >= OrdersConsts.MaxLines)
        {
            throw OrderValues.TooMany("lines", OrdersConsts.MaxLines);
        }

        var line = new TransportOrderLine(newId(), TenantId, Id, Lines.Count + 1, input, newId);
        Lines.Add(line);
        return line;
    }

    public TransportOrderLine GetLine(Guid lineId)
        => Lines.FirstOrDefault(l => l.Id == lineId) ?? throw new EntityNotFoundException(typeof(TransportOrderLine), lineId);

    public void RemoveLine(Guid lineId)
    {
        Lines.Remove(GetLine(lineId));
        var position = 1;
        foreach (var line in Lines.OrderBy(l => l.Position).ToList())
        {
            line.SetPosition(position++);
        }
    }

    // ── references ──────────────────────────────────────────────────────────────

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
            References.Add(new TransportOrderReference(newId(), TenantId, Id, position++, reference));
        }
    }

    // ── the status machine (design 56 "Status machines", design 58 "Lifecycle") ──

    public bool IsEditable => Status is TransportOrderStatus.Draft or TransportOrderStatus.Open;

    public void EnsureEditable()
    {
        if (!IsEditable)
        {
            throw new BusinessException(OrdersErrorCodes.OrderNotEditable).WithData("status", Status.ToString());
        }
    }

    /// <summary>
    /// Draft → Open with the number the service drew. The required fields of the type, at least one line and two stops
    /// per line are checked here, so a Draft may be saved incomplete (Assumption 6).
    /// </summary>
    public void Confirm(string number, TransportOrderRequiredFields requiredFields, Func<Guid, WaypointActivity?> activityOf)
    {
        Require(TransportOrderStatus.Open, TransportOrderStatus.Draft);
        EnsureComplete(requiredFields, activityOf);
        Number = OrderValues.RequiredText(number, OrdersConsts.MaxNumberLength, "number");
        Status = TransportOrderStatus.Open;
    }

    public void Cancel()
    {
        Require(TransportOrderStatus.Cancelled, TransportOrderStatus.Draft, TransportOrderStatus.Open);
        Status = TransportOrderStatus.Cancelled;
    }

    /// <summary>Open → ReadyForInvoicing or Archived, the status the approval proposed (Q6).</summary>
    public void Approve(TransportOrderStatus proposedStatus)
    {
        if (proposedStatus is not (TransportOrderStatus.ReadyForInvoicing or TransportOrderStatus.Archived))
        {
            throw new BusinessException(OrdersErrorCodes.InvalidTransition).WithData("from", Status.ToString()).WithData("to", proposedStatus.ToString());
        }

        Require(proposedStatus, TransportOrderStatus.Open);
        Status = proposedStatus;
    }

    /// <summary>ReadyForInvoicing/Archived → Open with a reason; Invoiced is refused (Q15).</summary>
    public void Revise(string? reason, Guid? userId, DateTime at, Guid remarkId)
    {
        var text = OrderValues.Text(reason, OrdersConsts.MaxRevisionReasonLength, "reason")
                   ?? throw new BusinessException(OrdersErrorCodes.RevisionReasonRequired);
        Require(TransportOrderStatus.Open, TransportOrderStatus.ReadyForInvoicing, TransportOrderStatus.Archived);
        Status = TransportOrderStatus.Open;
        Remarks.Add(new TransportOrderRemark(remarkId, TenantId, Id, OrderRemarkKind.Revision, text, at, userId));
    }

    public void Archive()
    {
        Require(TransportOrderStatus.Archived, TransportOrderStatus.ReadyForInvoicing, TransportOrderStatus.Invoiced);
        Status = TransportOrderStatus.Archived;
    }

    /// <summary>Written by the invoicing run only (design 56); no API command exposes it.</summary>
    public void MarkInvoiced()
    {
        Require(TransportOrderStatus.Invoiced, TransportOrderStatus.ReadyForInvoicing);
        Status = TransportOrderStatus.Invoiced;
    }

    /// <summary>A delete is allowed while Draft or Open; the service checks that no leg is assigned.</summary>
    public void EnsureDeletable()
    {
        if (!IsEditable)
        {
            throw new BusinessException(OrdersErrorCodes.InvalidTransition).WithData("from", Status.ToString()).WithData("to", "Deleted");
        }
    }

    private void Require(TransportOrderStatus to, params TransportOrderStatus[] from)
    {
        if (!from.Contains(Status))
        {
            throw new BusinessException(OrdersErrorCodes.InvalidTransition).WithData("from", Status.ToString()).WithData("to", to.ToString());
        }
    }

    /// <summary>The checks of Confirm (orders.md scenarios 4 and 5): the type's required fields, ≥ 1 line, ≥ 2 stops each.</summary>
    public void EnsureComplete(TransportOrderRequiredFields requiredFields, Func<Guid, WaypointActivity?> activityOf)
    {
        if (Lines.Count == 0)
        {
            throw new BusinessException(OrdersErrorCodes.LineRequired);
        }

        foreach (var line in Lines.OrderBy(l => l.Position))
        {
            if (line.Stops.Count < OrdersConsts.MinStopsPerLine)
            {
                throw new BusinessException(OrdersErrorCodes.LineNeedsTwoStops).WithData("position", line.Position);
            }
        }

        if (requiredFields.HasFlag(TransportOrderRequiredFields.CustomerReference) && string.IsNullOrEmpty(CustomerReference))
        {
            throw Missing("customerReference");
        }

        foreach (var line in Lines.OrderBy(l => l.Position))
        {
            var f = line.Freight;
            if (requiredFields.HasFlag(TransportOrderRequiredFields.ProductPerLine) && f.ProductId == null) throw Missing("productId");
            if (requiredFields.HasFlag(TransportOrderRequiredFields.TransportUnitPerLine) && f.TransportUnitId == Guid.Empty) throw Missing("transportUnitId");
            if (requiredFields.HasFlag(TransportOrderRequiredFields.QuantityPerLine) && f.Quantity <= 0) throw Missing("quantity");
            if (requiredFields.HasFlag(TransportOrderRequiredFields.WeightPerLine) && f.WeightKg is null or <= 0) throw Missing("weightKg");
            if (requiredFields.HasFlag(TransportOrderRequiredFields.VolumePerLine) && f.VolumeM3 is null or <= 0) throw Missing("volumeM3");
            if (requiredFields.HasFlag(TransportOrderRequiredFields.LoadingMetersPerLine) && f.LoadingMeters is null or <= 0) throw Missing("loadingMeters");
            if (requiredFields.HasFlag(TransportOrderRequiredFields.Temperature) && f.TemperatureId == null) throw Missing("temperatureId");

            foreach (var stop in line.Stops)
            {
                var activity = activityOf(stop.Details.WaypointTypeId);
                var hasWindow = stop.Details.TimeSlotId != null || stop.Details.WindowFrom != null;
                var hasReference = !string.IsNullOrEmpty(stop.Details.Reference);
                if (activity == WaypointActivity.Load)
                {
                    if (requiredFields.HasFlag(TransportOrderRequiredFields.LoadingReference) && !hasReference) throw Missing("loadingReference");
                    if (requiredFields.HasFlag(TransportOrderRequiredFields.LoadingTimeWindow) && !hasWindow) throw Missing("loadingTimeWindow");
                }
                else if (activity == WaypointActivity.Unload)
                {
                    if (requiredFields.HasFlag(TransportOrderRequiredFields.UnloadingReference) && !hasReference) throw Missing("unloadingReference");
                    if (requiredFields.HasFlag(TransportOrderRequiredFields.UnloadingTimeWindow) && !hasWindow) throw Missing("unloadingTimeWindow");
                }
            }
        }

        static BusinessException Missing(string field) => new BusinessException(OrdersErrorCodes.RequiredFieldMissing).WithData("field", field);
    }

    // ── stored derived values (OrderSummaryCalculator only) ───────────────────

    public void WriteSummary(OrderSummary summary, DateTime calculatedAt)
    {
        Check.NotNull(summary, nameof(summary));
        RevenueAmount = summary.RevenueAmount;
        CostOfRevenueAmount = summary.CostOfRevenueAmount;
        MarginAmount = summary.MarginAmount;
        Co2Kg = summary.Co2Kg;
        ExecutionProgress = OrderValues.Defined(summary.ExecutionProgress, "executionProgress");
        LineCount = summary.LineCount;
        StopCount = summary.StopCount;
        FirstStopName = OrderValues.Text(summary.FirstStopName, OrdersConsts.MaxStopNameLength, "firstStopName");
        FirstStopCity = OrderValues.Text(summary.FirstStopCity, OrdersConsts.MaxCityLength, "firstStopCity");
        FirstStopDate = summary.FirstStopDate;
        LastStopName = OrderValues.Text(summary.LastStopName, OrdersConsts.MaxStopNameLength, "lastStopName");
        LastStopCity = OrderValues.Text(summary.LastStopCity, OrdersConsts.MaxCityLength, "lastStopCity");
        LastStopDate = summary.LastStopDate;
        TripNumbers = OrderValues.Text(summary.TripNumbers, OrdersConsts.MaxTripNumbersLength, "tripNumbers");
        CalculatedAt = calculatedAt;
    }

    /// <summary>The counts and first/last stop facts the order itself knows (names come from Master Data in the calculator).</summary>
    public (TransportOrderStop? First, TransportOrderStop? Last, int StopCount) StopEnds()
    {
        var stops = Lines.OrderBy(l => l.Position).SelectMany(l => l.Stops.OrderBy(s => s.Position)).ToList();
        return (stops.FirstOrDefault(), stops.LastOrDefault(), stops.Count);
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    private static void SyncPositioned<TRow>(
        ICollection<TRow> rows, IReadOnlyList<(Guid Id, int Position)> wanted, Func<TRow, Guid> key, Func<Guid, int, TRow> create, Action<TRow, int> move)
    {
        foreach (var stale in rows.Where(r => wanted.All(w => w.Id != key(r))).ToList())
        {
            rows.Remove(stale);
        }

        foreach (var (id, position) in wanted)
        {
            var existing = rows.FirstOrDefault(r => key(r) == id);
            if (existing == null)
            {
                rows.Add(create(id, position));
            }
            else
            {
                move(existing, position);
            }
        }
    }
}

/// <summary>What a lines update changed, for the events the service publishes (ids only, rule 5).</summary>
public sealed class LineChanges
{
    public List<Guid> LineIds { get; } = new();

    public List<Guid> StopChangedLineIds { get; } = new();

    public List<Guid> RemovedLineIds { get; } = new();
}

/// <summary>The values the order summary calculator writes (design 56, flow 2).</summary>
public sealed record OrderSummary(
    decimal? RevenueAmount,
    decimal? CostOfRevenueAmount,
    decimal? MarginAmount,
    decimal? Co2Kg,
    ExecutionProgress ExecutionProgress,
    int LineCount,
    int StopCount,
    string? FirstStopName,
    string? FirstStopCity,
    DateOnly? FirstStopDate,
    string? LastStopName,
    string? LastStopCity,
    DateOnly? LastStopDate,
    string? TripNumbers);

/// <summary>A revenue price list applied to the order, in position order (QP5).</summary>
public class TransportOrderPriceList : Entity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid OrderId { get; private set; }
    public Guid PriceListId { get; private set; }
    public int Position { get; private set; }

    protected TransportOrderPriceList()
    {
    }

    public TransportOrderPriceList(Guid id, Guid? tenantId, Guid orderId, Guid priceListId, int position)
        : base(id)
    {
        TenantId = tenantId;
        OrderId = orderId;
        PriceListId = priceListId;
        Position = position;
    }

    public void SetPosition(int position) => Position = position;
}

/// <summary>A discount or surcharge selected on the order: a customer default or a manual pick.</summary>
public class TransportOrderDiscountOrSurcharge : Entity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid OrderId { get; private set; }
    public Guid DiscountOrSurchargeId { get; private set; }
    public DiscountOrSurchargeSource Source { get; private set; }
    public int Position { get; private set; }

    protected TransportOrderDiscountOrSurcharge()
    {
    }

    public TransportOrderDiscountOrSurcharge(Guid id, Guid? tenantId, Guid orderId, Guid discountOrSurchargeId, DiscountOrSurchargeSource source, int position)
        : base(id)
    {
        TenantId = tenantId;
        OrderId = orderId;
        DiscountOrSurchargeId = discountOrSurchargeId;
        Set(source, position);
    }

    public void Set(DiscountOrSurchargeSource source, int position)
    {
        Source = source;
        Position = position;
    }
}

/// <summary>An external reference of the order (OpenTMS references): a typed value.</summary>
public class TransportOrderReference : Entity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid OrderId { get; private set; }
    public int Position { get; private set; }
    public string Type { get; private set; } = default!;
    public string Value { get; private set; } = default!;
    public string? Description { get; private set; }

    protected TransportOrderReference()
    {
    }

    public TransportOrderReference(Guid id, Guid? tenantId, Guid orderId, int position, ReferenceInput input)
        : base(id)
    {
        TenantId = tenantId;
        OrderId = orderId;
        Position = position;
        Type = OrderValues.RequiredText(input.Type, OrdersConsts.MaxReferenceTypeLength, "type");
        Value = OrderValues.RequiredText(input.Value, OrdersConsts.MaxReferenceValueLength, "value");
        Description = OrderValues.Text(input.Description, OrdersConsts.MaxReferenceDescriptionLength, "description");
    }
}

/// <summary>A stop of the order's pattern (the waypoint strip), at a position; composite key (order, position).</summary>
public class TransportOrderDefaultStop : Entity, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid OrderId { get; private set; }
    public int Position { get; private set; }
    public StopDetails Details { get; private set; } = default!;
    public int DayOffset { get; private set; }

    protected TransportOrderDefaultStop()
    {
    }

    public TransportOrderDefaultStop(Guid? tenantId, Guid orderId, int position, StopDetails details, int dayOffset)
    {
        TenantId = tenantId;
        OrderId = orderId;
        Position = position;
        Set(details, dayOffset);
    }

    public void Set(StopDetails details, int dayOffset)
    {
        Details = Check.NotNull(details, nameof(details));
        DayOffset = dayOffset;
    }

    public override object[] GetKeys() => [OrderId, Position];
}

/// <summary>A dated remark the order keeps apart (design 58: the reason of a revision).</summary>
public class TransportOrderRemark : Entity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid OrderId { get; private set; }
    public OrderRemarkKind Kind { get; private set; }
    public string Text { get; private set; } = default!;
    public DateTime At { get; private set; }
    public Guid? UserId { get; private set; }

    protected TransportOrderRemark()
    {
    }

    public TransportOrderRemark(Guid id, Guid? tenantId, Guid orderId, OrderRemarkKind kind, string text, DateTime at, Guid? userId)
        : base(id)
    {
        TenantId = tenantId;
        OrderId = orderId;
        Kind = kind;
        Text = text;
        At = at;
        UserId = userId;
    }
}
