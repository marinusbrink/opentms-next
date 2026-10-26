using System;
using System.Collections.Generic;
using System.Linq;
using Volo.Abp;

namespace OpenTms.Orders.TransportOrders;

/// <summary>
/// When a template materialises (design 58 "Templates and recurrence"): weekly on the flagged days every
/// <see cref="Interval"/> weeks, or monthly on a day of the month every <see cref="Interval"/> months, from
/// <see cref="ValidFrom"/> till <see cref="ValidTill"/>, looking <see cref="HorizonDays"/> ahead. Owned by the template.
/// </summary>
public class TemplateSchedule
{
    public TemplateRecurrence Recurrence { get; private set; }
    public int Interval { get; private set; } = 1;
    public RecurrenceDays DaysOfWeek { get; private set; }
    public int? DayOfMonth { get; private set; }
    public int HorizonDays { get; private set; } = OrdersConsts.DefaultHorizonDays;
    public DateOnly ValidFrom { get; private set; }
    public DateOnly? ValidTill { get; private set; }

    protected TemplateSchedule()
    {
    }

    public static TemplateSchedule None() => new() { Recurrence = TemplateRecurrence.None, Interval = 1, ValidFrom = DateOnly.MinValue };

    public static TemplateSchedule Create(TemplateRecurrence recurrence, int interval, RecurrenceDays daysOfWeek, int? dayOfMonth, int horizonDays, DateOnly validFrom, DateOnly? validTill)
    {
        if (!Enum.IsDefined(recurrence))
        {
            throw new BusinessException(OrdersErrorCodes.InvalidSchedule).WithData("field", "recurrence");
        }

        if (interval is < OrdersConsts.MinScheduleInterval or > OrdersConsts.MaxScheduleInterval)
        {
            throw new BusinessException(OrdersErrorCodes.InvalidSchedule).WithData("field", "interval");
        }

        if (horizonDays is < OrdersConsts.MinHorizonDays or > OrdersConsts.MaxHorizonDays)
        {
            throw new BusinessException(OrdersErrorCodes.InvalidSchedule).WithData("field", "horizonDays");
        }

        if ((daysOfWeek & ~(RecurrenceDays)127) != 0)
        {
            throw new BusinessException(OrdersErrorCodes.InvalidSchedule).WithData("field", "daysOfWeek");
        }

        if (recurrence == TemplateRecurrence.Weekly && daysOfWeek == RecurrenceDays.None)
        {
            throw new BusinessException(OrdersErrorCodes.InvalidSchedule).WithData("field", "daysOfWeek");
        }

        if (recurrence == TemplateRecurrence.Monthly && dayOfMonth is null or < 1 or > 31)
        {
            throw new BusinessException(OrdersErrorCodes.InvalidSchedule).WithData("field", "dayOfMonth");
        }

        if (validTill != null && validTill < validFrom)
        {
            throw new BusinessException(OrdersErrorCodes.InvalidSchedule).WithData("field", "validTill");
        }

        return new TemplateSchedule
        {
            Recurrence = recurrence,
            Interval = interval,
            DaysOfWeek = recurrence == TemplateRecurrence.Weekly ? daysOfWeek : RecurrenceDays.None,
            DayOfMonth = recurrence == TemplateRecurrence.Monthly ? dayOfMonth : null,
            HorizonDays = horizonDays,
            ValidFrom = validFrom,
            ValidTill = validTill
        };
    }

    public bool IsWithinValidity(DateOnly date) => date >= ValidFrom && (ValidTill == null || date <= ValidTill);

    /// <summary>
    /// Whether an order is due on <paramref name="date"/>: within the validity, on a flagged weekday in a week that is a
    /// multiple of the interval from the week of ValidFrom (weekly), or on the day of month (the last day when the
    /// month is shorter) in a month that is a multiple of the interval from ValidFrom's month (monthly).
    /// </summary>
    public bool Occurs(DateOnly date)
    {
        if (Recurrence == TemplateRecurrence.None || !IsWithinValidity(date))
        {
            return false;
        }

        if (Recurrence == TemplateRecurrence.Weekly)
        {
            if (!DaysOfWeek.HasFlag(ToFlag(date.DayOfWeek)))
            {
                return false;
            }

            var weeks = (MondayOf(date).DayNumber - MondayOf(ValidFrom).DayNumber) / 7;
            return weeks % Interval == 0;
        }

        var months = (date.Year - ValidFrom.Year) * 12 + date.Month - ValidFrom.Month;
        if (months % Interval != 0)
        {
            return false;
        }

        var day = Math.Min(DayOfMonth!.Value, DateTime.DaysInMonth(date.Year, date.Month));
        return date.Day == day;
    }

    /// <summary>The due dates from <paramref name="from"/> over the horizon (from included).</summary>
    public IReadOnlyList<DateOnly> DueDates(DateOnly from)
        => Enumerable.Range(0, HorizonDays).Select(from.AddDays).Where(Occurs).ToList();

