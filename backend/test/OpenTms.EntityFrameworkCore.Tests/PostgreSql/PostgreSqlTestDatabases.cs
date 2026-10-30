using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
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
///
/// Database names carry a per-checkout suffix (<see cref="DatabaseSuffix"/>) so two test processes
/// on the same server never share a database: a second checkout or worktree, or the Stop hook's
/// verify gate running next to a `dotnet test` in another session, each get their own pair.
/// The suffix is OPENTMS_TEST_PG_DB_SUFFIX when set (letters, digits and underscores; anything
/// else becomes an underscore, longer values are cut so the names stay under PostgreSQL's 63-char
/// identifier limit), otherwise the first eight hex characters of the SHA-256 of the full path of
/// the directory holding OpenTms.slnx. The same checkout therefore always reuses its own
/// databases; a deleted worktree leaves its pair behind (docs/local-dev.md says how to clean up).
/// </summary>
public static class PostgreSqlTestDatabases
{
    private const string HostDatabasePrefix = "OpenTms_Tests_";
    private const string TenantADatabasePostfix = "_TenantA";
    private const string TransportDatabasePostfix = "_RunA";

    /// <summary>PostgreSQL identifiers are at most 63 bytes; the suffix gets what the fixed parts leave.</summary>
    private static readonly int MaxSuffixLength = 63 - HostDatabasePrefix.Length - TenantADatabasePostfix.Length;

    /// <summary>Per-checkout part of both database names. See the class summary for how it is derived.</summary>
    public static readonly string DatabaseSuffix = ComputeDatabaseSuffix(
        Environment.GetEnvironmentVariable("OPENTMS_TEST_PG_DB_SUFFIX"),
        FindSolutionDirectory() ?? AppContext.BaseDirectory);

    public static readonly string HostDatabase = HostDatabasePrefix + DatabaseSuffix;
    public static readonly string TenantADatabase = HostDatabasePrefix + DatabaseSuffix + TenantADatabasePostfix;

    /// <summary>
    /// The own database of <see cref="TransportTenantAId"/>: the tests whose rows must commit (the transport-run chain,
    /// the calculators driven through the real jobs) write there, so tenant A stays a rolled-back-only tenant whose
    /// counts the volume and seed tests can rely on.
    /// </summary>
    public static readonly string TransportTenantADatabase = HostDatabasePrefix + DatabaseSuffix + TransportDatabasePostfix;

    /// <summary>Tenant with its own database (<see cref="TenantADatabase"/>). Set once the databases exist.</summary>
    public static Guid TenantAId { get; private set; }

    /// <summary>Shared-tier tenant: no connection string, lives in the host database.</summary>
    public static Guid SharedTenantId { get; private set; }

    /// <summary>The committing tests' tenant with its own database (<see cref="TransportTenantADatabase"/>).</summary>
    public static Guid TransportTenantAId { get; private set; }

    /// <summary>The committing tests' shared-tier tenant (host database): the cross-tier half of their isolation checks.</summary>
    public static Guid TransportSharedTenantId { get; private set; }

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static bool _ready;
    private static Exception? _failure;

    public static string ServerConnectionString =>
        Environment.GetEnvironmentVariable("OPENTMS_TEST_PG")?.TrimEnd(';')
        ?? "Host=localhost;Port=5432;Username=postgres;Password=postgres";

    public static string ConnectionStringFor(string database) => $"{ServerConnectionString};Database={database}";

    public static string HostConnectionString => ConnectionStringFor(HostDatabase);

    /// <summary>
    /// The suffix for a configured value (OPENTMS_TEST_PG_DB_SUFFIX) or, when that is absent or
    /// blank, for a checkout directory. Pure, so the naming rules are unit-tested without a server.
    /// </summary>
    internal static string ComputeDatabaseSuffix(string? configured, string checkoutDirectory)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var builder = new StringBuilder();
            foreach (var c in configured.Trim())
            {
                builder.Append(char.IsAsciiLetterOrDigit(c) || c == '_' ? c : '_');
            }

            if (builder.Length > MaxSuffixLength)
            {
                builder.Length = MaxSuffixLength;
            }

            return builder.ToString();
        }

        var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(checkoutDirectory));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(path));
        return Convert.ToHexStringLower(hash)[..8];
    }

    /// <summary>The directory holding OpenTms.slnx, found by walking up from the test assembly; null outside a checkout.</summary>
    private static string? FindSolutionDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OpenTms.slnx")))
            {
                return directory.FullName;
            }
        }

        return null;
    }

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
        foreach (var database in new[] { HostDatabase, TenantADatabase, TransportTenantADatabase })
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

            var transportA = await tenantManager.CreateAsync("transport-run-a");
            transportA.SetDefaultConnectionString(ConnectionStringFor(TransportTenantADatabase));
            await tenantRepository.InsertAsync(transportA);

            var transportShared = await tenantManager.CreateAsync("transport-run-shared");
            await tenantRepository.InsertAsync(transportShared);

            await unitOfWork.CompleteAsync();

            TenantAId = tenantA.Id;
            SharedTenantId = sharedTenant.Id;
            TransportTenantAId = transportA.Id;
            TransportSharedTenantId = transportShared.Id;
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
