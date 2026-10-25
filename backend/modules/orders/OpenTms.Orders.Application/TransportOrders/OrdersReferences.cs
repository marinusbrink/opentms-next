using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using OpenTms.Financial.Published;
using OpenTms.MasterData.Published;
using OpenTms.Platform.NumberSequences;
using OpenTms.Platform.ReferenceData;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Timing;

namespace OpenTms.Orders.TransportOrders;

/// <summary>
/// The cross-module id checks and names of the order (design 56 "The three flows, applied": the synchronous calls this
/// run allows are Master Data's and Financial's published lookups). Every check is one batched call; a missing or
/// soft-deleted row is <c>ReferenceData.ReferenceNotFound</c> with the field. Names are read for DTOs, never stored
/// except the customer's (the list's search column).
/// </summary>
public class OrdersReferences : ITransientDependency
{
    private readonly IMasterDataReferenceLookup _masterData;
    private readonly IFinancialReferenceLookup _financial;
    private readonly IRepository<WaypointType, Guid> _waypointTypes;
    private readonly IRepository<TimeSlot, Guid> _timeSlots;
    private readonly IRepository<TransportOrderType, Guid> _orderTypes;
    private readonly IRepository<OrderAttributeDefinition, Guid> _attributeDefinitions;

    public OrdersReferences(
        IMasterDataReferenceLookup masterData,
        IFinancialReferenceLookup financial,
        IRepository<WaypointType, Guid> waypointTypes,
        IRepository<TimeSlot, Guid> timeSlots,
        IRepository<TransportOrderType, Guid> orderTypes,
        IRepository<OrderAttributeDefinition, Guid> attributeDefinitions)
    {
        _masterData = masterData;
        _financial = financial;
        _waypointTypes = waypointTypes;
        _timeSlots = timeSlots;
        _orderTypes = orderTypes;
        _attributeDefinitions = attributeDefinitions;
    }

    // ── Orders' own rows ───────────────────────────────────────────────────────

    public async Task<TransportOrderType> GetOrderTypeAsync(Guid id)
        => await _orderTypes.FindAsync(id, includeDetails: true) ?? throw NotFound("transportOrderTypeId");

    public async Task<Dictionary<Guid, WaypointType>> WaypointTypesAsync(IEnumerable<Guid> ids, bool require)
    {
        var keys = Keys(ids);
        var found = keys.Count == 0 ? [] : await _waypointTypes.GetListAsync(w => keys.Contains(w.Id));
        if (require && found.Count != keys.Count)
        {
            throw NotFound("waypointTypeId");
        }

        return found.ToDictionary(w => w.Id);
    }

    public async Task<Dictionary<Guid, TimeSlot>> TimeSlotsAsync(IEnumerable<Guid> ids, bool require)
    {
        var keys = Keys(ids);
        var found = keys.Count == 0 ? [] : await _timeSlots.GetListAsync(t => keys.Contains(t.Id));
        if (require && found.Count != keys.Count)
        {
            throw NotFound("timeSlotId");
        }

        return found.ToDictionary(t => t.Id);
    }

    /// <summary>Q12: every attribute key must be a defined attribute of the target (archived ones stay readable).</summary>
    public async Task EnsureAttributeKeysAsync(OrderAttributeTarget target, IEnumerable<IReadOnlyDictionary<string, string>?> attributeSets)
    {
        var keys = attributeSets.Where(a => a != null).SelectMany(a => a!.Keys).Select(k => k.Trim()).Distinct(StringComparer.Ordinal).ToList();
        if (keys.Count == 0)
        {
            return;
        }

        var defined = (await _attributeDefinitions.GetListAsync(d => d.Target == target && keys.Contains(d.Key))).Select(d => d.Key).ToHashSet(StringComparer.Ordinal);
        var unknown = keys.FirstOrDefault(k => !defined.Contains(k));
        if (unknown != null)
        {
            throw new BusinessException(OrdersErrorCodes.InvalidAttributes).WithData("key", unknown);
        }
    }

    // ── Master Data ────────────────────────────────────────────────────────────

