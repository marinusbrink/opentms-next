using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using OpenTms.Platform.Tenants;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;

namespace OpenTms.Platform.EntityFrameworkCore.Tenants;

/// <summary>
/// <see cref="ITenantDatabaseProvisioner"/> for PostgreSQL (docs/designs/tenants-and-editions.md). DDL takes no
/// parameters, so every statement is composed by the server itself with <c>format('%I', …)</c> for identifiers
/// and <c>%L</c> for the password, then executed; nothing user-typed is concatenated into SQL. Names are checked
/// against <see cref="TenantDatabaseNaming.IsGenerated"/> before anything is created or dropped. Raw SQL is used
/// deliberately: roles and databases are server objects, not entities (coding conventions: documented exception).
/// </summary>
public class PostgreSqlTenantDatabaseProvisioner : ITenantDatabaseProvisioner, ITransientDependency
{
    private const string MaskedPassword = "********";

    private static int _hostLoginWarned;

    public ILogger<PostgreSqlTenantDatabaseProvisioner> Logger { get; set; } = NullLogger<PostgreSqlTenantDatabaseProvisioner>.Instance;

    private readonly TenantDatabaseOptions _options;
    private readonly AbpDbConnectionOptions _connectionOptions;

    public PostgreSqlTenantDatabaseProvisioner(
        IOptions<TenantDatabaseOptions> options,
        IOptions<AbpDbConnectionOptions> connectionOptions)
    {
        _options = options.Value;
        _connectionOptions = connectionOptions.Value;
    }

    public string BuildConnectionString(string databaseAndRoleName, string password)
    {
        EnsureGenerated(databaseAndRoleName);

        var pool = _options.ConnectionPool;
        var builder = new NpgsqlConnectionStringBuilder(HostConnectionString)
        {
            Database = databaseAndRoleName,
            Username = databaseAndRoleName,
            Password = password,
            Pooling = true,
            MinPoolSize = pool.MinimumPoolSize,
            MaxPoolSize = pool.MaximumPoolSize,
            ConnectionIdleLifetime = pool.ConnectionIdleLifetime,
            ConnectionPruningInterval = pool.ConnectionPruningInterval
        };

        if (!string.IsNullOrWhiteSpace(_options.Database.Host))
        {
            builder.Host = _options.Database.Host;
        }

        if (_options.Database.Port is { } port)
        {
            builder.Port = port;
        }

        return builder.ConnectionString;
    }

    public string Mask(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        if (!string.IsNullOrEmpty(builder.Password))
        {
            builder.Password = MaskedPassword;
        }

        return builder.ConnectionString;
    }

