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
            "Permission:MasterData", "Feature:MasterData", "Feature:FirstRun", "Feature:FirstRun.Description"
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
}
