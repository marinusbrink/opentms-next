using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Npgsql;
using OpenTms.Data;
using OpenTms.EntityFrameworkCore;
using OpenTms.EntityFrameworkCore.Platform.Tenants;
using OpenTms.Features;
using OpenTms.Platform.Administration;
using OpenTms.Platform.Tenants;
using OpenTms.Platform.TestData;
using OpenTms.TestDoubles;
using Volo.Abp;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.Data;
using Volo.Abp.FeatureManagement;
using Volo.Abp.Features;
using Volo.Abp.Modularity;
using Volo.Abp.PermissionManagement;
using Volo.Abp.TenantManagement;
using Volo.Abp.Threading;
using Volo.Abp.Uow;

namespace OpenTms.PostgreSql.TestData;

/// <summary>
/// The database of the "Reset to dataset" integration tests (docs/designs/65-tenant-test-datasets.md): a host database
/// of its own with two shared-tier tenants (no connection string, both in the host database), created, migrated and
/// seeded by the real DbMigrator once per process. A database of its own because a reset commits batch by batch and
/// wipes whole tenants; the shared test databases of <see cref="PostgreSqlTestDatabases"/> hold tenants whose rows and
/// counts other test classes rely on (and the Maintenance tests count the shared-tier tenants of that host database).
/// </summary>
public static class TestDatasetTestDatabases
{
    public static readonly string HostDatabase = "OpenTms_Tests_" + PostgreSqlTestDatabases.DatabaseSuffix + "_Datasets";

    public static string HostConnectionString => PostgreSqlTestDatabases.ConnectionStringFor(HostDatabase);

    /// <summary>The tenant the tests reset. Shared tier.</summary>
    public static Guid TenantAId { get; private set; }

    /// <summary>The bystander: shared tier in the same host database as <see cref="TenantAId"/>.</summary>
    public static Guid TenantBId { get; private set; }

    /// <summary>Rows per tenant-scoped table of tenant A right after the DbMigrator seeded it, before any reset.</summary>
    public static IReadOnlyDictionary<string, long> SeededCountsOfA { get; private set; } = new Dictionary<string, long>();

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static bool _ready;
    private static Exception? _failure;

    public static async Task EnsureCreatedAsync(IServiceProvider serviceProvider)
    {
        await Gate.WaitAsync();
        try
        {
            if (_ready)
            {
                return;
            }

            if (_failure != null)
            {
                throw new InvalidOperationException("The dataset test database could not be created earlier in this process.", _failure);
            }

            try
            {
                await CreateAsync(serviceProvider);
                _ready = true;
            }
            catch (Exception ex)
            {
                _failure = ex;
                throw;
            }
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task CreateAsync(IServiceProvider serviceProvider)
    {
        await using (var connection = new NpgsqlConnection(PostgreSqlTestDatabases.ConnectionStringFor("postgres")))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{HostDatabase}\" WITH (FORCE)", connection);
            await command.ExecuteNonQueryAsync();
        }

        NpgsqlConnection.ClearAllPools();

        using var scope = serviceProvider.CreateScope();
        var migrator = scope.ServiceProvider.GetRequiredService<OpenTmsDbMigrationService>();
        ThrowIfFailed(await migrator.MigrateAsync());

        var unitOfWorkManager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        var tenantManager = scope.ServiceProvider.GetRequiredService<ITenantManager>();
        var tenantRepository = scope.ServiceProvider.GetRequiredService<ITenantRepository>();
        using (var unitOfWork = unitOfWorkManager.Begin(new AbpUnitOfWorkOptions { IsTransactional = true }, requiresNew: true))
        {
            var a = await tenantManager.CreateAsync("dataset-a");
            await tenantRepository.InsertAsync(a);
            var b = await tenantManager.CreateAsync("dataset-b");
            await tenantRepository.InsertAsync(b);
            await unitOfWork.CompleteAsync();
            TenantAId = a.Id;
            TenantBId = b.Id;
        }

        /* Pass 2 seeds both shared-tier tenants, as the DbMigrator does in production. */
        ThrowIfFailed(await migrator.MigrateAsync());

        SeededCountsOfA = await TenantTables.CountAsync(TenantAId);
    }

    private static void ThrowIfFailed(IReadOnlyList<OpenTmsDbMigrationService.MigrationResult> results)
    {
        foreach (var result in results)
        {
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Dataset test database migration failed for {result.Target}: {result.Error}\n--- logged errors ---\n{CapturingLoggerProvider.Drain()}");
            }
        }
    }
}

