using Shouldly;
using Xunit;

namespace OpenTms.MasterData;

/// <summary>
/// Unit tests for the shared normalisation and shape rules of run 2 (design 43: Relation, "Rule" column;
/// RelationBankAccount IBAN mod 97; the SearchKey the lookup and the matcher run on). Risk class: Critical
/// for the IBAN check (a wrong IBAN on a carrier settlement is money; a false negative blocks every
/// creditor), High for the VAT shape and the search-key folding the hottest lookup depends on.
/// </summary>
public class MasterDataValuesTests
{
    // ── IBAN (ISO 7064 mod 97-10) ─────────────────────────────────────────────

    [Theory]
    [InlineData("NL91ABNA0417164300")]
    [InlineData("NL20INGB0001234567")]
    [InlineData("NL39RABO0300065264")]
    [InlineData("DE89370400440532013000")]
    [InlineData("BE68539007547034")]
    [InlineData("GB82WEST12345698765432")]
    [InlineData("FR7630006000011234567890189")]
    public void IsValidIban_accepts_ibans_with_a_correct_check_digit(string iban)
    {
        MasterDataValues.IsValidIban(iban).ShouldBeTrue(iban);
    }

    [Theory]
    [InlineData("NL91ABNA0417164301", "last digit changed")]
    [InlineData("NL92ABNA0417164300", "check digits changed")]
    [InlineData("NL91ABNA0417164030", "two digits swapped")]
    [InlineData("NL91ABNA04171643", "too short (below 15)")]
    [InlineData("NL91ABNA0417164300000000000000000000", "too long (above 34)")]
    [InlineData("9191ABNA0417164300", "country code must be letters")]
    [InlineData("NL91ABNA041716430O", "letter O instead of zero")]
    [InlineData("NL91 ABNA 0417 1643 00", "spaces are not allowed in the normalised form")]
    [InlineData("nl91abna0417164300", "lower case is not the normalised form")]
    [InlineData("", "empty")]
    public void IsValidIban_rejects_wrong_check_digits_lengths_and_characters(string iban, string because)
    {
        MasterDataValues.IsValidIban(iban).ShouldBeFalse(because);
    }

