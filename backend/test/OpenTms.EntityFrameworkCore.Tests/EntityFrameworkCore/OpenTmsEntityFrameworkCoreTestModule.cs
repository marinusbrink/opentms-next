using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenTms.Platform.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Sqlite;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.FeatureManagement;
using Volo.Abp.Modularity;
using Volo.Abp.PermissionManagement;
using Volo.Abp.Uow;

namespace OpenTms.EntityFrameworkCore;

[DependsOn(
    typeof(OpenTmsApplicationTestModule),
    typeof(OpenTmsEntityFrameworkCoreModule),
    typeof(AbpEntityFrameworkCoreSqliteModule)
)]
public class OpenTmsEntityFrameworkCoreTestModule : AbpModule
{
    private AbpUnitTestSqliteDatabase? _database;

    public override void PreConfigureServices(ServiceConfigurationContext context)
    {
        PreConfigure<AbpSqliteOptions>(x => x.BusyTimeout = null);
    }

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
        context.Services.AddAlwaysDisableUnitOfWorkTransaction();

        /* SQLite tests run without transactions, so the transactional outbox cannot apply here:
         * distributed events are delivered directly. The outbox itself is covered by the
         * PostgreSQL-backed tests (OpenTmsPostgreSqlTestModule). */
        Configure<AbpDistributedEventBusOptions>(options =>
        {
            options.Outboxes.Clear();
        });

        ConfigureInMemorySqlite(context.Services);

    }

    private void ConfigureInMemorySqlite(IServiceCollection services)
    {
        _database = new AbpUnitTestSqliteDatabase();
        _database.CreateTables(
            new OpenTmsDbContext(new DbContextOptionsBuilder<OpenTmsDbContext>().UseSqlite(_database.ConnectionString).Options));

        /* The module contexts own their tables (schema-per-module); the Platform context holds the
         * per-user grid settings under test. Same shared in-memory database as the host context. */
        _database.CreateTables(
            new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseSqlite(_database.ConnectionString).Options));

        services.Configure<AbpDbConnectionOptions>(options =>
        {
            options.ConnectionStrings.Default = _database.ConnectionString;
        });

        services.Configure<AbpDbContextOptions>(options =>
        {
            options.Configure(context =>
            {
                context.UseSqlite();
            });

            /* PlatformEntityFrameworkCoreModule registers a context-specific Npgsql configurer, which
             * takes precedence over the default one above; replace it for the SQLite tests. */
            options.Configure<PlatformDbContext>(context =>
            {
                context.UseSqlite();
            });
        });
    }

    public override void OnApplicationShutdown(ApplicationShutdownContext context)
    {
        _database?.Dispose();
    }
}
