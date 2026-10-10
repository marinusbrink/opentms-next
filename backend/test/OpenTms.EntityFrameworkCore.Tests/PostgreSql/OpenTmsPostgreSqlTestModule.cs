using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.FeatureManagement;
using Volo.Abp.Modularity;
using Volo.Abp.PermissionManagement;
using Volo.Abp.Threading;
using OpenTms.EntityFrameworkCore;
using OpenTms.Platform.Administration;

namespace OpenTms.PostgreSql;

/// <summary>
/// Test module for the parts the test-engineer risk matrix classes as critical: it runs the
/// production EF Core stack (Npgsql, real transactions, database per tenant) against the
/// PostgreSQL databases of <see cref="PostgreSqlTestDatabases"/>. Nothing is replaced or faked
/// below the application services.
/// </summary>
[DependsOn(
    typeof(OpenTmsApplicationTestModule),
    typeof(OpenTmsEntityFrameworkCoreModule)
)]
public class OpenTmsPostgreSqlTestModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        Configure<FeatureManagementOptions>(options =>
        {
            options.SaveStaticFeaturesToDatabase = false;
            options.IsDynamicFeatureStoreEnabled = false;
        });
        Configure<PermissionManagementOptions>(options =>
        {
            options.SaveStaticPermissionsToDatabase = false;
            options.IsDynamicPermissionStoreEnabled = false;
        });

        Configure<AbpDbConnectionOptions>(options =>
        {
            options.ConnectionStrings.Default = PostgreSqlTestDatabases.HostConnectionString;
        });

        context.Services.AddLogging(logging => logging.AddProvider(new CapturingLoggerProvider()));

        /* Seeding mirrors OpenTms.DbMigrator, which does not load Platform.Application: with that
         * contributor present, ABP's permission seeder and the Platform one grant the same admin
         * permissions inside one unit of work and the tenant seed fails on the unique index. */
        Configure<AbpDataSeedOptions>(options =>
        {
            options.Contributors.Remove<AdministrationPermissionDataSeedContributor>();
        });
    }

    public override void OnPreApplicationInitialization(ApplicationInitializationContext context)
    {
        /* Before any module seeds data (OpenTmsTestBaseModule does so in OnApplicationInitialization).
         * AbpIntegratedTest initializes synchronously, so the sync lifecycle method is the one that runs. */
        AsyncHelper.RunSync(() => PostgreSqlTestDatabases.EnsureCreatedAsync(context.ServiceProvider));
    }
}