    [Theory]
    [InlineData(" nl91 abna 0417 1643 00 ", "NL91ABNA0417164300")]
    [InlineData("NL91ABNA0417164300", "NL91ABNA0417164300")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void NormalizeIban_removes_spaces_upper_cases_and_turns_empty_into_null(string? input, string? expected)
    {
        MasterDataValues.NormalizeIban(input).ShouldBe(expected);
    }

    [Fact]
    public void A_typed_iban_is_valid_after_normalisation()
    {
        var normalised = MasterDataValues.NormalizeIban("nl91 abna 0417 1643 00");
        normalised.ShouldNotBeNull();
        MasterDataValues.IsValidIban(normalised).ShouldBeTrue();
    }

    // ── VAT and chamber-of-commerce numbers ──────────────────────────────────

    [Theory]
    [InlineData(" nl 8012.34.567.b01 ", "NL801234567B01")]
    [InlineData("DE 123 456 789", "DE123456789")]
    [InlineData("12-34-56-78", "12345678")]
    [InlineData("kvk 12345678", "KVK12345678")]
    [InlineData("", null)]
    [InlineData(" . ", null)]
    [InlineData(null, null)]
    public void NormalizeLegalNumber_removes_spaces_dots_and_dashes_and_upper_cases(string? input, string? expected)
    {
        MasterDataValues.NormalizeLegalNumber(input).ShouldBe(expected);
    }

    [Theory]
    [InlineData("NL801234567B01", true)]
    [InlineData("DE123456789", true)]
    [InlineData("BE0123456789", true)]
    [InlineData("GB12", true)]
    [InlineData("FR1234567890123", true)]
    [InlineData("N1801234567B01", false)]
    [InlineData("NL1", false)]
    [InlineData("NL12345678901234", false)]
    [InlineData("NL8012.34567B01", false)]
    [InlineData("nl801234567b01", false)]
    [InlineData("801234567B01", false)]
    [InlineData("", false)]
    public void IsVatNumberShape_requires_two_letters_and_2_to_13_alphanumerics(string value, bool expected)
    {
        MasterDataValues.IsVatNumberShape(value).ShouldBe(expected);
    }

    // ── E-mail shape ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("planner@acme.nl", true)]
    [InlineData("first.last+tag@sub.example.co.uk", true)]
    [InlineData("planner@acme", false)]
    [InlineData("planner acme.nl", false)]
    [InlineData("@acme.nl", false)]
    [InlineData("planner@", false)]
    [InlineData("a@b@c.nl", false)]
    [InlineData("", false)]
    public void IsEmailShape_requires_one_at_sign_and_a_dotted_domain(string value, bool expected)
    {
        MasterDataValues.IsEmailShape(value).ShouldBe(expected);
    }

    // ── SearchKey ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(new[] { "ACME Transport" }, "acme transport")]
    [InlineData(new[] { "Müller Logistik" }, "muller logistik")]
    [InlineData(new[] { "Ångström & Zonen" }, "angstrom zonen")]
    [InlineData(new[] { "Jansen  &  Zn.  B.V." }, "jansen zn b v")]
    [InlineData(new[] { "  padded   name  " }, "padded name")]
    [InlineData(new[] { "Café-Éclair (Ltd.)" }, "cafe eclair ltd")]
    [InlineData(new[] { "ACME Transport", "ACM01" }, "acme transport acm01")]
    [InlineData(new[] { "Name", null, "", "  ", "CODE" }, "name code")]
    [InlineData(new[] { "", null }, "")]
    [InlineData(new string?[0], "")]
    public void SearchKey_lower_cases_folds_diacritics_and_collapses_punctuation_and_whitespace(string?[] parts, string expected)
    {
        MasterDataValues.SearchKey(parts).ShouldBe(expected);
    }

    [Fact]
    public void SearchKey_contains_no_upper_case_no_double_spaces_and_no_punctuation_for_any_input()
    {
        var key = MasterDataValues.SearchKey("ÄÖÜ ß — 'quoted' \"double\" tab\tnew\nline, semi; colon: slash/back\\slash");

        key.ShouldBe(key.ToLowerInvariant());
        key.ShouldNotContain("  ");
        key.ShouldNotStartWith(" ");
        key.ShouldNotEndWith(" ");
        key.ShouldAllBe(c => char.IsLetterOrDigit(c) || c == ' ');
    }

    [Fact]
    public void SearchKey_is_cut_to_the_column_length()
    {
        /* Name, full name and code fill the column (128 + 128 + 32 plus separators); a fourth part is cut. */
        var key = MasterDataValues.SearchKey(new string('a', 128), new string('b', 128), new string('c', 32), new string('d', 64));

        key.Length.ShouldBe(MasterDataConsts.MaxSearchKeyLength);
        key.ShouldStartWith(new string('a', 128) + " ");
    }

    [Fact]
    public void SearchKey_of_a_term_matches_the_key_of_the_stored_name_it_was_typed_for()
    {
        /* The lookup folds the typed term the same way as the stored name, so these pairs must agree. */
        MasterDataValues.SearchKey("ACME Transport B.V.").ShouldContain(MasterDataValues.SearchKey("acme transport"));
        MasterDataValues.SearchKey("Müller Logistik").ShouldContain(MasterDataValues.SearchKey("MULLER"));
        MasterDataValues.SearchKey("Müller Logistik").ShouldContain(MasterDataValues.SearchKey("müller"));
    }

    // ── Culture ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(" NL ", "nl")]
    [InlineData("en", "en")]
    [InlineData("", null)]
    [InlineData("  ", null)]
    [InlineData(null, null)]
    public void NormalizeCulture_lower_cases_and_turns_empty_into_null(string? input, string? expected)
    {
        MasterDataValues.NormalizeCulture(input).ShouldBe(expected);
    }
}
