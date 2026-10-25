using System;
using System.Collections.Generic;
using System.Linq;
using OpenTms.Platform.ReferenceData;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.MultiTenancy;

namespace OpenTms.Orders.TransportOrders;

/// <summary>
/// A line of a transport order (OpenTMS freight; design 58): the freight facts, its measurements, the transport
/// characteristics it requires and the stops it asks for (≥ 2 once confirmed). The stored distance, duration and CO₂
/// are copies of Planning's leg values written by the order summary calculator.
/// </summary>
public class TransportOrderLine : Entity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }

    public Guid OrderId { get; private set; }

    public int Position { get; private set; }

    public FreightDetails Freight { get; private set; } = default!;

    public Dictionary<string, string> Attributes { get; private set; } = new();

    public virtual ICollection<TransportOrderLineMeasurement> Measurements { get; private set; } = new List<TransportOrderLineMeasurement>();

    public virtual ICollection<TransportOrderLineCharacteristic> RequiredTransportCharacteristics { get; private set; } = new List<TransportOrderLineCharacteristic>();

    public virtual ICollection<TransportOrderStop> Stops { get; private set; } = new List<TransportOrderStop>();

    /* Stored derived values (copies of Planning's legs, written by OrderSummaryCalculator). */

    public int? DistanceM { get; private set; }

    public int? DurationS { get; private set; }

    public decimal? Co2Kg { get; private set; }

    protected TransportOrderLine()
    {
    }

    public TransportOrderLine(Guid id, Guid? tenantId, Guid orderId, int position, LineInput input, Func<Guid> newId)
        : base(id)
    {
        TenantId = tenantId;
        OrderId = orderId;
        Set(position, input, newId);
    }

    public void SetPosition(int position) => Position = position;

    /// <summary>Applies the input; returns true when a stop's place or window changed (Planning regenerates the leg).</summary>
    public bool Set(int position, LineInput input, Func<Guid> newId)
    {
        Check.NotNull(input, nameof(input));
        Position = position;
        Freight = FreightDetails.From(input.Freight);
        Attributes = OrderValues.Attributes(input.Attributes);
        SetMeasurements(input.Measurements);
        SetRequiredTransportCharacteristics(input.RequiredTransportCharacteristicIds);
        return SetStops(input.Stops, newId);
    }

    public void SetMeasurements(IReadOnlyList<MeasurementInput> measurements)
    {
        Check.NotNull(measurements, nameof(measurements));
        var wanted = measurements
            .Select(m => new MeasurementInput(OrderValues.Defined(m.Kind, "measurements.kind"), m.Position, m.Value))
            .ToList();
        if (wanted.Any(m => m.Position is < 1 or > OrdersConsts.MaxMeasurementsPerKind))
        {
            throw new BusinessException(ReferenceDataErrorCodes.InvalidCode).WithData("field", "measurements.position").WithData("value", OrdersConsts.MaxMeasurementsPerKind);
        }

        if (wanted.Select(m => (m.Kind, m.Position)).Distinct().Count() != wanted.Count)
        {
            throw new BusinessException(ReferenceDataErrorCodes.DuplicateKey).WithData("field", "measurements").WithData("value", "");
        }

        foreach (var m in wanted)
        {
            OrderValues.NonNegative(m.Value, "measurements.value");
        }

        foreach (var stale in Measurements.Where(m => wanted.All(w => w.Kind != m.Kind || w.Position != m.Position)).ToList())
        {
            Measurements.Remove(stale);
        }

        foreach (var m in wanted)
        {
            var existing = Measurements.FirstOrDefault(x => x.Kind == m.Kind && x.Position == m.Position);
            if (existing == null)
            {
                Measurements.Add(new TransportOrderLineMeasurement(TenantId, Id, m.Kind, m.Position, m.Value));
            }
            else
            {
                existing.SetValue(m.Value);
            }
        }
    }

    public void SetRequiredTransportCharacteristics(IReadOnlyList<Guid> ids)
    {
        Check.NotNull(ids, nameof(ids));
        var wanted = ids.Where(id => id != Guid.Empty).Distinct().ToList();
        if (wanted.Count > OrdersConsts.MaxRequiredCharacteristicsPerLine)
        {
            throw OrderValues.TooMany("requiredTransportCharacteristicIds", OrdersConsts.MaxRequiredCharacteristicsPerLine);
        }

        foreach (var stale in RequiredTransportCharacteristics.Where(c => !wanted.Contains(c.TransportCharacteristicId)).ToList())
        {
            RequiredTransportCharacteristics.Remove(stale);
        }

        foreach (var id in wanted.Where(id => RequiredTransportCharacteristics.All(c => c.TransportCharacteristicId != id)))
        {
            RequiredTransportCharacteristics.Add(new TransportOrderLineCharacteristic(TenantId, Id, id));
        }
    }

    /// <summary>Replaces the stops (matched on id); returns true when any stop's place or window changed, or a stop came or went.</summary>
    public bool SetStops(IReadOnlyList<StopInput> stops, Func<Guid> newId)
    {
        Check.NotNull(stops, nameof(stops));
        if (stops.Count > OrdersConsts.MaxStopsPerLine)
        {
            throw OrderValues.TooMany("stops", OrdersConsts.MaxStopsPerLine);
        }

        var changed = false;
        var keep = new HashSet<Guid>();
        for (var i = 0; i < stops.Count; i++)
        {
            var input = stops[i];
            var position = i + 1;
            var existing = input.Id == null ? null : Stops.FirstOrDefault(s => s.Id == input.Id.Value);
            if (existing == null)
            {
                var stop = new TransportOrderStop(newId(), TenantId, Id, position, input);
                Stops.Add(stop);
                keep.Add(stop.Id);
                changed = true;
            }
            else
            {
                changed |= existing.Set(position, input);
                keep.Add(existing.Id);
            }
        }

        foreach (var stale in Stops.Where(s => !keep.Contains(s.Id)).ToList())
        {
            Stops.Remove(stale);
            changed = true;
        }

        return changed;
    }

    public void WriteLegValues(int? distanceM, int? durationS, decimal? co2Kg)
    {
        DistanceM = distanceM;
        DurationS = durationS;
        Co2Kg = co2Kg;
    }

    public LineInput ToInput() => new(
        Id,
        Freight.ToInput(),
        Measurements.Select(m => new MeasurementInput(m.Kind, m.Position, m.Value)).ToList(),
        RequiredTransportCharacteristics.Select(c => c.TransportCharacteristicId).ToList(),
        Stops.OrderBy(s => s.Position).Select(s => s.ToInput()).ToList(),
        Attributes);
}

