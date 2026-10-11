using System;
using OpenTms.MasterData.Regions;
using Shouldly;
using Xunit;

namespace OpenTms.MasterData.Regions;

/// <summary>
/// Table-driven unit tests for the postcode matcher (design 41, "Postal-code matching").
/// Risk class: High — the matcher decides which region (and later which tariff and planning zone)
/// a stop falls in; the OpenTMS bug it fixes made every numeric range match a UK postcode.
/// </summary>
public class PostalCodeRangeMatcherTests
{
    [Theory]
    // numeric range, Dutch postcodes
    [InlineData(1000, 1999, null, "1234AB", true)]
    [InlineData(1000, 1999, null, "1234 AB", true)]
    [InlineData(1000, 1999, null, " 1234 ab ", true)]
    [InlineData(1000, 1999, null, "1000", true)]
    [InlineData(1000, 1999, null, "1000AA", true)]
    [InlineData(1000, 1999, null, "1999ZZ", true)]
    [InlineData(1000, 1999, null, "0999", false)]
    [InlineData(1000, 1999, null, "999", false)]
    [InlineData(1000, 1999, null, "2000", false)]
    [InlineData(1000, 1999, null, "2000AA", false)]
    // numeric range, German five-digit postcode
    [InlineData(80000, 81999, null, "80331", true)]
    [InlineData(80000, 81999, null, "82000", false)]
    // numeric range, postcode without leading digits never matches (assessment §5)
    [InlineData(1000, 1999, null, "SW1A1AA", false)]
    [InlineData(1000, 1999, null, "SW1A 1AA", false)]
    [InlineData(0, 999999999, null, "SW1A1AA", false)]
    [InlineData(0, 999999999, null, "D02X285", false)]
    // numeric range, empty postcode
    [InlineData(1000, 1999, null, "", false)]
    [InlineData(1000, 1999, null, "   ", false)]
    [InlineData(1000, 1999, null, null, false)]
    // prefix range
    [InlineData(null, null, "SW", "SW1A1AA", true)]
    [InlineData(null, null, "SW", "sw1a 1aa", true)]
    [InlineData(null, null, "SW", " sw 1a 1aa ", true)]
    [InlineData(null, null, "SW1A", "SW1A1AA", true)]
    [InlineData(null, null, "SW", "W1A1AA", false)]
    [InlineData(null, null, "SW", "", false)]
    [InlineData(null, null, "SW", null, false)]
    // prefix stored with spaces or lower case still matches
    [InlineData(null, null, " s w ", "SW1A1AA", true)]
    [InlineData(null, null, "sw 1", "SW1A1AA", true)]
    [InlineData(null, null, "12", "1234AB", true)]
    // a prefix takes precedence over numeric bounds when both are given
    [InlineData(1000, 1999, "SW", "SW1A1AA", true)]
    [InlineData(1000, 1999, "SW", "1234AB", false)]
    // no usable form
    [InlineData(null, null, null, "1234AB", false)]
    [InlineData(null, null, "", "1234AB", false)]
    [InlineData(1000, null, null, "1234AB", false)]
    [InlineData(null, 1999, null, "1234AB", false)]
    public void Matches_decides_per_the_design_table(int? from, int? till, string? prefix, string? postalCode, bool expected)
    {
        PostalCodeRangeMatcher.Matches(from, till, prefix, postalCode).ShouldBe(expected);
    }

    [Theory]
    [InlineData("1234AB", 1234)]
    [InlineData("80331", 80331)]
    [InlineData("0001AA", 1)]
    [InlineData("123456789", 123456789)]
    [InlineData("1234567890", null)]
    [InlineData("SW1A1AA", null)]
    [InlineData("", null)]
    [InlineData("A1", null)]
    public void LeadingNumber_takes_the_leading_digit_run_or_nothing(string normalizedPostalCode, int? expected)
    {
        PostalCodeRangeMatcher.LeadingNumber(normalizedPostalCode).ShouldBe(expected);
    }

    [Fact]
    public void Matches_on_a_range_entity_uses_its_stored_form()
    {
        var numeric = new GeographicalRegionPostalCodeRange(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        numeric.Set(Guid.NewGuid(), 1000, 1999, null);

        var prefix = new GeographicalRegionPostalCodeRange(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        prefix.Set(Guid.NewGuid(), null, null, " sw ");

        PostalCodeRangeMatcher.Matches(numeric, "1234 AB").ShouldBeTrue();
        PostalCodeRangeMatcher.Matches(numeric, "SW1A 1AA").ShouldBeFalse();
        PostalCodeRangeMatcher.Matches(prefix, "sw1a 1aa").ShouldBeTrue();
        PostalCodeRangeMatcher.Matches(prefix, "1234 AB").ShouldBeFalse();
    }
}
