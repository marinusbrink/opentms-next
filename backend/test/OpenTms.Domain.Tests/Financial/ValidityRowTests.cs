using System;
using System.Collections.Generic;
using System.Linq;
using OpenTms.Financial.Invoicing;
using OpenTms.Financial.Tariffs;
using OpenTms.Platform.Localization;
using OpenTms.Platform.ReferenceData;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace OpenTms.Financial;

/* Design 52, §3.4: the validity rows of VAT percentages and discounts or surcharges. Risk class: Critical (money
 * history) — RateOn/ValueOn per date, past rows fixed, future rows editable, at least one row, one per date. */
public class ValidityRowTests
{
    private static readonly DateOnly Today = new(2026, 10, 22);

    private static VatPercentage High(params (DateOnly, decimal)[] rates)
    {
        var vat = new VatPercentage(Guid.NewGuid(), Guid.NewGuid(), "HIGH",
            LocalizedName.Create(new Dictionary<string, string?> { ["en"] = "High" }), VatKind.Standard, "nl");
        vat.SetRates(rates, fixedOn: null, Guid.NewGuid);
        return vat;
    }

    private static DiscountOrSurcharge Diesel(DiscountOrSurchargeValueKind kind, params (DateOnly, decimal, string?)[] values)
    {
        var item = new DiscountOrSurcharge(Guid.NewGuid(), Guid.NewGuid(), "DIESEL", "Diesel surcharge",
            DiscountOrSurchargeDirection.Surcharge, kind, DiscountOrSurchargeSide.Both);
        item.SetValues(values, fixedOn: null, Guid.NewGuid);
        return item;
    }

    private static string CodeOf(Action action) => Should.Throw<BusinessException>(action).Code!;

    [Theory]
    [InlineData("2000-12-31", null)]
    [InlineData("2001-01-01", 19.0)]
    [InlineData("2012-09-30", 19.0)]
    [InlineData("2012-10-01", 21.0)]
    [InlineData("2030-01-01", 21.0)]
    public void RateOn_returns_the_row_valid_on_the_date(string date, double? expected)
    {
        var vat = High((new DateOnly(2012, 10, 1), 21m), (new DateOnly(2001, 1, 1), 19m));

        decimal? want = expected == null ? null : (decimal)expected.Value;
        decimal? actual = vat.RateOn(DateOnly.Parse(date))?.Percentage;
        actual.ShouldBe(want);
        vat.CountryCode.ShouldBe("NL");
    }

    [Fact]
    public void ValueOn_returns_the_row_valid_on_the_date_with_its_currency()
    {
        var diesel = Diesel(DiscountOrSurchargeValueKind.PerKilometre,
            (new DateOnly(2026, 9, 1), 0.12m, "eur"),
            (new DateOnly(2026, 10, 1), 0.1275m, "EUR"));

        diesel.ValueOn(new DateOnly(2026, 8, 31)).ShouldBeNull();
        diesel.ValueOn(new DateOnly(2026, 9, 30))!.Value.ShouldBe(0.12m);
        var october = diesel.ValueOn(Today)!;
        october.Value.ShouldBe(0.1275m);
        october.CurrencyCode.ShouldBe("EUR");
    }

    [Fact]
    public void On_create_rows_of_any_date_are_accepted()
    {
        High((new DateOnly(2001, 1, 1), 19m), (Today, 20m), (Today.AddYears(1), 22m)).Rates.Count.ShouldBe(3);
    }

    [Fact]
    public void Future_rows_can_be_changed_added_and_removed()
    {
        var vat = High((new DateOnly(2001, 1, 1), 19m), (Today.AddDays(10), 22m));

        vat.SetRates([(new DateOnly(2001, 1, 1), 19m), (Today.AddDays(10), 23m), (Today.AddDays(20), 24m)], Today, Guid.NewGuid);
        vat.Rates.OrderBy(r => r.ValidFrom).Select(r => r.Percentage).ShouldBe([19m, 23m, 24m]);

        vat.SetRates([(new DateOnly(2001, 1, 1), 19m)], Today, Guid.NewGuid);
        vat.Rates.ShouldHaveSingleItem().Percentage.ShouldBe(19m);
    }

