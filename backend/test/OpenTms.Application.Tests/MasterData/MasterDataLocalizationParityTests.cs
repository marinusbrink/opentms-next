using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenTms.MasterData.Relations;
using Shouldly;
using Xunit;

namespace OpenTms.MasterData.Localization;

/// <summary>
/// Key-parity test over the MasterData localization resource (design 41, test risk analysis, Low):
/// every key in en.json exists in nl.json and vice versa, every error code and permission has a text.
/// </summary>
public class MasterDataLocalizationParityTests
{
    private static Dictionary<string, string> Read(string culture)
    {
        var path = Path.Combine(FindRepositoryRoot(), "backend", "modules", "master-data", "OpenTms.MasterData.Domain.Shared", "Localization", "MasterData", culture + ".json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var texts = document.RootElement.GetProperty("texts");
        return texts.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? string.Empty);
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

    [Fact]
    public void Every_error_code_permission_and_feature_has_a_text()
    {
        var en = Read("en");

        var expected = new List<string>
        {
            MasterDataErrorCodes.DuplicateKey, MasterDataErrorCodes.InUse, MasterDataErrorCodes.DefaultCannotBeDeleted,
            MasterDataErrorCodes.DefaultMustBeActive, MasterDataErrorCodes.InvalidTimeZone, MasterDataErrorCodes.SelfIncompatibility,
            MasterDataErrorCodes.AdrFieldsRequired, MasterDataErrorCodes.InvalidPostalCodeRange, MasterDataErrorCodes.InvalidTemperatureRange,
            MasterDataErrorCodes.InvalidCode, MasterDataErrorCodes.ReferenceNotFound, MasterDataErrorCodes.DuplicateLoadFactor,
            MasterDataErrorCodes.UnknownSortColumn, MasterDataErrorCodes.NameRequired, MasterDataErrorCodes.TooManyRows,
            "MasterData:InUseByCountries", "MasterData:InUseByGeographicalRegions", "MasterData:InUseByTransportUnits",
            /* The feature texts moved to the Platform resource with the app features (PO 2026-10-14); see
             * AppFeatureLocalizationTests. */
            "Permission:MasterData"
        };
        foreach (var (entity, _, _, _, _) in Permissions.MasterDataPermissions.All)
        {
            expected.Add($"Permission:{entity}");
            expected.Add($"Permission:{entity}.Create");
            expected.Add($"Permission:{entity}.Update");
            expected.Add($"Permission:{entity}.Delete");
        }

        expected.Where(key => !en.ContainsKey(key)).ShouldBeEmpty();
    }

    [Fact]
    public void Every_run_2_error_code_and_every_block_reason_has_a_text()
    {
        var en = Read("en");

        var expected = new List<string>
        {
            MasterDataErrorCodes.OwnCompanyAlreadyExists, MasterDataErrorCodes.RolesRequired, MasterDataErrorCodes.EmployerRoleRequired,
            MasterDataErrorCodes.InvalidIban, MasterDataErrorCodes.InvalidVatNumber, MasterDataErrorCodes.InvalidEmail, MasterDataErrorCodes.InvalidCulture,
            MasterDataErrorCodes.UserNotFound, MasterDataErrorCodes.AnonymisedRecordIsReadOnly, MasterDataErrorCodes.TooManyChildren,
            MasterDataErrorCodes.BlockReasonRequired, "MasterData:BlockReason", "MasterData:BlockReasonRequired"
        };
        /* Every selectable reason has a label; None is the empty choice and needs none. */
        expected.AddRange(Enum.GetValues<RelationBlockReason>().Where(r => r != RelationBlockReason.None).Select(r => $"MasterData:BlockReason.{r}"));

        expected.Where(key => !en.ContainsKey(key)).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("en")]
    [InlineData("nl")]
    public void The_own_company_archive_refusal_and_its_editor_hint_have_a_text(string culture)
    {
        /* PO 2026-10-16: the own company is never archived; the server's refusal and the editor's hint. */
        var texts = Read(culture);

        texts.ShouldContainKey(MasterDataErrorCodes.OwnCompanyCannotBeArchived);
        texts.ShouldContainKey("MasterData:OwnCompanyCannotBeArchivedHint");
        texts[MasterDataErrorCodes.OwnCompanyCannotBeArchived].ShouldNotBeNullOrWhiteSpace();
        texts["MasterData:OwnCompanyCannotBeArchivedHint"].ShouldNotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("en")]
    [InlineData("nl")]
    public void Every_run_3_error_code_in_use_reason_and_enum_value_has_a_text(string culture)
    {
        /* Design 48 (test risk analysis, Low): the nl/en key parity above covers the new keys; this pins that the
         * keys the server and the editors use exist at all, in both languages. */
        var texts = Read(culture);

        var expected = new List<string>
        {
            MasterDataErrorCodes.OwnerRoleRequired, MasterDataErrorCodes.HomeDepotRoleRequired, MasterDataErrorCodes.InvalidLicencePlate,
            MasterDataErrorCodes.InvalidVin, MasterDataErrorCodes.RegistrationCountryRequired, MasterDataErrorCodes.CombinationMotorisedFirst,
            MasterDataErrorCodes.CombinationMemberCount, MasterDataErrorCodes.VehicleAlreadyInCombination, MasterDataErrorCodes.ProductRelationCodeTargetRequired,
            "MasterData:InUseByVehicles", "MasterData:InUseByVehicleCombinations", "MasterData:InUseByProductRelationCodes",
            "MasterData:Vehicles", "MasterData:VehicleCombinations", "MasterData:ProductRelationCodes",
            "Permission:Vehicles", "Permission:VehicleCombinations", "Permission:ProductRelationCodes"
        };
        expected.AddRange(Enum.GetValues<OpenTms.MasterData.Fleet.FuelType>().Select(f => $"MasterData:FuelType.{f}"));
        expected.AddRange(Enum.GetValues<OpenTms.MasterData.Fleet.EuroClass>().Select(e => $"MasterData:EuroClass.{e}"));

        expected.Where(key => !texts.ContainsKey(key)).ShouldBeEmpty($"{culture}.json");
        texts[MasterDataErrorCodes.CombinationMemberCount].ShouldContain("{min}");
        texts[MasterDataErrorCodes.CombinationMemberCount].ShouldContain("{max}");
        texts[MasterDataErrorCodes.VehicleAlreadyInCombination].ShouldContain("{name}");
    }
}
