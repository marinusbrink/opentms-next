using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OpenTms.Data;
using Volo.Abp.Data;
using Volo.Abp.MultiTenancy;
using Volo.Abp.TenantManagement;
using Volo.Abp.Uow;

namespace OpenTms.PostgreSql;

/// <summary>
/// The PostgreSQL databases behind the critical-class integration tests: a host database, one
/// tenant with its own database (database-per-tenant default) and one shared-tier tenant without
/// a connection string. Created and migrated once per test process by the real DbMigrator
/// service, so the schema under test is the schema that ships.
///
/// Server: localhost:5432, postgres/postgres (CLAUDE.md "Running locally"; CI's postgres service),
/// overridable with OPENTMS_TEST_PG (a connection string without a Database= part).
/// </summary>
public static class PostgreSqlTestDatabases
{
    public const string HostDatabase = "OpenTms_Tests";
    public const string TenantADatabase = "OpenTms_Tests_TenantA";

    /// <summary>Tenant with its own database (<see cref="TenantADatabase"/>). Set once the databases exist.</summary>
    public static Guid TenantAId { get; private set; }

    /// <summary>Shared-tier tenant: no connection string, lives in the host database.</summary>
    public static Guid SharedTenantId { get; private set; }

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static bool _ready;
    private static Exception? _failure;

    public static string ServerConnectionString =>
        Environment.GetEnvironmentVariable("OPENTMS_TEST_PG")?.TrimEnd(';')
        ?? "Host=localhost;Port=5432;Username=postgres;Password=postgres";

    public static string ConnectionStringFor(string database) => $"{ServerConnectionString};Database={database}";

    public static string HostConnectionString => ConnectionStringFor(HostDatabase);

    /// <summary>Drops, creates, migrates and seeds the test databases — once per process.</summary>
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
                throw new InvalidOperationException("The PostgreSQL test databases could not be created earlier in this process.", _failure);
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
        {
            await DropDatabasesAsync();

            using (var scope = serviceProvider.CreateScope())
            {
                var migrator = scope.ServiceProvider.GetRequiredService<OpenTmsDbMigrationService>();

                /* Pass 1: the host database (the migrator creates it). */
                ThrowIfFailed(await migrator.MigrateAsync());

                await CreateTenantsAsync(scope.ServiceProvider);

                /* Pass 2: tenant A's own database plus seeding of both tenants. */
                ThrowIfFailed(await migrator.MigrateAsync());
            }
        }
    }

    public static async Task<long> CountOutboxRowsAsync(string database, string eventDataContains)
    {
        await using var connection = new NpgsqlConnection(ConnectionStringFor(database));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM platform.\"AbpEventOutbox\" WHERE convert_from(\"EventData\", 'UTF8') LIKE @needle",
            connection);
        command.Parameters.AddWithValue("needle", "%" + eventDataContains + "%");
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static async Task DropDatabasesAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionStringFor("postgres"));
        await connection.OpenAsync();
        foreach (var database in new[] { HostDatabase, TenantADatabase })
        {
            await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{database}\" WITH (FORCE)", connection);
            await command.ExecuteNonQueryAsync();
        }

        /* Idle pooled connections to a dropped database are dead; the next user would see 57P01. */
        NpgsqlConnection.ClearAllPools();
    }

    private static async Task CreateTenantsAsync(IServiceProvider serviceProvider)
    {
        var unitOfWorkManager = serviceProvider.GetRequiredService<IUnitOfWorkManager>();
        var tenantManager = serviceProvider.GetRequiredService<ITenantManager>();
        var tenantRepository = serviceProvider.GetRequiredService<ITenantRepository>();

        using (var unitOfWork = unitOfWorkManager.Begin(new AbpUnitOfWorkOptions { IsTransactional = true }, requiresNew: true))
        {
            var tenantA = await tenantManager.CreateAsync("outbox-tenant-a");
            tenantA.SetDefaultConnectionString(ConnectionStringFor(TenantADatabase));
            await tenantRepository.InsertAsync(tenantA);

            /* Shared tier: no connection string → lives in the host database. */
            var sharedTenant = await tenantManager.CreateAsync("outbox-tenant-shared");
            await tenantRepository.InsertAsync(sharedTenant);

            await unitOfWork.CompleteAsync();

            TenantAId = tenantA.Id;
            SharedTenantId = sharedTenant.Id;
        }
    }

    private static void ThrowIfFailed(System.Collections.Generic.IReadOnlyList<OpenTmsDbMigrationService.MigrationResult> results)
    {
        foreach (var result in results)
        {
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Test database migration failed for {result.Target}: {result.Error}\n--- logged errors ---\n{CapturingLoggerProvider.Drain()}");
            }
        }
    }
}
