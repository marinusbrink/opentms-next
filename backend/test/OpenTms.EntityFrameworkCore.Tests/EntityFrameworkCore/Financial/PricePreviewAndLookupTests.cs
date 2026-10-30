using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Financial;
using OpenTms.Financial.Invoicing;
using OpenTms.Financial.Published;
using OpenTms.Financial.Tariffs;
using OpenTms.Financial.Tariffs.Published;
using OpenTms.MasterData.Relations;
using OpenTms.Platform.ReferenceData;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace OpenTms.EntityFrameworkCore.Financial;

/// <summary>
/// The two published, in-process services of Financial on SQLite (design 56 "The three flows, applied"): the price
/// preview over an unsaved snapshot (pure, the same resolver as the calculator) and the reference lookup Orders validates
/// its financial terms with. Risk class: Critical (tenant isolation, money).
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class PricePreviewAndLookupTests : FinancialAppServiceTestBase
{
    private readonly IPricePreviewService _preview;
    private readonly IFinancialReferenceLookup _lookup;
    private readonly IPriceListAppService _priceLists;
    private readonly IRelationAppService _relations;

    public PricePreviewAndLookupTests()
    {
        _preview = GetRequiredService<IPricePreviewService>();
        _lookup = GetRequiredService<IFinancialReferenceLookup>();
        _priceLists = GetRequiredService<IPriceListAppService>();
        _relations = GetRequiredService<IRelationAppService>();
    }

    private async Task<Guid> RelationAsync(string code)
        => (await _relations.CreateAsync(new RelationCreateDto { Code = code, Name = code, Roles = RelationRoles.Customer })).Id;

    private async Task<(Guid ListId, Guid Ams, Guid Bru, Guid Fuel)> SeedAsync(string currency = "EUR")
    {
        await CurrencyAsync("EUR");
        if (currency != "EUR")
        {
            await CurrencyAsync(currency);
        }

        var ams = await RelationAsync("AMS");
        var bru = await RelationAsync("BRU");
        var list = await _priceLists.CreateAsync(new PriceListCreateDto { Code = "NLBE-" + currency, Name = "NL-BE", CurrencyCode = currency, ValidFrom = new DateOnly(2026, 1, 1), Columns = [5m, null] });
        await _priceLists.CreateRuleAsync(list.Id, new PriceListRuleInputDto
        {
            FromRelationId = ams,
            ToRelationId = bru,
            Brackets = [new PriceListBracketInputDto { UpperBound = null, UnitPrices = [100m, 90m] }]
        });
        var fuel = await GetRequiredService<IDiscountOrSurchargeAppService>().CreateAsync(new DiscountOrSurchargeCreateDto
        {
            Code = "FUEL",
            Name = "Fuel",
            ValueKind = DiscountOrSurchargeValueKind.Percentage,
            Values = [new DiscountOrSurchargeValueInputDto { ValidFrom = new DateOnly(2026, 1, 1), Value = 5m }]
        });
        return (list.Id, ams, bru, fuel.Id);
    }

    private static PricingSnapshotDto Snapshot(Guid listId, Guid ams, Guid bru, Guid? fuel, decimal quantity, string currency = "EUR", decimal? fixedPrice = null) => new()
    {
        CustomerRelationId = Guid.NewGuid(),
        OrderDate = new DateOnly(2026, 10, 26),
        CurrencyCode = currency,
        PriceListIds = [listId],
        DiscountOrSurchargeIds = fuel == null ? [] : [fuel.Value],
        FixedPriceAmount = fixedPrice,
        Lines =
        [
            new PricingLineDto
            {
                Position = 1,
                TransportUnitId = Guid.NewGuid(),
                Quantity = quantity,
                Stops =
                [
                    new PricingStopDto { Position = 1, RelationId = ams, Date = new DateOnly(2026, 10, 26) },
                    new PricingStopDto { Position = 2, RelationId = bru, Date = new DateOnly(2026, 10, 27) }
                ]
            }
        ]
    };

    [Fact]
    public async Task The_preview_prices_an_unsaved_order_with_its_surcharges_and_stores_nothing()
    {
        using var _ = InTenantA();
        var (listId, ams, bru, fuel) = await SeedAsync();

        var preview = await _preview.PreviewAsync(Snapshot(listId, ams, bru, fuel, quantity: 8m));

        preview.CurrencyCode.ShouldBe("EUR");
        preview.Lines.Select(l => (l.Kind, l.Quantity, l.UnitPrice, l.Amount)).ShouldBe([(RateLineKind.PriceList, 8m, 90m, 720m), (RateLineKind.DiscountOrSurcharge, 720m, 0.05m, 36m)]);
        preview.Lines[0].OrderLineId.ShouldBeNull("an unsaved line has no id");
        preview.Lines[0].PriceListId.ShouldBe(listId);
        preview.Lines[1].DiscountOrSurchargeId.ShouldBe(fuel);
        preview.LinesAmount.ShouldBe(720m);
        preview.DiscountsAndSurchargesAmount.ShouldBe(36m);
        preview.TotalAmount.ShouldBe(756m);
        preview.Warnings.ShouldBeEmpty();

        (await GetRequiredService<IRateLineAppService>().GetAsync(new RateLineGetRequestDto { OrderId = Guid.NewGuid() })).Lines.ShouldBeEmpty();
        (await GetRequiredService<Volo.Abp.Domain.Repositories.IRepository<RateLine, Guid>>().GetCountAsync()).ShouldBe(0, "a preview persists nothing");
    }

    [Fact]
    public async Task The_preview_reports_a_currency_mismatch_and_honours_a_fixed_price()
    {
        using var _ = InTenantA();
        var (listId, ams, bru, _) = await SeedAsync("USD");

        var mismatch = await _preview.PreviewAsync(Snapshot(listId, ams, bru, null, quantity: 2m));
        mismatch.Lines.ShouldBeEmpty();
        mismatch.Warnings.ShouldBe([FinancialErrorCodes.CurrencyMismatchWarning]);

        var fixedPrice = await _preview.PreviewAsync(Snapshot(listId, ams, bru, null, quantity: 2m, currency: "USD", fixedPrice: 500m));
        fixedPrice.Lines.Select(l => l.Kind).ShouldBe([RateLineKind.PriceList, RateLineKind.FixedPrice]);
        fixedPrice.TotalAmount.ShouldBe(500m);
        fixedPrice.LinesAmount.ShouldBe(500m);
    }

    [Fact]
    public async Task The_lookup_names_price_lists_discounts_and_services_of_the_tenant_only_and_bounds_its_input()
    {
        Guid listId;
        Guid fuel;
        Guid service;
        using (InTenantA())
        {
            var seeded = await SeedAsync();
            listId = seeded.ListId;
            fuel = seeded.Fuel;
            service = (await GetRequiredService<IAdditionalServiceAppService>().CreateAsync(new AdditionalServiceCreateDto { Code = "WAIT", Name = "Waiting time", Unit = ServiceUnit.Hour })).Id;

            var lists = await _lookup.GetPriceListsAsync([listId, Guid.NewGuid()]);
            var list = lists.ShouldHaveSingleItem();
            (list.Code, list.Side, list.CurrencyCode, list.IsArchived).ShouldBe(("NLBE-EUR", RateSide.Revenue, "EUR", false));
            (await _lookup.GetDiscountsOrSurchargesAsync([fuel])).ShouldHaveSingleItem().Name.ShouldBe("Fuel");
            (await _lookup.GetAdditionalServicesAsync([service])).ShouldHaveSingleItem().Code.ShouldBe("WAIT");
            (await _lookup.GetVatPercentagesAsync([Guid.NewGuid()])).ShouldBeEmpty();
            (await _lookup.GetPriceListsAsync([])).ShouldBeEmpty();
            (await Should.ThrowAsync<BusinessException>(() => _lookup.GetPriceListsAsync(Enumerable.Range(0, IFinancialReferenceLookup.MaxKeys + 1).Select(_ => Guid.NewGuid()).ToList())))
                .Code.ShouldBe(ReferenceDataErrorCodes.TooManyRows);
        }

        using (InTenantB())
        {
            (await _lookup.GetPriceListsAsync([listId])).ShouldBeEmpty();
            (await _lookup.GetDiscountsOrSurchargesAsync([fuel])).ShouldBeEmpty();
            (await _lookup.GetAdditionalServicesAsync([service])).ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task The_lookup_returns_the_relations_financial_defaults_with_the_surcharges_valid_on_the_date_for_the_tenant_only()
    {
        Guid customer;
        using (InTenantA())
        {
            var (listId, _, _, fuel) = await SeedAsync();
            customer = await RelationAsync("CUST");
            var winter = (await GetRequiredService<IDiscountOrSurchargeAppService>().CreateAsync(new DiscountOrSurchargeCreateDto
            {
                Code = "WINTER", Name = "Winter", ValueKind = DiscountOrSurchargeValueKind.Percentage, Values = [new DiscountOrSurchargeValueInputDto { ValidFrom = new DateOnly(2026, 1, 1), Value = 2m }]
            })).Id;
            await GetRequiredService<IRelationFinancialSettingsAppService>().UpsertByRelationAsync(customer, new RelationFinancialSettingsUpsertDto
            {
                RevenuePriceListIds = [listId], RevenueMultipleMatches = MultipleMatches.LowestWins, PaymentTermDays = 14, CurrencyCode = "EUR", ChargeFixedPriceByDefault = true, SendFreightDocumentsWithInvoice = true,
                DiscountsOrSurcharges =
                [
                    new RelationDiscountOrSurchargeInputDto { DiscountOrSurchargeId = fuel, ValidFrom = new DateOnly(2026, 1, 1) },
                    new RelationDiscountOrSurchargeInputDto { DiscountOrSurchargeId = winter, ValidFrom = new DateOnly(2026, 11, 1), ValidTill = new DateOnly(2027, 2, 28) }
                ]
            });

            var october = (await _lookup.GetRelationFinancialSettingsAsync(customer, new DateOnly(2026, 10, 26))).ShouldNotBeNull();
            (october.RelationId, october.RevenueMultipleMatches, october.PaymentTermDays, october.CurrencyCode, october.ChargeFixedPriceByDefault, october.SendFreightDocumentsWithInvoice)
                .ShouldBe((customer, MultipleMatches.LowestWins, 14, "EUR", true, true));
            october.RevenuePriceListIds.ShouldBe([listId]);
            october.VatPercentageId.ShouldBeNull();
            october.DiscountOrSurchargeIds.ShouldBe([fuel], "the winter surcharge is not valid yet");
            (await _lookup.GetRelationFinancialSettingsAsync(customer, new DateOnly(2026, 12, 15)))!.DiscountOrSurchargeIds.ShouldBe([fuel, winter], ignoreOrder: true);
            (await _lookup.GetRelationFinancialSettingsAsync(Guid.NewGuid(), new DateOnly(2026, 10, 26))).ShouldBeNull("a relation without settings has no defaults");
        }

        using (InTenantB())
        {
            (await _lookup.GetRelationFinancialSettingsAsync(customer, new DateOnly(2026, 10, 26))).ShouldBeNull();
        }
    }
}
