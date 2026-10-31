using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.MasterData.Relations;
using OpenTms.MasterData.TestData;
using OpenTms.Platform.ReferenceData;
using OpenTms.Platform.TestData;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;

namespace OpenTms.Financial.Tariffs.TestData;

/// <summary>A generated price list as the Invoicing part needs it to link the list to a relation.</summary>
public readonly record struct TestPriceList(Guid Id, QuantitySource Basis, DateOnly ValidFrom);

/// <summary>What the Tariffs part generated, for the Invoicing part (relation defaults) and the published output.</summary>
public sealed class TariffsTestData
{
    public List<Guid> PriceListIds { get; } = new();

    public List<Guid> DiscountOrSurchargeIds { get; } = new();

    public List<Guid> AdditionalServiceIds { get; } = new();

    /// <summary>The own revenue lists of a customer that are not archived and not expired.</summary>
    public Dictionary<Guid, List<TestPriceList>> RevenueListsByRelation { get; } = new();

    /// <summary>The own cost lists of a carrier that are not archived and not expired.</summary>
    public Dictionary<Guid, List<TestPriceList>> CostListsByRelation { get; } = new();

    /// <summary>The standard lists for relations without an own price agreement.</summary>
    public List<TestPriceList> GeneralRevenueLists { get; } = new();

    public List<TestPriceList> GeneralCostLists { get; } = new();

    /// <summary>The base discounts and surcharges by their code (DIESEL, TOL-NL, …).</summary>
    public Dictionary<string, Guid> SurchargeIdsByCode { get; } = new(StringComparer.Ordinal);

    /// <summary>Customer-specific discounts and surcharges (e.g. a negotiated diesel percentage) by relation id.</summary>
    public Dictionary<Guid, List<Guid>> SurchargesByRelation { get; } = new();

    public int PriceListCount { get; set; }

    public int RuleCount { get; set; }
}

/// <summary>
/// The Tariffs part of Financial's "Reset to dataset" contribution (docs/designs/65-tenant-test-datasets.md): additional
/// services, discounts and surcharges, and price lists with their rules, brackets and cells, in the shape a Dutch carrier
/// uses (pallet zone tariffs, groupage per kg, kilometre and hourly tariffs per vehicle type, fixed lane prices; cost
/// lists for charter carriers). Everything is built through the domain constructors and setters, so every invariant
/// holds. All randomness comes from the run's seeded generator: the same profile gives the same dataset.
/// </summary>
public class TariffsTestDataGenerator : ITransientDependency
{
    private const string Currency = "EUR";

    /// <summary>The cost lists of charter carriers sit this far below the revenue level.</summary>
    private const decimal CostLevel = 0.82m;

    private static readonly decimal?[] PalletColumns = [1, 2, 4, 6, 10, 16, null];
    private static readonly decimal[] PalletDegression = [1.00m, 0.93m, 0.86m, 0.80m, 0.74m, 0.69m, 0.64m];

    private static readonly decimal?[] WeightColumns = [100, 250, 500, 1000, 2500, 5000, 10000, null];
    private static readonly decimal[] WeightDegression = [1.00m, 0.82m, 0.66m, 0.52m, 0.40m, 0.31m, 0.24m, 0.19m];

    private static readonly decimal?[] DistanceBrackets = [50, 150, 300, 600, null];
    private static readonly decimal[] DistanceDegression = [1.30m, 1.00m, 0.88m, 0.80m, 0.74m];

    private static readonly decimal?[] HourBrackets = [4, 8, null];
    private static readonly decimal[] HourDegression = [1.12m, 1.00m, 0.95m];

