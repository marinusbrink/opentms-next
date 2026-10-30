using System;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using Shouldly;
using Xunit;

namespace OpenTms.Platform.TestData;

/// <summary>
/// <see cref="TestNames"/> (docs/designs/65-tenant-test-datasets.md, "Domain impact"): invented names whose identifiers
/// look real. Risk class: Critical by the run they serve (a host action that rewrites a tenant's data), and the
/// <c>example.com</c> rule is the guarantee that no generated record can reach a real mailbox (constitution rule 5); the
/// IBAN/VAT check digits keep generated relations valid for the Financial validators.
/// </summary>
public class TestNamesTests
{
    private static readonly string[] Countries = { "NL", "BE", "DE" };

    private static readonly Regex SafeDomain = new(@"^[a-z0-9]+(-[a-z0-9]+)*\.example\.com$", RegexOptions.CultureInvariant);

    /* ── IBAN (ISO 13616: mod 97 of the rearranged number is 1) ─────────────────────────────── */

    [Theory]
    [InlineData("NL", 18)]
    [InlineData("BE", 16)]
    [InlineData("DE", 22)]
    public void Iban_has_valid_mod97_check_digits_and_the_national_length(string country, int length)
    {
        var random = new Random(97);
        for (var i = 0; i < 2_000; i++)
        {
            var iban = TestNames.Iban(random, country);

            iban.Length.ShouldBe(length, iban);
            iban.ShouldStartWith(country);
            iban.All(char.IsAsciiLetterOrDigit).ShouldBeTrue(iban);
            IbanMod97(iban).ShouldBe(1, $"{iban} fails the ISO 13616 check");
        }
    }

    [Fact]
    public void Nl_iban_carries_a_four_letter_bank_code_and_ten_digits()
    {
        var random = new Random(1);
        for (var i = 0; i < 200; i++)
        {
            TestNames.Iban(random, "NL").ShouldMatch(@"^NL\d{2}[A-Z]{4}\d{10}$");
        }
    }

    [Fact]
    public void Be_iban_carries_a_valid_national_check_on_the_bban()
    {
        /* Belgian BBAN: 10 digits + 2 check digits = first 10 mod 97 (97 when 0). */
        var random = new Random(2);
        for (var i = 0; i < 2_000; i++)
        {
            var bban = TestNames.Iban(random, "BE")[4..];
            var first10 = long.Parse(bban[..10], CultureInfo.InvariantCulture);
            var check = int.Parse(bban[10..], CultureInfo.InvariantCulture);
            check.ShouldBe(first10 % 97 == 0 ? 97 : (int)(first10 % 97), bban);
        }
    }

    [Fact]
    public void Iban_detects_a_single_changed_digit()
    {
        /* Proves the check above is not vacuous. */
        var iban = TestNames.Iban(new Random(3), "NL");
        var last = iban[^1];
        var changed = iban[..^1] + (last == '9' ? '0' : (char)(last + 1));
        IbanMod97(changed).ShouldNotBe(1);
    }

    /* ── VAT ────────────────────────────────────────────────────────────────────────────────── */

    [Fact]
    public void Be_vat_number_passes_the_national_mod97_check()
    {
        var random = new Random(4);
        for (var i = 0; i < 5_000; i++)
        {
            var vat = TestNames.VatNumber(random, "BE");

            vat.ShouldMatch(@"^BE0\d{9}$");
            var first8 = long.Parse(vat[2..10], CultureInfo.InvariantCulture);
            var check = int.Parse(vat[10..], CultureInfo.InvariantCulture);
            (check + first8 % 97).ShouldBe(97, $"{vat}: check digits are 97 - (first 8 digits mod 97)");
        }
    }

    [Fact]
    public void Nl_and_de_vat_numbers_have_the_national_format()
    {
        var random = new Random(5);
        for (var i = 0; i < 500; i++)
        {
            TestNames.VatNumber(random, "NL").ShouldMatch(@"^NL\d{9}B\d{2}$");
            TestNames.VatNumber(random, "DE").ShouldMatch(@"^DE\d{9}$");
        }
    }

    /* ── E-mail: always under the reserved example.com ──────────────────────────────────────── */

    [Theory]
    [InlineData("Van der Meer Transport B.V.")]
    [InlineData("Müller & Schäfer GmbH & Co. KG")]
    [InlineData("Négoce Matériaux SRL")]
    [InlineData("Weiß Spedition e.K.")]
    [InlineData("A")]
    [InlineData("Een hele lange bedrijfsnaam die veel langer is dan veertig tekens Holding B.V.")]
    [InlineData("gmail.com")]
    [InlineData("evil.example.org")]
    public void Email_domain_always_ends_in_example_com(string companyName)
    {
        var domain = TestNames.EmailDomain(companyName);

        domain.ShouldMatch(SafeDomain.ToString());
        domain.Length.ShouldBeLessThanOrEqualTo(40 + ".example.com".Length);
    }

