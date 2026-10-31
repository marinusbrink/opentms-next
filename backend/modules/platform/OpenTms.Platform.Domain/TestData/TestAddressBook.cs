using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenTms.Platform.TestData;

/// <summary>A real, existing address from the open address registers of NL, BE and DE (TestData/Addresses/SOURCES.md).</summary>
public sealed record TestAddress(
    string CountryCode,
    string Street,
    string? HouseNumber,
    string? HouseNumberSuffix,
    string PostalCode,
    string City,
    double Latitude,
    double Longitude);

/// <summary>
/// The real addresses of one reset run, drawn without repeats: every relation, depot or stop gets an address
/// no other record has until the register is used up, then the draw starts over in a new order. The country
/// mix is that of a Dutch carrier: mostly Dutch, a share of Belgian and German addresses.
/// </summary>
public sealed class TestAddressBook
{
    /// <summary>Share per country of the addresses drawn without a country.</summary>
    public static readonly IReadOnlyDictionary<string, double> CountryMix =
        new Dictionary<string, double> { ["NL"] = 0.70, ["BE"] = 0.15, ["DE"] = 0.15 };

    private readonly Dictionary<string, TestAddress[]> _byCountry;
    private readonly Dictionary<string, int> _position = new();
    private readonly Random _random;

    public TestAddressBook(IEnumerable<TestAddress> addresses, Random random)
    {
        _random = random;
        _byCountry = addresses
            .GroupBy(a => a.CountryCode, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
        if (_byCountry.Count == 0)
        {
            throw new InvalidOperationException("The test address register is empty.");
        }

        foreach (var (country, list) in _byCountry)
        {
            _random.Shuffle(list);
            _position[country] = 0;
        }
    }

    public IReadOnlyCollection<string> Countries => _byCountry.Keys;

    public int Count(string countryCode) => _byCountry.TryGetValue(countryCode, out var list) ? list.Length : 0;

    /// <summary>The next unused address, in the country mix.</summary>
    public TestAddress Next() => Next(PickCountry());

    /// <summary>The next unused address in one country.</summary>
    public TestAddress Next(string countryCode)
    {
        if (!_byCountry.TryGetValue(countryCode, out var list))
        {
            throw new ArgumentException($"No test addresses for country {countryCode}.", nameof(countryCode));
        }

        var position = _position[countryCode];
        if (position == list.Length)
        {
            _random.Shuffle(list);
            position = 0;
        }

        _position[countryCode] = position + 1;
        return list[position];
    }

    private string PickCountry()
    {
        var roll = _random.NextDouble();
        var cumulative = 0d;
        string? last = null;
        foreach (var (country, share) in CountryMix)
        {
            if (!_byCountry.ContainsKey(country))
            {
                continue;
            }

            last = country;
            cumulative += share;
            if (roll < cumulative)
            {
                return country;
            }
        }

        return last ?? _byCountry.Keys.First();
    }
}
