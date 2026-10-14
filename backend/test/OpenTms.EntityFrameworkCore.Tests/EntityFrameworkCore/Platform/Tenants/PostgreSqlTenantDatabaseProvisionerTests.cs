using System;
using System.Threading.Tasks;
using Npgsql;
using OpenTms.Platform.EntityFrameworkCore.Tenants;
using OpenTms.Platform.Tenants;
using Shouldly;
using Xunit;

namespace OpenTms.EntityFrameworkCore.Platform.Tenants;

/// <summary>
/// Unit tests for the parts of <see cref="PostgreSqlTenantDatabaseProvisioner"/> that need no server: the
/// connection string it writes (PO decision D3), masking, parsing, and the guards that run before any connection
/// is opened. Server hosts are <c>.invalid</c> so a guard that failed to fire would surface as a connection error,
/// not as the expected refusal. The DDL itself is covered against PostgreSQL in PostgreSql/Tenants.
/// Risk class: Critical — the connection string carries a tenant's credentials; the guards protect the host database.
/// </summary>
public class PostgreSqlTenantDatabaseProvisionerTests
{
    private const string HostConnectionString = "Host=pg.host.invalid;Port=6543;Database=OpenTms;Username=postgres;Password=host-secret";

    private static readonly string Name = TenantDatabaseNaming.For("acme", Guid.Parse("0a1b2c3d-0000-0000-0000-000000000000"));

    private static PostgreSqlTenantDatabaseProvisioner Create(string? hostConnectionString = null, TenantDatabaseOptions? options = null) =>
        FakeTenantDatabaseProvisioner.Create(hostConnectionString ?? HostConnectionString, options);

    [Fact]
    public void The_connection_string_points_at_the_host_server_with_the_tenants_own_database_role_and_pool_settings()
    {
        var built = new NpgsqlConnectionStringBuilder(Create().BuildConnectionString(Name, "tenant-secret"));

        built.Host.ShouldBe("pg.host.invalid");
        built.Port.ShouldBe(6543);
        built.Database.ShouldBe(Name);
        built.Username.ShouldBe(Name);
        built.Password.ShouldBe("tenant-secret");
        built.Pooling.ShouldBeTrue();
        built.MinPoolSize.ShouldBe(0);
        built.MaxPoolSize.ShouldBe(10);
        built.ConnectionIdleLifetime.ShouldBe(60);
        built.ConnectionPruningInterval.ShouldBe(10);
    }

    [Fact]
    public void The_host_credentials_never_end_up_in_a_tenant_connection_string()
    {
        var built = Create().BuildConnectionString(Name, "tenant-secret");

        built.ShouldNotContain("host-secret");
        built.ShouldNotContain("postgres", Case.Insensitive);
        built.ShouldNotContain("OpenTms;");
    }

    [Fact]
    public void Configured_pool_settings_are_used()
    {
        var options = new TenantDatabaseOptions
        {
            ConnectionPool = { MinimumPoolSize = 1, MaximumPoolSize = 25, ConnectionIdleLifetime = 120, ConnectionPruningInterval = 20 }
        };

        var built = new NpgsqlConnectionStringBuilder(Create(options: options).BuildConnectionString(Name, "pw"));

        built.MinPoolSize.ShouldBe(1);
        built.MaxPoolSize.ShouldBe(25);
        built.ConnectionIdleLifetime.ShouldBe(120);
        built.ConnectionPruningInterval.ShouldBe(20);
    }

    [Fact]
    public void A_configured_server_overrides_the_host_servers_host_and_port()
    {
        var options = new TenantDatabaseOptions { Database = { Host = "pooler.invalid", Port = 6432 } };

        var built = new NpgsqlConnectionStringBuilder(Create(options: options).BuildConnectionString(Name, "pw"));

        built.Host.ShouldBe("pooler.invalid");
        built.Port.ShouldBe(6432);
    }

    [Fact]
    public void A_configured_host_without_a_port_keeps_the_host_servers_port()
    {
        var options = new TenantDatabaseOptions { Database = { Host = "second.invalid", Port = null } };

        var built = new NpgsqlConnectionStringBuilder(Create(options: options).BuildConnectionString(Name, "pw"));

        built.Host.ShouldBe("second.invalid");
        built.Port.ShouldBe(6543);
    }

