using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.DependencyInjection;

namespace OpenTms.Platform.Tenants;

/* STUB — full wiring arrives at department onboarding.
 *
 * Provisioning steps for the database-per-tenant default:
 *   1. CreateTenantDatabaseAsync  — CREATE DATABASE + save the tenant's "Default" connection string.
 *   2. MigrateTenantDatabaseAsync — run all IDbSchemaMigrator implementations for the tenant.
 *   3. SeedTenantDatabaseAsync    — seed admin user/roles and default settings for the tenant.
 *
 * A shared-database tier skips step 1 and 2 (no connection string = host database, ABP default),
 * so the mechanism stays per-tenant configurable without rearchitecting.
 */
public class TenantProvisioningJob : AsyncBackgroundJob<TenantProvisioningArgs>, ITransientDependency
{
    public override async Task ExecuteAsync(TenantProvisioningArgs args)
    {
        Logger.LogInformation(
            "Tenant provisioning started for tenant {TenantId} (database {DatabaseName})",
            args.TenantId, args.DatabaseName ?? "<derived>");

        await CreateTenantDatabaseAsync(args);
        await MigrateTenantDatabaseAsync(args);
        await SeedTenantDatabaseAsync(args);
    }

    private Task CreateTenantDatabaseAsync(TenantProvisioningArgs args)
    {
        throw new NotImplementedException(
            "TODO(provisioning): CREATE DATABASE and register the tenant's 'Default' connection string. " +
            "Until this is wired, provision manually: createdb + insert connection string + run OpenTms.DbMigrator.");
    }

    private Task MigrateTenantDatabaseAsync(TenantProvisioningArgs args)
    {
        throw new NotImplementedException(
            "TODO(provisioning): run all IDbSchemaMigrator implementations under ICurrentTenant.Change(tenantId).");
    }

    private Task SeedTenantDatabaseAsync(TenantProvisioningArgs args)
    {
        throw new NotImplementedException(
            "TODO(provisioning): seed the tenant admin user, roles and default settings via IDataSeeder.");
    }
}
