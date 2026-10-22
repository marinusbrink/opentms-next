using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using OpenTms.PlanningExecution.Permissions;
using OpenTms.Platform.ReferenceData;
using Shouldly;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace OpenTms.EntityFrameworkCore.Platform.ReferenceData;

/// <summary>
/// The permissions and texts of the reference data of Orders, Financial and Planning &amp; Execution (designs 50–53):
/// every list has view, create, update and delete through the real definition manager, on both sides, each with a
/// text; every module resource has the same keys in nl and en; every Platform reference-data error code has a text.
/// Risk class: Critical for the permissions (a wrong name grants or denies access fleet-wide), Low for the texts.
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class ReferenceDataDefinitionsTests : OpenTmsEntityFrameworkCoreTestBase
{
    /// <summary>Every list's view permission; its .Create, .Update and .Delete children are checked too.</summary>
    public static readonly string[] ViewPermissions =
    [
        PlanningExecutionPermissions.TransportTripTypes.Default,
    ];

    /// <summary>The module resources (folder under backend/modules, resource name) whose nl/en parity is checked.</summary>
    private static readonly (string Module, string Project, string Resource)[] Resources =
    [
        ("planning-execution", "OpenTms.PlanningExecution.Domain.Shared", "PlanningExecution"),
        ("orders", "OpenTms.Orders.Domain.Shared", "Orders"),
        ("financial", "OpenTms.Financial.Domain.Shared", "Financial"),
        ("platform", "OpenTms.Platform.Domain.Shared", "Platform"),
    ];

    [Fact]
    public async Task Every_list_has_view_create_update_and_delete_on_both_sides()
    {
        var manager = GetRequiredService<IPermissionDefinitionManager>();
        foreach (var view in ViewPermissions)
        {
            var definition = await manager.GetAsync(view);
            definition.MultiTenancySide.ShouldBe(MultiTenancySides.Both, view);
            definition.Children.Select(c => c.Name).ShouldBe([view + ".Create", view + ".Update", view + ".Delete"], ignoreOrder: true);
            definition.Children.ShouldAllBe(c => c.MultiTenancySide == MultiTenancySides.Both);
        }
    }

    [Fact]
    public void Every_module_resource_has_the_same_keys_in_nl_and_en()
    {
        foreach (var (module, project, resource) in Resources)
        {
            var en = Read(module, project, resource, "en");
            var nl = Read(module, project, resource, "nl");
            nl.Keys.Except(en.Keys).ShouldBeEmpty($"{resource}: keys only in nl.json");
            en.Keys.Except(nl.Keys).ShouldBeEmpty($"{resource}: keys only in en.json");
            en.Concat(nl).Where(p => string.IsNullOrWhiteSpace(p.Value)).Select(p => p.Key).ShouldBeEmpty($"{resource}: empty texts");
        }
    }

    [Fact]
    public void Every_reference_data_error_code_has_a_text()
    {
        var en = Read("platform", "OpenTms.Platform.Domain.Shared", "Platform", "en");
        var codes = typeof(ReferenceDataErrorCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.Name != nameof(ReferenceDataErrorCodes.Namespace))
            .Select(f => (string)f.GetRawConstantValue()!);
        codes.Where(code => !en.ContainsKey(code)).ShouldBeEmpty();
    }

    [Fact]
    public void Every_permission_of_the_new_lists_has_a_text_in_its_resource()
    {
        var texts = Resources.SelectMany(r => Read(r.Module, r.Project, r.Resource, "en").Keys).ToHashSet();
        foreach (var view in ViewPermissions)
        {
            var entity = view[(view.IndexOf('.') + 1)..];
            foreach (var key in new[] { $"Permission:{entity}", $"Permission:{entity}.Create", $"Permission:{entity}.Update", $"Permission:{entity}.Delete" })
            {
                texts.ShouldContain(key);
            }
        }
    }

    private static Dictionary<string, string> Read(string module, string project, string resource, string culture)
    {
        var path = Path.Combine(FindRepositoryRoot(), "backend", "modules", module, project, "Localization", resource, culture + ".json");
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
        return directory!.FullName;
    }
}