    [Fact]
    public void A_password_with_separators_and_quotes_survives_the_round_trip()
    {
        const string password = "a;b=c'd\"e f";

        var built = new NpgsqlConnectionStringBuilder(Create().BuildConnectionString(Name, password));

        built.Password.ShouldBe(password);
        built.Database.ShouldBe(Name);
    }

    [Theory]
    [InlineData("OpenTms")]
    [InlineData("OpenTms_Demo")]
    [InlineData("postgres")]
    [InlineData("t_acme_0a1b2c3d; DROP DATABASE x")]
    [InlineData("")]
    public void Only_generated_names_get_a_connection_string(string name)
    {
        Should.Throw<InvalidOperationException>(() => Create().BuildConnectionString(name, "pw"));
    }

    [Fact]
    public void Mask_hides_the_password_and_keeps_the_rest()
    {
        var provisioner = Create();
        var masked = provisioner.Mask(provisioner.BuildConnectionString(Name, "tenant-secret"));

        masked.ShouldNotContain("tenant-secret");
        var parsed = new NpgsqlConnectionStringBuilder(masked);
        parsed.Password.ShouldBe("********");
        parsed.Database.ShouldBe(Name);
        parsed.Host.ShouldBe("pg.host.invalid");
    }

    [Fact]
    public void Mask_leaves_a_string_without_a_password_without_one()
    {
        var masked = Create().Mask("Host=h.invalid;Database=" + Name + ";Username=" + Name);

        new NpgsqlConnectionStringBuilder(masked).Password.ShouldBeNull();
    }

    [Fact]
    public void Parse_reads_server_database_and_user()
    {
        var location = Create().Parse(Create().BuildConnectionString(Name, "pw")).ShouldNotBeNull();

        location.ShouldBe(new TenantDatabaseLocation("pg.host.invalid", 6543, Name, Name));
    }

    [Theory]
    [InlineData("Host=h.invalid")]
    [InlineData("Database=x")]
    [InlineData("Data Source=:memory:")]
    [InlineData("this is not a connection string")]
    public void Parse_returns_null_for_what_it_cannot_place(string connectionString)
    {
        Create().Parse(connectionString).ShouldBeNull();
    }

    [Fact]
    public void The_host_location_is_read_from_the_default_connection_string()
    {
        Create().GetHostLocation().ShouldBe(new TenantDatabaseLocation("pg.host.invalid", 6543, "OpenTms", "postgres"));
    }

    [Theory]
    [InlineData("OpenTms")]
    [InlineData("OpenTms_Demo")]
    [InlineData("postgres")]
    public async Task Ensure_refuses_a_name_it_did_not_generate_before_connecting(string name)
    {
        var connectionString = $"Host=never.invalid;Database={name};Username={name};Password=pw";

        var refused = await Should.ThrowAsync<InvalidOperationException>(() => Create().EnsureDatabaseAsync(connectionString));
        refused.Message.ShouldContain("Not a generated tenant database name");
    }

    [Fact]
    public async Task Ensure_refuses_a_database_whose_owner_role_has_another_name()
    {
        var connectionString = $"Host=never.invalid;Database={Name};Username=postgres;Password=pw";

        var refused = await Should.ThrowAsync<InvalidOperationException>(() => Create().EnsureDatabaseAsync(connectionString));
        refused.Message.ShouldContain("owned by the role of the same name");
    }

    [Theory]
    [InlineData("OpenTms")]
    [InlineData("OpenTms_Demo")]
    [InlineData("postgres")]
    [InlineData("template1")]
    public async Task Drop_refuses_a_name_it_did_not_generate_before_connecting(string name)
    {
        var refused = await Should.ThrowAsync<InvalidOperationException>(() =>
            Create().DropDatabaseAsync(new TenantDatabaseLocation("never.invalid", 5432, name, name)));
        refused.Message.ShouldContain("Not a generated tenant database name");
    }

    [Theory]
    [InlineData("pg.host.invalid", 6543)]
    [InlineData("PG.HOST.INVALID", 6543)]
    public async Task Drop_refuses_the_host_database_even_when_its_name_looks_generated(string host, int port)
    {
        var hostName = TenantDatabaseNaming.For("host", Guid.NewGuid());
        var provisioner = Create($"Host=pg.host.invalid;Port=6543;Database={hostName};Username=postgres;Password=pw");

        var refused = await Should.ThrowAsync<InvalidOperationException>(() =>
            provisioner.DropDatabaseAsync(new TenantDatabaseLocation(host, port, hostName, hostName)));
        refused.Message.ShouldContain("host database");
    }
}