    [Fact]
    public void A_fixed_row_cannot_be_changed_removed_or_added_afterwards()
    {
        var vat = High((new DateOnly(2001, 1, 1), 19m), (Today, 21m));

        CodeOf(() => vat.SetRates([(new DateOnly(2001, 1, 1), 20m), (Today, 21m)], Today, Guid.NewGuid)).ShouldBe(FinancialErrorCodes.FixedValidityRowChanged);
        CodeOf(() => vat.SetRates([(Today, 21m)], Today, Guid.NewGuid)).ShouldBe(FinancialErrorCodes.FixedValidityRowChanged);
        CodeOf(() => vat.SetRates([(new DateOnly(2001, 1, 1), 19m), (new DateOnly(2010, 1, 1), 20m), (Today, 21m)], Today, Guid.NewGuid))
            .ShouldBe(FinancialErrorCodes.FixedValidityRowChanged);

        /* A refused replacement leaves the rows as they were. */
        vat.Rates.OrderBy(r => r.ValidFrom).Select(r => r.Percentage).ShouldBe([19m, 21m]);
    }

    [Fact]
    public void A_row_becomes_fixed_on_its_own_date()
    {
        var vat = High((new DateOnly(2001, 1, 1), 19m), (Today.AddDays(1), 21m));

        /* Tomorrow it is fixed: the same change is refused. */
        vat.SetRates([(new DateOnly(2001, 1, 1), 19m), (Today.AddDays(1), 22m)], Today, Guid.NewGuid);
        CodeOf(() => vat.SetRates([(new DateOnly(2001, 1, 1), 19m), (Today.AddDays(1), 23m)], Today.AddDays(1), Guid.NewGuid))
            .ShouldBe(FinancialErrorCodes.FixedValidityRowChanged);
    }

    [Fact]
    public void At_least_one_row_one_per_date_and_at_most_the_bound()
    {
        CodeOf(() => High()).ShouldBe(FinancialErrorCodes.ValidityRowRequired);
        CodeOf(() => High((Today, 21m), (Today, 9m))).ShouldBe(FinancialErrorCodes.DuplicateValidFrom);
        CodeOf(() => High(Enumerable.Range(0, FinancialConsts.MaxVatRates + 1).Select(i => (Today.AddDays(i), 21m)).ToArray()))
            .ShouldBe(ReferenceDataErrorCodes.TooManyChildren);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(100.01)]
    [InlineData(21.005)]
    public void A_vat_percentage_lies_between_0_and_100_with_two_decimals(double percentage)
    {
        CodeOf(() => High((Today, (decimal)percentage))).ShouldBe(FinancialErrorCodes.PercentageOutOfRange);
    }

    [Fact]
    public void Decimals_round_trip_exactly()
    {
        High((Today, 12.34m)).RateOn(Today)!.Percentage.ShouldBe(12.34m);
        Diesel(DiscountOrSurchargeValueKind.FixedAmount, (Today, 0.1m + 0.2m, "EUR")).ValueOn(Today)!.Value.ShouldBe(0.3m);
    }

    [Fact]
    public void Currency_is_required_for_amounts_and_absent_for_a_percentage()
    {
        CodeOf(() => Diesel(DiscountOrSurchargeValueKind.FixedAmount, (Today, 25m, null))).ShouldBe(FinancialErrorCodes.CurrencyRequired);
        CodeOf(() => Diesel(DiscountOrSurchargeValueKind.PerKilometre, (Today, 0.12m, " "))).ShouldBe(FinancialErrorCodes.CurrencyRequired);
        CodeOf(() => Diesel(DiscountOrSurchargeValueKind.Percentage, (Today, 12.5m, "EUR"))).ShouldBe(FinancialErrorCodes.CurrencyNotAllowed);
        Diesel(DiscountOrSurchargeValueKind.Percentage, (Today, 12.5m, null)).ValueOn(Today)!.CurrencyCode.ShouldBeNull();
    }