    private static readonly (string Code, string Name, ServiceUnit Unit)[] ServiceTemplates =
    [
        ("LAADKLEP", "Laadklep bij lossen", ServiceUnit.Piece),
        ("WACHTTIJD", "Wachttijd", ServiceUnit.Hour),
        ("ADR", "ADR-afhandeling gevaarlijke stoffen", ServiceUnit.Piece),
        ("AVISEREN", "Telefonisch aviseren", ServiceUnit.Piece),
        ("TIJDSLOT", "Levering op tijdslot", ServiceUnit.Piece),
        ("PALLETRUIL", "Palletruil", ServiceUnit.Piece),
        ("EXTRASTOP", "Extra laad- of losadres", ServiceUnit.Stop),
        ("OPSLAG", "Opslag per palletplaats", ServiceUnit.Day),
        ("CROSSDOCK", "Cross-dock handling", ServiceUnit.Piece),
        ("ORDERPICK", "Orderpicking", ServiceUnit.Hour),
        ("ETIKET", "Etiketteren", ServiceUnit.Piece),
        ("KOELING", "Geconditioneerd transport", ServiceUnit.Piece),
        ("DOUANE", "Douanedocumenten (T1/EX-A)", ServiceUnit.Piece),
        ("BIJRIJDER", "Bijrijder", ServiceUnit.Hour),
        ("EMBALLAGE", "Retour lege emballage", ServiceUnit.Piece),
        ("OMRIJDEN", "Omrijkilometers", ServiceUnit.Kilometre),
        ("WEEGBON", "Weegbon", ServiceUnit.Piece),
        ("ZWAARCOLLO", "Toeslag zwaar collo", ServiceUnit.Kilogram),
        ("ZATERDAG", "Zaterdaglevering", ServiceUnit.Piece),
        ("AFLEVERBON", "Papieren afleverbon retour", ServiceUnit.Piece),
        ("1EDEUR", "Plaatsen achter de eerste deur", ServiceUnit.Piece),
        ("STAPELEN", "Stapelen en destapelen", ServiceUnit.Piece),
        ("FOLIE", "Wikkelen in stretchfolie", ServiceUnit.Piece),
        ("RETOUR", "Retourzending", ServiceUnit.Piece),
        ("CMR", "Extra CMR-vrachtbrief", ServiceUnit.Piece),
        ("TERMINAL", "Terminal- en havenkosten", ServiceUnit.Piece),
    ];

    private enum ValuePattern
    {
        /// <summary>A new value on the first of every month for two years (the diesel index).</summary>
        Monthly,

        /// <summary>Last year's value and this year's indexed value.</summary>
        Indexed,

        /// <summary>One value since <see cref="SurchargeTemplate.Since"/> (or three years back).</summary>
        Since
    }

    private sealed record SurchargeTemplate(
        string Code,
        string Name,
        DiscountOrSurchargeDirection Direction,
        DiscountOrSurchargeValueKind Kind,
        DiscountOrSurchargeSide Side,
        decimal BaseValue,
        ValuePattern Pattern,
        string LedgerAccount,
        DaysOfWeek Days = DaysOfWeek.All,
        KilometreBasis KilometreBasis = KilometreBasis.Distance,
        decimal BillableKilometres = 100m,
        bool RoundTrip = false,
        bool AfterOthers = false,
        RegionAppliesAt? Regional = null,
        DateOnly? Since = null,
        bool CustomerVariants = false);

