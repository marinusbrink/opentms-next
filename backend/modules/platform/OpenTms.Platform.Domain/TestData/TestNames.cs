using System;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;

namespace OpenTms.Platform.TestData;

/// <summary>
/// Fictitious but realistic Dutch, Belgian and German names and identifiers for test datasets. Companies and
/// people are invented (paired with real addresses from <see cref="TestAddressBook"/>); e-mail domains are
/// subdomains of the reserved <c>example.com</c> (RFC 2606), so no test record can ever reach a real mailbox;
/// IBAN and VAT numbers carry valid check digits but invented account numbers.
/// </summary>
public static class TestNames
{
    private static readonly string[] NlSurnames =
    {
        "de Jong", "Jansen", "de Vries", "van den Berg", "van Dijk", "Bakker", "Janssen", "Visser", "Smit", "Meijer",
        "de Boer", "Mulder", "de Groot", "Bos", "Vos", "Peters", "Hendriks", "van Leeuwen", "Dekker", "Brouwer",
        "de Wit", "Dijkstra", "Smits", "de Graaf", "van der Meer", "van der Linden", "Kok", "Jacobs", "de Haan",
        "Vermeulen", "van den Heuvel", "van der Veen", "van den Broek", "de Bruijn", "van der Heijden", "Schouten",
        "van Beek", "Willems", "van Vliet", "van de Ven", "Hoekstra", "Maas", "Verhoeven", "Koster", "van Dam",
        "van der Wal", "Prins", "Blom", "Huisman", "de Jonge", "Kuipers", "van Veen", "Post", "Kuiper", "Veenstra",
        "Kramer", "van den Brink", "Scholten", "van Wijk", "Postma", "Martens", "Vink", "de Ridder", "Jonker",
        "van Rijn", "Bosch", "Kamp", "Hermans", "Wouters", "Hofman", "Timmermans", "Groen", "Gerritsen", "Koning",
        "Evers", "Verbeek", "Lammers", "Sanders", "de Lange", "Molenaar", "Boersma", "Wijnands", "Tromp", "Zwart"
    };

    private static readonly string[] BeSurnames =
    {
        "Peeters", "Janssens", "Maes", "Jacobs", "Mertens", "Willems", "Claes", "Goossens", "Wouters", "De Smet",
        "Dubois", "Lambert", "Dupont", "Martin", "Simon", "Leclercq", "Laurent", "Renard", "Lejeune", "Van Damme",
        "Hermans", "Michiels", "Desmet", "Lemmens", "Pauwels", "Cools", "Dumont", "Lenaerts", "Aerts", "Smets",
        "Coppens", "De Backer", "Leroy", "Stevens", "Verstraete", "Segers", "Vandenberghe", "Declercq", "Van Acker",
        "Wuyts", "Verhaegen", "Bogaerts", "Thys", "Mathieu", "Denis", "Vermeersch", "De Clercq", "Hendrickx"
    };

    private static readonly string[] DeSurnames =
    {
        "Müller", "Schmidt", "Schneider", "Fischer", "Weber", "Meyer", "Wagner", "Becker", "Schulz", "Hoffmann",
        "Schäfer", "Koch", "Bauer", "Richter", "Klein", "Wolf", "Schröder", "Neumann", "Schwarz", "Zimmermann",
        "Braun", "Krüger", "Hofmann", "Hartmann", "Lange", "Schmitt", "Werner", "Schmitz", "Krause", "Meier",
        "Lehmann", "Schmid", "Schulze", "Maier", "Köhler", "Herrmann", "König", "Walter", "Mayer", "Huber",
        "Kaiser", "Fuchs", "Peters", "Lang", "Scholz", "Möller", "Weiß", "Jung", "Hahn", "Vogel", "Brandt", "Otto"
    };

