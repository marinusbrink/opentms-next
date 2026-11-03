using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using OpenTms.DbMigrator;
using OpenTms.Platform.Administration;
using OpenTms.Platform.Impersonation;
using OpenTms.PlanningExecution.Seed;
using Shouldly;
using Volo.Abp.Data;
using Volo.Abp.Modularity;
using Xunit;

namespace OpenTms.ArchitectureTests;

/// <summary>
/// The DbMigrator seeds the host and every tenant database (CLAUDE.md, "Multi-tenancy model"), but ABP's data seeder only
/// finds contributors in the assemblies of the modules it loads, and the DbMigrator loads no Application module. A
/// contributor placed in an Application project therefore never runs there: the static host role "Customer Success
/// Manager" (design 63) was never seeded that way. Every OpenTms seed contributor must sit in the DbMigrator's module
/// graph, except the ones listed below with their reason.
/// </summary>
public class DbMigratorSeedTests
{
    private static readonly Dictionary<Type, string> RunOutsideTheDbMigrator = new()
    {
        /* ABP's own PermissionDataSeedContributor already grants the admin role every permission in the DbMigrator; this
         * one only matters where the application layer runs (the provisioning job), and in the DbMigrator both together
         * would add the same grants twice in one unit of work (OpenTmsPostgreSqlTestModule). */
        [typeof(AdministrationPermissionDataSeedContributor)] = "admin grants come from ABP's seed in the DbMigrator",
        /* Design 68 §1: needs Master Data's published lookup from the application layer. */
        [typeof(PlanningPlanboardBackfillContributor)] = "runs wherever the application layer is loaded (design 68)",
    };

    [Fact]
    public void Every_seed_contributor_runs_in_the_DbMigrator_unless_listed()
    {
        var loaded = ModuleAssemblies(typeof(OpenTmsDbMigratorModule));

        var outside = OpenTmsAssemblies()
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IDataSeedContributor).IsAssignableFrom(t))
            .Where(t => !loaded.Contains(t.Assembly) && !RunOutsideTheDbMigrator.ContainsKey(t))
            .Select(t => t.FullName)
            .ToList();

        outside.ShouldBeEmpty("move these into a Domain project, or list them here with the reason they run elsewhere");
    }

    [Fact]
    public void The_Customer_Success_Manager_seed_runs_in_the_DbMigrator()
    {
        ModuleAssemblies(typeof(OpenTmsDbMigratorModule)).ShouldContain(typeof(CustomerSuccessManagerRoleDataSeedContributor).Assembly);
    }

    [Fact]
    public void The_exceptions_really_run_outside_the_DbMigrator()
    {
        /* Keeps the list honest: an entry that moved into the graph is removed from it. */
        var loaded = ModuleAssemblies(typeof(OpenTmsDbMigratorModule));
        RunOutsideTheDbMigrator.Keys.ShouldAllBe(t => !loaded.Contains(t.Assembly));
    }

    private static HashSet<Assembly> ModuleAssemblies(Type startupModule)
    {
        var seen = new HashSet<Type>();
        var pending = new Stack<Type>([startupModule]);
        while (pending.Count > 0)
        {
            var module = pending.Pop();
            if (!seen.Add(module))
            {
                continue;
            }

            foreach (var provider in module.GetCustomAttributes().OfType<IDependedTypesProvider>())
            {
                foreach (var depended in provider.GetDependedTypes())
                {
                    pending.Push(depended);
                }
            }
        }

        return seen.Select(t => t.Assembly).ToHashSet();
    }

    private static IEnumerable<Assembly> OpenTmsAssemblies()
        => Directory.GetFiles(AppContext.BaseDirectory, "OpenTms.*.dll")
            .Where(p => !Path.GetFileName(p).Contains("Tests", StringComparison.Ordinal))
            .Select(p => Assembly.LoadFrom(p))
            .Distinct();
}
