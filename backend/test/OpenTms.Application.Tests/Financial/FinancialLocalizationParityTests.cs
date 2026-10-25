using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using OpenTms.Financial.Permissions;
using OpenTms.Financial.Tariffs;
using OpenTms.Financial.Tariffs.Calculators;
using OpenTms.Financial.Tariffs.Pricing;
using Shouldly;
using Xunit;

namespace OpenTms.Financial.Localization;

/// <summary>
/// Key-parity test over the Financial localization resource (design 57, test risk analysis "Localization parity nl/en",
/// Low; the MasterDataLocalizationParityTests pattern): every key in en.json exists in nl.json and vice versa, no empty
/// texts, and every error code, warning code, permission, enum member and calculated-line description key has a text.
/// </summary>
public class FinancialLocalizationParityTests
{
    private static Dictionary<string, string> Read(string culture)
    {
        var path = Path.Combine(FindRepositoryRoot(), "backend", "modules", "financial", "OpenTms.Financial.Domain.Shared", "Localization", "Financial", culture + ".json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.GetProperty("texts").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? string.Empty);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "backend", "OpenTms.slnx")))
        {
            directory = directory.Parent;
        }

        directory.ShouldNotBeNull("the test must run inside the repository");
        return directory.FullName;
    }

    [Fact]
    public void nl_and_en_have_the_same_keys_and_no_empty_texts()
    {
        var en = Read("en");
        var nl = Read("nl");

        nl.Keys.Except(en.Keys).ShouldBeEmpty("keys only in nl.json");
        en.Keys.Except(nl.Keys).ShouldBeEmpty("keys only in en.json");
        en.Where(p => string.IsNullOrWhiteSpace(p.Value)).Select(p => p.Key).ShouldBeEmpty();
        nl.Where(p => string.IsNullOrWhiteSpace(p.Value)).Select(p => p.Key).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("en")]
    [InlineData("nl")]
    public void Every_error_code_warning_permission_enum_member_and_description_key_has_a_text(string culture)
    {
        var texts = Read(culture);
        var expected = new List<string>();

        /* Every error code constant ("OpenTms.Financial:…") and every warning code ("Financial:…") of FinancialErrorCodes. */
        expected.AddRange(typeof(FinancialErrorCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string) && f.Name != nameof(FinancialErrorCodes.Namespace))
            .Select(f => (string)f.GetRawConstantValue()!));

        /* Every permission of FinancialPermissions. */
        expected.Add("Permission:" + FinancialPermissions.GroupName);
        foreach (var group in typeof(FinancialPermissions).GetNestedTypes())
        {
            foreach (var permission in group.GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral))
            {
                var name = (string)permission.GetRawConstantValue()!;
                expected.Add("Permission:" + name[(FinancialPermissions.GroupName.Length + 1)..]);
            }
        }

        /* Every member of the design-57 enums. */
        expected.AddRange(Enum.GetNames<RateSide>().Select(n => "Financial:RateSide." + n));
        expected.AddRange(Enum.GetNames<RateLineKind>().Select(n => "Financial:RateLineKind." + n));
        expected.AddRange(Enum.GetNames<QuantitySource>().Select(n => "Financial:QuantitySource." + n));
        expected.AddRange(Enum.GetNames<PriceMultiplier>().Select(n => "Financial:PriceMultiplier." + n));
        expected.AddRange(Enum.GetNames<ColumnQuantitySource>().Select(n => "Financial:ColumnQuantitySource." + n));
        expected.AddRange(Enum.GetNames<StopMatching>().Select(n => "Financial:StopMatching." + n));
        expected.AddRange(Enum.GetNames<MultipleMatches>().Select(n => "Financial:MultipleMatches." + n));
        expected.AddRange(Enum.GetNames<PriceRounding>().Select(n => "Financial:PriceRounding." + n));
        expected.AddRange(Enum.GetNames<KilometreBasis>().Select(n => "Financial:KilometreBasis." + n));

        /* The description keys of calculated lines and the list names. */
        expected.AddRange([
            PriceResolver.PriceListDescriptionKey, PriceResolver.DiscountOrSurchargeDescriptionKey, TripRateLineCalculator.OwnFleetDescriptionKey,
            "Financial:RateLine.FixedPrice", "Financial:RateLine.AgreedPrice",
            "Financial:PriceLists", "Financial:RelationFinancialSettings", "Financial:RateLines",
            "Financial:PriceListInUseByRelations", "Financial:PriceListInUseByRateLines", "Financial:OwnFleetCostPerKm"
        ]);

        expected.Distinct().Where(key => !texts.ContainsKey(key)).ShouldBeEmpty($"{culture}.json");
        texts[FinancialErrorCodes.ImportInvalid].ShouldContain("{row}");
        texts[FinancialErrorCodes.ImportInvalid].ShouldContain("{column}");
        texts[FinancialErrorCodes.TooManyRules].ShouldContain("{max}");
    }
}
