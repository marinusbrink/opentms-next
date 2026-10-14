using System.Threading;
using System.Threading.Tasks;

namespace OpenTms.Platform.Tenants;

/// <summary>Where a tenant database lives and who logs in to it, as read from (or written into) a connection string.</summary>
public sealed record TenantDatabaseLocation(string Host, int Port, string DatabaseName, string? UserName);

/// <summary>
/// The port behind tenant provisioning and deletion: builds a tenant's connection string and creates or
/// drops its database and login role on the server. The PostgreSQL adapter lives in
/// Platform.EntityFrameworkCore (docs/designs/tenants-and-editions.md).
/// </summary>
public interface ITenantDatabaseProvisioner
{
    /// <summary>
    /// The connection string for a new tenant: the configured server (default: the host database's), the
    /// generated database and role, the given password and the configured pool settings.
    /// </summary>
    string BuildConnectionString(string databaseAndRoleName, string password);

    /// <summary>The same string with the password replaced, for display.</summary>
    string Mask(string connectionString);

    /// <summary>Server, database and user of a connection string; null when it cannot be parsed.</summary>
    TenantDatabaseLocation? Parse(string connectionString);

    /// <summary>The host database's location, to keep the drop away from it.</summary>
    TenantDatabaseLocation GetHostLocation();

    /// <summary>
    /// Makes sure the login role exists with this password and owns a database of the same name that
    /// only it may connect to. Idempotent: a retry after a partial run finishes the job.
    /// </summary>
    Task EnsureDatabaseAsync(string connectionString, CancellationToken cancellationToken = default);

    /// <summary>Drops the database (closing its connections) and its login role, when they exist.</summary>
    Task DropDatabaseAsync(TenantDatabaseLocation location, CancellationToken cancellationToken = default);
}
