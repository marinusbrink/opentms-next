using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Shouldly;
using Xunit;

namespace OpenTms.ArchitectureTests;

/* Design 60 ("Configuration: no literal hosts", High): OpenTMS kept every engine host in code
 * (osrm.services.opentms.nl, the Cloud Function on cloudfunctions.net — assessment §1.3). Here every host comes from
 * configuration, so the Integrations module's sources must not name those domains anywhere: not in a default, not in a
 * comment, not in a test fixture. The one shape that is allowed is an account name of the form "...@opentms.nl": the
 * pdfgeneratorapi workspaces of design 55 are identifiers of an account, not hosts. */
public class IntegrationsConfigurationTests
{
    /// <summary>The domains, not preceded by "@" (a workspace identifier is an e-mail-shaped account name).</summary>
    private static readonly Regex LiteralHost = new(@"(?<!@)\b(opentms\.nl|cloudfunctions\.net)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly string[] SourcePatterns = ["*.cs", "*.json"];

    [Fact]
    public void The_Integrations_module_names_no_engine_host_in_its_sources()
    {
        var root = Path.Combine(FindBackendRoot(), "modules", "integrations");
        Directory.Exists(root).ShouldBeTrue(root);

        var hits = new List<string>();
        foreach (var file in SourceFiles(root))
        {
            foreach (var (line, number) in File.ReadLines(file).Select((line, index) => (line, index + 1)))
            {
                if (LiteralHost.IsMatch(line))
                {
                    hits.Add($"{Path.GetRelativePath(root, file)}:{number}: {line.Trim()}");
                }
            }
        }

        hits.ShouldBeEmpty("engine hosts come from Integrations:Routing configuration, never from code (design 60). Found: " + string.Join("; ", hits));
    }

    [Fact]
    public void The_pattern_catches_the_legacy_hosts_and_allows_a_workspace_account_name()
    {
        /* Self-test: a green run means something only if the pattern finds what OpenTMS had in code. */
        LiteralHost.IsMatch("https://osrm.services.opentms.nl/route/v1/driving/").ShouldBeTrue();
        LiteralHost.IsMatch("\"https://europe-west4-filogic-opentms.cloudfunctions.net/prod-filogic-services-route\"").ShouldBeTrue();
        LiteralHost.IsMatch("BaseUrl = \"https://OPENTMS.NL\"").ShouldBeTrue();
        LiteralHost.IsMatch("public string DefaultWorkspace { get; set; } = \"default-workspace@opentms.nl\";").ShouldBeFalse("a pdfgeneratorapi workspace is an account name (design 55)");
        LiteralHost.IsMatch("https://osrm.invalid/").ShouldBeFalse();
    }

    private static IEnumerable<string> SourceFiles(string root)
        => SourcePatterns
            .SelectMany(pattern => Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories))
            .Where(file => !file.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"));

    private static string FindBackendRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "OpenTms.slnx")))
        {
            directory = directory.Parent;
        }

        directory.ShouldNotBeNull("could not locate the backend root (folder containing OpenTms.slnx)");
        return directory!.FullName;
    }
}