    public TenantDatabaseLocation? Parse(string connectionString)
    {
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            return string.IsNullOrEmpty(builder.Database) || string.IsNullOrEmpty(builder.Host)
                ? null
                : new TenantDatabaseLocation(builder.Host, builder.Port, builder.Database, builder.Username);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    public TenantDatabaseLocation GetHostLocation() =>
        Parse(HostConnectionString) ?? throw new InvalidOperationException("The host connection string has no host or database.");

    public async Task EnsureDatabaseAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        var target = new NpgsqlConnectionStringBuilder(connectionString);
        var name = target.Database!;
        EnsureGenerated(name);
        if (!string.Equals(target.Username, name, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("A tenant database is owned by the role of the same name.");
        }

        await using var connection = await OpenServerConnectionAsync(target.Host!, target.Port, cancellationToken);

        var roleExists = await ExistsAsync(connection, "SELECT 1 FROM pg_roles WHERE rolname = @name", name, cancellationToken);
        await ExecuteFormattedAsync(
            connection,
            roleExists
                ? "SELECT format('ALTER ROLE %I WITH LOGIN PASSWORD %L', @name, @password)"
                : "SELECT format('CREATE ROLE %I WITH LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE PASSWORD %L', @name, @password)",
            name, target.Password, cancellationToken);

        if (!await ExistsAsync(connection, "SELECT 1 FROM pg_database WHERE datname = @name", name, cancellationToken))
        {
            await ExecuteFormattedAsync(connection, "SELECT format('CREATE DATABASE %I OWNER %I', @name, @name)", name, null, cancellationToken);
        }

        /* Only the tenant's own role connects: every other login (other tenants included) is refused. */
        await ExecuteFormattedAsync(connection, "SELECT format('REVOKE CONNECT, TEMPORARY ON DATABASE %I FROM PUBLIC', @name)", name, null, cancellationToken);

        Logger.LogInformation("Tenant database {DatabaseName} is present on {Server}", name, target.Host);
    }

    public async Task DropDatabaseAsync(TenantDatabaseLocation location, CancellationToken cancellationToken = default)
    {
        EnsureGenerated(location.DatabaseName);
        var host = GetHostLocation();
        if (string.Equals(location.DatabaseName, host.DatabaseName, StringComparison.Ordinal)
            && string.Equals(location.Host, host.Host, StringComparison.OrdinalIgnoreCase)
            && location.Port == host.Port)
        {
            throw new InvalidOperationException("Refusing to drop the host database.");
        }

        await using var connection = await OpenServerConnectionAsync(location.Host, location.Port, cancellationToken);

        await ExecuteFormattedAsync(connection, "SELECT format('DROP DATABASE IF EXISTS %I WITH (FORCE)', @name)", location.DatabaseName, null, cancellationToken);
        if (location.UserName != null && TenantDatabaseNaming.IsGenerated(location.UserName))
        {
            await ExecuteFormattedAsync(connection, "SELECT format('DROP ROLE IF EXISTS %I', @name)", location.UserName, null, cancellationToken);
        }

        Logger.LogInformation("Tenant database {DatabaseName} dropped on {Server}", location.DatabaseName, location.Host);
    }

    private string HostConnectionString =>
        _connectionOptions.ConnectionStrings.Default
        ?? throw new InvalidOperationException("No default connection string is configured.");

    /// <summary>The provisioning login on the target server's maintenance database, without pooling.</summary>
    private async Task<NpgsqlConnection> OpenServerConnectionAsync(string host, int port, CancellationToken cancellationToken)
    {
        var useHostLogin = string.IsNullOrWhiteSpace(_options.Provisioning.ConnectionString);
        if (useHostLogin && Interlocked.Exchange(ref _hostLoginWarned, 1) == 0)
        {
            /* Least privilege (constitution rule 7): the host login then needs CREATEDB and CREATEROLE, which every
             * request would carry. Fine for local development; production sets a provisioning login of its own. */
            Logger.LogWarning(
                "Tenancy:Provisioning:ConnectionString is not set: tenant databases are created with the host database's login.");
        }

        var admin = new NpgsqlConnectionStringBuilder(useHostLogin ? HostConnectionString : _options.Provisioning.ConnectionString)
        {
            Host = host,
            Port = port,
            Database = "postgres",
            Pooling = false
        };

        var connection = new NpgsqlConnection(admin.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static async Task<bool> ExistsAsync(NpgsqlConnection connection, string sql, string name, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("name", name);
        return await command.ExecuteScalarAsync(cancellationToken) != null;
    }

    private static async Task ExecuteFormattedAsync(
        NpgsqlConnection connection, string formatSql, string name, string? password, CancellationToken cancellationToken)
    {
        string statement;
        await using (var format = new NpgsqlCommand(formatSql, connection))
        {
            format.Parameters.AddWithValue("name", name);
            if (password != null)
            {
                format.Parameters.AddWithValue("password", password);
            }

            statement = (string)(await format.ExecuteScalarAsync(cancellationToken))!;
        }

        await using var command = new NpgsqlCommand(statement, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void EnsureGenerated(string name)
    {
        if (!TenantDatabaseNaming.IsGenerated(name))
        {
            throw new InvalidOperationException("Not a generated tenant database name; refusing to touch it.");
        }
    }
}