    private static readonly string[] NlFirstNames =
    {
        "Daan", "Sem", "Lucas", "Levi", "Finn", "Bram", "Thijs", "Jesse", "Ruben", "Lars", "Tim", "Kevin", "Mark",
        "Peter", "Jan", "Erik", "Sander", "Bas", "Joost", "Wouter", "Emma", "Julia", "Sophie", "Anna", "Lisa", "Eva",
        "Sanne", "Fleur", "Iris", "Lotte", "Marieke", "Anouk", "Linda", "Ingrid", "Monique", "Esther", "Femke", "Noor"
    };

    private static readonly string[] BeFirstNames =
    {
        "Wout", "Arne", "Jens", "Pieter", "Koen", "Stijn", "Thomas", "Nicolas", "Julien", "Maxime", "Olivier",
        "Bart", "Dirk", "Lieve", "Els", "Ann", "Katrien", "Sofie", "Charlotte", "Camille", "Manon", "Marie", "Elise"
    };

    private static readonly string[] DeFirstNames =
    {
        "Lukas", "Leon", "Jonas", "Felix", "Maximilian", "Paul", "Tobias", "Stefan", "Michael", "Thomas", "Andreas",
        "Jürgen", "Klaus", "Mia", "Hannah", "Lena", "Laura", "Katharina", "Sabine", "Petra", "Claudia", "Anja", "Jana"
    };

    private static readonly string[] NlIndustries =
    {
        "Transport", "Logistiek", "Distributie", "Groothandel", "Bouwmaterialen", "Metaalbewerking", "Kunststoffen",
        "Tuinbouw", "Kwekerij", "Zuivel", "Kaashandel", "Vleeswaren", "Bakkerij", "Meubelfabriek", "Papier",
        "Verpakkingen", "Installatietechniek", "Agrarische Handel", "Food Group", "Machinebouw", "Staalbouw",
        "Houthandel", "Diervoeders", "Elektrotechniek", "Bloemen", "Fruit", "Dranken", "Koeltransport", "Containers"
    };

    private static readonly string[] BeIndustries =
    {
        "Transport", "Logistics", "Distribution", "Bouwmaterialen", "Chemie", "Metaal", "Voeding", "Groenten",
        "Brouwerij", "Textiel", "Verpakking", "Staal", "Négoce", "Matériaux", "Alimentation", "Expeditie"
    };

    private static readonly string[] DeIndustries =
    {
        "Spedition", "Logistik", "Transporte", "Baustoffe", "Metallbau", "Maschinenbau", "Kunststofftechnik",
        "Lebensmittel", "Getränke", "Holzhandel", "Papierfabrik", "Verpackung", "Agrarhandel", "Stahlhandel",
        "Elektrotechnik", "Möbelwerk", "Kühllogistik", "Großhandel", "Chemie"
    };

    private static readonly string[] NlForms = { "B.V.", "B.V.", "B.V.", "B.V.", "V.O.F.", "N.V.", "Holding B.V." };
    private static readonly string[] BeForms = { "BV", "BV", "NV", "SRL", "SA", "CommV" };
    private static readonly string[] DeForms = { "GmbH", "GmbH", "GmbH", "GmbH & Co. KG", "AG", "e.K.", "KG" };

    private static readonly string[] NlAreaCodes = { "10", "13", "20", "24", "26", "30", "35", "38", "40", "43", "45", "50", "53", "58", "70", "73", "74", "76", "77", "78", "85", "88" };
    private static readonly string[] BeAreaCodes = { "2", "3", "9", "4", "50", "51", "56", "57", "11", "13", "14", "15", "16", "71", "81" };
    private static readonly string[] DeAreaCodes = { "30", "40", "69", "89", "211", "221", "231", "201", "241", "251", "521", "561", "341", "351" };

    private static readonly string[] NlBanks = { "INGB", "RABO", "ABNA", "TRIO", "SNSB", "KNAB", "BUNQ" };
    private static readonly string[] BeBanks = { "001", "063", "068", "096", "143", "310", "363", "551", "645", "737" };
    private static readonly string[] DeBanks = { "37040044", "10070000", "50010517", "20041133", "70020270", "60050101", "30050110" };

