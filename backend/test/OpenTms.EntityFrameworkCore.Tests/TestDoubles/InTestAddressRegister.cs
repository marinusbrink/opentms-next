using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using OpenTms.Platform.TestData;
using Volo.Abp.DependencyInjection;

namespace OpenTms.TestDoubles;

/// <summary>
/// A small, fixed address register for the "Reset to dataset" tests (docs/designs/65-tenant-test-datasets.md), so no
/// test depends on the embedded <c>addresses.csv.gz</c> extract, which may be absent or still being built. Real Dutch,
/// Belgian and German city centres with plausible streets; 140 NL, 30 BE, 30 DE addresses. Can be told to fail.
/// </summary>
[DisableConventionalRegistration]
public class InTestAddressRegister : TestAddressRegister
{
    private static readonly (string Country, string City, string Postal, double Lat, double Lon, string[] Streets)[] Cities =
    {
        ("NL", "Utrecht", "3511", 52.0907, 5.1214, new[] { "Oudegracht", "Vredenburg", "Lange Viestraat", "Neude" }),
        ("NL", "Zwolle", "8011", 52.5168, 6.0830, new[] { "Melkmarkt", "Diezerstraat", "Grote Markt", "Sassenstraat" }),
        ("NL", "Eindhoven", "5611", 51.4416, 5.4697, new[] { "Markt", "Stratumseind", "Kerkstraat", "Vestdijk" }),
        ("NL", "Rotterdam", "3011", 51.9225, 4.4792, new[] { "Coolsingel", "Meent", "Binnenrotte", "Hoogstraat" }),
        ("NL", "Groningen", "9711", 53.2194, 6.5665, new[] { "Herestraat", "Vismarkt", "Oude Ebbingestraat", "Folkingestraat" }),
        ("NL", "Venlo", "5911", 51.3704, 6.1724, new[] { "Markt", "Parade", "Lomstraat", "Gasthuisstraat" }),
        ("NL", "Tilburg", "5038", 51.5555, 5.0913, new[] { "Heuvel", "Heuvelstraat", "Piusstraat", "Willem II-straat" }),
        ("BE", "Antwerpen", "2000", 51.2194, 4.4025, new[] { "Meir", "Groenplaats", "Kammenstraat", "Lange Nieuwstraat" }),
        ("BE", "Gent", "9000", 51.0543, 3.7174, new[] { "Veldstraat", "Korenmarkt", "Vrijdagmarkt" }),
        ("DE", "Duisburg", "47051", 51.4344, 6.7623, new[] { "Königstraße", "Sonnenwall", "Münzstraße" }),
        ("DE", "Düsseldorf", "40213", 51.2277, 6.7735, new[] { "Königsallee", "Bolkerstraße", "Flinger Straße" }),
    };

    public Exception? Failure { get; set; }

    public int Loads { get; private set; }

    public static IReadOnlyList<TestAddress> Build()
    {
        var addresses = new List<TestAddress>();
        foreach (var (country, city, postal, lat, lon, streets) in Cities)
        {
            var perCity = country == "NL" ? 20 : 15;
            for (var i = 0; i < perCity; i++)
            {
                var street = streets[i % streets.Length];
                var number = (i / streets.Length + 1) * 3 + i % 5;
                var postalCode = country == "NL"
                    ? $"{postal} {(char)('A' + i % 26)}{(char)('B' + i / 26 % 24)}"
                    : postal;
                addresses.Add(new TestAddress(
                    country,
                    street,
                    number.ToString(CultureInfo.InvariantCulture),
                    i % 7 == 3 ? "A" : null,
                    postalCode,
                    city,
                    Math.Round(lat + i * 0.0007, 6),
                    Math.Round(lon + i * 0.0009, 6)));
            }
        }

        return addresses;
    }

    public override Task<IReadOnlyList<TestAddress>> LoadAsync()
    {
        Loads++;
        return Failure == null ? Task.FromResult(Build()) : Task.FromException<IReadOnlyList<TestAddress>>(Failure);
    }
}