    [Fact]
    public void Value_ranges_follow_the_kind()
    {
        CodeOf(() => Diesel(DiscountOrSurchargeValueKind.Percentage, (Today, 1000.01m, null))).ShouldBe(FinancialErrorCodes.PercentageOutOfRange);
        Diesel(DiscountOrSurchargeValueKind.Percentage, (Today, 1000m, null)).ValueOn(Today)!.Value.ShouldBe(1000m);
        CodeOf(() => Diesel(DiscountOrSurchargeValueKind.FixedAmount, (Today, -1m, "EUR"))).ShouldBe(ReferenceDataErrorCodes.InvalidCode);
        CodeOf(() => Diesel(DiscountOrSurchargeValueKind.FixedAmount, (Today, 1.00001m, "EUR"))).ShouldBe(ReferenceDataErrorCodes.InvalidCode);
        CodeOf(() => Diesel(DiscountOrSurchargeValueKind.FixedAmount, (Today, 1m, "EURO"))).ShouldBe(ReferenceDataErrorCodes.InvalidCode);
    }

    [Fact]
    public void Days_of_week_are_never_empty()
    {
        var diesel = Diesel(DiscountOrSurchargeValueKind.Percentage, (Today, 5m, null));
        diesel.DaysOfWeek.ShouldBe(DaysOfWeek.All);

        CodeOf(() => diesel.SetDaysOfWeek(DaysOfWeek.None)).ShouldBe(FinancialErrorCodes.DaysOfWeekRequired);
        CodeOf(() => diesel.SetDaysOfWeek((DaysOfWeek)128)).ShouldBe(ReferenceDataErrorCodes.InvalidCode);
        diesel.SetDaysOfWeek(DaysOfWeek.Saturday | DaysOfWeek.Sunday);
        diesel.DaysOfWeek.ShouldBe(DaysOfWeek.Saturday | DaysOfWeek.Sunday);
    }

    [Fact]
    public void Regions_are_unique_bounded_and_replaced()
    {
        var diesel = Diesel(DiscountOrSurchargeValueKind.Percentage, (Today, 5m, null));
        var randstad = Guid.NewGuid();
        var limburg = Guid.NewGuid();

        diesel.SetRegions([(randstad, RegionAppliesAt.Loading), (limburg, RegionAppliesAt.Either)], Guid.NewGuid);
        diesel.SetRegions([(randstad, RegionAppliesAt.Unloading)], Guid.NewGuid);
        diesel.Regions.ShouldHaveSingleItem().AppliesAt.ShouldBe(RegionAppliesAt.Unloading);

        CodeOf(() => diesel.SetRegions([(randstad, RegionAppliesAt.Loading), (randstad, RegionAppliesAt.Unloading)], Guid.NewGuid))
            .ShouldBe(ReferenceDataErrorCodes.DuplicateKey);
        CodeOf(() => diesel.SetRegions(Enumerable.Range(0, FinancialConsts.MaxDiscountOrSurchargeRegions + 1).Select(_ => (Guid.NewGuid(), RegionAppliesAt.Either)), Guid.NewGuid))
            .ShouldBe(ReferenceDataErrorCodes.TooManyChildren);
    }

    [Fact]
    public void The_default_outbound_invoice_type_keeps_its_kind()
    {
        var type = new OutboundInvoiceType(Guid.NewGuid(), Guid.NewGuid(), "INV",
            LocalizedName.Create(new Dictionary<string, string?> { ["en"] = "Invoice" }), OutboundInvoiceKind.Invoice,
            Platform.NumberSequences.NumberSequenceFormat.Create("F", true, false, "-", 5));
        type.SetDefault(true);

        CodeOf(() => type.SetKind(OutboundInvoiceKind.CreditNote)).ShouldBe(ReferenceDataErrorCodes.DefaultRequired);
        CodeOf(() => type.SetArchived(true)).ShouldBe(ReferenceDataErrorCodes.DefaultRequired);
        OutboundInvoiceType.SequenceKeyOf(type.Id).ShouldBe("Financial.OutboundInvoiceType:" + type.Id);
    }
}
