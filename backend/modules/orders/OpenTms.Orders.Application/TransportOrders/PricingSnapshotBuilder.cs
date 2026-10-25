using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Financial.Tariffs.Published;
using OpenTms.MasterData.Published;
using OpenTms.PlanningExecution.Published;
using Volo.Abp.DependencyInjection;

namespace OpenTms.Orders.TransportOrders;

/// <summary>
/// The pricing snapshot of an order (design 58 "Published contract"; the shape is Financial's PricingSnapshotDto): the
/// terms, the lines with their quantities, the stops with the ids the lanes match on (relation, address, regions,
/// country, postcode), the windows resolved in the address's zone, and the legs' distances when Planning has them.
/// Serves IOrderPricingReader (stored order) and the price preview (the screen's unsaved state over the stored order).
/// </summary>
public class PricingSnapshotBuilder : ITransientDependency
{
    private readonly OrdersReferences _references;
    private readonly ILegReader _legs;

    public PricingSnapshotBuilder(OrdersReferences references, ILegReader legs)
    {
        _references = references;
        _legs = legs;
    }

    public Task<PricingSnapshotDto> BuildAsync(TransportOrder order)
        => BuildAsync(order.Id, order.Status, order.HeaderFor(), order.FinancialTermsInput(), order.Lines.OrderBy(l => l.Position).Select(l => l.ToInput()).ToList());

    /// <summary>Several orders with one address and one leg read (the calculator's bulk path).</summary>
    public async Task<IReadOnlyList<PricingSnapshotDto>> BuildManyAsync(IReadOnlyList<TransportOrder> orders)
    {
        if (orders.Count == 0)
        {
            return [];
        }

        var addressIds = orders.SelectMany(o => o.Lines).SelectMany(l => l.Stops).Select(s => s.Details.RelationAddressId).OfType<Guid>();
        var addresses = await _references.AddressesAsync(addressIds, require: false);
        var slots = await _references.TimeSlotsAsync(orders.SelectMany(o => o.Lines).SelectMany(l => l.Stops).Select(s => s.Details.TimeSlotId).OfType<Guid>(), require: false);
        var legs = (await _legs.GetByOrdersAsync(orders.Select(o => o.Id).ToList())).ToLookup(l => l.OrderId);
        return orders.Select(o => Compose(o.Id, (int)o.Status, o.HeaderFor(), o.FinancialTermsInput(), o.Lines.OrderBy(l => l.Position).Select(l => l.ToInput()).ToList(), addresses, slots, legs[o.Id].ToList())).ToList();
    }

    public async Task<PricingSnapshotDto> BuildAsync(Guid? orderId, TransportOrderStatus status, OrderHeaderInput header, FinancialTermsInput terms, IReadOnlyList<LineInput> lines)
    {
        var addresses = await _references.AddressesAsync(lines.SelectMany(l => l.Stops).Select(s => s.RelationAddressId), require: false);
        var slots = await _references.TimeSlotsAsync(lines.SelectMany(l => l.Stops).Select(s => s.TimeSlotId).OfType<Guid>(), require: false);
        var legs = orderId == null ? [] : await _legs.GetByOrderAsync(orderId.Value);
        return Compose(orderId, (int)status, header, terms, lines, addresses, slots, legs);
    }

