using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTms.Platform.Data;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Identity;
using Volo.Abp.MultiTenancy;
using OpenTms.MultiTenancy;
using Volo.Abp.TenantManagement;

namespace OpenTms.Data;

/* Migrates and seeds the HOST database first, then every tenant.
 *
 * Multi-tenancy model (see /CLAUDE.md):
 *   - Database-per-tenant is the provisioning default: a tenant with its own connection
 *     string gets all module schemas migrated into its own database.
 *   - Tenants WITHOUT a connection string share the host database (shared-database tier) —
 *     for them only seeding runs; the host migration already created the schemas.
 *
 * A failing tenant never aborts the run: the failure is recorded, the remaining tenants
 * are still processed, and the run reports per-tenant results at the end. The process
 * exit code is non-zero when any tenant (or the host) failed, so CI catches it.
 */
public class OpenTmsDbMigrationService : ITransientDependency
{
    public ILogger<OpenTmsDbMigrationService> Logger { get; set; }

    private readonly IDataSeeder _dataSeeder;
    private readonly IEnumerable<IDbSchemaMigrator> _dbSchemaMigrators;
    private readonly ITenantRepository _tenantRepository;
    private readonly ICurrentTenant _currentTenant;

    public OpenTmsDbMigrationService(
        IDataSeeder dataSeeder,
        ITenantRepository tenantRepository,
        ICurrentTenant currentTenant,
        IEnumerable<IDbSchemaMigrator> dbSchemaMigrators)
    {
        _dataSeeder = dataSeeder;
        _tenantRepository = tenantRepository;
        _currentTenant = currentTenant;
        _dbSchemaMigrators = dbSchemaMigrators;

        Logger = NullLogger<OpenTmsDbMigrationService>.Instance;
    }

    public sealed record MigrationResult(string Target, bool Succeeded, string? Error);

    /// <summary>Runs all migrations and seeds. Returns per-target results (host + each tenant).</summary>
    public async Task<IReadOnlyList<MigrationResult>> MigrateAsync()
    {
        var results = new List<MigrationResult>();

        Logger.LogInformation("Started database migrations...");

        try
        {
            await MigrateDatabaseSchemaAsync();
            await SeedDataAsync();
            results.Add(new MigrationResult("host", true, null));
            Logger.LogInformation("Successfully completed host database migrations.");
        }
        catch (Exception ex)
        {
            /* A broken host database is fatal: tenant migration depends on the tenant store. */
            Logger.LogError(ex, "Host database migration FAILED.");
            results.Add(new MigrationResult("host", false, ex.Message));
            ReportResults(results);
            return results;
        }

        if (MultiTenancyConsts.IsEnabled)
        {
            var tenants = await _tenantRepository.GetListAsync(includeDetails: true);

            var migratedDatabaseSchemas = new HashSet<string>();
            foreach (var tenant in tenants)
            {
                try
                {
                    using (_currentTenant.Change(tenant.Id))
                    {
                        if (tenant.ConnectionStrings.Any())
                        {
                            var tenantConnectionStrings = tenant.ConnectionStrings
                                .Select(x => x.Value)
                                .ToList();

                            if (!migratedDatabaseSchemas.IsSupersetOf(tenantConnectionStrings))
                            {
                                await MigrateDatabaseSchemaAsync(tenant);

                                migratedDatabaseSchemas.AddIfNotContains(tenantConnectionStrings);
                            }
                        }

                        await SeedDataAsync(tenant);
                    }

                    results.Add(new MigrationResult(tenant.Name, true, null));
                    Logger.LogInformation("Successfully completed {TenantName} tenant database migrations.", tenant.Name);
                }
                catch (Exception ex)
                {
                    /* Continue past a failing tenant: one broken tenant database must never
                     * block the migration of every other tenant. */
                    results.Add(new MigrationResult(tenant.Name, false, ex.Message));
                    Logger.LogError(ex, "Tenant {TenantName} ({TenantId}) database migration FAILED; continuing with remaining tenants.",
                        tenant.Name, tenant.Id);
                }
            }
        }

        ReportResults(results);
        return results;
    }

    private void ReportResults(IReadOnlyList<MigrationResult> results)
    {
        var failed = results.Count(r => !r.Succeeded);

        Logger.LogInformation("Migration results ({Total} targets, {Failed} failed):", results.Count, failed);
        foreach (var result in results)
        {
            if (result.Succeeded)
            {
                Logger.LogInformation("  [OK]     {Target}", result.Target);
            }
            else
            {
                Logger.LogError("  [FAILED] {Target}: {Error}", result.Target, result.Error);
            }
        }
    }

    private async Task MigrateDatabaseSchemaAsync(Tenant? tenant = null)
    {
        Logger.LogInformation(
            "Migrating schema for {Target} database...", tenant == null ? "host" : tenant.Name + " tenant");

        /* The "Framework" migrator (ABP tables) runs first, then every module context —
         * each into its own schema with its own migrations history table. */
        foreach (var migrator in _dbSchemaMigrators
                     .OrderBy(m => m.ModuleName == "Framework" ? 0 : 1)
                     .ThenBy(m => m.ModuleName, StringComparer.Ordinal))
        {
            Logger.LogInformation("  Migrating module {ModuleName}...", migrator.ModuleName);
            await migrator.MigrateAsync();
        }
    }

    private async Task SeedDataAsync(Tenant? tenant = null)
    {
        Logger.LogInformation("Executing {Target} database seed...", tenant == null ? "host" : tenant.Name + " tenant");

        await _dataSeeder.SeedAsync(new DataSeedContext(tenant?.Id)
            .WithProperty(IdentityDataSeedContributor.AdminEmailPropertyName,
                OpenTmsConsts.AdminEmailDefaultValue)
            .WithProperty(IdentityDataSeedContributor.AdminPasswordPropertyName,
                OpenTmsConsts.AdminPasswordDefaultValue)
        );
    }
}