    private static readonly SurchargeTemplate[] SurchargeTemplates =
    [
        new("DIESEL", "Dieseltoeslag", DiscountOrSurchargeDirection.Surcharge, DiscountOrSurchargeValueKind.Percentage, DiscountOrSurchargeSide.Revenue, 11.5m, ValuePattern.Monthly, "8050", CustomerVariants: true),
        new("DIESEL-INK", "Dieseltoeslag inkoop charter", DiscountOrSurchargeDirection.Surcharge, DiscountOrSurchargeValueKind.Percentage, DiscountOrSurchargeSide.Cost, 10m, ValuePattern.Monthly, "7050"),
        new("TOL-NL", "Vrachtwagenheffing Nederland", DiscountOrSurchargeDirection.Surcharge, DiscountOrSurchargeValueKind.PerKilometre, DiscountOrSurchargeSide.Both, 0.17m, ValuePattern.Since, "8060", KilometreBasis: KilometreBasis.TollDistance, Since: new DateOnly(2026, 7, 1)),
        new("MAUT-DE", "Maut Duitsland", DiscountOrSurchargeDirection.Surcharge, DiscountOrSurchargeValueKind.PerKilometre, DiscountOrSurchargeSide.Both, 0.348m, ValuePattern.Since, "8061", KilometreBasis: KilometreBasis.TollDistance, Since: new DateOnly(2023, 12, 1)),
        new("TOL-BE", "Kilometerheffing België", DiscountOrSurchargeDirection.Surcharge, DiscountOrSurchargeValueKind.PerKilometre, DiscountOrSurchargeSide.Both, 0.152m, ValuePattern.Indexed, "8062", KilometreBasis: KilometreBasis.TollDistance),
        new("LAADKLEP", "Toeslag laadklep", DiscountOrSurchargeDirection.Surcharge, DiscountOrSurchargeValueKind.FixedAmount, DiscountOrSurchargeSide.Revenue, 27.50m, ValuePattern.Indexed, "8070"),
        new("ADR", "ADR-toeslag", DiscountOrSurchargeDirection.Surcharge, DiscountOrSurchargeValueKind.FixedAmount, DiscountOrSurchargeSide.Revenue, 45m, ValuePattern.Indexed, "8071"),
        new("WACHTTIJD", "Wachttijdtoeslag", DiscountOrSurchargeDirection.Surcharge, DiscountOrSurchargeValueKind.FixedAmount, DiscountOrSurchargeSide.Revenue, 42.50m, ValuePattern.Indexed, "8072"),
        new("WEEKEND", "Weekendtoeslag", DiscountOrSurchargeDirection.Surcharge, DiscountOrSurchargeValueKind.Percentage, DiscountOrSurchargeSide.Revenue, 25m, ValuePattern.Since, "8073", Days: DaysOfWeek.Saturday | DaysOfWeek.Sunday),
        new("ZONDAG", "Zondagtoeslag", DiscountOrSurchargeDirection.Surcharge, DiscountOrSurchargeValueKind.Percentage, DiscountOrSurchargeSide.Both, 50m, ValuePattern.Since, "8074", Days: DaysOfWeek.Sunday),
        new("EILANDEN", "Toeslag Waddeneilanden", DiscountOrSurchargeDirection.Surcharge, DiscountOrSurchargeValueKind.FixedAmount, DiscountOrSurchargeSide.Revenue, 95m, ValuePattern.Indexed, "8075", Regional: RegionAppliesAt.Unloading),
        new("ZE-ZONE", "Toeslag zero-emissiezone", DiscountOrSurchargeDirection.Surcharge, DiscountOrSurchargeValueKind.FixedAmount, DiscountOrSurchargeSide.Revenue, 17.50m, ValuePattern.Indexed, "8076", Regional: RegionAppliesAt.Either),
        new("REGIO", "Regiotoeslag", DiscountOrSurchargeDirection.Surcharge, DiscountOrSurchargeValueKind.FixedAmount, DiscountOrSurchargeSide.Revenue, 22.50m, ValuePattern.Indexed, "8077", Regional: RegionAppliesAt.Either),
        new("CRISIS", "Crisistoeslag", DiscountOrSurchargeDirection.Surcharge, DiscountOrSurchargeValueKind.Percentage, DiscountOrSurchargeSide.Revenue, 3m, ValuePattern.Since, "8078", AfterOthers: true),
        new("ADMIN", "Administratiekosten", DiscountOrSurchargeDirection.Surcharge, DiscountOrSurchargeValueKind.FixedAmount, DiscountOrSurchargeSide.Revenue, 7.50m, ValuePattern.Indexed, "8079"),
        new("VOLKORT", "Volumekorting", DiscountOrSurchargeDirection.Discount, DiscountOrSurchargeValueKind.Percentage, DiscountOrSurchargeSide.Revenue, 5m, ValuePattern.Since, "8900", CustomerVariants: true),
        new("CONTRKORT", "Contractkorting", DiscountOrSurchargeDirection.Discount, DiscountOrSurchargeValueKind.Percentage, DiscountOrSurchargeSide.Revenue, 2.5m, ValuePattern.Since, "8901", CustomerVariants: true),
        new("LEEGKM", "Leegkilometers charter", DiscountOrSurchargeDirection.Surcharge, DiscountOrSurchargeValueKind.PerKilometre, DiscountOrSurchargeSide.Cost, 0.85m, ValuePattern.Indexed, "7060", BillableKilometres: 50m, RoundTrip: true),
        new("INKKORT", "Korting inkoop charter", DiscountOrSurchargeDirection.Discount, DiscountOrSurchargeValueKind.Percentage, DiscountOrSurchargeSide.Cost, 3m, ValuePattern.Since, "7900"),
    ];

    private enum ListKind
    {
        Pallet,
        Weight,
        Distance,
        FixedLane,
        Hourly
    }

    private sealed record ListSpec(Guid Id, ListKind Kind, RateSide Side, TestRelation? Owner, int ColumnCount, decimal PriceLevel, int RuleCount);

    private readonly record struct Lane(Guid? FromRelationId, Guid? FromRegionId, Guid? ToRelationId, Guid? ToRegionId, double? Kilometres);

    private readonly TestDatasetWriter _writer;
    private readonly IRepository<AdditionalService, Guid> _additionalServices;
    private readonly IRepository<DiscountOrSurcharge, Guid> _discountsOrSurcharges;
    private readonly IRepository<PriceList, Guid> _priceLists;
    private readonly IRepository<PriceListRule, Guid> _priceListRules;
    private readonly IRepository<RateLine, Guid> _rateLines;
    private readonly IGuidGenerator _guidGenerator;