    [Fact]
    public void Generated_company_names_and_people_always_give_safe_addresses()
    {
        var random = new Random(6);
        foreach (var country in Countries)
        {
            for (var i = 0; i < 1_000; i++)
            {
                var company = TestNames.CompanyName(random, country, i % 2 == 0 ? "'s-Hertogenbosch" : null);
                var domain = TestNames.EmailDomain(company);
                domain.ShouldMatch(SafeDomain.ToString(), company);

                var email = TestNames.Email(TestNames.PersonFirstName(random, country), TestNames.PersonLastName(random, country), domain);
                email.ShouldMatch(@"^[a-z0-9]+(-[a-z0-9]+)*\.[a-z0-9]+@[a-z0-9-]+\.example\.com$", email);
                email.ShouldEndWith(".example.com");
            }
        }
    }

    [Fact]
    public void Email_drops_spaces_and_accents_from_the_person()
    {
        TestNames.Email("Jürgen", "van der Meer", "acme.example.com").ShouldBe("jurgen.vandermeer@acme.example.com");
        TestNames.Email("Hélène", "De Smet", "acme.example.com").ShouldBe("helene.desmet@acme.example.com");
    }

    /* ── Slug ───────────────────────────────────────────────────────────────────────────────── */

    [Theory]
    [InlineData("Müller", "muller")]
    [InlineData("Schäfer & Söhne", "schafer-sohne")]
    [InlineData("Négoce Matériaux", "negoce-materiaux")]
    [InlineData("Façade Hôtel", "facade-hotel")]
    [InlineData("  --Van der Meer B.V.--  ", "van-der-meer-b-v")]
    [InlineData("Köhler   Kühllogistik", "kohler-kuhllogistik")]
    [InlineData("", "")]
    [InlineData("!!!", "")]
    public void Slug_is_lowercase_ascii_with_single_dashes(string value, string expected)
    {
        TestNames.Slug(value).ShouldBe(expected);
    }

    [Theory]
    [InlineData("Weiß")]
    [InlineData("Großhandel")]
    [InlineData("Straße")]
    public void Slug_replaces_sharp_s_by_ascii(string value)
    {
        var slug = TestNames.Slug(value);
        slug.ShouldMatch("^[a-z]+$");
        slug.ShouldNotContain("ß");
        slug.Length.ShouldBeGreaterThanOrEqualTo(value.Length - 1);
    }

    [Fact]
    public void Slug_of_every_surname_and_industry_is_ascii()
    {
        var random = new Random(7);
        foreach (var country in Countries)
        {
            for (var i = 0; i < 500; i++)
            {
                TestNames.Slug(TestNames.CompanyName(random, country)).ShouldMatch("^[a-z0-9]+(-[a-z0-9]+)*$");
            }
        }
    }

    /* ── Other identifiers ──────────────────────────────────────────────────────────────────── */

    [Fact]
    public void Code_is_upper_case_letters_of_at_most_the_given_length()
    {
        TestNames.Code("Van der Meer Transport B.V.").ShouldBe("VANDER");
        TestNames.Code("Müller", 8).ShouldBe("MULLER");
        TestNames.Code("Depot Zwolle", 4).ShouldBe("DEPO");
    }

    [Fact]
    public void Same_seed_gives_the_same_names()
    {
        string Draw(int seed)
        {
            var random = new Random(seed);
            var builder = new StringBuilder();
            foreach (var country in Countries)
            {
                builder.Append(TestNames.CompanyName(random, country)).Append(TestNames.Iban(random, country))
                    .Append(TestNames.VatNumber(random, country)).Append(TestNames.Phone(random, country));
            }

            return builder.ToString();
        }

        Draw(42).ShouldBe(Draw(42));
        Draw(42).ShouldNotBe(Draw(43));
    }

    [Fact]
    public void Phone_numbers_carry_the_country_prefix()
    {
        var random = new Random(8);
        TestNames.Phone(random, "NL").ShouldStartWith("+31 ");
        TestNames.Phone(random, "BE").ShouldStartWith("+32 ");
        TestNames.Phone(random, "DE").ShouldStartWith("+49 ");
        TestNames.Mobile(random, "NL").ShouldStartWith("+31 6 ");
    }

    private static int IbanMod97(string iban)
    {
        var rearranged = iban[4..] + iban[..4];
        var numeric = new StringBuilder();
        foreach (var c in rearranged)
        {
            numeric.Append(char.IsLetter(c) ? (c - 'A' + 10).ToString(CultureInfo.InvariantCulture) : c.ToString());
        }

        return (int)(BigInteger.Parse(numeric.ToString(), CultureInfo.InvariantCulture) % 97);
    }
}
