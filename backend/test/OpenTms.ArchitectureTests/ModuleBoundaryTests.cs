using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using NetArchTest.Rules;
using Shouldly;
using Xunit;

namespace OpenTms.ArchitectureTests;

/* The module boundary rules from /CLAUDE.md, enforced mechanically:
 *
 *   1. A domain module never references another domain module's implementation projects
 *      (Domain, Application, EntityFrameworkCore). Cross-module communication is the local
 *      event bus or the published surface (Domain.Shared, Application.Contracts) only.
 *   2. Platform is the horizontal layer: it may be referenced by every module, but it
 *      references no domain module — the dependency arrow always points downward.
 *
 * Two enforcement levels:
 *   - Project level: the .csproj files are scanned for forbidden <ProjectReference> entries.
 *     This catches a violation the moment the reference is added, even before any type uses it.
 *   - Type level (NetArchTest): compiled assemblies are scanned for type dependencies,
 *     as a belt-and-braces check on top of the project scan.
 */
public class ModuleBoundaryTests
{
    private static readonly string[] DomainModules =
    {
        "Orders", "PlanningExecution", "Financial", "MasterData", "Integrations", "Reporting"
    };

    private const string PlatformModule = "Platform";

    private static readonly string[] PublishedSurfaceLayers = { "Domain.Shared", "Application.Contracts" };

    private static readonly string[] AllLayers =
    {
        "Domain.Shared", "Domain", "Application.Contracts", "Application", "EntityFrameworkCore"
    };

    // ---------- project-reference level ----------

    public static IEnumerable<object[]> AllModuleProjects()
    {
        var modulesRoot = Path.Combine(FindBackendRoot(), "modules");
        foreach (var csproj in Directory.EnumerateFiles(modulesRoot, "*.csproj", SearchOption.AllDirectories))
        {
            yield return new object[] { csproj };
        }
    }

    [Theory]
    [MemberData(nameof(AllModuleProjects))]
    public void Domain_modules_reference_other_modules_only_via_their_published_surface(string csprojPath)
    {
        var ownModule = ModuleOf(Path.GetFileNameWithoutExtension(csprojPath));

        var references = XDocument.Load(csprojPath)
            .Descendants()
            .Where(e => e.Name.LocalName == "ProjectReference")
            .Select(e => Path.GetFileNameWithoutExtension(e.Attribute("Include")!.Value.Replace('\\', '/')))
            .Where(name => name.StartsWith("OpenTms.", StringComparison.Ordinal))
            .ToList();

        foreach (var reference in references)
        {
            var referencedModule = ModuleOf(reference);

            if (referencedModule == ownModule)
            {
                continue; // own lower layers are always fine
            }

            if (ownModule == PlatformModule)
            {
                // Rule 2: Platform references no domain module, not even its published surface.
                referencedModule.ShouldBeNull(
                    $"{Path.GetFileName(csprojPath)} references {reference} — Platform must not depend on any domain module");
                continue;
            }

            if (referencedModule == PlatformModule)
            {
                continue; // Rule 2: everyone may depend on Platform.
            }

            if (referencedModule != null)
            {
                // Rule 1: another domain module — only the published surface is allowed.
                PublishedSurfaceLayers.Any(l => reference == $"OpenTms.{referencedModule}.{l}").ShouldBeTrue(
                    $"{Path.GetFileName(csprojPath)} references {reference} — cross-module references may only target " +
                    $"Domain.Shared or Application.Contracts (the published surface). Use the local event bus or the " +
                    $"published interfaces instead.");
            }
        }
    }

    // ---------- type level (NetArchTest) ----------

    [Fact]
    public void Platform_types_do_not_depend_on_any_domain_module()
    {
        foreach (var assembly in ModuleAssemblies(PlatformModule))
        {
            var forbidden = DomainModules.Select(m => $"OpenTms.{m}").ToArray();

            var result = Types.InAssembly(assembly)
                .ShouldNot()
                .HaveDependencyOnAny(forbidden)
                .GetResult();

            result.IsSuccessful.ShouldBeTrue(
                $"Platform assembly {assembly.GetName().Name} depends on a domain module: " +
                string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>()));
        }
    }

    [Fact]
    public void Domain_modules_do_not_depend_on_another_modules_EntityFrameworkCore()
    {
        foreach (var module in DomainModules)
        {
            var forbidden = DomainModules
                .Where(other => other != module)
                .Select(other => $"OpenTms.{other}.EntityFrameworkCore")
                .ToArray();

            foreach (var assembly in ModuleAssemblies(module))
            {
                var result = Types.InAssembly(assembly)
                    .ShouldNot()
                    .HaveDependencyOnAny(forbidden)
                    .GetResult();

                result.IsSuccessful.ShouldBeTrue(
                    $"{assembly.GetName().Name} reaches into another module's DbContext: " +
                    string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>()));
            }
        }
    }

    // ---------- helpers ----------

    /// <summary>Returns the module name for an OpenTms.{Module}.{Layer} project/assembly name, or null for app-level projects.</summary>
    private static string? ModuleOf(string projectName)
    {
        foreach (var module in DomainModules.Append(PlatformModule))
        {
            if (projectName == $"OpenTms.{module}" || projectName.StartsWith($"OpenTms.{module}.", StringComparison.Ordinal))
            {
                return module;
            }
        }

        return null;
    }

    private static IEnumerable<Assembly> ModuleAssemblies(string module)
    {
        foreach (var layer in AllLayers)
        {
            var path = Path.Combine(AppContext.BaseDirectory, $"OpenTms.{module}.{layer}.dll");
            File.Exists(path).ShouldBeTrue($"expected module assembly {path} in test output — was the project renamed?");
            yield return Assembly.LoadFrom(path);
        }
    }

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