    public TariffsTestDataGenerator(
        TestDatasetWriter writer,
        IRepository<AdditionalService, Guid> additionalServices,
        IRepository<DiscountOrSurcharge, Guid> discountsOrSurcharges,
        IRepository<PriceList, Guid> priceLists,
        IRepository<PriceListRule, Guid> priceListRules,
        IRepository<RateLine, Guid> rateLines,
        IGuidGenerator guidGenerator)
    {
        _writer = writer;
        _additionalServices = additionalServices;
        _discountsOrSurcharges = discountsOrSurcharges;
        _priceLists = priceLists;
        _priceListRules = priceListRules;
        _rateLines = rateLines;
        _guidGenerator = guidGenerator;
    }

    /// <summary>
    /// Deletes the Tariffs data, dependants first. Rate lines have no foreign key to anything (AD-20) and belong to the
    /// wiped orders and trips. Brackets and cells go with their rule, columns with their list, values and regions with
    /// their discount or surcharge (cascading keys). The relation settings that restrict-reference price lists and
    /// discounts or surcharges are wiped by the Invoicing part before this runs.
    /// </summary>
    public async Task WipeAsync()
    {
        await _writer.DeleteAllAsync(_rateLines);
        await _writer.DeleteAllAsync(_priceListRules);
        await _writer.DeleteAllAsync(_priceLists);
        await _writer.DeleteAllAsync(_discountsOrSurcharges);
        await _writer.DeleteAllAsync(_additionalServices);
    }

    public async Task<TariffsTestData> GenerateAsync(TestDatasetContext context, MasterDataTestDataset masterData)
    {
        var result = new TariffsTestData();
        var pools = new Pools(masterData);
        await GenerateAdditionalServicesAsync(context, pools, result);
        await GenerateDiscountsOrSurchargesAsync(context, pools, result);
        await GeneratePriceListsAsync(context, pools, result);
        return result;
    }

    private async Task GenerateAdditionalServicesAsync(TestDatasetContext context, Pools pools, TariffsTestData result)
    {
        var random = context.Random;
        var count = context.NextCount();
        var services = new List<AdditionalService>(count);
        for (var i = 0; i < count; i++)
        {
            var templateIndex = i % ServiceTemplates.Length;
            var template = ServiceTemplates[templateIndex];
            var variant = i >= ServiceTemplates.Length;

            /* Variants are the location-bound versions a carrier ends up with ("Wachttijd Venlo"). */
            var code = variant ? VariantCode(template.Code, i) : template.Code;
            var name = variant ? WithSuffix(template.Name, pools.RandomCity(random) ?? i.ToString()) : template.Name;
            var service = new AdditionalService(_guidGenerator.Create(), context.TenantId, code, name, template.Unit);
            service.SetLedgerAccounts((8400 + templateIndex).ToString(), (7400 + templateIndex).ToString());
            service.IsArchived = variant && random.NextDouble() < 0.04;
            services.Add(service);
            if (!service.IsArchived)
            {
                result.AdditionalServiceIds.Add(service.Id);
            }
        }

        await _writer.InsertAsync(_additionalServices, services);
    }

    private async Task GenerateDiscountsOrSurchargesAsync(TestDatasetContext context, Pools pools, TariffsTestData result)
    {
        var random = context.Random;
        var today = DateOnly.FromDateTime(context.Today);
        var variantTemplates = SurchargeTemplates.Where(t => t.CustomerVariants || t.Regional != null).ToArray();
        var count = Math.Max(context.NextCount(), SurchargeTemplates.Length);
        var items = new List<DiscountOrSurcharge>(count);

        for (var i = 0; i < count; i++)
        {
            var variant = i >= SurchargeTemplates.Length;
            var template = variant ? variantTemplates[random.Next(variantTemplates.Length)] : SurchargeTemplates[i];
            var baseValue = template.BaseValue;
            var code = template.Code;
            var name = template.Name;
            TestRelation? owner = null;

            if (variant)
            {
                code = VariantCode(template.Code, i);
                if (template.CustomerVariants && pools.Customers.Count > 0)
                {
                    /* A negotiated version for one customer: its own diesel percentage or discount. */
                    owner = pools.Customers[random.Next(pools.Customers.Count)];
                    name = WithSuffix(template.Name, owner.Name);
                    baseValue = template.Direction == DiscountOrSurchargeDirection.Discount
                        ? Round(Between(random, 1, 10), 1)
                        : baseValue * Between(random, 0.75, 1.25);
                }
                else
                {
                    name = WithSuffix(template.Name, pools.RandomCity(random) ?? i.ToString());
                    baseValue *= Between(random, 0.8, 1.3);
                }
            }

            var item = new DiscountOrSurcharge(
                _guidGenerator.Create(), context.TenantId, code, name, template.Direction, template.Kind, template.Side);
            item.SetDaysOfWeek(template.Days);
            item.SetLedgerAccount(template.LedgerAccount);
            item.SetKilometreRule(template.KilometreBasis, template.BillableKilometres, template.RoundTrip);
            item.AppliesAfterOthers = template.AfterOthers;
            item.SetValues(Values(template, baseValue, random, today), fixedOn: null, _guidGenerator.Create);
            if (template.Regional is { } appliesAt && pools.Regions.Count > 0)
            {
                var regions = pools.Regions
                    .OrderBy(_ => random.Next())
                    .Take(random.Next(1, Math.Min(6, pools.Regions.Count + 1)))
                    .Select(id => (id, appliesAt));
                item.SetRegions(regions, _guidGenerator.Create);
            }

            items.Add(item);
            result.DiscountOrSurchargeIds.Add(item.Id);
            if (!variant)
            {
                result.SurchargeIdsByCode[template.Code] = item.Id;
            }

            if (owner != null)
            {
                Bucket(result.SurchargesByRelation, owner.Id).Add(item.Id);
            }
        }

        await _writer.InsertAsync(_discountsOrSurcharges, items);
    }

