using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Financial.Invoicing;
using OpenTms.Financial.Tariffs;
using OpenTms.Orders;
using OpenTms.Orders.Events;
using OpenTms.Orders.TransportOrders;
using OpenTms.Platform.ReferenceData;
using OpenTms.TransportRun;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.SettingManagement;
using Xunit;

namespace OpenTms.EntityFrameworkCore.Orders;

/// <summary>
/// Design 61 "Order page review" point 3 with build notes 1, 2 and 6, Orders' side on SQLite with Financial's real
/// services: <c>chargedBases</c> on the order's terms (create, update, read in the legacy order, validated with the field,
/// the list bases parallel to the list ids, in the pricing snapshot), copied from the customer's <c>revenueChargedBases</c>
/// on create without terms and on a customer change, on the template's terms and through materialisation, copy and
/// template-from-order; the approval rule on the effective bases; the price preview writing open lines for a stored and an
/// unsaved order, none under a fixed price; tenant isolation of the preview and of the customer's defaults. Risk class:
/// Critical (money: pricing input), High (order chain).
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class TransportOrderChargedBasesTests : TransportOrderTestBase<OpenTmsEntityFrameworkCoreTestModule>
{
    private ITransportOrderTemplateAppService Templates => GetRequiredService<ITransportOrderTemplateAppService>();

    private IRelationFinancialSettingsAppService Settings => GetRequiredService<IRelationFinancialSettingsAppService>();

    private static TransportOrderFinancialTermsInputDto Terms(TransportRunWorld f, IEnumerable<Guid>? lists = null, params QuantitySource[] bases)
        => new() { CurrencyCode = "EUR", VatPercentageId = f.VatId, PaymentTermDays = 30, PriceListIds = (lists ?? [f.PriceListId]).ToList(), ChargedBases = bases.ToList() };

    private static bool IsOpen(OpenTms.Financial.Tariffs.Published.PricePreviewLineDto l) => l.Kind == RateLineKind.PriceList && l.PriceListId == null;

    [Fact]
    public async Task The_terms_carry_the_charged_bases_in_the_legacy_order_and_each_lists_basis_and_an_update_replaces_them()
    {
        using var _ = InTenantA();
        var f = await FixtureAsync();
        var weightList = await Run.PriceListAsync("W" + f.Suffix, RateSide.Revenue, f.DepotId, f.ShopId, 0.5m, QuantitySource.WeightKg);
        var dto = CreateDto(f);
        dto.FinancialTerms = Terms(f, [f.PriceListId, weightList], QuantitySource.DistanceKm, QuantitySource.LineQuantity);

        var order = await Orders.CreateAsync(dto);

        order.FinancialTerms.ChargedBases.ShouldBe([QuantitySource.LineQuantity, QuantitySource.DistanceKm]);
        order.FinancialTerms.PriceListIds.ShouldBe([f.PriceListId, weightList]);
        order.FinancialTerms.PriceListBases.ShouldBe([QuantitySource.LineQuantity, QuantitySource.WeightKg], "parallel to the list ids");
        (await Orders.GetAsync(order.Id)).FinancialTerms.ChargedBases.ShouldBe([QuantitySource.LineQuantity, QuantitySource.DistanceKm]);
        (await GetRequiredService<OpenTms.Orders.Published.IOrderPricingReader>().GetSnapshotAsync(order.Id))!.ChargedBases
            .ShouldBe([QuantitySource.LineQuantity, QuantitySource.DistanceKm], "Financial prices from the snapshot");

        var before = Events.Of<TransportOrderFinancialTermsChangedEto>(order.Id).Count;
        var updated = await Orders.UpdateFinancialTermsAsync(order.Id, new TransportOrderFinancialTermsUpdateDto
        {
            CurrencyCode = "EUR", PriceListIds = [f.PriceListId], ChargedBases = [QuantitySource.Colli], ConcurrencyStamp = order.ConcurrencyStamp
        });
        updated.FinancialTerms.ChargedBases.ShouldBe([QuantitySource.Colli]);
        Events.Of<TransportOrderFinancialTermsChangedEto>(order.Id).Count.ShouldBe(before + 1, "switching a basis reprices the order");

        var refusal = await Should.ThrowAsync<BusinessException>(() => Orders.UpdateFinancialTermsAsync(order.Id, new TransportOrderFinancialTermsUpdateDto
        {
            CurrencyCode = "EUR", ChargedBases = [QuantitySource.Manual], ConcurrencyStamp = updated.ConcurrencyStamp
        }));
        refusal.Code.ShouldBe(ReferenceDataErrorCodes.InvalidCode);
        refusal.Data["field"].ShouldBe("chargedBases");
        (await Orders.GetAsync(order.Id)).FinancialTerms.ChargedBases.ShouldBe([QuantitySource.Colli], "the refused save changed nothing");
    }

    [Theory]
    [InlineData(QuantitySource.TripDistanceKm, ReferenceDataErrorCodes.InvalidCode)]
    [InlineData(QuantitySource.Manual, ReferenceDataErrorCodes.InvalidCode)]
    [InlineData(QuantitySource.WeightKg, ReferenceDataErrorCodes.DuplicateKey)]
    public async Task Create_refuses_bases_that_cannot_be_charged_or_repeat_and_writes_no_order(QuantitySource extra, string code)
    {
        using var _ = InTenantA();
        var f = await FixtureAsync();
        var dto = CreateDto(f);
        dto.FinancialTerms = Terms(f, null, QuantitySource.WeightKg, extra);

        var refusal = await Should.ThrowAsync<BusinessException>(() => Orders.CreateAsync(dto));

        refusal.Code.ShouldBe(code);
        refusal.Data["field"].ShouldBe("chargedBases");
        (await Orders.GetListAsync(new TransportOrderListRequestDto())).TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task The_customers_revenue_charged_bases_are_copied_on_create_without_terms_and_on_a_customer_change()
    {
        using var _ = InTenantA();
        var f = await FixtureAsync();
        var current = await Settings.GetByRelationAsync(f.CustomerId);
        await Settings.UpsertByRelationAsync(f.CustomerId, new RelationFinancialSettingsUpsertDto
        {
            RevenuePriceListIds = [f.PriceListId], VatPercentageId = f.VatId, PaymentTermDays = 30, CurrencyCode = "EUR",
            RevenueChargedBases = [QuantitySource.WeightKg, QuantitySource.LineQuantity], ConcurrencyStamp = current!.ConcurrencyStamp
        });

        var copied = await Orders.CreateAsync(Run.CreateDto(f, withTerms: false));
        copied.FinancialTerms.ChargedBases.ShouldBe([QuantitySource.LineQuantity, QuantitySource.WeightKg]);

        var bare = Run.CreateDto(f, withTerms: false);
        bare.CustomerRelationId = f.ShopId;
        var plain = await Orders.CreateAsync(bare);
        plain.FinancialTerms.ChargedBases.ShouldBeEmpty("a customer without settings: the module's default terms");

        var moved = await Orders.UpdateAsync(plain.Id, HeaderUpdate(plain, u => u.CustomerRelationId = f.CustomerId));
        moved.FinancialTerms.ChargedBases.ShouldBe([QuantitySource.LineQuantity, QuantitySource.WeightKg]);

        var posted = CreateDto(f);
        posted.FinancialTerms = Terms(f, null, QuantitySource.Colli);
        (await Orders.CreateAsync(posted)).FinancialTerms.ChargedBases.ShouldBe([QuantitySource.Colli], "posted terms are kept as posted");
    }

    private TransportOrderTemplateCreateDto TemplateDto(TransportRunWorld f, params QuantitySource[] bases) => new()
    {
        Name = "Charged " + f.Suffix,
        TransportOrderTypeId = f.OrderTypeId,
        CustomerRelationId = f.CustomerId,
        FinancialTerms = Terms(f, null, bases),
        Lines =
        [
            new TemplateLineInputDto
            {
                TransportUnitId = f.TransportUnitId, Quantity = 3, WeightKg = 120,
                Stops =
                [
                    new() { WaypointTypeId = f.LoadTypeId, RelationId = f.DepotId, RelationAddressId = f.DepotAddressId, DayOffset = 0 },
                    new() { WaypointTypeId = f.UnloadTypeId, RelationId = f.ShopId, RelationAddressId = f.ShopAddressId, DayOffset = 1 }
                ]
            }
        ],
        Schedule = new TemplateScheduleDto { Recurrence = TemplateRecurrence.None, Interval = 1, ValidFrom = new DateOnly(2026, 1, 1) }
    };

    [Fact]
    public async Task Templates_store_the_charged_bases_and_materialisation_copy_and_template_from_order_carry_them()
    {
        using var _ = InTenantA();
        var f = await FixtureAsync();

        var template = await Templates.CreateAsync(TemplateDto(f, QuantitySource.PerLine, QuantitySource.Epq));
        template.FinancialTerms.ChargedBases.ShouldBe([QuantitySource.Epq, QuantitySource.PerLine]);
        (await Templates.GetAsync(template.Id)).FinancialTerms.ChargedBases.ShouldBe([QuantitySource.Epq, QuantitySource.PerLine]);

        var orderId = (await Templates.MaterialiseAsync(template.Id, new MaterialiseTemplateDto { Dates = [new DateOnly(2026, 11, 2)] })).CreatedOrderIds.Single();
        var order = await Orders.GetAsync(orderId);
        order.FinancialTerms.ChargedBases.ShouldBe([QuantitySource.Epq, QuantitySource.PerLine], "the materialised order copies the template's terms");

        var copy = await Orders.GetAsync((await Templates.CopyOrderAsync(orderId, new MaterialiseTemplateDto { Dates = [new DateOnly(2026, 11, 9)] })).CreatedOrderIds.Single());
        copy.FinancialTerms.ChargedBases.ShouldBe([QuantitySource.Epq, QuantitySource.PerLine]);

        (await Templates.CreateFromOrderAsync(orderId)).FinancialTerms.ChargedBases.ShouldBe([QuantitySource.Epq, QuantitySource.PerLine]);

        var bad = TemplateDto(f, QuantitySource.TripDistanceKm);
        bad.Name = "Bad " + f.Suffix;
        (await Should.ThrowAsync<BusinessException>(() => Templates.CreateAsync(bad))).Data["field"].ShouldBe("chargedBases");
    }

    [Fact]
    public async Task Approval_compares_the_effective_bases_no_false_warning_for_old_settings_with_lists_and_a_warning_for_an_extra_basis()
    {
        using var _ = InTenantA();
        var f = await FixtureAsync();
        await GetRequiredService<ISettingManager>().SetForTenantAsync(TenantA, OrdersSettings.ActiveApprovalRules,
            OrdersSettings.DefaultActiveApprovalRules + "," + nameof(ApprovalRule.FinancialDefaultsMismatch));

        /* The fixture's settings: the quantity list, no stored bases (a relation saved before design 61). */
        var asDefault = CreateDto(f, confirm: true);
        var plain = await Orders.CreateAsync(asDefault);
        var explicitDto = CreateDto(f, confirm: true, customerReference: "PO-2");
        explicitDto.FinancialTerms = Terms(f, null, QuantitySource.LineQuantity);
        var explicitBasis = await Orders.CreateAsync(explicitDto);
        var extraDto = CreateDto(f, confirm: true, customerReference: "PO-3");
        extraDto.FinancialTerms = Terms(f, null, QuantitySource.WeightKg);
        var extra = await Orders.CreateAsync(extraDto);

        var validation = await Orders.ValidateApprovalAsync(new ApprovalValidateRequestDto { Ids = [plain.Id, explicitBasis.Id, extra.Id] });

        validation.Orders.Single(o => o.OrderId == plain.Id).Results.ShouldNotContain(r => r.Rule == ApprovalRule.FinancialDefaultsMismatch);
        validation.Orders.Single(o => o.OrderId == explicitBasis.Id).Results.ShouldNotContain(r => r.Rule == ApprovalRule.FinancialDefaultsMismatch, "a basis with a list counts as on");
        var warning = validation.Orders.Single(o => o.OrderId == extra.Id).Results.Single(r => r.Rule == ApprovalRule.FinancialDefaultsMismatch);
        warning.Severity.ShouldBe(ApprovalSeverity.Warning);
        warning.Message.ShouldContain("chargedBases");
        warning.Message.ShouldNotContain("priceListIds");
    }

    [Fact]
    public async Task The_preview_writes_open_lines_for_a_charged_basis_without_a_list_for_the_stored_and_an_unsaved_order()
    {
        using var _ = InTenantA();
        var f = await FixtureAsync();
        var dto = CreateDto(f, confirm: true);
        dto.FinancialTerms = Terms(f, null, QuantitySource.WeightKg);
        var order = await Orders.CreateAsync(dto);

        var stored = await Orders.PreviewPricesAsync(order.Id, new PricePreviewRequestDto());
        var open = stored.Lines.Single(IsOpen);
        (open.QuantitySource, open.Quantity, open.UnitPrice, open.Amount, open.DescriptionKey).ShouldBe((QuantitySource.WeightKg, 100m, 0m, 0m, "Financial:RateLine.OpenBasis"));
        open.OrderLineId.ShouldBe(order.Lines.Single().Id);
        stored.Lines.Single(l => l.PriceListId == f.PriceListId).Amount.ShouldBe(2m * TransportRunFixture.RevenueUnitPrice);
        stored.TotalAmount.ShouldBe(2m * TransportRunFixture.RevenueUnitPrice, "an open line adds nothing");

        var unsaved = await Orders.PreviewPricesAsync(order.Id, new PricePreviewRequestDto { FinancialTerms = Terms(f, null, QuantitySource.LineQuantity, QuantitySource.PerLine) });
        unsaved.Lines.Where(IsOpen).Select(l => (l.QuantitySource, l.Quantity)).ShouldBe([(QuantitySource.PerLine, 1m)], "the quantity basis has a list; the unsaved terms were priced, not the stored ones");

        var fixedPrice = Terms(f, null, QuantitySource.WeightKg);
        fixedPrice.FixedPriceAmount = 500m;
        (await Orders.PreviewPricesAsync(order.Id, new PricePreviewRequestDto { FinancialTerms = fixedPrice })).Lines.ShouldNotContain(l => IsOpen(l), "none under a fixed price");
    }

    [Fact]
    public async Task Another_tenant_can_neither_preview_the_order_nor_start_an_order_from_the_customers_defaults()
    {
        Guid orderId;
        TransportRunWorld f;
        using (InTenantA())
        {
            f = await FixtureAsync();
            var current = await Settings.GetByRelationAsync(f.CustomerId);
            await Settings.UpsertByRelationAsync(f.CustomerId, new RelationFinancialSettingsUpsertDto
            {
                RevenuePriceListIds = [f.PriceListId], CurrencyCode = "EUR", RevenueChargedBases = [QuantitySource.WeightKg], ConcurrencyStamp = current!.ConcurrencyStamp
            });
            var dto = CreateDto(f);
            dto.FinancialTerms = Terms(f, null, QuantitySource.WeightKg);
            orderId = (await Orders.CreateAsync(dto)).Id;
        }

        using (InTenantB())
        {
            await Should.ThrowAsync<EntityNotFoundException>(() => Orders.PreviewPricesAsync(orderId, new PricePreviewRequestDto()));
            await Should.ThrowAsync<EntityNotFoundException>(() => Orders.UpdateFinancialTermsAsync(orderId, new TransportOrderFinancialTermsUpdateDto { CurrencyCode = "EUR", ConcurrencyStamp = "x" }));
            (await GetRequiredService<OpenTms.Financial.Published.IFinancialReferenceLookup>().GetRelationFinancialSettingsAsync(f.CustomerId, Today)).ShouldBeNull();
            await Should.ThrowAsync<BusinessException>(() => Orders.CreateAsync(Run.CreateDto(f, withTerms: false)));
        }

        using (InTenantA())
        {
            (await Orders.GetAsync(orderId)).FinancialTerms.ChargedBases.ShouldBe([QuantitySource.WeightKg]);
        }
    }
}
