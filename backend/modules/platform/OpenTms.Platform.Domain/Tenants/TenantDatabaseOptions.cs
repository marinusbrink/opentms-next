namespace OpenTms.Platform.Tenants;

/// <summary>
/// The <c>Tenancy</c> configuration section: where a new tenant's database goes and how its connection
/// string pools (docs/designs/tenants-and-editions.md, PO decision D3, 2026-10-14).
/// </summary>
public class TenantDatabaseOptions
{
    public const string SectionName = "Tenancy";

    public TenantDatabaseServerOptions Database { get; set; } = new();

    public TenantConnectionPoolOptions ConnectionPool { get; set; } = new();

    public TenantProvisioningConnectionOptions Provisioning { get; set; } = new();
}

/// <summary>
/// The server new tenant databases are created on. Empty values fall back to the host database's
/// server, the default (PO 2026-10-14). A second server (AP-09) or a pooler (AO-06) changes only these.
/// </summary>
public class TenantDatabaseServerOptions
{
    public string? Host { get; set; }

    public int? Port { get; set; }
}

/// <summary>
/// Npgsql pool settings written into every tenant connection string. A database per tenant means a pool
/// per tenant per instance (04-data-and-tenancy: connections are the constraint), so the floor is zero:
/// an idle tenant holds no connection. AP-08 will turn these into a budget per role.
/// </summary>
public class TenantConnectionPoolOptions
{
    public int MinimumPoolSize { get; set; } = 0;

    public int MaximumPoolSize { get; set; } = 10;

    /// <summary>Seconds an idle connection above the minimum stays open.</summary>
    public int ConnectionIdleLifetime { get; set; } = 60;

    /// <summary>Seconds between the pool's checks for idle connections to close.</summary>
    public int ConnectionPruningInterval { get; set; } = 10;
}

public class TenantProvisioningConnectionOptions
{
    /// <summary>
    /// The login that creates and drops tenant databases and roles (needs CREATEDB and CREATEROLE). It becomes a member
    /// of every tenant role it creates, which PostgreSQL 16+ requires to create a database owned by that role.
    /// Empty: the host database's login, which only <see cref="AllowHostLogin"/> permits.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Lets the host database's login provision tenants: when <see cref="ConnectionString"/> is empty, or names that
    /// same login. Local development only (appsettings.Development.json); everywhere else provisioning refuses, so the
    /// login every request uses never needs CREATEDB or CREATEROLE (constitution rule 7).
    /// </summary>
    public bool AllowHostLogin { get; set; }
}
