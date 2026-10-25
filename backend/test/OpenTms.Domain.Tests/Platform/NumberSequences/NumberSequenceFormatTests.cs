using System;
using OpenTms.Platform.NumberSequences;
using OpenTms.Platform.ReferenceData;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace OpenTms.Platform.NumberSequences;

/* Design 50: the format and period rules. The frontend mirror (src/lib/reference-data/number-sequence.ts) is tested
 * against the same table. */
public class NumberSequenceFormatTests
{
    private static readonly DateOnly October22 = new(2026, 10, 22);

    [Theory]
    [InlineData("TO", true, true, "-", 5, 42, "TO-2026-10-00042")]
    [InlineData("TO", true, false, "-", 5, 42, "TO-2026-00042")]
    [InlineData("TO", false, false, "-", 5, 42, "TO-00042")]
    [InlineData("TO", false, false, "", 5, 42, "TO00042")]
    [InlineData(null, true, false, "/", 3, 7, "2026/007")]
    [InlineData(null, false, false, "", 3, 7, "007")]
    [InlineData("f", true, false, ".", 4, 12345, "F.2026.12345")]
    [InlineData("TR_", false, false, "", 10, 1, "TR_0000000001")]
    public void Formats_prefix_year_month_and_a_padded_counter(string? prefix, bool year, bool month, string separator, int digits, long counter, string expected)
    {
        NumberSequenceFormat.Create(prefix, year, month, separator, digits).Format(counter, October22).ShouldBe(expected);
    }

    [Theory]
    [InlineData(false, false, "")]
    [InlineData(true, false, "2026")]
    [InlineData(true, true, "2026-10")]
    public void The_period_follows_year_and_month(bool year, bool month, string expected)
    {
        NumberSequenceFormat.Create("X", year, month, "-", 5).Period(October22).ShouldBe(expected);
    }

    [Theory]
    [InlineData("TO 1", true, false, "-", 5)]
    [InlineData("12345678901234567", false, false, "-", 5)]
    [InlineData("TO", false, true, "-", 5)]
    [InlineData("TO", true, false, "#", 5)]
    [InlineData("TO", true, false, "-", 2)]
    [InlineData("TO", true, false, "-", 11)]
    public void Refuses_an_invalid_format(string prefix, bool year, bool month, string separator, int digits)
    {
        var error = Should.Throw<BusinessException>(() => NumberSequenceFormat.Create(prefix, year, month, separator, digits));
        error.Code.ShouldBe(ReferenceDataErrorCodes.InvalidNumberSequence);
    }

    [Fact]
    public void A_counter_that_outgrows_its_digits_gets_longer()
    {
        NumberSequenceFormat.Create("A", false, false, "", 3).Format(12345, October22).ShouldBe("A12345");
    }

    [Fact]
    public void Blank_prefix_is_no_prefix_and_equal_formats_are_equal()
    {
        var a = NumberSequenceFormat.Create("  ", false, false, null, 5);
        a.Prefix.ShouldBeNull();
        a.Separator.ShouldBe("");
        a.ShouldBe(NumberSequenceFormat.Create(null, false, false, "", 5));
    }
}
