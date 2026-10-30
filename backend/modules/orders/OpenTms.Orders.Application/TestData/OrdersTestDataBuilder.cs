using System;
using System.Collections.Generic;
using System.Linq;
using OpenTms.Financial.TestData;
using OpenTms.MasterData.Relations;
using OpenTms.MasterData.TestData;
using OpenTms.Orders.TransportOrders;
using OpenTms.Platform.TestData;

namespace OpenTms.Orders.TestData;

/// <summary>The Orders reference data a reset run builds on: seeded before generation, never generated.</summary>
public sealed record OrdersTestReferences(
    TransportOrderType OrderType,
    WaypointType Load,
    WaypointType Unload,
    IReadOnlyList<WaypointType> WaypointTypes,
    IReadOnlyList<TimeSlot> TimeSlots);

/// <summary>
/// Builds the Orders part of a test dataset in memory, through the aggregates' own constructors and commands (so every
/// rule of design 58 holds: ≥ 2 stops per line, the type's required fields on Confirm, the status machine), from the
/// relations Master Data published. One instance per run; every choice comes from the run's seeded random generator,
/// so the same profile gives the same dataset. Shapes of a Dutch carrier's order book:
/// <list type="bullet">
/// <item>a few customers give most orders (a Zipf-like spread); each customer ships from its own address or a depot to
/// a fixed circle of consignees, mostly in its own country;</item>
/// <item>most orders are one pickup and one delivery (one to three freight items, each a line with its own two stops);
/// multi-drop orders deliver 2–4 consignees from one pickup (3–5 stops), multi-pick orders collect into one consignee,
/// return orders bring empty pallets back;</item>
/// <item>weekdays carry the volume, Saturday a little, Sunday almost nothing; the status follows the date: past orders
/// approved (ReadyForInvoicing, some Archived), the last working days partly still Open, today and later Open with a
/// few Drafts from agents and EDI, a few percent cancelled throughout;</item>
/// <item>recurring templates produce their orders on their own days (source Template), so the hourly materialisation
/// finds every due date already present and creates nothing after the reset.</item>
/// </list>
/// </summary>
public sealed class OrdersTestDataBuilder
{
    /// <summary>Share of the orders that recurring templates produce at most.</summary>
    public const double TemplateOrderShare = 0.2;

    private const double MaxTruckLdm = 13.6;
    private const double MaxTruckWeightKg = 24_000;
    private const double MinLineWeightKg = 50;

    private static readonly string[] StopRemarks =
    {
        "Melden bij de receptie", "Laadklep vereist", "Bellen 30 minuten voor aankomst", "Inrit via de achterzijde",
        "Geen toegang na 16:00", "Afmelden bij de portier", "Lossen aan dock 4", "Pallets ruilen", "Bouwplaats, beperkte ruimte"
    };

    private static readonly string[] OrderRemarks =
    {
        "Klant wil een track & trace-bericht", "Smalle straat, geen trekker-oplegger", "Vaste chauffeur gewenst",
        "Afleveren voor de middag", "Douanedocumenten meegeven"
    };

    private static readonly string[] InvoicingRemarks = { "Factuur per e-mail naar de crediteurenadministratie", "Inkoopordernummer op de factuur vermelden" };

    private static readonly string[] LineInstructions = { "Niet stapelen", "Breekbaar", "ADR klasse 3, LQ", "Droog houden", "Bovenop laden" };

    private static readonly string[] RevisionReasons =
    {
        "Wachttijd bij het lossen toegevoegd", "Aantal pallets gecorrigeerd door de klant", "Referentie van de klant aangepast",
        "Extra losadres op verzoek van de klant"
    };

    private readonly TestDatasetContext _context;
    private readonly Random _random;
    private readonly MasterDataTestDataset _masterData;
    private readonly FinancialTestDataset? _financial;
    private readonly OrdersTestReferences _refs;
    private readonly Func<Guid> _newId;
    private readonly Guid? _tenantId;
    private readonly DateTime _nowUtc;
    private readonly TransportOrderRequiredFields _required;
    private readonly IReadOnlyList<DefaultStopInput> _defaultStops;
    private readonly Dictionary<Guid, WaypointActivity> _activities;

    private readonly List<TestRelation> _customers;
    private readonly double[] _customerWeights;
    private readonly Dictionary<string, List<Place>> _consigneesByCountry;
    private readonly List<Place> _consignees;
    private readonly List<Place> _depots;
    private readonly Dictionary<Guid, CustomerLanes> _lanes = new();
    private readonly List<TimeSlot> _loadingSlots;
    private readonly List<TimeSlot> _afternoonSlots;
    private readonly List<TimeSlot> _daySlots;
    private readonly List<TemplatePlan> _templatePlans = new();

    public OrdersTestDataBuilder(
        TestDatasetContext context, MasterDataTestDataset masterData, FinancialTestDataset? financial, OrdersTestReferences refs, Func<Guid> newId, Guid? tenantId, DateTime nowUtc)
    {
        _context = context;
        _random = context.Random;
        _masterData = masterData;
        _financial = financial;
        _refs = refs;
        _newId = newId;
        _tenantId = tenantId;
        _nowUtc = nowUtc;
        _required = refs.OrderType.RequiredFields;
        _activities = refs.WaypointTypes.ToDictionary(w => w.Id, w => w.Activity);
        _defaultStops = refs.OrderType.DefaultWaypoints.OrderBy(w => w.Position)
            .Select(w => new DefaultStopInput(w.WaypointTypeId, null, null, null, 0, null, null, null, null)).ToList();

        Today = DateOnly.FromDateTime(context.Today);
        FirstDate = Today.AddDays(-context.Profile.OrderDaysBack);
        LastDate = Today.AddDays(context.Profile.OrderDaysAhead);

        var withAddress = masterData.Relations.Where(r => r.Addresses.Count > 0).ToList();
        _customers = withAddress.Where(r => r.Roles.HasFlag(RelationRoles.Customer) && !r.Roles.HasFlag(RelationRoles.OwnCompany)).ToList();
        _random.Shuffle(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_customers));
        var active = Math.Min(_customers.Count, Math.Max(20, context.Profile.Orders / 10));
        _customers = _customers.Take(active).ToList();
        _customerWeights = Cumulative(_customers.Count, r => 1d / Math.Pow(r + 1, 0.85));