    private static List<(DateOnly ValidFrom, decimal Value, string? CurrencyCode)> Values(
        SurchargeTemplate template, decimal baseValue, Random random, DateOnly today)
    {
        var currency = DiscountOrSurcharge.NeedsCurrency(template.Kind) ? Currency : null;
        var scale = template.Kind == DiscountOrSurchargeValueKind.PerKilometre ? 4 : 2;
        var rows = new List<(DateOnly, decimal, string?)>();
        switch (template.Pattern)
        {
            case ValuePattern.Monthly:
                var first = new DateOnly(today.Year, today.Month, 1).AddMonths(-23);
                var value = baseValue;
                for (var month = 0; month < 24; month++)
                {
                    value = Math.Clamp(value + Between(random, -0.6, 0.6), baseValue * 0.6m, baseValue * 1.4m);
                    rows.Add((first.AddMonths(month), Round(value, 2), currency));
                }

                break;
            case ValuePattern.Indexed:
                var lastYear = Round(baseValue, scale);
                rows.Add((new DateOnly(today.Year - 1, 1, 1), lastYear, currency));
                rows.Add((new DateOnly(today.Year, 1, 1), Round(lastYear * Between(random, 1.02, 1.05), scale), currency));
                break;
            default:
                rows.Add((template.Since ?? new DateOnly(today.Year - 3, 1, 1), Round(baseValue, scale), currency));
                break;
        }

        return rows;
    }

