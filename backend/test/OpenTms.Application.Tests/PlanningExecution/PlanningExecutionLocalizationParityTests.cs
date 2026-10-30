using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenTms.PlanningExecution.Legs;
using OpenTms.PlanningExecution.Permissions;
using OpenTms.PlanningExecution.TransportTrips;
using Shouldly;
using Xunit;

namespace OpenTms.PlanningExecution.Localization;

/// <summary>
/// Key parity of the Planning &amp; Execution resource (design 59; risk class Low — texts): nl and en carry the same keys,
/// no text is empty, and every permission, enum member, warning code and error code of the module has a text.
/// </summary>
public class PlanningExecutionLocalizationParityTests
{
    private static Dictionary<string, string> Read(string culture)
    {
        var path = Path.Combine(FindRepositoryRoot(), "backend", "modules", "planning-execution", "OpenTms.PlanningExecution.Domain.Shared", "Localization", "PlanningExecution", culture + ".json");
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
    public void Every_permission_enum_member_warning_and_error_code_has_a_text(string culture)
    {
        var texts = Read(culture);
        var expected = new List<string>();

        foreach (var group in new[] { typeof(PlanningExecutionPermissions.TransportTripTypes), typeof(PlanningExecutionPermissions.TransportTrips), typeof(PlanningExecutionPermissions.Legs), typeof(PlanningExecutionPermissions.ViaDepotRules), typeof(PlanningExecutionPermissions.TripAttributeDefinitions) })
        {
            foreach (var field in group.GetFields().Where(f => f.IsLiteral))
            {
                var permission = (string)field.GetRawConstantValue()!;
                expected.Add("Permission:" + permission[(PlanningExecutionPermissions.GroupName.Length + 1)..]);
            }
        }

        foreach (var enumType in new[] { typeof(TransportTripStatus), typeof(StopCompositionMode), typeof(FirstLastStopRule), typeof(TripWarningCode), typeof(TripChangeKind), typeof(ViaDepotScope), typeof(StopLegRole), typeof(LegSource), typeof(TripExecution) })
        {
            expected.AddRange(Enum.GetNames(enumType).Select(name => $"PlanningExecution:{enumType.Name}.{name}"));
        }

        expected.AddRange(PlanningExecutionErrorCodes.ConflictCodes);
        expected.AddRange(PlanningExecutionErrorCodes.BadRequestCodes);
        expected.AddRange(["Permission:PlanningExecution", "PlanningExecution:NotFound", "PlanningExecution:TransportTrips", "PlanningExecution:ViaDepotRules", "PlanningExecution:TripAttributeDefinitions"]);

        expected.Where(key => !texts.ContainsKey(key)).ShouldBeEmpty();
    }

    [Fact]
    public void Every_error_code_is_in_exactly_one_status_list_and_none_is_shared()
    {
        var codes = typeof(PlanningExecutionErrorCodes).GetFields().Where(f => f.IsLiteral && f.FieldType == typeof(string) && f.Name != nameof(PlanningExecutionErrorCodes.Namespace))
            .Select(f => (string)f.GetRawConstantValue()!).ToList();
        var mapped = PlanningExecutionErrorCodes.ConflictCodes.Concat(PlanningExecutionErrorCodes.BadRequestCodes).ToList();

        codes.Except(mapped).ShouldBeEmpty("codes without an HTTP status");
        mapped.GroupBy(c => c).Where(g => g.Count() > 1).ShouldBeEmpty();
        PlanningExecutionErrorCodes.ConflictCodes.Intersect(PlanningExecutionErrorCodes.BadRequestCodes).ShouldBeEmpty();
        codes.ShouldAllBe(c => c.StartsWith("OpenTms.PlanningExecution:"));
    }
}
