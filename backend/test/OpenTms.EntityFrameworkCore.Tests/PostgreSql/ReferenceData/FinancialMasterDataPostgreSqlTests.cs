using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using OpenTms.Financial;
using OpenTms.Financial.Invoicing;
using OpenTms.Financial.Seed;
using OpenTms.Financial.Tariffs;
using OpenTms.Platform.Localization;
using OpenTms.Platform.NumberSequences;
using Shouldly;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Xunit;

namespace OpenTms.PostgreSql.ReferenceData;

/// <summary>
/// The Financial master data against real PostgreSQL (design 52): the seed the DbMigrator ran for tenant A (own
/// database) and the shared tenant (host database), idempotent seeding, tenant isolation of every entity and child,
/// the unique and one-default indexes (VAT per tenant, invoice type per kind), one validity row per date, one row per
/// region, and decimals stored without binary rounding. Risk class: Critical.
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class FinancialMasterDataPostgreSqlTests : ReferenceDataPostgreSqlTestBase
{
    private static readonly DateOnly Since2001 = new(2001, 1, 1);

    private readonly IRepository<VatPercentage, Guid> _vat;
    private readonly IRepository<VatPercentageRate, Guid> _rates;
    private readonly IRepository<OutboundInvoiceType, Guid> _outbound;
    private readonly IRepository<InboundInvoiceType, Guid> _inbound;
    private readonly IRepository<DiscountOrSurcharge, Guid> _discounts;
    private readonly IRepository<DiscountOrSurchargeValue, Guid> _values;
    private readonly IVatPercentageAppService _vatService;
    private readonly IDiscountOrSurchargeAppService _discountService;
    private readonly FinancialDataSeedContributor _seed;
    private readonly IGuidGenerator _guids;

    public FinancialMasterDataPostgreSqlTests()
    {
        _vat = GetRequiredService<IRepository<VatPercentage, Guid>>();
        _rates = GetRequiredService<IRepository<VatPercentageRate, Guid>>();
        _outbound = GetRequiredService<IRepository<OutboundInvoiceType, Guid>>();
        _inbound = GetRequiredService<IRepository<InboundInvoiceType, Guid>>();
        _discounts = GetRequiredService<IRepository<DiscountOrSurcharge, Guid>>();
        _values = GetRequiredService<IRepository<DiscountOrSurchargeValue, Guid>>();
        _vatService = GetRequiredService<IVatPercentageAppService>();
        _discountService = GetRequiredService<IDiscountOrSurchargeAppService>();
        _seed = GetRequiredService<FinancialDataSeedContributor>();
        _guids = GetRequiredService<IGuidGenerator>();
    }

    private static LocalizedName En(string text) => LocalizedName.Create(new Dictionary<string, string?> { ["en"] = text });

    private VatPercentage NewVat(Guid tenantId, string code)
    {
        var vat = new VatPercentage(_guids.Create(), tenantId, code, En(code), VatKind.Standard, "NL");
        vat.SetRates([(Since2001, 21m)], fixedOn: null, _guids.Create);
        return vat;
    }

    private OutboundInvoiceType NewInvoiceType(Guid tenantId, string code, OutboundInvoiceKind kind)
        => new(_guids.Create(), tenantId, code, En(code), kind, NumberSequenceFormat.Create("T", false, false, "", 5));

    private DiscountOrSurcharge NewSurcharge(Guid tenantId, string code)
    {
        var item = new DiscountOrSurcharge(_guids.Create(), tenantId, code, code, DiscountOrSurchargeDirection.Surcharge, DiscountOrSurchargeValueKind.Percentage, DiscountOrSurchargeSide.Both);
        item.SetValues([(Since2001, 5m, null)], fixedOn: null, _guids.Create);
        return item;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Every_tenant_has_the_seeded_rows_and_seeding_again_changes_nothing(bool ownDatabase)
    {
        var tenantId = ownDatabase ? TenantA : SharedTenant;
        await InRolledBackTransactionAsync(tenantId, async () =>
        {
            var vat = await _vat.GetListAsync(includeDetails: true);
            vat.Select(v => v.Code).ShouldBe(["HIGH", "LOW", "ZERO", "EXEMPT", "REVERSE", "ICP", "EXPORT"], ignoreOrder: true);
            vat.ShouldAllBe(v => v.TenantId == tenantId && v.CountryCode == "NL");
            var high = vat.Single(v => v.Code == "HIGH");
            high.IsDefault.ShouldBeTrue();
            high.Name.Resolve("nl").ShouldBe("Hoog");
            high.RateOn(new DateOnly(2012, 9, 30))!.Percentage.ShouldBe(19m);
            high.RateOn(new DateOnly(2026, 10, 22))!.Percentage.ShouldBe(21m);
            vat.Single(v => v.Code == "LOW").RateOn(new DateOnly(2026, 10, 22))!.Percentage.ShouldBe(9m);
            vat.Single(v => v.Code == "ICP").Kind.ShouldBe(VatKind.IntraCommunity);
            vat.Count(v => v.IsDefault).ShouldBe(1);

            var outbound = await _outbound.GetListAsync();
            outbound.Select(t => (t.Code, t.Kind, t.IsDefault)).ShouldBe(
                [("INV", OutboundInvoiceKind.Invoice, true), ("CRN", OutboundInvoiceKind.CreditNote, true)], ignoreOrder: true);
            outbound.Single(t => t.Code == "INV").Numbering.ShouldBe(NumberSequenceFormat.Create("F", true, false, "-", 5));
            (await _inbound.GetListAsync()).Select(t => (t.Code, t.Kind)).ShouldBe(
                [("CARRIER", InboundInvoiceKind.CarrierInvoice), ("OTHER", InboundInvoiceKind.Other)], ignoreOrder: true);

            var rateCount = await _rates.GetCountAsync();
            await _seed.SeedAsync(new DataSeedContext(tenantId));
            await FlushAsync();
            (await _vat.GetCountAsync()).ShouldBe(7);
            (await _rates.GetCountAsync()).ShouldBe(rateCount);
            (await _outbound.GetCountAsync()).ShouldBe(2);
            (await _inbound.GetCountAsync()).ShouldBe(2);
        });
    }

    [Fact]
    public async Task A_tenant_never_sees_another_tenants_rows_or_children()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            await _discounts.InsertAsync(NewSurcharge(TenantA, "ONLYA"), autoSave: true);
            (await _discountService.GetListAsync(List())).Rows.Select(r => r.Code).ShouldBe(["ONLYA"]);
        });

        await InRolledBackTransactionAsync(SharedTenant, async () =>
        {
            await _discounts.InsertAsync(NewSurcharge(SharedTenant, "SHARED"), autoSave: true);
            (await _discountService.GetListAsync(List())).Rows.Select(r => r.Code).ShouldBe(["SHARED"]);
            (await _values.GetListAsync()).ShouldAllBe(v => v.TenantId == SharedTenant);
            (await _rates.GetListAsync()).ShouldAllBe(r => r.TenantId == SharedTenant);
            (await _vatService.GetListAsync(List())).Rows.Count.ShouldBe(7);
        });
    }

    [Fact]
    public async Task A_duplicate_code_is_refused_by_the_index()
    {
        await InRolledBackTransactionAsync(TenantA, () => ShouldViolateUniqueIndexAsync(
            () => _vat.InsertAsync(NewVat(TenantA, "LOW"), autoSave: true),
            "IX_InvoicingVatPercentages_TenantId_Code"));
    }

    [Fact]
    public async Task A_second_default_vat_category_is_refused_by_the_index()
    {
        await InRolledBackTransactionAsync(TenantA, () => ShouldViolateUniqueIndexAsync(
            async () =>
            {
                var second = NewVat(TenantA, "SECOND");
                second.SetDefault(true);
                await _vat.InsertAsync(second, autoSave: true);
            },
            "IX_InvoicingVatPercentages_TenantId_Default"));
    }

    [Fact]
    public async Task One_default_invoice_type_per_kind_is_enforced_by_the_index()
    {
        await InRolledBackTransactionAsync(TenantA, () => ShouldViolateUniqueIndexAsync(
            async () =>
            {
                var second = NewInvoiceType(TenantA, "INV2", OutboundInvoiceKind.Invoice);
                second.SetDefault(true);
                await _outbound.InsertAsync(second, autoSave: true);
            },
            "IX_InvoicingOutboundInvoiceTypes_TenantId_Kind_Default"));
    }

    [Fact]
    public async Task A_second_validity_row_on_one_date_is_refused_by_the_index()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var high = (await _vat.GetListAsync()).Single(v => v.Code == "HIGH");
            await ShouldViolateUniqueIndexAsync(
                () => _rates.InsertAsync(new VatPercentageRate(_guids.Create(), TenantA, high.Id, Since2001, 5m), autoSave: true),
                "IX_InvoicingVatPercentageRates_VatPercentageId_ValidFrom");
        });

        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var item = NewSurcharge(TenantA, "DIESEL");
            await _discounts.InsertAsync(item, autoSave: true);
            await ShouldViolateUniqueIndexAsync(
                () => _values.InsertAsync(new DiscountOrSurchargeValue(_guids.Create(), TenantA, item.Id, Since2001, 6m, null), autoSave: true),
                "IX_TariffsDiscountOrSurchargeValues_Parent_ValidFrom");
        });
    }

    [Fact]
    public async Task A_region_twice_on_one_surcharge_is_refused_by_the_index()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var item = NewSurcharge(TenantA, "REGIO");
            var region = Guid.NewGuid();
            item.SetRegions([(region, RegionAppliesAt.Loading)], _guids.Create);
            await _discounts.InsertAsync(item, autoSave: true);

            var regions = GetRequiredService<IRepository<DiscountOrSurchargeRegion, Guid>>();
            await ShouldViolateUniqueIndexAsync(
                () => regions.InsertAsync(new DiscountOrSurchargeRegion(_guids.Create(), TenantA, item.Id, region, RegionAppliesAt.Unloading), autoSave: true),
                "IX_TariffsDiscountOrSurchargeRegions_Parent_Region");
        });
    }

    [Fact]
    public async Task Money_and_percentages_round_trip_without_binary_rounding()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var item = new DiscountOrSurcharge(_guids.Create(), TenantA, "DIESEL", "Diesel", DiscountOrSurchargeDirection.Surcharge, DiscountOrSurchargeValueKind.PerKilometre, DiscountOrSurchargeSide.Both);
            item.SetValues([(Since2001, 0.1m + 0.2m, "EUR"), (new DateOnly(2002, 1, 1), 12345678901234.1234m, "EUR")], fixedOn: null, _guids.Create);
            await _discounts.InsertAsync(item, autoSave: true);
            var vat = NewVat(TenantA, "ODD");
            vat.SetRates([(Since2001, 12.34m)], fixedOn: null, _guids.Create);
            await _vat.InsertAsync(vat, autoSave: true);

            /* Read past the change tracker: the numeric columns, not the tracked instances. */
            var values = await (await _values.GetQueryableAsync()).AsNoTracking()
                .Where(v => v.DiscountOrSurchargeId == item.Id).OrderBy(v => v.ValidFrom).Select(v => v.Value).ToListAsync();
            values.ShouldBe([0.3m, 12345678901234.1234m]);
            (await (await _rates.GetQueryableAsync()).AsNoTracking().SingleAsync(r => r.VatPercentageId == vat.Id)).Percentage.ShouldBe(12.34m);
        });
    }
}
