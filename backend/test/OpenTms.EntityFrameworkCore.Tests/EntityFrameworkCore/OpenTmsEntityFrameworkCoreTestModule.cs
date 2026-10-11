using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using OpenTms.MasterData.EntityFrameworkCore;
using OpenTms.Platform.EntityFrameworkCore;
using OpenTms.Platform.EntityFrameworkCore.Localization;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Sqlite;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.FeatureManagement;
using Volo.Abp.Features;
using OpenTms.Features;
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

        /* Test-side feature values: the Master Data services carry [RequiresFeature(MasterData.FirstRun)]
         * (default off); the provider answers "true" unless a test overrides it (TestFeatureValueProvider). */
        Configure<AbpFeatureOptions>(options =>
        {
            options.ValueProviders.Add<TestFeatureValueProvider>();
        });

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

        /* The module contexts own their tables (schema-per-module) in the same shared in-memory database:
         * the Platform context holds the per-user grid settings under test, the Master Data context the
         * ten reference entities (their connection string names fall back to Default). */
        _database.CreateTables(
            new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseSqlite(_database.ConnectionString).Options));

        using (var masterData = new MasterDataDbContext(
                   new DbContextOptionsBuilder<MasterDataDbContext>().UseSqlite(_database.ConnectionString).UseLocalizedNameTranslation().Options))
        {
            masterData.GetService<IRelationalDatabaseCreator>().CreateTables();
        }

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

            /* A module's own Configure<TDbContext> (UseNpgsql) replaces the default action for that
             * context, so each module context is pointed at SQLite here explicitly. */
            options.Configure<PlatformDbContext>(context =>
            {
                context.UseSqlite();
            });
            options.Configure<MasterDataDbContext>(context =>
            {
                context.UseSqlite();
                context.DbContextOptions.UseLocalizedNameTranslation();
            });
        });
    }

    public override void OnApplicationShutdown(ApplicationShutdownContext context)
    {
        _database?.Dispose();
    }
}
