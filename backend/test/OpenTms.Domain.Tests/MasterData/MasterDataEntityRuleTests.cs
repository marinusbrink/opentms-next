using System;
using System.Collections.Generic;
using System.Linq;
using OpenTms.MasterData.Fleet;
using OpenTms.MasterData.Reference;
using OpenTms.MasterData.Regions;
using OpenTms.MasterData.Relations;
using OpenTms.MasterData.Transport;
using OpenTms.Platform.Localization;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace OpenTms.MasterData;

/// <summary>
/// Unit tests for the invariants the Master Data entities enforce themselves (design 41, "Entities").
/// Risk class: High — these rules are the last line before the database: code normalisation,
/// the active default currency, the ADR block, temperature bounds, child-collection replace semantics
/// and the two forms of a postal-code range.
/// </summary>
public class MasterDataEntityRuleTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    private static LocalizedName Name(string en, string? nl = null)
        => LocalizedName.Create(new Dictionary<string, string?> { ["en"] = en, ["nl"] = nl ?? en });

    // ── MasterDataCodes ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(" 1234 ab ", "1234AB")]
    [InlineData("sw1a 1aa", "SW1A1AA")]
    [InlineData("1 2 3 4", "1234")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData(null, "")]
    public void NormalizePostalCode_removes_spaces_trims_and_upper_cases(string? input, string expected)
    {
        MasterDataCodes.NormalizePostalCode(input).ShouldBe(expected);
    }

    [Fact]
    public void Normalize_trims_upper_cases_and_turns_empty_into_null()
    {
        MasterDataCodes.Normalize(" eur ").ShouldBe("EUR");
        MasterDataCodes.Normalize("").ShouldBeNull();
        MasterDataCodes.Normalize("  ").ShouldBeNull();
        MasterDataCodes.Normalize(null).ShouldBeNull();
    }

    [Fact]
    public void NormalizeRequired_rejects_empty_and_too_long_codes_with_InvalidCode()
    {
        MasterDataCodes.NormalizeRequired(" abc ", 32, "code").ShouldBe("ABC");

        var empty = Should.Throw<BusinessException>(() => MasterDataCodes.NormalizeRequired("  ", 32, "code"));
        empty.Code.ShouldBe(MasterDataErrorCodes.InvalidCode);
        empty.Data["field"].ShouldBe("code");

        Should.Throw<BusinessException>(() => MasterDataCodes.NormalizeRequired(new string('a', 33), 32, "code")).Code.ShouldBe(MasterDataErrorCodes.InvalidCode);
    }

    [Theory]
    [InlineData("1203", true)]
    [InlineData("0001", true)]
    [InlineData("120", false)]
    [InlineData("12034", false)]
    [InlineData("12A3", false)]
    [InlineData("", false)]
    public void IsUnNumber_accepts_exactly_four_digits(string value, bool expected)
    {
        MasterDataCodes.IsUnNumber(value).ShouldBe(expected);
    }

    // ── Description (shared base) ─────────────────────────────────────────────

    [Fact]
    public void SetDescription_trims_turns_empty_into_null_and_rejects_more_than_2500_characters()
    {
        var category = new RelationCategory(Guid.NewGuid(), Tenant, "Customers");

        category.SetDescription("  text  ");
        category.Description.ShouldBe("text");

        category.SetDescription("   ");
        category.Description.ShouldBeNull();

        category.SetDescription(new string('d', MasterDataConsts.MaxDescriptionLength));
        category.Description!.Length.ShouldBe(2500);

        Should.Throw<ArgumentException>(() => category.SetDescription(new string('d', MasterDataConsts.MaxDescriptionLength + 1)));
    }

    [Fact]
    public void Names_are_trimmed_and_required()
    {
        new RelationCategory(Guid.NewGuid(), Tenant, "  Customers ").Name.ShouldBe("Customers");
        Should.Throw<ArgumentException>(() => new RelationCategory(Guid.NewGuid(), Tenant, "   "));
        Should.Throw<ArgumentException>(() => new RelationCategory(Guid.NewGuid(), Tenant, new string('n', MasterDataConsts.MaxNameLength + 1)));
    }

    // ── Currency ──────────────────────────────────────────────────────────────

    [Fact]
    public void Currency_normalises_the_iso_code_to_upper_case()
    {
        var currency = new Currency(Guid.NewGuid(), Tenant, " eur ", Name("Euro"), isActive: true);

        currency.IsoCode.ShouldBe("EUR");
        currency.TenantId.ShouldBe(Tenant);
        currency.IsActive.ShouldBeTrue();
        currency.IsDefault.ShouldBeFalse();
    }

    [Theory]
    [InlineData("EURO")]
    [InlineData("EU")]
    [InlineData("E1R")]
    [InlineData("")]
    [InlineData("   ")]
    public void Currency_rejects_an_iso_code_that_is_not_three_letters(string code)
    {
        var ex = Should.Throw<BusinessException>(() => new Currency(Guid.NewGuid(), Tenant, code, Name("Euro"), true));

        ex.Code.ShouldBe(MasterDataErrorCodes.InvalidCode);
        ex.Data["field"].ShouldBe("isoCode");
    }

    [Fact]
    public void Currency_cannot_be_deactivated_while_it_is_the_default()
    {
        var currency = new Currency(Guid.NewGuid(), Tenant, "EUR", Name("Euro"), true);
        currency.MarkDefault();

        var ex = Should.Throw<BusinessException>(() => currency.SetActive(false));

        ex.Code.ShouldBe(MasterDataErrorCodes.DefaultMustBeActive);
        currency.IsActive.ShouldBeTrue();
        currency.IsDefault.ShouldBeTrue();
    }

    [Fact]
    public void Currency_cannot_become_the_default_while_inactive()
    {
        var currency = new Currency(Guid.NewGuid(), Tenant, "XAU", Name("Gold"), isActive: false);

        Should.Throw<BusinessException>(() => currency.MarkDefault()).Code.ShouldBe(MasterDataErrorCodes.DefaultMustBeActive);
        currency.IsDefault.ShouldBeFalse();
    }

    [Fact]
    public void Currency_default_can_be_cleared_and_the_currency_deactivated_afterwards()
    {
        var currency = new Currency(Guid.NewGuid(), Tenant, "EUR", Name("Euro"), true);
        currency.MarkDefault();

        currency.ClearDefault();
        currency.SetActive(false);

        currency.IsDefault.ShouldBeFalse();
        currency.IsActive.ShouldBeFalse();
    }

    [Fact]
    public void Currency_bounds_minor_units_and_numeric_code_and_normalises_the_symbol()
    {
        var currency = new Currency(Guid.NewGuid(), Tenant, "EUR", Name("Euro"), true);

        currency.SetMinorUnits(0);
        currency.SetMinorUnits(4);
        currency.SetMinorUnits(null);
        Should.Throw<ArgumentOutOfRangeException>(() => currency.SetMinorUnits(5));
        Should.Throw<ArgumentOutOfRangeException>(() => currency.SetMinorUnits(-1));

        currency.SetIsoNumeric(978);
        currency.SetIsoNumeric(null);
        Should.Throw<ArgumentOutOfRangeException>(() => currency.SetIsoNumeric(1000));
        Should.Throw<ArgumentOutOfRangeException>(() => currency.SetIsoNumeric(-1));

        currency.SetSymbol("  €  ");
        currency.Symbol.ShouldBe("€");
        currency.SetSymbol("   ");
        currency.Symbol.ShouldBeNull();
        Should.Throw<ArgumentException>(() => currency.SetSymbol(new string('$', MasterDataConsts.MaxSymbolLength + 1)));
    }

    // ── Country ───────────────────────────────────────────────────────────────

    [Fact]
    public void Country_normalises_both_iso_codes_to_upper_case()
    {
        var country = new Country(Guid.NewGuid(), Tenant, " nl ", "nld", Name("Netherlands", "Nederland"));

        country.IsoAlpha2.ShouldBe("NL");
        country.IsoAlpha3.ShouldBe("NLD");
        country.DefaultCurrencyId.ShouldBeNull();
        country.PrimaryTimeZoneId.ShouldBeNull();
    }

    [Theory]
    [InlineData("N", "NLD", "isoAlpha2")]
    [InlineData("NLD", "NLD", "isoAlpha2")]
    [InlineData("N1", "NLD", "isoAlpha2")]
    [InlineData("NL", "NL", "isoAlpha3")]
    [InlineData("NL", "NLDS", "isoAlpha3")]
    [InlineData("NL", "", "isoAlpha3")]
    public void Country_rejects_malformed_iso_codes_naming_the_field(string alpha2, string alpha3, string field)
    {
        var ex = Should.Throw<BusinessException>(() => new Country(Guid.NewGuid(), Tenant, alpha2, alpha3, Name("X")));

        ex.Code.ShouldBe(MasterDataErrorCodes.InvalidCode);
        ex.Data["field"].ShouldBe(field);
    }

    [Fact]
    public void Country_accepts_a_known_iana_time_zone_and_clears_an_empty_one()
    {
        var country = new Country(Guid.NewGuid(), Tenant, "NL", "NLD", Name("Netherlands"));

        country.SetPrimaryTimeZone(" Europe/Amsterdam ");
        country.PrimaryTimeZoneId.ShouldBe("Europe/Amsterdam");

        country.SetPrimaryTimeZone("");
        country.PrimaryTimeZoneId.ShouldBeNull();

        country.SetPrimaryTimeZone("UTC");
        country.PrimaryTimeZoneId.ShouldBe("UTC");

        country.SetPrimaryTimeZone(null);
        country.PrimaryTimeZoneId.ShouldBeNull();
    }

    [Theory]
    [InlineData("Mars/Olympus")]
    [InlineData("Europe/Amsterdam/Centrum")]
    [InlineData("not a zone")]
    public void Country_rejects_an_unknown_time_zone(string zone)
    {
        var country = new Country(Guid.NewGuid(), Tenant, "NL", "NLD", Name("Netherlands"));
        country.SetPrimaryTimeZone("Europe/Amsterdam");

        var ex = Should.Throw<BusinessException>(() => country.SetPrimaryTimeZone(zone));

        ex.Code.ShouldBe(MasterDataErrorCodes.InvalidTimeZone);
        ex.Data["value"].ShouldBe(zone);
        country.PrimaryTimeZoneId.ShouldBe("Europe/Amsterdam");
    }

    [Fact]
    public void Country_rejects_a_time_zone_id_longer_than_64_characters()
    {
        var country = new Country(Guid.NewGuid(), Tenant, "NL", "NLD", Name("Netherlands"));

        Should.Throw<BusinessException>(() => country.SetPrimaryTimeZone("Europe/" + new string('a', 60))).Code.ShouldBe(MasterDataErrorCodes.InvalidTimeZone);
    }

    [Fact]
    public void Country_bounds_the_numeric_code()
    {
        var country = new Country(Guid.NewGuid(), Tenant, "NL", "NLD", Name("Netherlands"));

        country.SetIsoNumeric(528);
        country.IsoNumeric.ShouldBe(528);
        Should.Throw<ArgumentOutOfRangeException>(() => country.SetIsoNumeric(1000));
        Should.Throw<ArgumentOutOfRangeException>(() => country.SetIsoNumeric(-1));
    }

    // ── AddressType ───────────────────────────────────────────────────────────

    [Fact]
    public void AddressType_rejects_an_undefined_kind_and_a_null_name()
    {
        var type = new AddressType(Guid.NewGuid(), Tenant, AddressTypeKind.Visiting, Name("Visiting address", "Bezoekadres"));

        type.Kind.ShouldBe(AddressTypeKind.Visiting);
        Should.Throw<ArgumentOutOfRangeException>(() => type.SetKind((AddressTypeKind)42));
        Should.Throw<ArgumentException>(() => type.SetName(null!));
    }

    // ── Temperature ───────────────────────────────────────────────────────────

    [Fact]
    public void Temperature_rejects_a_minimum_above_the_maximum_and_accepts_open_and_equal_bounds()
    {
        var temperature = new Temperature(Guid.NewGuid(), Tenant, "Frozen");

        var ex = Should.Throw<BusinessException>(() => temperature.SetRange(5, -5));
        ex.Code.ShouldBe(MasterDataErrorCodes.InvalidTemperatureRange);
        ex.Data["min"].ShouldBe(5d);
        ex.Data["max"].ShouldBe(-5d);
        temperature.MinCelsius.ShouldBeNull();

        temperature.SetRange(-25, -18);
        temperature.MinCelsius.ShouldBe(-25);
        temperature.MaxCelsius.ShouldBe(-18);

        temperature.SetRange(-18, -18);
        temperature.SetRange(null, 4);
        temperature.SetRange(2, null);
        temperature.SetRange(null, null);
        temperature.MinCelsius.ShouldBeNull();
        temperature.MaxCelsius.ShouldBeNull();
    }

    // ── Product ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null, "3")]
    [InlineData("", "3")]
    [InlineData("12", "3")]
    [InlineData("12034", "3")]
    [InlineData("12A3", "3")]
    [InlineData("1203", null)]
    [InlineData("1203", "   ")]
    [InlineData("1203", "123456789")]
    public void Product_adr_requires_a_four_digit_un_number_and_a_class(string? unNumber, string? adrClass)
    {
        var product = new Product(Guid.NewGuid(), Tenant, "Petrol");

        Should.Throw<BusinessException>(() => product.SetAdr(true, unNumber, adrClass, null, null)).Code.ShouldBe(MasterDataErrorCodes.AdrFieldsRequired);
        product.IsAdr.ShouldBeFalse();
    }

    [Fact]
    public void Product_adr_stores_the_normalised_block()
    {
        var product = new Product(Guid.NewGuid(), Tenant, "Petrol");

        product.SetAdr(true, " 1203 ", " 3 ", "ii", "  Petrol, flammable  ");

        product.IsAdr.ShouldBeTrue();
        product.AdrUnNumber.ShouldBe("1203");
        product.AdrClass.ShouldBe("3");
        product.AdrPackingGroup.ShouldBe("II");
        product.AdrDescription.ShouldBe("Petrol, flammable");
    }

    [Fact]
    public void Product_without_adr_clears_every_adr_field()
    {
        var product = new Product(Guid.NewGuid(), Tenant, "Petrol");
        product.SetAdr(true, "1203", "3", "II", "Petrol");

        product.SetAdr(false, "1203", "3", "II", "Petrol");

        product.IsAdr.ShouldBeFalse();
        product.AdrUnNumber.ShouldBeNull();
        product.AdrClass.ShouldBeNull();
        product.AdrPackingGroup.ShouldBeNull();
        product.AdrDescription.ShouldBeNull();
    }

    [Fact]
    public void Product_adr_bounds_the_packing_group_and_description()
    {
        var product = new Product(Guid.NewGuid(), Tenant, "Petrol");

        Should.Throw<ArgumentOutOfRangeException>(() => product.SetAdr(true, "1203", "3", "IIII", null));
        Should.Throw<ArgumentException>(() => product.SetAdr(true, "1203", "3", null, new string('d', MasterDataConsts.MaxAdrDescriptionLength + 1)));

        product.SetAdr(true, "1203", "3", "  ", "  ");
        product.AdrPackingGroup.ShouldBeNull();
        product.AdrDescription.ShouldBeNull();
    }

    // ── TransportUnit ─────────────────────────────────────────────────────────

    [Fact]
    public void TransportUnit_normalises_the_sutc_code()
    {
        var unit = new TransportUnit(Guid.NewGuid(), Tenant, "Europallet");

        unit.SetSutcCode(" pal ");
        unit.SutcCode.ShouldBe("PAL");
        unit.SetSutcCode("  ");
        unit.SutcCode.ShouldBeNull();
        Should.Throw<ArgumentException>(() => unit.SetSutcCode(new string('s', MasterDataConsts.MaxSutcCodeLength + 1)));
    }

    [Fact]
    public void TransportUnit_rejects_two_load_factors_for_the_same_vehicle_type()
    {
        var unit = new TransportUnit(Guid.NewGuid(), Tenant, "Europallet");
        var vehicleType = Guid.NewGuid();

        var ex = Should.Throw<BusinessException>(() => unit.SetLoadFactors(
            [(null, vehicleType, 1.0, 33), (null, vehicleType, 2.0, 20)],
            Guid.NewGuid));

        ex.Code.ShouldBe(MasterDataErrorCodes.DuplicateLoadFactor);
        unit.LoadFactors.ShouldBeEmpty();
    }

    [Fact]
    public void TransportUnit_load_factors_are_replaced_keeping_rows_matched_by_id()
    {
        var unit = new TransportUnit(Guid.NewGuid(), Tenant, "Europallet");
        var tractor = Guid.NewGuid();
        var trailer = Guid.NewGuid();
        var van = Guid.NewGuid();
        unit.SetLoadFactors([(null, tractor, 1.0, 33), (null, trailer, 2.0, 20)], Guid.NewGuid);
        var tractorRow = unit.LoadFactors.Single(f => f.VehicleTypeId == tractor);
        var trailerRow = unit.LoadFactors.Single(f => f.VehicleTypeId == trailer);
        tractorRow.TenantId.ShouldBe(Tenant);
        tractorRow.TransportUnitId.ShouldBe(unit.Id);

        unit.SetLoadFactors([(tractorRow.Id, tractor, 1.5, 30), (null, van, 0.5, 8)], Guid.NewGuid);

        unit.LoadFactors.Count.ShouldBe(2);
        var kept = unit.LoadFactors.Single(f => f.VehicleTypeId == tractor);
        kept.Id.ShouldBe(tractorRow.Id);
        kept.LoadingCapacityFactor.ShouldBe(1.5);
        kept.MaxCapacity.ShouldBe(30);
        unit.LoadFactors.ShouldNotContain(f => f.Id == trailerRow.Id);
        unit.LoadFactors.ShouldContain(f => f.VehicleTypeId == van && f.MaxCapacity == 8);
    }

    [Fact]
    public void TransportUnit_load_factor_without_an_id_is_matched_by_vehicle_type()
    {
        var unit = new TransportUnit(Guid.NewGuid(), Tenant, "Europallet");
        var tractor = Guid.NewGuid();
        unit.SetLoadFactors([(null, tractor, 1.0, 33)], Guid.NewGuid);
        var originalId = unit.LoadFactors.Single().Id;

        unit.SetLoadFactors([(null, tractor, 3.0, 10)], Guid.NewGuid);

        unit.LoadFactors.Single().Id.ShouldBe(originalId);
        unit.LoadFactors.Single().LoadingCapacityFactor.ShouldBe(3.0);
    }

    [Fact]
    public void TransportUnit_load_factor_matched_by_id_may_switch_vehicle_type()
    {
        var unit = new TransportUnit(Guid.NewGuid(), Tenant, "Europallet");
        var tractor = Guid.NewGuid();
        var trailer = Guid.NewGuid();
        unit.SetLoadFactors([(null, tractor, 1.0, 33)], Guid.NewGuid);
        var id = unit.LoadFactors.Single().Id;

        unit.SetLoadFactors([(id, trailer, 1.0, 33)], Guid.NewGuid);

        unit.LoadFactors.Single().Id.ShouldBe(id);
        unit.LoadFactors.Single().VehicleTypeId.ShouldBe(trailer);
    }

    [Fact]
    public void TransportUnit_empty_load_factor_set_removes_every_row()
    {
        var unit = new TransportUnit(Guid.NewGuid(), Tenant, "Europallet");
        unit.SetLoadFactors([(null, Guid.NewGuid(), 1.0, 33), (null, Guid.NewGuid(), 2.0, 20)], Guid.NewGuid);

        unit.SetLoadFactors([], Guid.NewGuid);

        unit.LoadFactors.ShouldBeEmpty();
    }

    // ── VehicleType ───────────────────────────────────────────────────────────

    [Fact]
    public void VehicleType_rejects_an_undefined_kind()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new VehicleType(Guid.NewGuid(), Tenant, "Truck", (VehicleTypeKind)0));
        Should.Throw<ArgumentOutOfRangeException>(() => new VehicleType(Guid.NewGuid(), Tenant, "Truck", (VehicleTypeKind)99));
        new VehicleType(Guid.NewGuid(), Tenant, "Truck", VehicleTypeKind.Rigid).Kind.ShouldBe(VehicleTypeKind.Rigid);
    }

    [Fact]
    public void VehicleType_characteristics_are_replaced_keeping_the_rows_that_stay()
    {
        var vehicleType = new VehicleType(Guid.NewGuid(), Tenant, "Reefer trailer", VehicleTypeKind.Trailer);
        var cooled = Guid.NewGuid();
        var tailLift = Guid.NewGuid();
        var adr = Guid.NewGuid();
        vehicleType.SetCharacteristics([cooled, tailLift, cooled]);
        vehicleType.Characteristics.Count.ShouldBe(2);
        var cooledRow = vehicleType.Characteristics.Single(c => c.TransportCharacteristicsId == cooled);
        cooledRow.TenantId.ShouldBe(Tenant);
        cooledRow.VehicleTypeId.ShouldBe(vehicleType.Id);
        cooledRow.GetKeys().ShouldBe([vehicleType.Id, cooled]);

        vehicleType.SetCharacteristics([cooled, adr]);

        vehicleType.Characteristics.Select(c => c.TransportCharacteristicsId).ShouldBe([cooled, adr], ignoreOrder: true);
        vehicleType.Characteristics.Single(c => c.TransportCharacteristicsId == cooled).ShouldBeSameAs(cooledRow);

        vehicleType.SetCharacteristics([]);
        vehicleType.Characteristics.ShouldBeEmpty();
    }

    // ── GeographicalRegion and ranges ─────────────────────────────────────────

    [Fact]
    public void PostalCodeRange_accepts_a_numeric_range_and_clears_the_prefix()
    {
        var range = new GeographicalRegionPostalCodeRange(Guid.NewGuid(), Tenant, Guid.NewGuid());
        var country = Guid.NewGuid();

        range.Set(country, 1000, 1999, null);

        range.CountryId.ShouldBe(country);
        range.FromNumeric.ShouldBe(1000);
        range.TillNumeric.ShouldBe(1999);
        range.Prefix.ShouldBeNull();
    }

    [Fact]
    public void PostalCodeRange_accepts_a_prefix_normalised_and_clears_the_numeric_fields()
    {
        var range = new GeographicalRegionPostalCodeRange(Guid.NewGuid(), Tenant, Guid.NewGuid());

        range.Set(Guid.NewGuid(), null, null, " sw 1a ");

        range.Prefix.ShouldBe("SW1A");
        range.FromNumeric.ShouldBeNull();
        range.TillNumeric.ShouldBeNull();
    }

    [Fact]
    public void PostalCodeRange_accepts_equal_bounds_and_zero()
    {
        var range = new GeographicalRegionPostalCodeRange(Guid.NewGuid(), Tenant, Guid.NewGuid());

        range.Set(Guid.NewGuid(), 0, 0, "");

        range.FromNumeric.ShouldBe(0);
        range.TillNumeric.ShouldBe(0);
        range.Prefix.ShouldBeNull();
    }

    [Theory]
    [InlineData(1000, null, null)]
    [InlineData(null, 1999, null)]
    [InlineData(2000, 1999, null)]
    [InlineData(-1, 1999, null)]
    [InlineData(null, null, null)]
    [InlineData(null, null, "")]
    [InlineData(null, null, "   ")]
    [InlineData(1000, 1999, "SW")]
    [InlineData(1000, null, "SW")]
    [InlineData(null, null, "ABCDEFGHIJKLMNOPQ")]
    public void PostalCodeRange_rejects_an_incomplete_mixed_or_too_long_form(int? from, int? till, string? prefix)
    {
        var range = new GeographicalRegionPostalCodeRange(Guid.NewGuid(), Tenant, Guid.NewGuid());
        range.Set(Guid.NewGuid(), 1, 2, null);

        var ex = Should.Throw<BusinessException>(() => range.Set(Guid.NewGuid(), from, till, prefix));

        ex.Code.ShouldBe(MasterDataErrorCodes.InvalidPostalCodeRange);
        range.FromNumeric.ShouldBe(1);
        range.TillNumeric.ShouldBe(2);
    }

    [Fact]
    public void PostalCodeRange_accepts_a_prefix_of_exactly_16_characters_after_normalisation()
    {
        var range = new GeographicalRegionPostalCodeRange(Guid.NewGuid(), Tenant, Guid.NewGuid());

        range.Set(Guid.NewGuid(), null, null, "abcd efgh ijkl mnop");

        range.Prefix.ShouldBe("ABCDEFGHIJKLMNOP");
    }

    [Fact]
    public void GeographicalRegion_ranges_are_replaced_keeping_rows_matched_by_id()
    {
        var region = new GeographicalRegion(Guid.NewGuid(), Tenant, "Randstad");
        var nl = Guid.NewGuid();
        var uk = Guid.NewGuid();
        region.SetPostalCodeRanges([(null, nl, 1000, 1999, null), (null, nl, 2000, 2999, null)], Guid.NewGuid);
        var first = region.PostalCodeRanges.Single(r => r.FromNumeric == 1000);
        var second = region.PostalCodeRanges.Single(r => r.FromNumeric == 2000);
        first.TenantId.ShouldBe(Tenant);
        first.GeographicalRegionId.ShouldBe(region.Id);

        region.SetPostalCodeRanges([(first.Id, nl, 1000, 1499, null), (null, uk, null, null, "SW")], Guid.NewGuid);

        region.PostalCodeRanges.Count.ShouldBe(2);
        var kept = region.PostalCodeRanges.Single(r => r.Id == first.Id);
        kept.TillNumeric.ShouldBe(1499);
        region.PostalCodeRanges.ShouldNotContain(r => r.Id == second.Id);
        region.PostalCodeRanges.ShouldContain(r => r.CountryId == uk && r.Prefix == "SW");
    }

    [Fact]
    public void GeographicalRegion_unknown_range_id_is_treated_as_a_new_row()
    {
        var region = new GeographicalRegion(Guid.NewGuid(), Tenant, "Randstad");
        var unknownId = Guid.NewGuid();

        region.SetPostalCodeRanges([(unknownId, Guid.NewGuid(), 1000, 1999, null)], Guid.NewGuid);

        region.PostalCodeRanges.Single().Id.ShouldNotBe(unknownId);
    }

    [Fact]
    public void GeographicalRegion_invalid_range_in_the_set_throws_before_the_set_is_applied_partially()
    {
        var region = new GeographicalRegion(Guid.NewGuid(), Tenant, "Randstad");
        var nl = Guid.NewGuid();

        Should.Throw<BusinessException>(() => region.SetPostalCodeRanges([(null, nl, 1000, 1999, null), (null, nl, 5000, 4000, null)], Guid.NewGuid))
            .Code.ShouldBe(MasterDataErrorCodes.InvalidPostalCodeRange);
    }

    // ── TransportCharacteristics ──────────────────────────────────────────────

    [Fact]
    public void TransportCharacteristics_normalises_the_code_and_trims_the_name()
    {
        var characteristics = new TransportCharacteristics(Guid.NewGuid(), Tenant, " cool ", " Cooled ");

        characteristics.Code.ShouldBe("COOL");
        characteristics.Name.ShouldBe("Cooled");
        Should.Throw<BusinessException>(() => characteristics.SetCode("  ")).Code.ShouldBe(MasterDataErrorCodes.InvalidCode);
    }
}