    public async Task<Dictionary<Guid, MasterDataReferenceDto>> RelationsAsync(IEnumerable<Guid> ids, bool require, string field = "relationId")
    {
        var keys = Keys(ids);
        var found = keys.Count == 0 ? [] : await _masterData.GetRelationsAsync(keys);
        if (require && found.Count != keys.Count)
        {
            throw NotFound(field);
        }

        return found.ToDictionary(r => r.Id);
    }

    public async Task<string> RelationNameAsync(Guid relationId, string field)
        => (await RelationsAsync([relationId], require: true, field))[relationId].Name;

    /// <summary>The addresses by id; with <paramref name="owners"/> each must belong to its stop's relation (StopAddressMismatch).</summary>
    public async Task<Dictionary<Guid, MasterDataAddressReferenceDto>> AddressesAsync(IEnumerable<Guid> ids, bool require, string field = "relationAddressId")
    {
        var keys = Keys(ids);
        var found = keys.Count == 0 ? [] : await _masterData.GetRelationAddressesAsync(keys);
        if (require && found.Count != keys.Count)
        {
            throw NotFound(field);
        }

        return found.ToDictionary(a => a.Id);
    }

    public async Task<Dictionary<Guid, MasterDataAddressReferenceDto>> EnsureStopPlacesAsync(IEnumerable<(Guid RelationId, Guid AddressId, int Position)> stops)
    {
        var list = stops.ToList();
        await RelationsAsync(list.Select(s => s.RelationId), require: true, "stops.relationId");
        var addresses = await AddressesAsync(list.Select(s => s.AddressId), require: true, "stops.relationAddressId");
        foreach (var (relationId, addressId, position) in list)
        {
            if (addresses[addressId].RelationId != relationId)
            {
                throw new BusinessException(OrdersErrorCodes.StopAddressMismatch).WithData("position", position);
            }
        }

        return addresses;
    }

    public async Task<Dictionary<Guid, string>> NamesAsync(MasterDataReferenceKind kind, IEnumerable<Guid> ids, bool require, string field)
    {
        var keys = Keys(ids);
        var found = keys.Count == 0 ? [] : await _masterData.GetReferencesAsync(kind, keys);
        if (require && found.Count != keys.Count)
        {
            throw NotFound(field);
        }

        return found.ToDictionary(r => r.Id, r => r.Name);
    }

    public Task<MasterDataReferenceDto?> OwnCompanyAsync() => _masterData.GetOwnCompanyAsync();

    // ── Financial ──────────────────────────────────────────────────────────────

    public async Task<FinancialNames> EnsureFinancialTermsAsync(FinancialTermsInput terms)
    {
        var vat = terms.VatPercentageId == null ? [] : await _financial.GetVatPercentagesAsync([terms.VatPercentageId.Value]);
        if (terms.VatPercentageId != null && vat.Count == 0)
        {
            throw NotFound("vatPercentageId");
        }

        var priceListIds = Keys(terms.PriceListIds);
        var priceLists = priceListIds.Count == 0 ? [] : await _financial.GetPriceListsAsync(priceListIds);
        if (priceLists.Count != priceListIds.Count)
        {
            throw NotFound("priceListIds");
        }

        var dsIds = Keys(terms.DiscountsOrSurcharges.Select(d => d.DiscountOrSurchargeId));
        var ds = dsIds.Count == 0 ? [] : await _financial.GetDiscountsOrSurchargesAsync(dsIds);
        if (ds.Count != dsIds.Count)
        {
            throw NotFound("discountsOrSurcharges");
        }

        return new FinancialNames(
            vat.ToDictionary(v => v.Id, v => v.Name),
            priceLists.ToDictionary(p => p.Id, p => p.Name),
            ds.ToDictionary(d => d.Id, d => d.Name));
    }