    public static string PersonFirstName(Random random, string countryCode) =>
        Pick(random, countryCode switch { "BE" => BeFirstNames, "DE" => DeFirstNames, _ => NlFirstNames });

    public static string PersonLastName(Random random, string countryCode) =>
        Pick(random, countryCode switch { "BE" => BeSurnames, "DE" => DeSurnames, _ => NlSurnames });

    /// <summary>An invented company name in the style of the country, e.g. "Van der Meer Transport B.V.".</summary>
    public static string CompanyName(Random random, string countryCode, string? city = null)
    {
        var industries = countryCode switch { "BE" => BeIndustries, "DE" => DeIndustries, _ => NlIndustries };
        var forms = countryCode switch { "BE" => BeForms, "DE" => DeForms, _ => NlForms };
        var surname = Capitalize(PersonLastName(random, countryCode));
        var industry = Pick(random, industries);
        var form = Pick(random, forms);

        return random.Next(6) switch
        {
            0 when city != null => $"{industry} {city} {form}",
            1 => $"{surname} & {Capitalize(PersonLastName(random, countryCode))} {form}",
            2 => $"{Acronym(random)} {industry} {form}",
            3 => $"{surname} {industry} {form}",
            4 => countryCode == "DE" ? $"{industry} {surname} {form}" : $"{surname} {industry} en {Pick(random, industries).ToLowerInvariant()} {form}",
            _ => $"{surname} {industry} {form}"
        };
    }

    /// <summary>A short company code, e.g. "JANSTR" — six letters from the name, plus digits for uniqueness by the caller.</summary>
    public static string Code(string name, int maxLength = 6)
    {
        var letters = new string(Slug(name).Where(char.IsLetter).Select(char.ToUpperInvariant).ToArray());
        return letters.Length <= maxLength ? letters : letters[..maxLength];
    }

    /// <summary>A safe e-mail domain for a company: <c>{slug}.example.com</c>.</summary>
    public static string EmailDomain(string companyName)
    {
        var slug = Slug(companyName);
        if (slug.Length > 40)
        {
            slug = slug[..40].Trim('-');
        }

        return $"{slug}.example.com";
    }

    public static string Email(string firstName, string lastName, string domain) =>
        $"{Slug(firstName)}.{Slug(lastName).Replace("-", string.Empty)}@{domain}";

    public static string Phone(Random random, string countryCode) => countryCode switch
    {
        "BE" => FormatBe(random),
        "DE" => FormatDe(random),
        _ => FormatNl(random)
    };

    public static string Mobile(Random random, string countryCode) => countryCode switch
    {
        "BE" => $"+32 4{random.Next(70, 100)} {Digits(random, 2)} {Digits(random, 2)} {Digits(random, 2)}",
        "DE" => $"+49 1{Pick(random, new[] { "51", "52", "60", "62", "70", "71", "76" })} {Digits(random, 8)}",
        _ => $"+31 6 {Digits(random, 4)} {Digits(random, 4)}"
    };

    /// <summary>An IBAN with valid check digits (ISO 13616) at an existing bank code, with an invented account.</summary>
    public static string Iban(Random random, string countryCode)
    {
        string bban = countryCode switch
        {
            "BE" => BeBban(random),
            "DE" => Pick(random, DeBanks) + Digits(random, 10),
            _ => Pick(random, NlBanks) + Digits(random, 10)
        };

        var checkDigits = 98 - Mod97(bban + countryCode + "00");
        return $"{countryCode}{checkDigits:00}{bban}";
    }

    /// <summary>A VAT identification number with the national check (BE mod 97) or a plausible format (NL, DE).</summary>
    public static string VatNumber(Random random, string countryCode)
    {
        switch (countryCode)
        {
            case "BE":
                var body = random.Next(2_000_000, 9_999_999) * 1L; // 0 + 7 digits, then 2 check digits
                var check = 97 - (body % 97);
                return $"BE0{body:0000000}{check:00}";
            case "DE":
                return $"DE{random.Next(100_000_000, 999_999_999)}";
            default:
                return $"NL{random.Next(100_000_000, 999_999_999)}B{random.Next(1, 4):00}";
        }
    }