        var consigneeRelations = withAddress
            .Where(r => r.Roles == RelationRoles.None
                        || (!r.Roles.HasFlag(RelationRoles.OwnCompany) && (r.Roles & ~(RelationRoles.Carrier | RelationRoles.Creditor)) != RelationRoles.None))
            .ToList();
        if (consigneeRelations.Count < 2)
        {
            consigneeRelations = withAddress;
        }

        _consignees = consigneeRelations.SelectMany(r => r.Addresses.Select(a => new Place(r.Id, r.Name, a))).ToList();
        _consigneesByCountry = _consignees.GroupBy(p => p.Address.CountryCode, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var depots = withAddress.Where(r => r.Roles.HasFlag(RelationRoles.Depot)).ToList();
        if (depots.Count == 0)
        {
            depots = withAddress.Where(r => r.Roles.HasFlag(RelationRoles.OwnCompany)).ToList();
        }

        _depots = depots.SelectMany(r => r.Addresses.Select(a => new Place(r.Id, r.Name, a))).ToList();

        var slots = refs.TimeSlots.Where(s => !s.IsOvernight).ToList();
        _loadingSlots = slots.Where(s => s.StartTime.Hour is >= 5 and <= 13).ToList();
        _afternoonSlots = slots.Where(s => s.StartTime.Hour is >= 11 and <= 15).ToList();
        _daySlots = slots.Where(s => s.StartTime.Hour is >= 5 and <= 15).ToList();
    }

    public DateOnly Today { get; }

    public DateOnly FirstDate { get; }

    public DateOnly LastDate { get; }

    public IEnumerable<DateOnly> Dates => Enumerable.Range(0, LastDate.DayNumber - FirstDate.DayNumber + 1).Select(FirstDate.AddDays);

    public OrdersTestDataset Dataset { get; } = new();

    /// <summary>The orders the recurring templates produce in the date range (known after <see cref="BuildTemplates"/>).</summary>
    public int TemplateOrderCount { get; private set; }

    /// <summary>Why nothing can be generated, or null when the input suffices.</summary>
    public string? MissingInput
    {
        get
        {
            if (_customers.Count == 0) return "no relation with the Customer role and an address";
            if (_consignees.Count < 2) return "fewer than two relation addresses to deliver to";
            if (_masterData.TransportUnitIds.Count == 0) return "no transport units";
            if (_required.HasFlag(TransportOrderRequiredFields.ProductPerLine) && _masterData.ProductIds.Count == 0) return "the order type requires a product per line and there are no products";
            if (_required.HasFlag(TransportOrderRequiredFields.Temperature) && _masterData.TemperatureIds.Count == 0) return "the order type requires a temperature and there are no temperatures";
            return null;
        }
    }

    // ── templates ──────────────────────────────────────────────────────────────

    /// <summary>
    /// <paramref name="count"/> templates: recurring ones (weekly on fixed days, a few monthly) until their orders in the
    /// date range reach <see cref="TemplateOrderShare"/> of the profile's orders, then one-off templates ("copy order" and
    /// regular lanes kept for manual use) and some paused recurring ones.
    /// </summary>
    public List<TransportOrderTemplate> BuildTemplates(int count)
    {
        var budget = (int)(_context.Profile.Orders * TemplateOrderShare);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var templates = new List<TransportOrderTemplate>(count);
        var dates = Dates.ToList();

        for (var i = 0; i < count; i++)
        {
            var customer = PickCustomer();
            var lanes = LanesOf(customer);
            var shipment = BuildShipment(customer, lanes, allowReturn: false);

            var schedule = TemplateSchedule.None();
            var active = true;
            var occurrences = new List<DateOnly>();
            if (TemplateOrderCount < budget && _random.NextDouble() < 0.6)
            {
                schedule = NewRecurringSchedule();
                occurrences = dates.Where(schedule.Occurs).ToList();
                if (TemplateOrderCount + occurrences.Count > budget)
                {
                    active = false;
                    occurrences.Clear();
                }
            }
            else if (_random.NextDouble() < 0.15)
            {
                schedule = NewRecurringSchedule();
                active = false;
            }

            var reference = _required.HasFlag(TransportOrderRequiredFields.CustomerReference) ? $"RAAM-{TestNames.Digits(_random, 6)}" : null;
            var header = new OrderHeaderInput(_refs.OrderType.Id, customer.Id, null, customer.Name, Today, reference, null, null, null);
            var template = new TransportOrderTemplate(_newId(), _tenantId, UniqueName(names, customer, shipment, schedule), header, TransportOrderFinancialTerms.Default(), schedule, _newId);
            template.SetDefaultStops(_defaultStops);
            template.SetLines(shipment.Lines.Select(l => new TemplateLineInput(
                l.Freight, [], l.Characteristics,
                l.Stops.Select(s => new TemplateStopInput(s.WaypointTypeId, s.Place.RelationId, s.Place.Address.Id, null, s.DayOffset, s.TimeSlotId, s.From, s.Till, s.Reference, s.Remarks, s.DurationMin)).ToList(),
                null)).ToList(), _newId);
            if (_random.NextDouble() < 0.3)
            {
                template.SetReferences([new ReferenceInput("Contract", $"RC-{Today.Year}-{TestNames.Digits(_random, 4)}", null)], _newId);
            }

            template.SetActive(active);
            if (occurrences.Count > 0)
            {
                template.RecordMaterialised(occurrences[^1]);
                TemplateOrderCount += occurrences.Count;
                _templatePlans.Add(new TemplatePlan(template, shipment, occurrences.ToHashSet()));
            }

            templates.Add(template);
            Dataset.TemplateIds.Add(template.Id);
        }

        return templates;
    }