    private async Task GeneratePriceListsAsync(TestDatasetContext context, Pools pools, TariffsTestData result)
    {
        var random = context.Random;
        var today = DateOnly.FromDateTime(context.Today);
        var thisYear = new DateOnly(today.Year, 1, 1);
        var total = context.NextCount();
        var specs = new List<ListSpec>(total);
        var lists = new List<PriceList>(total);

        /* A standard list holds the region-to-region matrix: as many lanes as the regions allow, within the profile. */
        var generalRules = pools.Regions.Count > 0
            ? (int)Math.Min((long)pools.Regions.Count * pools.Regions.Count, context.Profile.MaxPerEntity)
            : context.Profile.MaxPerEntity / 2;

        void Add(ListKind kind, RateSide side, TestRelation? owner, DateOnly validFrom, DateOnly? validTill, bool archived, int ruleCount)
        {
            var code = $"PL{lists.Count + 1:000000}";
            var kindName = KindName(kind, side);
            var name = owner == null
                ? $"Standaard {char.ToLowerInvariant(kindName[0])}{kindName[1..]} {validFrom.Year}"
                : $"{kindName} {owner.Name} {validFrom.Year}";
            var list = new PriceList(_guidGenerator.Create(), context.TenantId, code, Truncate(name), side, Currency, BasisOf(kind), validFrom);
            list.SetMultiplier(kind == ListKind.FixedLane ? PriceMultiplier.PerLine : PriceMultiplier.PerUnit);
            list.SetColumnQuantity(kind switch
            {
                ListKind.Pallet => ColumnQuantitySource.SumOfLaneLines,
                ListKind.Weight => ColumnQuantitySource.LaneWeightKg,
                _ => ColumnQuantitySource.LineBasis
            });
            list.SetStopMatching(StopMatching.Both);
            decimal?[] columns = kind switch
            {
                ListKind.Pallet => PalletColumns,
                ListKind.Weight => WeightColumns,
                _ => [null]
            };
            list.SetColumns(columns);
            list.SetValidity(validFrom, validTill);
            list.IsArchived = archived;
            if (owner == null)
            {
                list.SetDescription("Algemeen tarief voor relaties zonder eigen prijsafspraak.");
            }

            lists.Add(list);
            var level = (side == RateSide.Cost ? CostLevel : 1m) * Between(random, 0.85, 1.2);
            specs.Add(new ListSpec(list.Id, kind, side, owner, columns.Length, level, ruleCount));

            if (archived)
            {
                return;
            }

            result.PriceListIds.Add(list.Id);
            if (validTill != null && validTill.Value < today)
            {
                return;
            }

            var entry = new TestPriceList(list.Id, list.Basis, validFrom);
            if (owner == null)
            {
                (side == RateSide.Revenue ? result.GeneralRevenueLists : result.GeneralCostLists).Add(entry);
            }
            else
            {
                Bucket(side == RateSide.Revenue ? result.RevenueListsByRelation : result.CostListsByRelation, owner.Id).Add(entry);
            }
        }

        foreach (var kind in new[] { ListKind.Pallet, ListKind.Weight, ListKind.Distance, ListKind.Hourly })
        {
            Add(kind, RateSide.Revenue, null, thisYear, null, false, generalRules);
        }

        if (pools.Carriers.Count > 0)
        {
            Add(ListKind.Distance, RateSide.Cost, null, thisYear, null, false, generalRules);
            Add(ListKind.FixedLane, RateSide.Cost, null, thisYear, null, false, generalRules);
        }

        var customers = pools.Customers.ToArray();
        var carriers = pools.Carriers.ToArray();
        random.Shuffle(customers);
        random.Shuffle(carriers);
        int nextCustomer = 0, nextCarrier = 0;

        while (lists.Count < total)
        {
            var cost = carriers.Length > 0 && (customers.Length == 0 || random.NextDouble() < 0.3);
            TestRelation? owner = cost
                ? carriers[nextCarrier++ % carriers.Length]
                : customers.Length > 0 ? customers[nextCustomer++ % customers.Length] : null;
            var kind = cost ? PickCostKind(random) : PickRevenueKind(random);

            /* Mostly this year's agreement; some expired ones (half of them archived) and next year's prepared tariff. */
            var roll = random.NextDouble();
            if (roll < 0.12)
            {
                Add(kind, cost ? RateSide.Cost : RateSide.Revenue, owner, thisYear.AddYears(-1), thisYear.AddDays(-1), random.NextDouble() < 0.5, random.Next(3, 25));
            }
            else if (roll < 0.20)
            {
                Add(kind, cost ? RateSide.Cost : RateSide.Revenue, owner, thisYear.AddYears(1), null, false, random.Next(3, 25));
            }
            else
            {
                Add(kind, cost ? RateSide.Cost : RateSide.Revenue, owner, thisYear, null, false, random.Next(3, 25));
            }
        }

        await _writer.InsertAsync(_priceLists, lists);
        result.PriceListCount = lists.Count;

        /* Streamed: rules are built per batch, never all in memory (the large profile writes tens of thousands). */
        result.RuleCount = await _writer.InsertAsync(
            _priceListRules, specs.SelectMany(spec => Rules(spec, context, pools)), batchSize: 250);
    }