    /// <summary>A chamber-of-commerce / company register number: KvK (8 digits), KBO (10), HRB.</summary>
    public static string RegistrationNumber(Random random, string countryCode) => countryCode switch
    {
        "BE" => $"0{random.Next(200_000_000, 999_999_999)}",
        "DE" => $"HRB {random.Next(1_000, 99_999)}",
        _ => random.Next(10_000_000, 99_999_999).ToString(CultureInfo.InvariantCulture)
    };

    /// <summary>A Dutch-style licence plate (sidecode 7–11), e.g. "12-ABC-3" or "AB-123-C".</summary>
    public static string LicensePlate(Random random, string countryCode)
    {
        const string letters = "BDFGHJKLNPRSTVXZ";
        string L(int n) => new(Enumerable.Range(0, n).Select(_ => letters[random.Next(letters.Length)]).ToArray());
        return countryCode switch
        {
            "BE" => $"{random.Next(1, 3)}-{L(3)}-{Digits(random, 3)}",
            "DE" => $"{Pick(random, new[] { "D", "K", "DU", "E", "AC", "MS", "BOR", "KLE" })}-{L(2)} {random.Next(1, 9999)}",
            _ => random.Next(3) switch
            {
                0 => $"{Digits(random, 2)}-{L(3)}-{random.Next(1, 10)}",
                1 => $"{L(2)}-{Digits(random, 3)}-{L(1)}",
                _ => $"{random.Next(1, 10)}-{L(3)}-{Digits(random, 2)}"
            }
        };
    }

    public static string Slug(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        var dash = false;
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            var ch = c == 'ß' ? 's' : char.ToLowerInvariant(c);
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                builder.Append(ch);
                dash = false;
            }
            else if (!dash && builder.Length > 0)
            {
                builder.Append('-');
                dash = true;
            }
        }

        return builder.ToString().Trim('-');
    }

    public static T Pick<T>(Random random, T[] values) => values[random.Next(values.Length)];

    public static string Digits(Random random, int count)
    {
        var chars = new char[count];
        for (var i = 0; i < count; i++)
        {
            chars[i] = (char)('0' + random.Next(10));
        }

        return new string(chars);
    }

    private static string FormatNl(Random random)
    {
        var area = Pick(random, NlAreaCodes);
        return $"+31 {area} {Digits(random, 9 - area.Length)}";
    }

    private static string FormatBe(Random random)
    {
        var area = Pick(random, BeAreaCodes);
        return $"+32 {area} {Digits(random, 8 - area.Length)}";
    }

    private static string FormatDe(Random random)
    {
        var area = Pick(random, DeAreaCodes);
        return $"+49 {area} {Digits(random, 10 - area.Length)}";
    }

    private static string BeBban(Random random)
    {
        var first10 = Pick(random, BeBanks) + Digits(random, 7);
        var check = long.Parse(first10, CultureInfo.InvariantCulture) % 97;
        return first10 + (check == 0 ? 97 : check).ToString("00", CultureInfo.InvariantCulture);
    }

    private static int Mod97(string value)
    {
        var numeric = new StringBuilder();
        foreach (var c in value)
        {
            numeric.Append(char.IsLetter(c) ? (char.ToUpperInvariant(c) - 'A' + 10).ToString(CultureInfo.InvariantCulture) : c.ToString());
        }

        return (int)(BigInteger.Parse(numeric.ToString(), CultureInfo.InvariantCulture) % 97);
    }

    private static string Acronym(Random random)
    {
        const string letters = "ABCDEFGHJKLMNPRSTVW";
        return new string(Enumerable.Range(0, random.Next(2, 4)).Select(_ => letters[random.Next(letters.Length)]).ToArray());
    }

    private static string Capitalize(string surname) =>
        surname.Length == 0 ? surname : char.ToUpperInvariant(surname[0]) + surname[1..];
}