    private TemplateSchedule NewRecurringSchedule()
    {
        var validFrom = FirstDate.AddDays(_random.NextDouble() < 0.8 ? -_random.Next(1, 240) : _random.Next(0, Math.Max(1, _context.Profile.OrderDaysBack)));
        DateOnly? validTill = _random.NextDouble() < 0.1 ? LastDate.AddDays(_random.Next(30, 365)) : null;
        var horizon = Math.Min(TestNames.Pick(_random, new[] { 5, 5, 5, 7, 10, 14 }), _context.Profile.OrderDaysAhead + 1);
        if (_random.NextDouble() < 0.1)
        {
            return TemplateSchedule.Create(TemplateRecurrence.Monthly, 1, RecurrenceDays.None, _random.Next(1, 29), horizon, validFrom, validTill);
        }

        var days = _random.NextDouble() switch
        {
            < 0.20 => RecurrenceDays.Monday | RecurrenceDays.Tuesday | RecurrenceDays.Wednesday | RecurrenceDays.Thursday | RecurrenceDays.Friday,
            < 0.40 => RecurrenceDays.Monday | RecurrenceDays.Wednesday | RecurrenceDays.Friday,
            < 0.55 => RecurrenceDays.Tuesday | RecurrenceDays.Thursday,
            < 0.95 => TemplateSchedule.ToFlag((DayOfWeek)_random.Next(1, 6)),
            _ => RecurrenceDays.Monday | RecurrenceDays.Tuesday | RecurrenceDays.Wednesday | RecurrenceDays.Thursday | RecurrenceDays.Friday | RecurrenceDays.Saturday
        };
        return TemplateSchedule.Create(TemplateRecurrence.Weekly, _random.NextDouble() < 0.85 ? 1 : 2, days, null, horizon, validFrom, validTill);
    }

    private static string UniqueName(HashSet<string> names, TestRelation customer, Shipment shipment, TemplateSchedule schedule)
    {
        var from = shipment.Lines[0].Stops[0].Place.Address.City;
        var to = shipment.Lines[^1].Stops[^1].Place.Address.City;
        var suffix = schedule.Recurrence switch
        {
            TemplateRecurrence.Weekly => " (" + DaysLabel(schedule.DaysOfWeek) + ")",
            TemplateRecurrence.Monthly => $" (dag {schedule.DayOfMonth})",
            _ => string.Empty
        };
        var code = customer.Code.Length > 12 ? customer.Code[..12] : customer.Code;
        var baseName = $"{code} {from} - {to}{suffix}";
        if (baseName.Length > OrdersConsts.MaxTemplateNameLength - 4)
        {
            baseName = baseName[..(OrdersConsts.MaxTemplateNameLength - 4)].TrimEnd();
        }

        var name = baseName;
        for (var n = 2; !names.Add(name); n++)
        {
            name = $"{baseName} {n}";
        }

        return name;
    }

    private static string DaysLabel(RecurrenceDays days)
    {
        var labels = new (RecurrenceDays Day, string Label)[]
        {
            (RecurrenceDays.Monday, "ma"), (RecurrenceDays.Tuesday, "di"), (RecurrenceDays.Wednesday, "wo"), (RecurrenceDays.Thursday, "do"),
            (RecurrenceDays.Friday, "vr"), (RecurrenceDays.Saturday, "za"), (RecurrenceDays.Sunday, "zo")
        };
        return string.Join("/", labels.Where(l => days.HasFlag(l.Day)).Select(l => l.Label));
    }

    // ── orders ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// The profile's orders, day by day from the first date (so numbers rise with the order date): the templates'
    /// occurrences of the day plus the day's share of the other orders (largest-remainder split by weekday weight).
    /// Lazily built, so the writer stores them in batches and only the published dataset stays in memory.
    /// <paramref name="nextNumber"/> draws the next number of the order type's sequence in the period of a date.
    /// </summary>
    public IEnumerable<TransportOrder> BuildOrders(Func<DateOnly, string> nextNumber)
    {
        var dates = Dates.ToList();
        var adHoc = Math.Max(0, _context.Profile.Orders - TemplateOrderCount);
        var weights = dates.Select(DayWeight).ToList();
        var total = weights.Sum();
        var exact = weights.Select(w => adHoc * w / total).ToList();
        var perDay = exact.Select(e => (int)Math.Floor(e)).ToArray();
        foreach (var index in exact.Select((e, i) => (Fraction: e - Math.Floor(e), Index: i)).OrderByDescending(x => x.Fraction).Take(adHoc - perDay.Sum()).Select(x => x.Index))
        {
            perDay[index]++;
        }

        for (var d = 0; d < dates.Count; d++)
        {
            var date = dates[d];
            foreach (var plan in _templatePlans.Where(p => p.Dates.Contains(date)))
            {
                yield return FromTemplate(plan, date, nextNumber);
            }

            for (var n = 0; n < perDay[d]; n++)
            {
                yield return AdHoc(date, nextNumber);
            }
        }
    }

    private static double DayWeight(DateOnly date) => date.DayOfWeek switch
    {
        DayOfWeek.Monday => 1.05,
        DayOfWeek.Friday => 0.9,
        DayOfWeek.Saturday => 0.1,
        DayOfWeek.Sunday => 0.03,
        _ => 1.0
    };