    private static PricingSnapshotDto Compose(
        Guid? orderId, int status, OrderHeaderInput header, FinancialTermsInput terms, IReadOnlyList<LineInput> lines,
        IReadOnlyDictionary<Guid, MasterDataAddressReferenceDto> addresses, IReadOnlyDictionary<Guid, TimeSlot> slots, IReadOnlyList<LegSnapshotDto> legs)
    {
        var legsByLine = legs.ToLookup(l => l.OrderLineId);
        var snapshot = new PricingSnapshotDto
        {
            OrderId = orderId,
            CustomerRelationId = header.CustomerRelationId,
            OrderDate = header.OrderDate,
            CurrencyCode = terms.CurrencyCode,
            VatPercentageId = terms.VatPercentageId,
            PriceListIds = terms.PriceListIds.ToList(),
            MultipleMatches = terms.MultipleMatches,
            DiscountOrSurchargeIds = terms.DiscountsOrSurcharges.Select(d => d.DiscountOrSurchargeId).ToList(),
            FixedPriceAmount = terms.FixedPriceAmount,
            Status = status
        };

        var position = 0;
        foreach (var line in lines)
        {
            position++;
            var lineLegs = line.Id == null ? [] : legsByLine[line.Id.Value].ToList();
            var f = line.Freight;
            snapshot.Lines.Add(new PricingLineDto
            {
                LineId = line.Id,
                Position = position,
                TransportUnitId = f.TransportUnitId,
                ProductId = f.ProductId,
                VehicleTypeId = f.VehicleTypeId,
                Quantity = f.Quantity,
                Positions = f.Positions,
                Colli = f.Colli,
                WeightKg = f.WeightKg,
                VolumeM3 = f.VolumeM3,
                LoadingMeters = f.LoadingMeters,
                LoadFactor = f.LoadFactor,
                Epq = f.Epq,
                DistanceM = Sum(lineLegs, l => l.DistanceM),
                TollDistanceM = Sum(lineLegs, l => l.TollDistanceM),
                DurationS = Sum(lineLegs, l => l.DurationS),
                TemperatureId = f.TemperatureId,
                RequiredTransportCharacteristicIds = line.RequiredTransportCharacteristicIds.ToList(),
                Stops = line.Stops.Select((s, i) => StopOf(s, i + 1, addresses, slots)).ToList()
            });
        }

        return snapshot;
    }

    private static PricingStopDto StopOf(StopInput stop, int position, IReadOnlyDictionary<Guid, MasterDataAddressReferenceDto> addresses, IReadOnlyDictionary<Guid, TimeSlot> slots)
    {
        addresses.TryGetValue(stop.RelationAddressId, out var address);
        var zone = address?.TimeZoneId;
        DateTimeOffset? from = null, till = null;
        if (stop.TimeSlotId != null && slots.TryGetValue(stop.TimeSlotId.Value, out var slot) && zone != null)
        {
            (from, till) = slot.ToWindow(stop.Date, zone);
        }
        else if (stop.WindowFrom != null && stop.WindowTill != null && zone != null)
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(zone);
            from = TimeSlot.Resolve(stop.Date.ToDateTime(stop.WindowFrom.Value), tz);
            till = TimeSlot.Resolve((stop.WindowTill < stop.WindowFrom ? stop.Date.AddDays(1) : stop.Date).ToDateTime(stop.WindowTill.Value), tz);
        }

        return new PricingStopDto
        {
            StopId = stop.Id,
            Position = position,
            RelationId = stop.RelationId,
            RelationAddressId = stop.RelationAddressId,
            CountryCode = address?.CountryCode,
            PostalCode = address?.PostalCode,
            GeographicalRegionIds = address?.GeographicalRegionIds.ToList() ?? [],
            Date = stop.Date,
            WaypointTypeId = stop.WaypointTypeId,
            ContactPersonId = stop.ContactPersonId,
            TimeSlotId = stop.TimeSlotId,
            WindowFrom = from,
            WindowTill = till,
            Reference = stop.Reference,
            Remarks = stop.Remarks,
            PlannedDurationMin = stop.PlannedDurationMin,
            Latitude = address?.Latitude,
            Longitude = address?.Longitude,
            TimeZoneId = zone,
            AddressName = address?.RelationName,
            City = address?.City
        };
    }

    private static int? Sum(IReadOnlyList<LegSnapshotDto> legs, Func<LegSnapshotDto, int?> value)
        => legs.Count == 0 || legs.Any(l => value(l) == null) ? null : legs.Sum(l => value(l)!.Value);
}