    public async Task<FinancialNames> FinancialNamesAsync(FinancialTermsInput terms)
    {
        var vat = terms.VatPercentageId == null ? [] : await _financial.GetVatPercentagesAsync([terms.VatPercentageId.Value]);
        var priceListIds = Keys(terms.PriceListIds);
        var priceLists = priceListIds.Count == 0 ? [] : await _financial.GetPriceListsAsync(priceListIds);
        var dsIds = Keys(terms.DiscountsOrSurcharges.Select(d => d.DiscountOrSurchargeId));
        var ds = dsIds.Count == 0 ? [] : await _financial.GetDiscountsOrSurchargesAsync(dsIds);
        return new FinancialNames(vat.ToDictionary(v => v.Id, v => v.Name), priceLists.ToDictionary(p => p.Id, p => p.Name), ds.ToDictionary(d => d.Id, d => d.Name));
    }

    // ── the whole draft ────────────────────────────────────────────────────────

    /// <summary>Every id of a draft, in a handful of batched calls; returns the customer's name for the header.</summary>
    public async Task<string> ValidateDraftAsync(TransportOrderDraft draft)
    {
        var customerName = await RelationNameAsync(draft.Header.CustomerRelationId, "customerRelationId");
        if (draft.Header.CustomerContactPersonId != null)
        {
            await NamesAsync(MasterDataReferenceKind.ContactPerson, [draft.Header.CustomerContactPersonId.Value], require: true, "customerContactPersonId");
        }

        await ValidateLinesAsync(draft.Lines.Select(l => (l.Freight, l.Stops.Select(s => (s.WaypointTypeId, (Guid?)s.RelationId, (Guid?)s.RelationAddressId, s.TimeSlotId, s.ContactPersonId)).ToList(), l.RequiredTransportCharacteristicIds, l.Attributes)).ToList());
        await ValidatePatternAsync(draft.DefaultStops.Select(s => (s.WaypointTypeId, s.RelationId, s.RelationAddressId, s.TimeSlotId)));
        await EnsureFinancialTermsAsync(draft.Terms);
        await EnsureAttributeKeysAsync(OrderAttributeTarget.Order, [draft.Attributes]);
        if (draft.TripRequest != null)
        {
            await ValidateTripRequestAsync(draft.TripRequest.DepartureRelationId, draft.TripRequest.DepartureAddressId, draft.TripRequest.ArrivalRelationId, draft.TripRequest.ArrivalAddressId,
                draft.TripRequest.CarrierRelationId, draft.TripRequest.VehicleCombinationId, draft.TripRequest.VehicleIds, draft.TripRequest.DriverIds);
        }

        return customerName;
    }

    public async Task ValidateLinesAsync(IReadOnlyList<(FreightInput Freight, List<(Guid WaypointTypeId, Guid? RelationId, Guid? AddressId, Guid? TimeSlotId, Guid? ContactPersonId)> Stops, IReadOnlyList<Guid> Characteristics, IReadOnlyDictionary<string, string>? Attributes)> lines)
    {
        await NamesAsync(MasterDataReferenceKind.TransportUnit, lines.Select(l => l.Freight.TransportUnitId), require: true, "transportUnitId");
        await NamesAsync(MasterDataReferenceKind.Product, lines.Select(l => l.Freight.ProductId).OfType<Guid>(), require: true, "productId");
        await NamesAsync(MasterDataReferenceKind.ProductRelationCode, lines.Select(l => l.Freight.ProductRelationCodeId).OfType<Guid>(), require: true, "productRelationCodeId");
        await NamesAsync(MasterDataReferenceKind.Temperature, lines.Select(l => l.Freight.TemperatureId).OfType<Guid>(), require: true, "temperatureId");
        await NamesAsync(MasterDataReferenceKind.VehicleType, lines.Select(l => l.Freight.VehicleTypeId).OfType<Guid>(), require: true, "vehicleTypeId");
        await NamesAsync(MasterDataReferenceKind.TransportCharacteristic, lines.SelectMany(l => l.Characteristics), require: true, "requiredTransportCharacteristicIds");
        await RelationsAsync(lines.Select(l => l.Freight.ShipperRelationId).OfType<Guid>(), require: true, "shipperRelationId");
        await EnsureAttributeKeysAsync(OrderAttributeTarget.Line, lines.Select(l => l.Attributes));

        var stops = lines.SelectMany(l => l.Stops).ToList();
        await WaypointTypesAsync(stops.Select(s => s.WaypointTypeId), require: true);
        await TimeSlotsAsync(stops.Select(s => s.TimeSlotId).OfType<Guid>(), require: true);
        await NamesAsync(MasterDataReferenceKind.ContactPerson, stops.Select(s => s.ContactPersonId).OfType<Guid>(), require: true, "stops.contactPersonId");
        var position = 0;
        await EnsureStopPlacesAsync(stops.Where(s => s.RelationId != null && s.AddressId != null).Select(s => (s.RelationId!.Value, s.AddressId!.Value, ++position)));
    }