    private TransportOrder AdHoc(DateOnly date, Func<DateOnly, string> nextNumber)
    {
        var customer = PickCustomer();
        var lanes = LanesOf(customer);
        var shipment = BuildShipment(customer, lanes, allowReturn: true);
        var target = TargetStatus(date, fromTemplate: false);

        OrderSource source;
        decimal? confidence = null;
        if (target == Target.Draft)
        {
            source = _random.NextDouble() < 0.6 ? OrderSource.Agent : OrderSource.Api;
        }
        else
        {
            source = _random.NextDouble() switch { < 0.72 => OrderSource.Screen, < 0.94 => OrderSource.Api, _ => OrderSource.Agent };
        }

        if (source == OrderSource.Agent)
        {
            confidence = Math.Round((decimal)(0.55 + _random.NextDouble() * 0.43), 4);
        }

        var customerReference = _required.HasFlag(TransportOrderRequiredFields.CustomerReference) || _random.NextDouble() < 0.85
            ? CustomerReference(lanes, date)
            : null;
        var header = new OrderHeaderInput(
            _refs.OrderType.Id, customer.Id, null, customer.Name, date, customerReference, null,
            _random.NextDouble() < 0.03 ? TestNames.Pick(_random, OrderRemarks) : null,
            _random.NextDouble() < 0.02 ? TestNames.Pick(_random, InvoicingRemarks) : null);

        var order = new TransportOrder(_newId(), _tenantId, header, Terms(), source, _newId);
        order.SetDraftConfidence(confidence);
        order.SetDefaultStops(_defaultStops);
        order.SetLines(LineInputs(shipment, date), _newId);
        order.SetReferences(OrderReferences(), _newId);
        return Finish(order, shipment, date, target, null, nextNumber);
    }

    /// <summary>What the materialiser makes of a template on a date (TemplateMaterialiser.ToDraft): its header, terms, stops and references, confirmed.</summary>
    private TransportOrder FromTemplate(TemplatePlan plan, DateOnly date, Func<DateOnly, string> nextNumber)
    {
        var template = plan.Template;
        var order = new TransportOrder(_newId(), _tenantId, template.HeaderFor(date), template.FinancialTermsInput(), OrderSource.Template, _newId);
        order.SetSourceTemplate(template.Id, 0);
        order.SetDefaultStops(_defaultStops);
        order.SetLines(LineInputs(plan.Shipment, date), _newId);
        order.SetReferences(template.References.OrderBy(r => r.Position).Select(r => new ReferenceInput(r.Type, r.Value, r.Description)).ToList(), _newId);
        return Finish(order, plan.Shipment, date, TargetStatus(date, fromTemplate: true), template.Id, nextNumber);
    }

    /// <summary>Walks the real status machine to the target, writes the grid columns of the summary and records the order for Planning.</summary>
    private TransportOrder Finish(TransportOrder order, Shipment shipment, DateOnly date, Target target, Guid? templateId, Func<DateOnly, string> nextNumber)
    {
        Func<Guid, WaypointActivity?> activityOf = id => _activities.TryGetValue(id, out var a) ? a : null;
        void Confirm() => order.Confirm(nextNumber(date), _required, activityOf);

        switch (target)
        {
            case Target.Draft:
                break;
            case Target.Open:
                Confirm();
                break;
            case Target.ReadyForInvoicing:
                Confirm();
                order.Approve(TransportOrderStatus.ReadyForInvoicing);
                break;
            case Target.Archived:
                Confirm();
                order.Approve(TransportOrderStatus.Archived);
                break;
            case Target.Revised:
                Confirm();
                order.Approve(TransportOrderStatus.ReadyForInvoicing);
                var at = date.AddDays(_random.Next(1, 4)).ToDateTime(new TimeOnly(_random.Next(8, 17), _random.Next(0, 60)), DateTimeKind.Utc);
                order.Revise(TestNames.Pick(_random, RevisionReasons), null, at < _nowUtc ? at : _nowUtc, _newId());
                break;
            case Target.Cancelled:
                if (templateId != null || _random.NextDouble() < 0.8)
                {
                    Confirm();
                }

                order.Cancel();
                break;
        }

        /* The grid columns OrderSummaryCalculator writes from the order and Master Data (counts, first and last stop); the
         * leg- and rate-based values (revenue, cost, CO₂, progress, trips) stay empty until the calculator runs. */
        var first = shipment.Lines[0].Stops[0];
        var last = shipment.Lines[^1].Stops[^1];
        var (firstStop, lastStop, stopCount) = order.StopEnds();
        order.WriteSummary(new OrderSummary(
            null, null, null, null, ExecutionProgress.Unplanned, order.Lines.Count, stopCount,
            first.Place.Name, first.Place.Address.City, firstStop?.Date,
            last.Place.Name, last.Place.Address.City, lastStop?.Date, null), _nowUtc);

        Dataset.Orders.Add(Record(order, shipment, date, templateId));
        return order;
    }

    private TestTransportOrder Record(TransportOrder order, Shipment shipment, DateOnly date, Guid? templateId)
    {
        var lines = order.Lines.OrderBy(l => l.Position).Select((line, i) =>
        {
            var plan = shipment.Lines[i];
            var f = line.Freight;
            var stops = line.Stops.OrderBy(s => s.Position).Select((stop, j) =>
            {
                var s = plan.Stops[j];
                var (from, till) = Window(s, stop.Date);
                return new TestTransportOrderStop(
                    stop.Id, stop.Position, s.WaypointTypeId, _activities[s.WaypointTypeId], s.Place.RelationId, s.Place.Address.Id, s.Place.Name,
                    s.Place.Address.City, s.Place.Address.CountryCode, s.Place.Address.Latitude, s.Place.Address.Longitude, s.Place.TimeZoneId,
                    stop.Date, from, till, s.Reference, s.DurationMin);
            }).ToList();
            return new TestTransportOrderLine(
                line.Id, line.Position, f.TransportUnitId, f.ProductId, f.TemperatureId, f.Quantity, f.WeightKg, f.VolumeM3, f.LoadingMeters, f.Epq, f.LoadFactor,
                line.RequiredTransportCharacteristics.Select(c => c.TransportCharacteristicId).ToList(), stops);
        }).ToList();

        return new TestTransportOrder(order.Id, order.Number, order.Status, date, order.CustomerRelationId, order.CustomerName, order.CustomerReference, templateId, lines);
    }