/// <summary>A measured weight or volume (OpenTMS MeasuredWeight1-3 / MeasuredVolume1-3); key (line, kind, position).</summary>
public class TransportOrderLineMeasurement : Entity, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid LineId { get; private set; }
    public MeasurementKind Kind { get; private set; }
    public int Position { get; private set; }
    public double Value { get; private set; }

    protected TransportOrderLineMeasurement()
    {
    }

    public TransportOrderLineMeasurement(Guid? tenantId, Guid lineId, MeasurementKind kind, int position, double value)
    {
        TenantId = tenantId;
        LineId = lineId;
        Kind = kind;
        Position = position;
        Value = value;
    }

    public void SetValue(double value) => Value = value;

    public override object[] GetKeys() => [LineId, Kind, Position];
}

/// <summary>A transport characteristic (Master Data id) the line's vehicle must have; key (line, characteristic).</summary>
public class TransportOrderLineCharacteristic : Entity, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid LineId { get; private set; }
    public Guid TransportCharacteristicId { get; private set; }

    protected TransportOrderLineCharacteristic()
    {
    }

    public TransportOrderLineCharacteristic(Guid? tenantId, Guid lineId, Guid transportCharacteristicId)
    {
        TenantId = tenantId;
        LineId = lineId;
        TransportCharacteristicId = transportCharacteristicId;
    }

    public override object[] GetKeys() => [LineId, TransportCharacteristicId];
}

/// <summary>The requested place and time of a line (OpenTMS waypoint); the address is explicit (assessment §3.4).</summary>
public class TransportOrderStop : Entity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid LineId { get; private set; }
    public int Position { get; private set; }
    public StopDetails Details { get; private set; } = default!;
    public DateOnly Date { get; private set; }

    protected TransportOrderStop()
    {
    }

    public TransportOrderStop(Guid id, Guid? tenantId, Guid lineId, int position, StopInput input)
        : base(id)
    {
        TenantId = tenantId;
        LineId = lineId;
        Set(position, input);
    }

    /// <summary>Returns true when the place, date or window changed (Planning regenerates the leg).</summary>
    public bool Set(int position, StopInput input)
    {
        Check.NotNull(input, nameof(input));
        if (input.RelationId == Guid.Empty)
        {
            throw new BusinessException(ReferenceDataErrorCodes.ReferenceNotFound).WithData("field", "stops.relationId");
        }

        if (input.RelationAddressId == Guid.Empty)
        {
            throw new BusinessException(ReferenceDataErrorCodes.ReferenceNotFound).WithData("field", "stops.relationAddressId");
        }

        var details = StopDetails.Create(input.WaypointTypeId, input.RelationId, input.RelationAddressId, input.ContactPersonId, input.TimeSlotId,
            input.WindowFrom, input.WindowTill, input.Reference, input.Remarks, input.PlannedDurationMin, position);
        var changed = Details == null || !Details.SamePlaceAndWindowAs(details) || Date != input.Date || Position != position;
        Position = position;
        Details = details;
        Date = input.Date;
        return changed;
    }

    public StopInput ToInput() => new(
        Id, Details.WaypointTypeId, Details.RelationId!.Value, Details.RelationAddressId!.Value, Details.ContactPersonId, Date, Details.TimeSlotId,
        Details.WindowFrom, Details.WindowTill, Details.Reference, Details.Remarks, Details.PlannedDurationMin);
}