    public async Task ValidatePatternAsync(IEnumerable<(Guid WaypointTypeId, Guid? RelationId, Guid? AddressId, Guid? TimeSlotId)> stops)
    {
        var list = stops.ToList();
        await WaypointTypesAsync(list.Select(s => s.WaypointTypeId), require: true);
        await TimeSlotsAsync(list.Select(s => s.TimeSlotId).OfType<Guid>(), require: true);
        await RelationsAsync(list.Select(s => s.RelationId).OfType<Guid>(), require: true, "defaultStops.relationId");
        var position = 0;
        await EnsureStopPlacesAsync(list.Where(s => s.RelationId != null && s.AddressId != null).Select(s => (s.RelationId!.Value, s.AddressId!.Value, ++position)));
    }

    public async Task ValidateTripRequestAsync(Guid departureRelationId, Guid departureAddressId, Guid arrivalRelationId, Guid arrivalAddressId, Guid? carrierRelationId, Guid? combinationId, IReadOnlyList<Guid> vehicleIds, IReadOnlyList<Guid> driverIds)
    {
        await EnsureStopPlacesAsync([(departureRelationId, departureAddressId, 1), (arrivalRelationId, arrivalAddressId, 2)]);
        if (carrierRelationId != null)
        {
            await RelationsAsync([carrierRelationId.Value], require: true, "tripRequest.carrierRelationId");
        }

        if (combinationId != null)
        {
            await NamesAsync(MasterDataReferenceKind.VehicleCombination, [combinationId.Value], require: true, "tripRequest.vehicleCombinationId");
        }

        await NamesAsync(MasterDataReferenceKind.Vehicle, vehicleIds, require: true, "tripRequest.vehicleIds");
        await NamesAsync(MasterDataReferenceKind.Driver, driverIds, require: true, "tripRequest.driverIds");
    }

    private static List<Guid> Keys(IEnumerable<Guid> ids) => ids.Where(id => id != Guid.Empty).Distinct().ToList();

    private static BusinessException NotFound(string field) => new BusinessException(ReferenceDataErrorCodes.ReferenceNotFound).WithData("field", field);
}

public sealed record FinancialNames(IReadOnlyDictionary<Guid, string> VatPercentages, IReadOnlyDictionary<Guid, string> PriceLists, IReadOnlyDictionary<Guid, string> DiscountsOrSurcharges);

/// <summary>
/// Today and the zone the order dates and the templates' times of day live in: the installation's local zone
/// (<c>NumberSequences:TimeZone</c>, Europe/Amsterdam by default), the rule of design 50 and Financial's FinancialToday.
/// </summary>
public class OrdersClock : ITransientDependency
{
    private readonly IClock _clock;
    private readonly NumberSequenceOptions _options;

    public OrdersClock(IClock clock, IOptions<NumberSequenceOptions> options)
    {
        _clock = clock;
        _options = options.Value;
    }

    public virtual TimeZoneInfo Zone => TimeZoneInfo.FindSystemTimeZoneById(_options.TimeZone);

    public virtual DateTime NowUtc
    {
        get
        {
            var now = _clock.Now;
            return now.Kind == DateTimeKind.Local ? now.ToUniversalTime() : DateTime.SpecifyKind(now, DateTimeKind.Utc);
        }
    }

    public virtual DateOnly Today() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(NowUtc, Zone));
}