    private IEnumerable<PriceListRule> Rules(ListSpec spec, TestDatasetContext context, Pools pools)
    {
        var random = context.Random;
        var used = new HashSet<(Guid?, Guid?, Guid?, Guid?, Guid?, Guid?, Guid?)>();
        var position = 0;
        for (var attempt = 0; attempt < spec.RuleCount * 3 && position < spec.RuleCount; attempt++)
        {
            var lane = NextLane(spec, random, pools);
            Guid? transportUnit = null, product = null, vehicleType = null;
            switch (spec.Kind)
            {
                case ListKind.Pallet when random.NextDouble() < 0.4:
                    transportUnit = pools.Pick(pools.TransportUnits, random);
                    break;
                case ListKind.Weight when random.NextDouble() < 0.2:
                    product = pools.Pick(pools.Products, random);
                    break;
                case ListKind.Distance or ListKind.Hourly when random.NextDouble() < 0.85:
                case ListKind.FixedLane when random.NextDouble() < 0.3:
                    vehicleType = pools.Pick(pools.VehicleTypes, random);
                    break;
            }

            if (!used.Add((lane.FromRelationId, lane.FromRegionId, lane.ToRelationId, lane.ToRegionId, transportUnit, product, vehicleType)))
            {
                continue;
            }

            position++;
            var rule = new PriceListRule(_guidGenerator.Create(), context.TenantId, spec.Id, position);
            rule.SetLane(lane.FromRelationId, lane.FromRegionId, lane.ToRelationId, lane.ToRegionId);
            rule.SetDimensions(transportUnit, product, vehicleType);

            var level = spec.PriceLevel;
            var rows = new List<(decimal? UpperBound, IReadOnlyList<decimal?> UnitPrices)>();
            decimal? minimum = null;
            switch (spec.Kind)
            {
                case ListKind.Pallet:
                    var perPallet = Between(random, 38, 145) * level;
                    rule.SetRowQuantity(QuantitySource.Manual);
                    rows.Add((null, PalletDegression.Select(d => (decimal?)Round(perPallet * d, 2)).ToList()));
                    minimum = Round(perPallet * 1.15m, 2);
                    break;
                case ListKind.Weight:
                    var perKg = Between(random, 0.42, 0.95) * level;
                    rule.SetRowQuantity(QuantitySource.Manual);
                    rows.Add((null, WeightDegression.Select(d => (decimal?)Round(perKg * d, 4)).ToList()));
                    minimum = Round(Between(random, 32, 65) * level, 2);
                    break;
                case ListKind.Distance:
                    var perKm = Between(random, 1.65, 2.45) * level;
                    rule.SetRowQuantity(QuantitySource.DistanceKm);
                    for (var i = 0; i < DistanceBrackets.Length; i++)
                    {
                        rows.Add((DistanceBrackets[i], [Round(perKm * DistanceDegression[i], 2)]));
                    }

                    minimum = Round(Between(random, 95, 185) * level, 2);
                    break;
                case ListKind.FixedLane:
                    var kilometres = (decimal)(lane.Kilometres ?? 30 + random.NextDouble() * 370);
                    rule.SetRowQuantity(QuantitySource.Manual);
                    rows.Add((null, [Round((85m + kilometres * 1.45m) * level * Between(random, 0.9, 1.1), 2)]));
                    break;
                case ListKind.Hourly:
                    var perHour = Between(random, 62, 92) * level;
                    rule.SetRowQuantity(QuantitySource.DurationH);
                    for (var i = 0; i < HourBrackets.Length; i++)
                    {
                        rows.Add((HourBrackets[i], [Round(perHour * HourDegression[i], 2)]));
                    }

                    minimum = Round(perHour * 4, 2);
                    break;
            }

            rule.SetMinMax(minimum, null);
            rule.SetBrackets(rows, spec.ColumnCount, _guidGenerator.Create);
            yield return rule;
        }
    }

    /// <summary>
    /// A customer's own list prices from its site (the from relation) to regions or receivers; carrier and standard lists
    /// price region to region. Fixed lane prices run relation to relation, priced by the straight-line distance.
    /// </summary>
    private static Lane NextLane(ListSpec spec, Random random, Pools pools)
    {
        var ownSite = spec.Side == RateSide.Revenue ? spec.Owner : null;
        var region = pools.Regions.Count > 0;
        switch (spec.Kind)
        {
            case ListKind.FixedLane:
                var from = ownSite ?? pools.AnyRelation(random);
                var to = pools.AnyRelation(random);
                if (to.Id == from.Id)
                {
                    to = pools.AnyRelation(random);
                }

                return new Lane(from.Id, null, to.Id, null, RoadKilometres(from, to));
            case ListKind.Hourly:
                return ownSite != null
                    ? new Lane(ownSite.Id, null, null, null, null)
                    : region
                        ? new Lane(null, pools.Pick(pools.Regions, random), null, null, null)
                        : new Lane(pools.AnyRelation(random).Id, null, null, null, null);
            default:
                if (ownSite != null)
                {
                    return region
                        ? new Lane(ownSite.Id, null, null, pools.Pick(pools.Regions, random), null)
                        : new Lane(ownSite.Id, null, pools.AnyRelation(random).Id, null, null);
                }

                return region
                    ? new Lane(null, pools.Pick(pools.Regions, random), null, pools.Pick(pools.Regions, random), null)
                    : new Lane(null, null, pools.AnyRelation(random).Id, null, null);
        }
    }

