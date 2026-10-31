using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Shouldly;
using Xunit;

namespace OpenTms.Platform.Features;

/// <summary>
/// Every app and navigation feature (PO 2026-10-14, "App features") has a name and a description in both languages
/// of the Platform resource, which the definition provider uses; the texts of the removed features are gone from the
/// Master Data resource. Risk class: Low (texts) — an edition's Features tab shows these names.
/// </summary>
public class AppFeatureLocalizationTests
{
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

    private static Dictionary<string, string> Read(string module, string resource, string culture)
    {
        var path = Path.Combine(FindRepositoryRoot(), "backend", "modules", module, $"OpenTms.{resource}.Domain.Shared", "Localization", resource, culture + ".json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.GetProperty("texts").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? string.Empty);
    }

    /// <summary>All string constants under <see cref="AppFeatures"/> except the group name.</summary>
    private static List<string> AllAppFeatures()
    {
        var names = new List<string>();
        void Collect(System.Type type)
        {
            names.AddRange(type.GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.IsLiteral && f.FieldType == typeof(string) && f.Name != nameof(AppFeatures.GroupName))
                .Select(f => (string)f.GetRawConstantValue()!));
            foreach (var nested in type.GetNestedTypes())
            {
                Collect(nested);
            }
        }

        Collect(typeof(AppFeatures));
        return names;
    }

    [Fact]
    public void The_constants_name_exactly_67_distinct_features()
    {
        /* 35 nodes up to design 48, plus the Platform domain group and its four lists (design 49). */
        var all = AllAppFeatures();
        all.Count.ShouldBe(67);
        all.Distinct().Count().ShouldBe(67);
        all.ShouldAllBe(name => name.StartsWith("Apps."));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("nl")]
    public void Every_app_feature_has_a_name_and_a_description(string culture)
    {
        var texts = Read("platform", "Platform", culture);

        texts.ShouldContainKey("Feature:Apps");
        foreach (var name in AllAppFeatures())
        {
            texts.TryGetValue("Feature:" + name, out var text).ShouldBeTrue($"{culture}: Feature:{name}");
            text.ShouldNotBeNullOrWhiteSpace();
            texts.TryGetValue("Feature:" + name + ".Description", out var description).ShouldBeTrue($"{culture}: Feature:{name}.Description");
            description.ShouldNotBeNullOrWhiteSpace();
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("nl")]
    public void The_texts_of_the_removed_features_are_gone(string culture)
    {
        var masterData = Read("master-data", "MasterData", culture);
        masterData.ShouldNotContainKey("Feature:FirstRun");
        masterData.ShouldNotContainKey("Feature:FirstRun.Description");

        var platform = Read("platform", "Platform", culture);
        platform.Keys.ShouldNotContain(k => k.StartsWith("Feature:Platform.Maintenance") || k.StartsWith("Feature:UI.CommonToolbar"));
    }
}