    /// <summary>The window as instants in the stop's zone, as Orders' pricing snapshot resolves it (PricingSnapshotBuilder.StopOf).</summary>
    private (DateTimeOffset? From, DateTimeOffset? Till) Window(StopPlan stop, DateOnly date)
    {
        if (stop.TimeSlotId != null)
        {
            var slot = _refs.TimeSlots.First(t => t.Id == stop.TimeSlotId.Value);
            var (from, till) = slot.ToWindow(date, stop.Place.TimeZoneId);
            return (from, till);
        }

        if (stop.From != null && stop.Till != null)
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(stop.Place.TimeZoneId);
            return (TimeSlot.Resolve(date.ToDateTime(stop.From.Value), zone),
                TimeSlot.Resolve((stop.Till < stop.From ? date.AddDays(1) : date).ToDateTime(stop.Till.Value), zone));
        }

        return (null, null);
    }

    private enum Target { Draft, Open, ReadyForInvoicing, Archived, Revised, Cancelled }

    /// <summary>The status an order of this date would realistically have today.</summary>
    private Target TargetStatus(DateOnly date, bool fromTemplate)
    {
        var days = date.DayNumber - Today.DayNumber;
        var r = _random.NextDouble();
        if (days > 0)
        {
            if (!fromTemplate && r < 0.07) return Target.Draft;
            return r < 0.10 ? Target.Cancelled : Target.Open;
        }

        if (days == 0)
        {
            if (!fromTemplate && r < 0.02) return Target.Draft;
            return r < 0.04 ? Target.Cancelled : Target.Open;
        }

        if (r < 0.03)
        {
            return Target.Cancelled;
        }

        var age = -days;
        if (age <= 5)
        {
            /* The last working days: approval catches up, from 15 % a day ago to 75 % five days ago. */
            var approved = 0.15 + 0.15 * (age - 1);
            var a = _random.NextDouble();
            if (a < approved) return _random.NextDouble() < 0.05 ? Target.Archived : Target.ReadyForInvoicing;
            return a < approved + 0.04 ? Target.Revised : Target.Open;
        }

        var o = _random.NextDouble();
        return o switch
        {
            < 0.07 => Target.Archived,
            < 0.09 => Target.Revised,
            < 0.11 => Target.Open,
            _ => Target.ReadyForInvoicing
        };
    }

    // ── shipments ──────────────────────────────────────────────────────────────

    private sealed record Place(Guid RelationId, string Name, TestRelationAddress Address)
    {
        public string TimeZoneId => Address.CountryCode switch { "BE" => "Europe/Brussels", "DE" => "Europe/Berlin", _ => "Europe/Amsterdam" };
    }

    private sealed record StopPlan(Place Place, Guid WaypointTypeId, int DayOffset, Guid? TimeSlotId, TimeOnly? From, TimeOnly? Till, string? Reference, string? Remarks, int? DurationMin);

    private sealed record LinePlan(FreightInput Freight, IReadOnlyList<Guid> Characteristics, IReadOnlyList<StopPlan> Stops);

    private sealed record Shipment(IReadOnlyList<LinePlan> Lines);

    private sealed record TemplatePlan(TransportOrderTemplate Template, Shipment Shipment, HashSet<DateOnly> Dates);

    private enum GoodsKind { EuroPallets, BlockPallets, Colli, FullTruckload, Chilled, EmptyPallets }

    /// <summary>What a customer ships, kept for the whole run so its orders repeat lanes and goods like a real account.</summary>
    private sealed class CustomerLanes
    {
        public List<Place> Pickups { get; } = new();
        public List<Place> Consignees { get; } = new();
        public GoodsKind MainKind { get; init; }
        public Guid[] Products { get; init; } = [];
        public int ReferenceStyle { get; init; }
        public string Code { get; init; } = string.Empty;
    }

    private CustomerLanes LanesOf(TestRelation customer)
    {
        if (_lanes.TryGetValue(customer.Id, out var lanes))
        {
            return lanes;
        }

        var kinds = new[] { GoodsKind.EuroPallets, GoodsKind.EuroPallets, GoodsKind.EuroPallets, GoodsKind.BlockPallets, GoodsKind.Colli, GoodsKind.Colli, GoodsKind.FullTruckload, GoodsKind.Chilled };
        lanes = new CustomerLanes
        {
            MainKind = TestNames.Pick(_random, kinds),
            Products = _masterData.ProductIds.Count == 0 ? [] : Enumerable.Range(0, _random.Next(1, 5)).Select(_ => _masterData.ProductIds[_random.Next(_masterData.ProductIds.Count)]).Distinct().ToArray(),
            ReferenceStyle = _random.Next(5),
            Code = TestNames.Code(customer.Name)
        };

        /* Pickup places: the customer's own addresses (the first is its main address) and, for a share of accounts, the
         * carrier's depot where the customer's stock is kept. */
        lanes.Pickups.AddRange(customer.Addresses.Select(a => new Place(customer.Id, customer.Name, a)));
        if (_depots.Count > 0 && _random.NextDouble() < 0.3)
        {
            lanes.Pickups.Add(_depots[_random.Next(_depots.Count)]);
        }

        var home = customer.Addresses[0].CountryCode;
        var wanted = _random.Next(4, 41);
        for (var attempt = 0; lanes.Consignees.Count < wanted && attempt < wanted * 3; attempt++)
        {
            var pool = _random.NextDouble() < 0.65 && _consigneesByCountry.TryGetValue(home, out var local) ? local : _consignees;
            var place = pool[_random.Next(pool.Count)];
            if (place.RelationId != customer.Id && lanes.Consignees.All(p => p.Address.Id != place.Address.Id))
            {
                lanes.Consignees.Add(place);
            }
        }

        if (lanes.Consignees.Count == 0)
        {
            lanes.Consignees.Add(_consignees.First(p => p.RelationId != customer.Id));
        }

        _lanes[customer.Id] = lanes;
        return lanes;
    }

    private Shipment BuildShipment(TestRelation customer, CustomerLanes lanes, bool allowReturn)
    {
        var shape = _random.NextDouble();
        var pickup = Pick(lanes.Pickups, 0.7);

        if (allowReturn && shape >= 0.96)
        {
            /* Empty pallets back from a consignee to the customer. */
            var from = Pick(lanes.Consignees, 0.0);
            return new Shipment([Line(lanes, GoodsKind.EmptyPallets, from, lanes.Pickups[0], MaxTruckLdm, MaxTruckWeightKg)]);
        }

        if (shape >= 0.93)
        {
            /* Multi-pick: two or three pickups consolidated into one consignee. */
            var to = Pick(lanes.Consignees, 0.0);
            var pickups = Distinct(lanes.Pickups.Concat(lanes.Consignees.Where(c => c.Address.Id != to.Address.Id)).ToList(), _random.Next(2, 4));
            var collected = pickups.Select(p => Line(lanes, SmallKind(lanes), p, to, 4.0, 6_000)).ToList();
            /* One delivery appointment for the whole consolidation, on the latest day any pickup needs. */
            var arrival = collected.Select(l => l.Stops[1]).MaxBy(s => s.DayOffset)!;
            if (collected.Any(l => DeliversTooEarly(l.Stops[0], arrival)))
            {
                arrival = arrival with { DayOffset = arrival.DayOffset + 1 };
            }

            return new Shipment(collected.Select(l => l with { Stops = [l.Stops[0], arrival with { }] }).ToList());
        }

        if (shape >= 0.78)
        {
            /* Multi-drop: one pickup, two to four consignees (3–5 stops). */
            var drops = Distinct(lanes.Consignees, _random.Next(2, 5));
            var budget = MaxTruckLdm / Math.Max(1, drops.Count);
            var dropLines = drops.Select(to => Line(lanes, SmallKind(lanes), pickup, to, Math.Max(0.8, budget), MaxTruckWeightKg / drops.Count)).ToList();
            /* One loading appointment for the whole round. */
            var loading = dropLines[0].Stops[0];
            return new Shipment(dropLines.Select(l => l with
            {
                Stops = [loading with { }, DeliversTooEarly(loading, l.Stops[1]) ? l.Stops[1] with { DayOffset = l.Stops[1].DayOffset + 1 } : l.Stops[1]]
            }).ToList());
        }

        /* One pickup, one delivery, one to three freight items sharing the truck. */
        var destination = Pick(lanes.Consignees, 0.0);
        var count = _random.NextDouble() switch { < 0.8 => 1, < 0.95 => 2, _ => 3 };
        var lines = new List<LinePlan>();
        double ldmLeft = MaxTruckLdm, weightLeft = MaxTruckWeightKg;
        for (var i = 0; i < count && ldmLeft >= 0.4 && weightLeft >= MinLineWeightKg; i++)
        {
            var kind = i == 0 ? lanes.MainKind : SmallKind(lanes);
            if (i > 0 && kind == GoodsKind.FullTruckload)
            {
                kind = GoodsKind.EuroPallets;
            }

            var line = Line(lanes, kind, pickup, destination, ldmLeft / (count - i), weightLeft / (count - i));
            ldmLeft -= line.Freight.LoadingMeters ?? 0;
            weightLeft -= line.Freight.WeightKg ?? 0;
            lines.Add(line with { Stops = i == 0 ? line.Stops : SameStops(lines[0].Stops) });
        }

        return new Shipment(lines);
    }

    /// <summary>A same-day delivery whose window opens less than two hours after the loading window opens (it moves a day on).</summary>
    private bool DeliversTooEarly(StopPlan loading, StopPlan delivery)
    {
        if (delivery.DayOffset > loading.DayOffset)
        {
            return false;
        }

        var deliveryStart = StartHour(delivery);
        return deliveryStart != null && deliveryStart < (StartHour(loading) ?? 6) + 2;
    }

    private int? StartHour(StopPlan stop)
        => stop.TimeSlotId != null ? _refs.TimeSlots.First(t => t.Id == stop.TimeSlotId.Value).StartTime.Hour : stop.From?.Hour;

    /// <summary>A later line of the same pickup and delivery: the same places and windows, its own stop rows.</summary>
    private static IReadOnlyList<StopPlan> SameStops(IReadOnlyList<StopPlan> stops) => stops.Select(s => s with { }).ToList();

    private GoodsKind SmallKind(CustomerLanes lanes)
        => lanes.MainKind is GoodsKind.FullTruckload or GoodsKind.EmptyPallets
            ? (_random.NextDouble() < 0.7 ? GoodsKind.EuroPallets : GoodsKind.Colli)
            : lanes.MainKind;

    private LinePlan Line(CustomerLanes lanes, GoodsKind kind, Place from, Place to, double maxLdm, double maxWeight)
    {
        var km = DistanceKm(from.Address, to.Address);
        var deliveryOffset = km > 350 || (km > 150 && _random.NextDouble() < 0.3) || _random.NextDouble() < 0.08 ? 1 : 0;
        var loadingDuration = kind == GoodsKind.FullTruckload && _random.NextDouble() < 0.5 ? TestNames.Pick(_random, new int?[] { 45, 60, 90 }) : null;

        var (loadSlot, loadFrom, loadTill) = LoadingWindow();
        var loadStart = loadSlot != null ? _refs.TimeSlots.First(t => t.Id == loadSlot.Value).StartTime.Hour : loadFrom?.Hour ?? 6;
        if (deliveryOffset == 0 && loadStart >= 11)
        {
            /* Loaded in the afternoon: delivered the next day. */
            deliveryOffset = 1;
        }

        var (unloadSlot, unloadFrom, unloadTill) = DeliveryWindow(deliveryOffset == 0, deliveryOffset == 0 ? loadStart + 2 : 0);
        var loading = new StopPlan(from, _refs.Load.Id, 0, loadSlot, loadFrom, loadTill,
            _required.HasFlag(TransportOrderRequiredFields.LoadingReference) || _random.NextDouble() < 0.5 ? $"LR{TestNames.Digits(_random, 6)}" : null,
            _random.NextDouble() < 0.06 ? TestNames.Pick(_random, StopRemarks) : null, loadingDuration);
        var delivery = new StopPlan(to, _refs.Unload.Id, deliveryOffset, unloadSlot, unloadFrom, unloadTill,
            _required.HasFlag(TransportOrderRequiredFields.UnloadingReference) || _random.NextDouble() < 0.4 ? $"DN 80{TestNames.Digits(_random, 6)}" : null,
            _random.NextDouble() < 0.08 ? TestNames.Pick(_random, StopRemarks) : null, null);

        var characteristics = _masterData.TransportCharacteristicsIds.Count > 0 && _random.NextDouble() < 0.06
            ? new[] { _masterData.TransportCharacteristicsIds[_random.Next(_masterData.TransportCharacteristicsIds.Count)] }
            : Array.Empty<Guid>();
        return new LinePlan(Freight(lanes, kind, maxLdm, maxWeight), characteristics, [loading, delivery]);
    }

    private FreightInput Freight(CustomerLanes lanes, GoodsKind kind, double maxLdm, double maxWeight)
    {
        maxLdm = Math.Clamp(maxLdm, 0.4, MaxTruckLdm);
        maxWeight = Math.Clamp(maxWeight, MinLineWeightKg, MaxTruckWeightKg);
        decimal quantity;
        decimal? positions = null, colli = null;
        double weight, volume, ldm;

        switch (kind)
        {
            case GoodsKind.FullTruckload:
                quantity = 1;
                positions = 33;
                weight = Math.Round(_random.Next(8_000, 24_001) / 10d) * 10;
                ldm = MaxTruckLdm;
                volume = Math.Round(70 + _random.NextDouble() * 20, 1);
                break;
            case GoodsKind.Colli:
            {
                var n = 2 + (int)(118 * Math.Pow(_random.NextDouble(), 2));
                quantity = n;
                colli = n;
                weight = n * (8 + _random.NextDouble() * 32);
                ldm = Math.Min(Math.Min(2.4, maxLdm), Math.Ceiling(n / 20d) * 0.4);
                volume = n * (0.04 + _random.NextDouble() * 0.16);
                break;
            }
            case GoodsKind.EmptyPallets:
            {
                var n = _random.Next(10, 34);
                quantity = n;
                positions = Math.Ceiling(n / 15m);
                weight = n * 22;
                ldm = (double)positions.Value * 0.4;
                volume = n * 0.96 * 0.144;
                break;
            }
            default:
            {
                /* Euro pallets 1.2 × 0.8 m (0.4 LDM each, 0.2 double-stacked), block pallets 1.2 × 1.0 m (0.5 LDM). */
                var perPallet = kind == GoodsKind.BlockPallets ? 0.5 : 0.4;
                var stacked = _random.NextDouble() < 0.15;
                var ldmPerPallet = stacked ? perPallet / 2 : perPallet;
                var maxPallets = Math.Max(1, Math.Min(kind == GoodsKind.BlockPallets ? 26 : 33, (int)Math.Floor(maxLdm / ldmPerPallet)));
                var n = 1 + (int)((maxPallets - 1) * Math.Pow(_random.NextDouble(), 2.2));
                var kgPerPallet = 150 + _random.NextDouble() * 700;
                n = Math.Max(1, Math.Min(n, (int)Math.Floor(maxWeight / kgPerPallet)));
                quantity = n;
                positions = stacked ? Math.Ceiling(n / 2m) : n;
                colli = _random.NextDouble() < 0.25 ? n * _random.Next(4, 41) : null;
                weight = n * kgPerPallet;
                ldm = Math.Ceiling(n * ldmPerPallet * 10) / 10;
                volume = n * (kind == GoodsKind.BlockPallets ? 1.2 : 0.96) * (0.8 + _random.NextDouble());
                break;
            }
        }

        weight = Math.Round(Math.Clamp(weight, MinLineWeightKg, MaxTruckWeightKg));
        Guid? product = lanes.Products.Length > 0 && (_required.HasFlag(TransportOrderRequiredFields.ProductPerLine) || (kind != GoodsKind.EmptyPallets && _random.NextDouble() < 0.7))
            ? lanes.Products[_random.Next(lanes.Products.Length)]
            : null;
        Guid? temperature = _masterData.TemperatureIds.Count > 0 && (kind == GoodsKind.Chilled || _required.HasFlag(TransportOrderRequiredFields.Temperature))
            ? _masterData.TemperatureIds[(int)kind % _masterData.TemperatureIds.Count]
            : null;
        var unit = _masterData.TransportUnitIds[(int)kind % _masterData.TransportUnitIds.Count];

        return new FreightInput(
            unit, product, null, temperature, null, quantity, positions, colli, weight, Math.Round(volume, 2), Math.Round(ldm, 1), null, null,
            null, null, null, null, null, null,
            _random.NextDouble() < 0.05 ? TestNames.Pick(_random, LineInstructions) : null,
            null, null, PaymentInstruction.NotSet, null, null, null, null, null, null);
    }

    private (Guid? Slot, TimeOnly? From, TimeOnly? Till) LoadingWindow()
    {
        var r = _random.NextDouble();
        if (r < 0.35 && _loadingSlots.Count > 0)
        {
            return (_loadingSlots[_random.Next(_loadingSlots.Count)].Id, null, null);
        }

        if (r < 0.75 || _required.HasFlag(TransportOrderRequiredFields.LoadingTimeWindow))
        {
            var start = TestNames.Pick(_random, new[] { 6, 7, 7, 8, 8, 9, 10, 13 });
            var length = TestNames.Pick(_random, new[] { 1, 2, 2, 3, 4 });
            return (null, new TimeOnly(start, 0), new TimeOnly(start + length, 0));
        }

        return (null, null, null);
    }

    /// <param name="minStartHour">A same-day delivery starts at least two hours after the loading window opens.</param>
    private (Guid? Slot, TimeOnly? From, TimeOnly? Till) DeliveryWindow(bool sameDay, int minStartHour)
    {
        var r = _random.NextDouble();
        var slots = (sameDay ? _afternoonSlots : _daySlots).Where(s => s.StartTime.Hour >= minStartHour).ToList();
        if (r < 0.25 && slots.Count > 0)
        {
            return (slots[_random.Next(slots.Count)].Id, null, null);
        }

        if (r < 0.35)
        {
            /* A booked unloading slot of half an hour (distribution centres). */
            var hour = sameDay ? _random.Next(Math.Max(12, minStartHour), Math.Max(13, minStartHour + 1)) : _random.Next(6, 16);
            var from = new TimeOnly(Math.Min(hour, 20), _random.Next(2) * 30);
            return (null, from, from.AddMinutes(30));
        }

        if (r < 0.80 || _required.HasFlag(TransportOrderRequiredFields.UnloadingTimeWindow))
        {
            var start = sameDay ? TestNames.Pick(_random, new[] { 11, 12, 13, 14 }) : TestNames.Pick(_random, new[] { 6, 7, 8, 9, 10, 12, 13 });
            start = Math.Min(Math.Max(start, minStartHour), 18);
            var length = TestNames.Pick(_random, new[] { 2, 3, 4 });
            return (null, new TimeOnly(start, 0), new TimeOnly(start + length, 0));
        }

        return (null, null, null);
    }

    private IReadOnlyList<LineInput> LineInputs(Shipment shipment, DateOnly date)
        => shipment.Lines.Select(l => new LineInput(
            null, l.Freight, [], l.Characteristics,
            l.Stops.Select(s => new StopInput(null, s.WaypointTypeId, s.Place.RelationId, s.Place.Address.Id, null, Shift(date.AddDays(s.DayOffset), s.DayOffset),
                s.TimeSlotId, s.From, s.Till, s.Reference, s.Remarks, s.DurationMin)).ToList(),
            null)).ToList();

    /// <summary>A next-day delivery that would fall on a Sunday goes to Monday.</summary>
    private static DateOnly Shift(DateOnly date, int offset) => offset > 0 && date.DayOfWeek == DayOfWeek.Sunday ? date.AddDays(1) : date;

    /// <summary>
    /// EUR, no price list (Financial publishes revenue and cost lists together, so none can be told apart as the
    /// customer's revenue list); a manual discount or surcharge on a few orders.
    /// </summary>
    private FinancialTermsInput Terms()
    {
        var terms = TransportOrderFinancialTerms.Default();
        var ids = _financial?.DiscountOrSurchargeIds;
        return ids is { Count: > 0 } && _random.NextDouble() < 0.08
            ? terms with { DiscountsOrSurcharges = [new DiscountOrSurchargeInput(ids[_random.Next(ids.Count)], DiscountOrSurchargeSource.Manual)] }
            : terms;
    }

    private IReadOnlyList<ReferenceInput> OrderReferences()
    {
        var references = new List<ReferenceInput>();
        if (_random.NextDouble() < 0.25) references.Add(new ReferenceInput("Pakbon", $"PB{TestNames.Digits(_random, 8)}", null));
        if (_random.NextDouble() < 0.10) references.Add(new ReferenceInput("Booking", $"BK{TestNames.Digits(_random, 7)}", null));
        if (_random.NextDouble() < 0.03) references.Add(new ReferenceInput("Zegel", $"ZG-{TestNames.Digits(_random, 6)}", "Container seal"));
        return references;
    }

    private string CustomerReference(CustomerLanes lanes, DateOnly date) => lanes.ReferenceStyle switch
    {
        0 => $"4500{TestNames.Digits(_random, 6)}",
        1 => $"PO{TestNames.Digits(_random, 7)}",
        2 => $"{(lanes.Code.Length == 0 ? "ORD" : lanes.Code)}-{date:yy}{TestNames.Digits(_random, 5)}",
        3 => $"SO-{TestNames.Digits(_random, 6)}",
        _ => TestNames.Digits(_random, 8)
    };

    // ── helpers ────────────────────────────────────────────────────────────────

    private TestRelation PickCustomer()
    {
        var roll = _random.NextDouble() * _customerWeights[^1];
        var index = Array.BinarySearch(_customerWeights, roll);
        return _customers[Math.Min(index < 0 ? ~index : index, _customers.Count - 1)];
    }

    /// <summary>The first element with probability <paramref name="firstShare"/>, else a random one.</summary>
    private Place Pick(List<Place> places, double firstShare)
        => places.Count == 1 || _random.NextDouble() < firstShare ? places[0] : places[_random.Next(places.Count)];

    private List<Place> Distinct(List<Place> places, int count)
    {
        var copy = places.DistinctBy(p => p.Address.Id).ToList();
        _random.Shuffle(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(copy));
        return copy.Take(Math.Max(1, Math.Min(count, copy.Count))).ToList();
    }

    private static double[] Cumulative(int count, Func<int, double> weight)
    {
        var result = new double[count];
        var sum = 0d;
        for (var i = 0; i < count; i++)
        {
            sum += weight(i);
            result[i] = sum;
        }

        return result;
    }

    private static double DistanceKm(TestRelationAddress a, TestRelationAddress b)
    {
        const double earthRadiusKm = 6_371;
        double Rad(double degrees) => degrees * Math.PI / 180;
        var dLat = Rad(b.Latitude - a.Latitude);
        var dLon = Rad(b.Longitude - a.Longitude);
        var h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(Rad(a.Latitude)) * Math.Cos(Rad(b.Latitude)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        /* Road distance is about 1.3 × the straight line in the Benelux and Germany. */
        return 2 * earthRadiusKm * Math.Asin(Math.Min(1, Math.Sqrt(h))) * 1.3;
    }
}