    private static double? RoadKilometres(TestRelation from, TestRelation to)
    {
        if (from.Addresses.Count == 0 || to.Addresses.Count == 0)
        {
            return null;
        }

        var a = from.Addresses[0];
        var b = to.Addresses[0];
        const double earthRadiusKm = 6371;
        var dLat = (b.Latitude - a.Latitude) * Math.PI / 180;
        var dLon = (b.Longitude - a.Longitude) * Math.PI / 180;
        var h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                + Math.Cos(a.Latitude * Math.PI / 180) * Math.Cos(b.Latitude * Math.PI / 180) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var straight = 2 * earthRadiusKm * Math.Asin(Math.Min(1, Math.Sqrt(h)));
        return Math.Max(10, straight * 1.25); // road distance is about a quarter longer than the straight line
    }

    private static ListKind PickRevenueKind(Random random) => random.Next(100) switch
    {
        < 35 => ListKind.Pallet,
        < 55 => ListKind.Weight,
        < 70 => ListKind.Distance,
        < 90 => ListKind.FixedLane,
        _ => ListKind.Hourly
    };

    private static ListKind PickCostKind(Random random) => random.Next(100) switch
    {
        < 40 => ListKind.Distance,
        < 85 => ListKind.FixedLane,
        _ => ListKind.Hourly
    };

    private static QuantitySource BasisOf(ListKind kind) => kind switch
    {
        ListKind.Pallet => QuantitySource.LineQuantity,
        ListKind.Weight => QuantitySource.WeightKg,
        ListKind.Distance => QuantitySource.DistanceKm,
        ListKind.FixedLane => QuantitySource.PerLine,
        _ => QuantitySource.DurationH
    };

    private static string KindName(ListKind kind, RateSide side)
    {
        var name = kind switch
        {
            ListKind.Pallet => "Pallettarief",
            ListKind.Weight => "Groupagetarief per kg",
            ListKind.Distance => "Kilometertarief",
            ListKind.FixedLane => "Vaste ritprijzen",
            _ => "Uurtarief"
        };
        return side == RateSide.Cost ? $"Inkoop {char.ToLowerInvariant(name[0])}{name[1..]}" : name;
    }

    /// <summary>A unique variant code within the 16 characters: the template code, a dash and the running number.</summary>
    private static string VariantCode(string templateCode, int index)
        => $"{(templateCode.Length > 9 ? templateCode[..9] : templateCode)}-{index:000000}";

    private static string WithSuffix(string name, string suffix) => Truncate($"{name} {suffix}");

    private static string Truncate(string name)
        => name.Length <= ReferenceDataConsts.MaxNameLength ? name : name[..ReferenceDataConsts.MaxNameLength].TrimEnd();

    private static decimal Between(Random random, double min, double max) => (decimal)(min + random.NextDouble() * (max - min));

    private static decimal Round(decimal value, int decimals) => Math.Round(value, decimals, MidpointRounding.AwayFromZero);

    private static List<TValue> Bucket<TValue>(Dictionary<Guid, List<TValue>> buckets, Guid key)
    {
        if (!buckets.TryGetValue(key, out var bucket))
        {
            buckets[key] = bucket = new List<TValue>();
        }

        return bucket;
    }

    /// <summary>The Master Data ids the tariffs draw from.</summary>
    private sealed class Pools
    {
        public Pools(MasterDataTestDataset masterData)
        {
            All = masterData.Relations;
            Customers = masterData.Relations.Where(r => r.Roles.HasFlag(RelationRoles.Customer)).ToList();
            Carriers = masterData.Relations.Where(r => r.Roles.HasFlag(RelationRoles.Carrier)).ToList();
            Regions = masterData.GeographicalRegionIds;
            TransportUnits = masterData.TransportUnitIds;
            Products = masterData.ProductIds;
            VehicleTypes = masterData.VehicleTypeIds;
            if (All.Count == 0)
            {
                throw new InvalidOperationException("Master Data published no relations; Financial cannot build tariffs.");
            }
        }

        public IReadOnlyList<TestRelation> All { get; }

        public IReadOnlyList<TestRelation> Customers { get; }

        public IReadOnlyList<TestRelation> Carriers { get; }

        public IReadOnlyList<Guid> Regions { get; }

        public IReadOnlyList<Guid> TransportUnits { get; }

        public IReadOnlyList<Guid> Products { get; }

        public IReadOnlyList<Guid> VehicleTypes { get; }

        public TestRelation AnyRelation(Random random) => All[random.Next(All.Count)];

        public Guid? Pick(IReadOnlyList<Guid> ids, Random random) => ids.Count == 0 ? null : ids[random.Next(ids.Count)];

        public string? RandomCity(Random random)
        {
            var relation = AnyRelation(random);
            return relation.Addresses.Count > 0 ? relation.Addresses[0].City : null;
        }
    }
}
