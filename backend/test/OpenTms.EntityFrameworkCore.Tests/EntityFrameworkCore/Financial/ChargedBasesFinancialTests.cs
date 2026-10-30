using System;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Financial;
using OpenTms.Financial.Invoicing;
using OpenTms.Financial.Published;
using OpenTms.Financial.Tariffs;
using OpenTms.MasterData.Relations;
using OpenTms.Platform.ReferenceData;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace OpenTms.EntityFrameworkCore.Financial;

/// <summary>
/// Design 61 "Order page review" point 3 with build notes 1 and 5, Financial's side on SQLite: the relation settings'
/// <c>revenueChargedBases</c> (upsert, read in the legacy order, validation with the field, the published reference that
/// Orders copies from), each relation price list's <c>basis</c>, the price-list lookup's <c>basis</c> filter, and
/// <c>DELETE rate-line</c> accepting an open line (adjusted or not) while refusing every other calculated line; tenant
/// isolation of the settings, the published lookup, the price-list lookup and the open line. Risk class: Critical (money,
/// tenant isolation).
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class ChargedBasesFinancialTests : FinancialAppServiceTestBase
{
    private readonly IRelationFinancialSettingsAppService _settings;
    private readonly IPriceListAppService _priceLists;
    private readonly IRateLineAppService _rateLines;
    private readonly IRepository<RateLine, Guid> _lines;

    public ChargedBasesFinancialTests()
    {
        _settings = GetRequiredService<IRelationFinancialSettingsAppService>();
        _priceLists = GetRequiredService<IPriceListAppService>();
        _rateLines = GetRequiredService<IRateLineAppService>();
        _lines = GetRequiredService<IRepository<RateLine, Guid>>();
    }

    private async Task<Guid> RelationAsync(string code)
        => (await GetRequiredService<IRelationAppService>().CreateAsync(new RelationCreateDto { Code = code, Name = code + " B.V.", Roles = RelationRoles.Customer })).Id;

    private async Task<Guid> ListAsync(string code, RateSide side, QuantitySource basis)
        => (await _priceLists.CreateAsync(new PriceListCreateDto { Code = code, Name = code, Side = side, Basis = basis, CurrencyCode = "EUR", ValidFrom = new DateOnly(2026, 1, 1) })).Id;

    [Fact]
    public async Task The_settings_store_the_revenue_charged_bases_read_them_in_the_legacy_order_and_carry_each_lists_basis()
    {
        using var _ = InTenantA();
        await CurrencyAsync("EUR");
        var acme = await RelationAsync("ACME");
        var byWeight = await ListAsync("WGT", RateSide.Revenue, QuantitySource.WeightKg);
        var byQuantity = await ListAsync("QTY", RateSide.Revenue, QuantitySource.LineQuantity);
        var cost = await ListAsync("CST", RateSide.Cost, QuantitySource.DistanceKm);

        var created = await _settings.UpsertByRelationAsync(acme, new RelationFinancialSettingsUpsertDto
        {
            RevenuePriceListIds = [byWeight, byQuantity],
            CostPriceListIds = [cost],
            RevenueChargedBases = [QuantitySource.TollDistanceKm, QuantitySource.DistanceKm, QuantitySource.Colli],
            CurrencyCode = "EUR"
        });

        created.RevenueChargedBases.ShouldBe([QuantitySource.Colli, QuantitySource.DistanceKm, QuantitySource.TollDistanceKm]);
        created.RevenuePriceLists.Select(p => (p.Code, p.Basis)).ShouldBe([("WGT", (QuantitySource?)QuantitySource.WeightKg), ("QTY", QuantitySource.LineQuantity)]);
        created.CostPriceLists.Single().Basis.ShouldBe(QuantitySource.DistanceKm);
        (await _settings.GetByRelationAsync(acme))!.RevenueChargedBases.ShouldBe([QuantitySource.Colli, QuantitySource.DistanceKm, QuantitySource.TollDistanceKm]);

        /* The published reference Orders copies into a new order's terms. */
        var lookup = GetRequiredService<IFinancialReferenceLookup>();
        var reference = (await lookup.GetRelationFinancialSettingsAsync(acme, Today)).ShouldNotBeNull();
        reference.RevenueChargedBases.ShouldBe([QuantitySource.Colli, QuantitySource.DistanceKm, QuantitySource.TollDistanceKm]);
        reference.RevenuePriceListIds.ShouldBe([byWeight, byQuantity]);
        (await lookup.GetPriceListsAsync([byWeight, byQuantity, cost])).Select(p => (p.Id, p.Basis))
            .ShouldBe([(byWeight, QuantitySource.WeightKg), (byQuantity, QuantitySource.LineQuantity), (cost, QuantitySource.DistanceKm)], ignoreOrder: true);

        /* An update replaces the bases; none clears them. */
        var updated = await _settings.UpsertByRelationAsync(acme, new RelationFinancialSettingsUpsertDto
        {
            RevenuePriceListIds = [byWeight], RevenueChargedBases = [QuantitySource.PerLine], CurrencyCode = "EUR", ConcurrencyStamp = created.ConcurrencyStamp
        });
        updated.RevenueChargedBases.ShouldBe([QuantitySource.PerLine]);
        var cleared = await _settings.UpsertByRelationAsync(acme, new RelationFinancialSettingsUpsertDto { RevenuePriceListIds = [byWeight], CurrencyCode = "EUR", ConcurrencyStamp = updated.ConcurrencyStamp });
        cleared.RevenueChargedBases.ShouldBeEmpty();
        (await lookup.GetRelationFinancialSettingsAsync(acme, Today))!.RevenueChargedBases.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(QuantitySource.Manual, ReferenceDataErrorCodes.InvalidCode)]
    [InlineData(QuantitySource.TripDistanceKm, ReferenceDataErrorCodes.InvalidCode)]
    [InlineData(QuantitySource.WeightKg, ReferenceDataErrorCodes.DuplicateKey)]
    public async Task The_upsert_refuses_a_basis_that_cannot_be_charged_or_a_repeat_and_writes_nothing(QuantitySource extra, string code)
    {
        using var _ = InTenantA();
        await CurrencyAsync("EUR");
        var acme = await RelationAsync("ACME" + (int)extra);

        var refusal = await Should.ThrowAsync<BusinessException>(() => _settings.UpsertByRelationAsync(acme, new RelationFinancialSettingsUpsertDto
        {
            RevenueChargedBases = [QuantitySource.WeightKg, extra], CurrencyCode = "EUR"
        }));

        refusal.Code.ShouldBe(code);
        refusal.Data["field"].ShouldBe("revenueChargedBases");
        (await _settings.GetByRelationAsync(acme)).ShouldBeNull();
    }

    [Fact]
    public async Task The_price_list_lookup_narrows_to_one_basis_and_side()
    {
        using var _ = InTenantA();
        await CurrencyAsync("EUR");
        var weight = await ListAsync("W1", RateSide.Revenue, QuantitySource.WeightKg);
        var weight2 = await ListAsync("W2", RateSide.Revenue, QuantitySource.WeightKg);
        var quantity = await ListAsync("Q1", RateSide.Revenue, QuantitySource.LineQuantity);
        var costWeight = await ListAsync("CW", RateSide.Cost, QuantitySource.WeightKg);

        (await _priceLists.GetLookupAsync(RateSide.Revenue, basis: QuantitySource.WeightKg)).Select(l => l.Id).ShouldBe([weight, weight2], ignoreOrder: true);
        (await _priceLists.GetLookupAsync(basis: QuantitySource.WeightKg)).Select(l => l.Id).ShouldBe([weight, weight2, costWeight], ignoreOrder: true);
        (await _priceLists.GetLookupAsync(RateSide.Revenue, basis: QuantitySource.DurationH)).ShouldBeEmpty();
        (await _priceLists.GetLookupAsync(RateSide.Revenue)).Select(l => l.Id).ShouldBe([weight, weight2, quantity], ignoreOrder: true, "without a basis every list of the side");
        (await _priceLists.GetLookupAsync(RateSide.Revenue, "W2", basis: QuantitySource.WeightKg)).ShouldHaveSingleItem().Id.ShouldBe(weight2);
    }

    // ── removing an open line (build note 5) ───────────────────────────────────

    private async Task<(Guid OrderId, RateLine Open, RateLine AdjustedOpen, RateLine ListLine, RateLine CostWithoutList)> SeedOrderAsync()
    {
        var orderId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        RateLine OpenLine(QuantitySource basis)
            => RateLine.Calculated(Guid.NewGuid(), TenantA, RateSide.Revenue, RateLineAnchor.OrderLine(orderId, lineId), RateLineKind.PriceList, "EUR", "Financial:RateLine.OpenBasis", 500m, basis, 0m, 0m);

        var open = OpenLine(QuantitySource.WeightKg);
        var adjusted = OpenLine(QuantitySource.DistanceKm);
        adjusted.AdjustUnitPrice(1.25m);
        var listLine = RateLine.Calculated(Guid.NewGuid(), TenantA, RateSide.Revenue, RateLineAnchor.OrderLine(orderId, lineId), RateLineKind.PriceList, "EUR", "Financial:RateLine.PriceList", 2m, QuantitySource.LineQuantity, 10m, 20m);
        listLine.SetProvenance(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1);
        var tripId = Guid.NewGuid();
        var costWithoutList = RateLine.Calculated(Guid.NewGuid(), TenantA, RateSide.Cost, RateLineAnchor.Leg(tripId, Guid.NewGuid()), RateLineKind.PriceList, "EUR", "Financial:RateLine.PriceList", 1m, QuantitySource.LineQuantity, 0m, 0m);
        await _lines.InsertManyAsync([open, adjusted, listLine, costWithoutList], autoSave: true);
        return (orderId, open, adjusted, listLine, costWithoutList);
    }

    [Fact]
    public async Task An_open_line_adjusted_or_not_can_be_removed_and_requests_a_recalculation_while_other_calculated_lines_stay_refused()
    {
        using var _ = InTenantA();
        await CurrencyAsync("EUR");
        var (orderId, open, adjusted, listLine, costWithoutList) = await SeedOrderAsync();
        var requests = GetRequiredService<OpenTms.TestDoubles.RecordedDerivedValueRequests>();
        var before = requests.For(OpenTms.Financial.Tariffs.Calculators.OrderRateLineCalculator.TargetType, orderId).Count;

        /* The read marks an open line: kind PriceList, no list, not adjusted. */
        var read = await _rateLines.GetAsync(new RateLineGetRequestDto { OrderId = orderId });
        var openDto = read.Lines.Single(l => l.Id == open.Id);
        (openDto.Kind, openDto.PriceListId, openDto.IsAdjusted, openDto.QuantitySource, openDto.DescriptionKey).ShouldBe((RateLineKind.PriceList, (Guid?)null, false, QuantitySource.WeightKg, "Financial:RateLine.OpenBasis"));

        await _rateLines.DeleteAsync(open.Id);
        await _rateLines.DeleteAsync(adjusted.Id);

        (await _rateLines.GetAsync(new RateLineGetRequestDto { OrderId = orderId })).Lines.Select(l => l.Id).ShouldBe([listLine.Id]);
        requests.For(OpenTms.Financial.Tariffs.Calculators.OrderRateLineCalculator.TargetType, orderId).Count.ShouldBe(before + 2, "each removal asks for a new calculation (the open line returns while its basis is charged)");

        (await Should.ThrowAsync<BusinessException>(() => _rateLines.DeleteAsync(listLine.Id))).Code.ShouldBe(FinancialErrorCodes.CalculatedLineReadOnly);
        (await Should.ThrowAsync<BusinessException>(() => _rateLines.DeleteAsync(costWithoutList.Id))).Code.ShouldBe(FinancialErrorCodes.CalculatedLineReadOnly, "an open line is a revenue line");
    }

    [Fact]
    public async Task An_open_line_can_be_priced_with_Adjust_unit_price()
    {
        using var _ = InTenantA();
        await CurrencyAsync("EUR");
        var (orderId, open, _, _, _) = await SeedOrderAsync();

        var priced = await _rateLines.UpdateAsync(open.Id, new RateLineUpdateDto { UnitPrice = 0.08m, ConcurrencyStamp = open.ConcurrencyStamp });

        (priced.IsAdjusted, priced.UnitPrice, priced.Quantity, priced.Amount, priced.PriceListId).ShouldBe((true, 0.08m, 500m, 40m, (Guid?)null));
    }

    [Fact]
    public async Task Another_tenant_sees_neither_the_settings_nor_the_lists_nor_the_open_line()
    {
        Guid acme;
        Guid weight;
        Guid openId;
        Guid orderId;
        using (InTenantA())
        {
            await CurrencyAsync("EUR");
            acme = await RelationAsync("ACME");
            weight = await ListAsync("WGT", RateSide.Revenue, QuantitySource.WeightKg);
            await _settings.UpsertByRelationAsync(acme, new RelationFinancialSettingsUpsertDto { RevenuePriceListIds = [weight], RevenueChargedBases = [QuantitySource.Colli], CurrencyCode = "EUR" });
            var seeded = await SeedOrderAsync();
            openId = seeded.Open.Id;
            orderId = seeded.OrderId;
        }

        using (InTenantB())
        {
            (await _settings.GetByRelationAsync(acme)).ShouldBeNull();
            (await GetRequiredService<IFinancialReferenceLookup>().GetRelationFinancialSettingsAsync(acme, Today)).ShouldBeNull();
            (await GetRequiredService<IFinancialReferenceLookup>().GetPriceListsAsync([weight])).ShouldBeEmpty();
            (await _priceLists.GetLookupAsync(RateSide.Revenue, basis: QuantitySource.WeightKg)).ShouldBeEmpty();
            (await _rateLines.GetAsync(new RateLineGetRequestDto { OrderId = orderId })).Lines.ShouldBeEmpty();
            await Should.ThrowAsync<EntityNotFoundException>(() => _rateLines.DeleteAsync(openId));
            (await Should.ThrowAsync<BusinessException>(() => _settings.UpsertByRelationAsync(acme, new RelationFinancialSettingsUpsertDto { RevenueChargedBases = [QuantitySource.WeightKg], CurrencyCode = "EUR" })))
                .Code.ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound, "tenant A's relation does not exist for tenant B");
        }

        using (InTenantA())
        {
            (await _settings.GetByRelationAsync(acme))!.RevenueChargedBases.ShouldBe([QuantitySource.Colli]);
            (await _rateLines.GetAsync(new RateLineGetRequestDto { OrderId = orderId })).Lines.ShouldContain(l => l.Id == openId);
        }
    }
}
