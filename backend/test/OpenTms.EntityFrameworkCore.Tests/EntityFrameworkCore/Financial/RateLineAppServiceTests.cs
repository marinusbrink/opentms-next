using System;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Financial;
using OpenTms.Financial.Tariffs;
using OpenTms.Platform.ReferenceData;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace OpenTms.EntityFrameworkCore.Financial;

/// <summary>
/// RateLineAppService on SQLite (design 57): the read with AD-08 totals and names, and every refusal that happens before
/// a recalculation is requested (the commands that succeed request a derived value, which needs a transaction: the
/// PostgreSQL tests cover them). Risk class: Critical (money read, tenant isolation).
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class RateLineAppServiceTests : FinancialAppServiceTestBase
{
    private readonly IRateLineAppService _service;
    private readonly IRepository<RateLine, Guid> _lines;

    public RateLineAppServiceTests()
    {
        _service = GetRequiredService<IRateLineAppService>();
        _lines = GetRequiredService<IRepository<RateLine, Guid>>();
    }

    private async Task<(Guid OrderId, Guid LineId, RateLine Calculated, RateLine Manual, Guid PriceListId)> SeedOrderAsync()
    {
        await CurrencyAsync("EUR");
        var list = await GetRequiredService<IPriceListAppService>().CreateAsync(new PriceListCreateDto { Code = "NLBE", Name = "NL-BE", CurrencyCode = "EUR", ValidFrom = new DateOnly(2026, 1, 1) });
        var orderId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var calculated = RateLine.Calculated(Guid.NewGuid(), TenantA, RateSide.Revenue, RateLineAnchor.OrderLine(orderId, lineId), RateLineKind.PriceList, "EUR", "Financial:RateLine.PriceList", 3m, QuantitySource.LineQuantity, 100m, 300m);
        calculated.SetProvenance(list.Id, Guid.NewGuid(), Guid.NewGuid(), 1);
        calculated.SetCalculation(DateTimeOffset.UtcNow, 1);
        calculated.Position = 1;
        var manual = RateLine.Manual(Guid.NewGuid(), TenantA, RateSide.Revenue, RateLineAnchor.Order(orderId), RateLineKind.Manual, "EUR", "Handling", 1m, QuantitySource.Manual, 25m, null);
        manual.Position = 2;
        var surcharge = RateLine.Calculated(Guid.NewGuid(), TenantA, RateSide.Revenue, RateLineAnchor.Order(orderId), RateLineKind.DiscountOrSurcharge, "EUR", "Financial:RateLine.DiscountOrSurcharge", 325m, QuantitySource.Manual, 0.05m, 16.25m);
        surcharge.Position = 3;
        await _lines.InsertManyAsync([calculated, manual, surcharge], autoSave: true);
        return (orderId, lineId, calculated, manual, list.Id);
    }

    [Fact]
    public async Task Get_returns_the_lines_in_position_order_with_the_totals_the_names_and_no_state_before_a_calculation()
    {
        using var _ = InTenantA();
        var (orderId, lineId, _, _, priceListId) = await SeedOrderAsync();

        var set = await _service.GetAsync(new RateLineGetRequestDto { OrderId = orderId });

        set.Lines.Select(l => l.Kind).ShouldBe([RateLineKind.PriceList, RateLineKind.Manual, RateLineKind.DiscountOrSurcharge]);
        set.Lines[0].PriceListCode.ShouldBe("NLBE");
        set.Lines[0].PriceListId.ShouldBe(priceListId);
        set.Lines[0].OrderLineId.ShouldBe(lineId);
        set.Lines[1].IsManual.ShouldBeTrue();
        set.Lines[1].Description.ShouldBe("Handling");
        set.CurrencyCode.ShouldBe("EUR");
        set.Totals.LinesAmount.ShouldBe(325m);
        set.Totals.DiscountsAndSurchargesAmount.ShouldBe(16.25m);
        set.Totals.Total.ShouldBe(341.25m);
        set.Totals.HasFixedPrice.ShouldBeFalse();
        set.Totals.ByAnchor.ShouldHaveSingleItem().AnchorId.ShouldBe(lineId);
        set.State.ShouldBeNull();

        var empty = await _service.GetAsync(new RateLineGetRequestDto { TripId = Guid.NewGuid() });
        empty.Lines.ShouldBeEmpty();
        empty.Totals.Total.ShouldBe(0m);
        empty.CurrencyCode.ShouldBeNull();
    }

    [Fact]
    public async Task The_refusals_of_the_commands_happen_before_anything_is_written()
    {
        using var _ = InTenantA();
        var (orderId, lineId, calculated, manual, _) = await SeedOrderAsync();

        (await Should.ThrowAsync<BusinessException>(() => _service.GetAsync(new RateLineGetRequestDto()))).Code.ShouldBe(FinancialErrorCodes.AnchorRequired);
        (await Should.ThrowAsync<BusinessException>(() => _service.GetAsync(new RateLineGetRequestDto { OrderId = orderId, TripId = Guid.NewGuid() }))).Code.ShouldBe(FinancialErrorCodes.AnchorRequired);

        (await Should.ThrowAsync<BusinessException>(() => _service.CreateManualAsync(new ManualRateLineCreateDto { OrderId = orderId, Quantity = 1m }))).Code.ShouldBe(FinancialErrorCodes.ManualLineRequiresUnitPrice);
        (await Should.ThrowAsync<BusinessException>(() => _service.CreateManualAsync(new ManualRateLineCreateDto { OrderId = orderId, Kind = RateLineKind.PriceList, UnitPrice = 1m }))).Code.ShouldBe(ReferenceDataErrorCodes.InvalidCode);
        (await Should.ThrowAsync<BusinessException>(() => _service.CreateManualAsync(new ManualRateLineCreateDto { OrderId = orderId, LegId = Guid.NewGuid(), UnitPrice = 1m }))).Code.ShouldBe(FinancialErrorCodes.AnchorRequired);
        (await Should.ThrowAsync<BusinessException>(() => _service.CreateManualAsync(new ManualRateLineCreateDto { TripId = Guid.NewGuid(), OrderLineId = lineId, UnitPrice = 1m }))).Code.ShouldBe(FinancialErrorCodes.AnchorRequired);
        (await Should.ThrowAsync<BusinessException>(() => _service.CreateManualAsync(new ManualRateLineCreateDto { OrderId = orderId, OrderLineId = lineId, StopId = Guid.NewGuid(), UnitPrice = 1m }))).Code.ShouldBe(FinancialErrorCodes.AnchorRequired);
        (await Should.ThrowAsync<BusinessException>(() => _service.CreateManualAsync(new ManualRateLineCreateDto { OrderId = orderId, Kind = RateLineKind.AdditionalService, UnitPrice = 1m, AdditionalServiceId = Guid.NewGuid() }))).Code.ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound);

        /* A calculated line takes the unit price only (QP6). */
        (await Should.ThrowAsync<BusinessException>(() => _service.UpdateAsync(calculated.Id, new RateLineUpdateDto { Quantity = 5m, UnitPrice = 90m, ConcurrencyStamp = calculated.ConcurrencyStamp }))).Code.ShouldBe(FinancialErrorCodes.CalculatedLineReadOnly);
        (await Should.ThrowAsync<BusinessException>(() => _service.UpdateAsync(calculated.Id, new RateLineUpdateDto { Description = "x", UnitPrice = 90m, ConcurrencyStamp = calculated.ConcurrencyStamp }))).Code.ShouldBe(FinancialErrorCodes.CalculatedLineReadOnly);
        (await Should.ThrowAsync<BusinessException>(() => _service.UpdateAsync(calculated.Id, new RateLineUpdateDto { ConcurrencyStamp = calculated.ConcurrencyStamp }))).Code.ShouldBe(FinancialErrorCodes.CalculatedLineReadOnly);
        (await Should.ThrowAsync<BusinessException>(() => _service.DeleteAsync(calculated.Id))).Code.ShouldBe(FinancialErrorCodes.CalculatedLineReadOnly);
        await Should.ThrowAsync<EntityNotFoundException>(() => _service.DeleteAsync(Guid.NewGuid()));

        /* Nothing changed. */
        var set = await _service.GetAsync(new RateLineGetRequestDto { OrderId = orderId });
        set.Lines.Count.ShouldBe(3);
        set.Lines.Single(l => l.Id == calculated.Id).IsAdjusted.ShouldBeFalse();
        set.Lines.Single(l => l.Id == manual.Id).Amount.ShouldBe(25m);
    }

    [Fact]
    public async Task Another_tenant_sees_nothing()
    {
        Guid orderId;
        Guid calculatedId;
        using (InTenantA())
        {
            var seeded = await SeedOrderAsync();
            orderId = seeded.OrderId;
            calculatedId = seeded.Calculated.Id;
        }

        using (InTenantB())
        {
            var set = await _service.GetAsync(new RateLineGetRequestDto { OrderId = orderId });
            set.Lines.ShouldBeEmpty();
            set.Totals.Total.ShouldBe(0m);
            await Should.ThrowAsync<EntityNotFoundException>(() => _service.DeleteAsync(calculatedId));
            await Should.ThrowAsync<EntityNotFoundException>(() => _service.UpdateAsync(calculatedId, new RateLineUpdateDto { UnitPrice = 1m, ConcurrencyStamp = "x" }));
        }
    }
}
