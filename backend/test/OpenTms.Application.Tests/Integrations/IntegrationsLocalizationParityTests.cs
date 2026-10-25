using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Shouldly;
using Xunit;

namespace OpenTms.Integrations.Localization;

/// <summary>
/// Key-parity test over the Integrations localization resource (design 44; risk class Low — texts):
/// every key in en.json exists in nl.json and vice versa, and the new permission and feature have a
/// text in both.
/// </summary>
public class IntegrationsLocalizationParityTests
{
    private static Dictionary<string, string> Read(string culture)
    {
        var path = Path.Combine(FindRepositoryRoot(), "backend", "modules", "integrations", "OpenTms.Integrations.Domain.Shared", "Localization", "Integrations", culture + ".json");
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

    [Theory]
    [InlineData("en")]
    [InlineData("nl")]
    public void The_address_lookup_permission_and_feature_have_a_text(string culture)
    {
        var texts = Read(culture);

        string[] expected = ["Permission:Integrations", "Permission:AddressLookup", "Feature:Integrations", "Feature:AddressLookup", "Feature:AddressLookup.Description"];

        expected.Where(key => !texts.ContainsKey(key)).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("en")]
    [InlineData("nl")]
    public void The_routing_permission_has_a_text(string culture)
    {
        /* Design 60: the Integrations.Routing permission of the status endpoint. */
        Read(culture).ShouldContainKey("Permission:Routing");
    }
}
