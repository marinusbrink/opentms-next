using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using OpenTms.MasterData.Reference;
using OpenTms.MasterData.Seed;
using OpenTms.Platform.Localization;
using Shouldly;
using Xunit;

namespace OpenTms.MasterData.Seed;

/// <summary>
/// Unit tests on the embedded seed files (design 41, "Seed": every country's currency code exists,
/// every time-zone id is known, nl and en present). Risk class: Critical — the seed is copied into
/// every tenant database; a broken file breaks every provisioning.
/// </summary>
public class MasterDataSeedFilesTests
{
    /* Reads the embedded files straight from the Domain.Shared assembly (the same bytes the ABP virtual
     * file system serves to MasterDataSeedData), so the test needs no container. */
    private readonly EmbeddedSeedData _seedData = new();

    private static bool HasText(Dictionary<string, string?> names, string culture)
        => names.TryGetValue(culture, out var text) && !string.IsNullOrWhiteSpace(text);

    [Fact]
    public async Task The_currency_file_holds_178_distinct_three_letter_codes_with_nl_and_en_names()
    {
        var currencies = await _seedData.GetCurrenciesAsync();

        currencies.Count.ShouldBe(178);
        currencies.Select(c => c.IsoCode).Distinct(StringComparer.Ordinal).Count().ShouldBe(178);
        currencies.ShouldAllBe(c => Regex.IsMatch(c.IsoCode, "^[A-Z]{3}$"));
        currencies.ShouldAllBe(c => c.IsoNumeric != null && c.IsoNumeric >= 0 && c.IsoNumeric <= 999);
        currencies.ShouldAllBe(c => c.MinorUnits == null || (c.MinorUnits >= 0 && c.MinorUnits <= 4));
        currencies.ShouldAllBe(c => HasText(c.Names, "nl"));
        currencies.ShouldAllBe(c => HasText(c.Names, "en"));
        currencies.ShouldAllBe(c => c.Names.Values.All(n => n!.Length <= LocalizedName.MaxTextLength));

        var eur = currencies.Single(c => c.IsoCode == "EUR");
        eur.IsoNumeric.ShouldBe(978);
        eur.MinorUnits.ShouldBe(2);
        eur.Symbol.ShouldBe("€");
    }

    [Fact]
    public async Task The_country_file_holds_249_distinct_countries_with_nl_and_en_names_and_known_time_zones()
    {
        var countries = await _seedData.GetCountriesAsync();

        countries.Count.ShouldBe(249);
        countries.Select(c => c.IsoAlpha2).Distinct(StringComparer.Ordinal).Count().ShouldBe(249);
        countries.Select(c => c.IsoAlpha3).Distinct(StringComparer.Ordinal).Count().ShouldBe(249);
        countries.ShouldAllBe(c => Regex.IsMatch(c.IsoAlpha2, "^[A-Z]{2}$"));
        countries.ShouldAllBe(c => Regex.IsMatch(c.IsoAlpha3, "^[A-Z]{3}$"));
        countries.ShouldAllBe(c => HasText(c.Names, "nl"));
        countries.ShouldAllBe(c => HasText(c.Names, "en"));

        var unknownZones = countries
            .Where(c => !string.IsNullOrEmpty(c.PrimaryTimeZoneId) && !TimeZoneInfo.TryFindSystemTimeZoneById(c.PrimaryTimeZoneId, out _))
            .Select(c => $"{c.IsoAlpha2}: {c.PrimaryTimeZoneId}")
            .ToList();
        unknownZones.ShouldBeEmpty("every seeded time zone must be a known IANA id");

        var nl = countries.Single(c => c.IsoAlpha2 == "NL");
        nl.IsoAlpha3.ShouldBe("NLD");
        nl.IsoNumeric.ShouldBe(528);
        nl.DefaultCurrency.ShouldBe("EUR");
        nl.PrimaryTimeZoneId.ShouldBe("Europe/Amsterdam");
        nl.Names["nl"].ShouldBe("Nederland");
        nl.Names["en"].ShouldBe("Netherlands");

        countries.Single(c => c.IsoAlpha2 == "AQ").DefaultCurrency.ShouldBeNull();
    }

    [Fact]
    public async Task Every_country_default_currency_exists_in_the_currency_file()
    {
        var currencyCodes = (await _seedData.GetCurrenciesAsync()).Select(c => c.IsoCode).ToHashSet(StringComparer.Ordinal);
        var countries = await _seedData.GetCountriesAsync();

        var dangling = countries
            .Where(c => !string.IsNullOrEmpty(c.DefaultCurrency) && !currencyCodes.Contains(c.DefaultCurrency))
            .Select(c => $"{c.IsoAlpha2} → {c.DefaultCurrency}")
            .ToList();

        dangling.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_address_type_file_holds_visiting_and_postal_only()
    {
        var types = await _seedData.GetAddressTypesAsync();

        types.Select(t => Enum.Parse<AddressTypeKind>(t.Kind, ignoreCase: true)).ShouldBe([AddressTypeKind.Visiting, AddressTypeKind.Postal], ignoreOrder: true);
        types.ShouldAllBe(t => HasText(t.Names, "nl") && HasText(t.Names, "en"));
    }

    private sealed class EmbeddedSeedData
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
        private static readonly Assembly SharedAssembly = typeof(MasterDataDomainSharedModule).Assembly;

        public Task<List<CurrencySeedRecord>> GetCurrenciesAsync() => ReadAsync<CurrencySeedRecord>("iso-4217-currencies.json");

        public Task<List<CountrySeedRecord>> GetCountriesAsync() => ReadAsync<CountrySeedRecord>("iso-3166-countries.json");

        public Task<List<AddressTypeSeedRecord>> GetAddressTypesAsync() => ReadAsync<AddressTypeSeedRecord>("address-types.json");

        private static async Task<List<T>> ReadAsync<T>(string fileName)
        {
            var resourceName = SharedAssembly.GetManifestResourceNames().SingleOrDefault(n => n.EndsWith("Seed." + fileName, StringComparison.Ordinal));
            resourceName.ShouldNotBeNull($"{fileName} must be embedded in {SharedAssembly.GetName().Name}");
            await using var stream = SharedAssembly.GetManifestResourceStream(resourceName)!;
            return await JsonSerializer.DeserializeAsync<List<T>>(stream, JsonOptions) ?? [];
        }
    }
}