/// <summary>
/// The production stack of <see cref="OpenTmsPostgreSqlTestModule"/> on <see cref="TestDatasetTestDatabases"/>, with the
/// embedded address register replaced by <see cref="InTestAddressRegister"/> (the real extract may be absent) and the
/// job queue by <see cref="FakeBackgroundJobManager"/> (the end-to-end test runs the queued job itself).
/// </summary>
[DependsOn(
    typeof(OpenTmsApplicationTestModule),
    typeof(OpenTmsEntityFrameworkCoreModule)
)]
public class TestDatasetPostgreSqlTestModule : AbpModule
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
        Configure<AbpFeatureOptions>(options => options.ValueProviders.Add<TestFeatureValueProvider>());
        Configure<AbpDbConnectionOptions>(options => options.ConnectionStrings.Default = TestDatasetTestDatabases.HostConnectionString);
        Configure<TenantDatabaseOptions>(options => options.Provisioning.AllowHostLogin = true);
        context.Services.AddLogging(logging => logging.AddProvider(new CapturingLoggerProvider()));

        /* As OpenTmsPostgreSqlTestModule: seeding mirrors OpenTms.DbMigrator. */
        Configure<AbpDataSeedOptions>(options => options.Contributors.Remove<AdministrationPermissionDataSeedContributor>());

        var addresses = new InTestAddressRegister();
        context.Services.AddSingleton(addresses);
        context.Services.Replace(ServiceDescriptor.Singleton<TestAddressRegister>(addresses));

        var jobs = new FakeBackgroundJobManager();
        context.Services.AddSingleton(jobs);
        context.Services.Replace(ServiceDescriptor.Singleton<IBackgroundJobManager>(jobs));
    }

    public override void OnPreApplicationInitialization(ApplicationInitializationContext context)
    {
        AsyncHelper.RunSync(() => TestDatasetTestDatabases.EnsureCreatedAsync(context.ServiceProvider));
    }
}

/// <summary>Raw counts over every tenant-scoped table (a <c>TenantId</c> column) of the dataset test database.</summary>
public static class TenantTables
{
    public static async Task<IReadOnlyDictionary<string, long>> CountAsync(Guid tenantId, string? extraCondition = null)
    {
        await using var connection = new NpgsqlConnection(TestDatasetTestDatabases.HostConnectionString);
        await connection.OpenAsync();

        var tables = new List<(string Schema, string Table, bool SoftDelete)>();
        await using (var list = new NpgsqlCommand(
                         """
                         SELECT c.table_schema, c.table_name,
                                EXISTS (SELECT 1 FROM information_schema.columns d
                                        WHERE d.table_schema = c.table_schema AND d.table_name = c.table_name AND d.column_name = 'IsDeleted')
                         FROM information_schema.columns c
                         JOIN information_schema.tables t ON t.table_schema = c.table_schema AND t.table_name = c.table_name
                         WHERE c.column_name = 'TenantId' AND t.table_type = 'BASE TABLE'
                           AND c.table_schema NOT IN ('pg_catalog', 'information_schema', 'hangfire')
                         ORDER BY 1, 2
                         """, connection))
        await using (var reader = await list.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                tables.Add((reader.GetString(0), reader.GetString(1), reader.GetBoolean(2)));
            }
        }

        var counts = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach (var (schema, table, softDelete) in tables)
        {
            if (extraCondition == "deleted" && !softDelete)
            {
                continue;
            }

            var where = extraCondition == "deleted" ? " AND \"IsDeleted\"" : string.Empty;
            await using var count = new NpgsqlCommand($"SELECT count(*) FROM \"{schema}\".\"{table}\" WHERE \"TenantId\" = @t{where}", connection);
            count.Parameters.AddWithValue("t", tenantId);
            counts[$"{schema}.{table}"] = (long)(await count.ExecuteScalarAsync())!;
        }

        return counts;
    }

    public static async Task<List<Guid>> IdsAsync(string qualifiedTable, Guid tenantId)
    {
        var (schema, table) = Split(qualifiedTable);
        await using var connection = new NpgsqlConnection(TestDatasetTestDatabases.HostConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"SELECT \"Id\" FROM \"{schema}\".\"{table}\" WHERE \"TenantId\" = @t ORDER BY \"Id\"", connection);
        command.Parameters.AddWithValue("t", tenantId);
        var ids = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            ids.Add(reader.GetGuid(0));
        }

        return ids;
    }

    public static async Task<long> CountByIdAsync(string qualifiedTable, Guid id)
    {
        var (schema, table) = Split(qualifiedTable);
        await using var connection = new NpgsqlConnection(TestDatasetTestDatabases.HostConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"SELECT count(*) FROM \"{schema}\".\"{table}\" WHERE \"Id\" = @id", connection);
        command.Parameters.AddWithValue("id", id);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static (string Schema, string Table) Split(string qualifiedTable)
    {
        var dot = qualifiedTable.IndexOf('.');
        return (qualifiedTable[..dot], qualifiedTable[(dot + 1)..]);
    }
}