    public static RecurrenceDays ToFlag(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => RecurrenceDays.Monday,
        DayOfWeek.Tuesday => RecurrenceDays.Tuesday,
        DayOfWeek.Wednesday => RecurrenceDays.Wednesday,
        DayOfWeek.Thursday => RecurrenceDays.Thursday,
        DayOfWeek.Friday => RecurrenceDays.Friday,
        DayOfWeek.Saturday => RecurrenceDays.Saturday,
        _ => RecurrenceDays.Sunday
    };

    private static DateOnly MondayOf(DateOnly date)
    {
        var offset = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-offset);
    }
}

/// <summary>
/// Turns a template into the draft of one order (design 58): the stops dated by their day offsets from the order
/// date, the trip request's times of day resolved in the tenant's zone. Pure; idempotency on template + date + copy
/// index is the service's (and the unique index's) concern.
/// </summary>
public static class TemplateMaterialiser
{
    public static TransportOrderDraft ToDraft(TransportOrderTemplate template, DateOnly orderDate, TimeZoneInfo zone)
    {
        Check.NotNull(template, nameof(template));
        Check.NotNull(zone, nameof(zone));

        var lines = template.Lines.OrderBy(l => l.Position).Select(l => new LineInput(
            null,
            l.Freight.ToInput(),
            l.Measurements.ToList(),
            l.RequiredTransportCharacteristicIds.ToList(),
            l.Stops.OrderBy(s => s.Position).Select(s => new StopInput(
                null, s.Details.WaypointTypeId, s.Details.RelationId!.Value, s.Details.RelationAddressId!.Value, s.Details.ContactPersonId,
                orderDate.AddDays(s.DayOffset), s.Details.TimeSlotId, s.Details.WindowFrom, s.Details.WindowTill, s.Details.Reference,
                s.Details.Remarks, s.Details.PlannedDurationMin)).ToList(),
            l.Attributes)).ToList();

        var defaultStops = template.DefaultStops.OrderBy(s => s.Position).Select(s => new DefaultStopInput(
            s.Details.WaypointTypeId, s.Details.RelationId, s.Details.RelationAddressId, s.Details.ContactPersonId, s.DayOffset,
            s.Details.TimeSlotId, s.Details.WindowFrom, s.Details.WindowTill, s.Details.PlannedDurationMin)).ToList();

        var references = template.References.OrderBy(r => r.Position).Select(r => new ReferenceInput(r.Type, r.Value, r.Description)).ToList();

        TripRequestInput? trip = null;
        if (template.TripRequest is { } t)
        {
            var departure = TimeSlot.Resolve(orderDate.AddDays(t.DepartureDayOffset).ToDateTime(t.DepartureTime), zone);
            var arrival = t.ArrivalDayOffset == null || t.ArrivalTime == null
                ? (DateTimeOffset?)null
                : TimeSlot.Resolve(orderDate.AddDays(t.ArrivalDayOffset.Value).ToDateTime(t.ArrivalTime.Value), zone);
            trip = new TripRequestInput(
                t.TransportTripTypeId, t.DepartureRelationId, t.DepartureAddressId, t.ArrivalRelationId, t.ArrivalAddressId, departure, arrival,
                t.CarrierRelationId, t.CarrierContactPersonId, t.VehicleCombinationId, t.VehicleIds.ToList(), t.DriverIds.ToList(),
                t.AgreedPurchaseAmount, t.AgreedPurchaseCurrencyCode);
        }

        return new TransportOrderDraft(template.HeaderFor(orderDate), template.FinancialTermsInput(), defaultStops, lines, references, trip, template.Attributes);
    }

    /// <summary>
    /// The wizard's details of one generated order (design 61 "Details"): the customer reference (null keeps the
    /// template's, empty clears it) and per line, by the line's index in position order, the quantity and weight.
    /// </summary>
    public static TransportOrderDraft WithDetails(TransportOrderDraft draft, MaterialisationDetails details)
    {
        Check.NotNull(draft, nameof(draft));
        Check.NotNull(details, nameof(details));
        var header = details.CustomerReference == null
            ? draft.Header
            : draft.Header with { CustomerReference = string.IsNullOrWhiteSpace(details.CustomerReference) ? null : details.CustomerReference.Trim() };
        var lines = draft.Lines.Select((line, index) => details.Lines.TryGetValue(index, out var values)
            ? line with { Freight = line.Freight with { Quantity = values.Quantity ?? line.Freight.Quantity, WeightKg = values.WeightKg ?? line.Freight.WeightKg } }
            : line).ToList();
        return draft with { Header = header, Lines = lines };
    }
}

/// <summary>The quantity and weight of one line of a generated order; null keeps the source's.</summary>
public sealed record MaterialisationLineDetails(decimal? Quantity, double? WeightKg);

/// <summary>The details of one generated order (design 61 wizard "Details"); lines keyed by their index in position order.</summary>
public sealed record MaterialisationDetails(string? CustomerReference, IReadOnlyDictionary<int, MaterialisationLineDetails> Lines);
