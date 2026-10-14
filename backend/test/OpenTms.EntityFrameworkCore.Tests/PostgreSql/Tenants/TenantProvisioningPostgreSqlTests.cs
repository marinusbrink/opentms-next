using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Npgsql;
using OpenTms.EntityFrameworkCore.Platform.Tenants;
using OpenTms.Platform.Administration;
using OpenTms.Platform.Tenants;
using Shouldly;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.Data;
using Volo.Abp.Emailing;
using Volo.Abp.Identity;
using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.TenantManagement;
using Volo.Abp.UI.Navigation.Urls;
using Xunit;
using TenantCreateDto = OpenTms.Platform.Tenants.TenantCreateDto;
using TenantUpdateDto = OpenTms.Platform.Tenants.TenantUpdateDto;

namespace OpenTms.PostgreSql.Tenants;

/// <summary>
/// The PostgreSQL test application with the two ports that leave the process replaced: the job queue (the test runs
/// the jobs itself, in order) and the mail sender (captured). The auth server's URL is configured as the host does.
/// </summary>
[DependsOn(typeof(OpenTmsPostgreSqlTestModule))]
public class TenantProvisioningPostgreSqlTestModule : AbpModule
{
    public override void PreConfigureServices(ServiceConfigurationContext context)
    {
        /* The host gets Identity's token providers from the Account web module (AbpIdentityAspNetCoreModule); the
         * invitation's password-reset token needs them, so the test container adds the same ones. */
        PreConfigure<IdentityBuilder>(builder => builder.AddDefaultTokenProviders());
    }

    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var jobs = new FakeBackgroundJobManager();
        context.Services.AddSingleton(jobs);
        context.Services.Replace(ServiceDescriptor.Singleton<IBackgroundJobManager>(jobs));

        context.Services.AddDataProtection();

        var mail = new CapturingEmailSender();
        context.Services.AddSingleton(mail);
        context.Services.Replace(ServiceDescriptor.Singleton<IEmailSender>(mail));

        Configure<AppUrlOptions>(options =>
        {
            options.Applications["MVC"].RootUrl = "https://auth.opentms.test";
        });
    }
}

/// <summary>
/// Tenants and editions against real PostgreSQL (docs/designs/tenants-and-editions.md): the provisioner's DDL (role,
/// database, REVOKE CONNECT FROM PUBLIC, drop), and the core flow end to end — create through the app service, the
/// provisioning job (database, every module schema, seed, invitation), delete, and the drop job. Every database and
/// role a test creates is <c>t_ttest_…</c> and is dropped in <see cref="DisposeAsync"/>, whatever the outcome.
/// Risk class: Critical — Platform; creates and drops databases and roles (integration against real PostgreSQL,
/// E2E on the core flow, negative isolation: another tenant's role cannot connect).
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class TenantProvisioningPostgreSqlTests : OpenTmsTestBase<TenantProvisioningPostgreSqlTestModule>, IAsyncLifetime
{
    private readonly ITenantDatabaseProvisioner _provisioner;
    private readonly List<string> _created = new();

    public TenantProvisioningPostgreSqlTests()
    {
        _provisioner = GetRequiredService<ITenantDatabaseProvisioner>();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        await using var connection = await OpenServerAsync();
        foreach (var name in _created.Distinct())
        {
            /* Only names this test generated: t_ttest_… — never anything else on the developer's server. */
            TenantDatabaseNaming.IsGenerated(name).ShouldBeTrue();
            name.ShouldStartWith("t_ttest_");
            await ExecuteAsync(connection, $"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)");
            await ExecuteAsync(connection, $"DROP ROLE IF EXISTS \"{name}\"");
        }

        /* The end-to-end test leaves a (soft-deleted) tenant row in the shared host database: remove it so the other
         * PostgreSQL tests keep seeing exactly the fixture's tenants. */
        await using var host = new NpgsqlConnection(PostgreSqlTestDatabases.HostConnectionString + ";Pooling=false");
        await host.OpenAsync();
        await ExecuteAsync(host, "DELETE FROM \"AbpTenantConnectionStrings\" WHERE \"TenantId\" IN (SELECT \"Id\" FROM \"AbpTenants\" WHERE \"Name\" = 'ttest')");
        await ExecuteAsync(host, "DELETE FROM \"AbpTenants\" WHERE \"Name\" = 'ttest'");
    }

    private string NewName(Guid? id = null)
    {
        var name = TenantDatabaseNaming.For("ttest", id ?? Guid.NewGuid());
        _created.Add(name);
        return name;
    }

    private static async Task<NpgsqlConnection> OpenServerAsync()
    {
        var connection = new NpgsqlConnection(PostgreSqlTestDatabases.ConnectionStringFor("postgres") + ";Pooling=false");
        await connection.OpenAsync();
        return connection;
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> CountAsync(string sql, string name)
    {
        await using var connection = await OpenServerAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("name", name);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static Task<long> DatabaseCountAsync(string name) => CountAsync("SELECT count(*) FROM pg_database WHERE datname = @name", name);

    private static Task<long> RoleCountAsync(string name) => CountAsync("SELECT count(*) FROM pg_roles WHERE rolname = @name", name);

    private static Task<long> PublicConnectGrantsAsync(string name) => CountAsync(
        "SELECT count(*) FROM pg_database d, aclexplode(d.datacl) a WHERE d.datname = @name AND a.grantee = 0 AND a.privilege_type IN ('CONNECT', 'TEMPORARY')",
        name);

    private static async Task<string> ConnectAsAsync(string connectionString, string database)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString) { Database = database, Pooling = false };
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT current_user || '@' || current_database()", connection);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    /* ── The provisioner's DDL ──────────────────────────────────────────────────────────────── */

    [Fact]
    public async Task Ensure_creates_the_role_and_a_database_it_owns_that_PUBLIC_cannot_connect_to()
    {
        var name = NewName();
        var connectionString = _provisioner.BuildConnectionString(name, "first-password");

        await _provisioner.EnsureDatabaseAsync(connectionString);

        (await DatabaseCountAsync(name)).ShouldBe(1);
        (await RoleCountAsync(name)).ShouldBe(1);
        (await CountAsync(
            "SELECT count(*) FROM pg_database d JOIN pg_roles r ON r.oid = d.datdba WHERE d.datname = @name AND r.rolname = @name",
            name)).ShouldBe(1, "the tenant role owns its database");
        (await CountAsync(
            "SELECT count(*) FROM pg_roles WHERE rolname = @name AND rolcanlogin AND NOT rolsuper AND NOT rolcreatedb AND NOT rolcreaterole",
            name)).ShouldBe(1, "a login role without any server-wide power");
        (await PublicConnectGrantsAsync(name)).ShouldBe(0);

        (await ConnectAsAsync(connectionString, name)).ShouldBe($"{name}@{name}");
    }

    [Fact]
    public async Task Ensure_is_idempotent_and_a_rerun_sets_the_new_password()
    {
        var name = NewName();

        await _provisioner.EnsureDatabaseAsync(_provisioner.BuildConnectionString(name, "first-password"));
        await _provisioner.EnsureDatabaseAsync(_provisioner.BuildConnectionString(name, "second-password"));

        (await DatabaseCountAsync(name)).ShouldBe(1);
        (await RoleCountAsync(name)).ShouldBe(1);
        (await PublicConnectGrantsAsync(name)).ShouldBe(0);
        (await CountAsync(
            "SELECT count(*) FROM pg_authid WHERE rolname = @name AND rolpassword IS NOT NULL",
            name)).ShouldBe(1);
    }

    [Fact]
    public async Task Ensure_finishes_a_partial_run_where_only_the_role_exists()
    {
        var name = NewName();
        await using (var connection = await OpenServerAsync())
        {
            await ExecuteAsync(connection, $"CREATE ROLE \"{name}\" LOGIN");
        }

        await _provisioner.EnsureDatabaseAsync(_provisioner.BuildConnectionString(name, "pw"));

        (await DatabaseCountAsync(name)).ShouldBe(1);
        (await PublicConnectGrantsAsync(name)).ShouldBe(0);
    }

    [Fact]
    public async Task Another_tenants_role_cannot_connect_to_a_tenant_database()
    {
        var a = NewName();
        var b = NewName();
        var aConnection = _provisioner.BuildConnectionString(a, "a-password");
        var bConnection = _provisioner.BuildConnectionString(b, "b-password");
        await _provisioner.EnsureDatabaseAsync(aConnection);
        await _provisioner.EnsureDatabaseAsync(bConnection);

        var refused = await Should.ThrowAsync<PostgresException>(() => ConnectAsAsync(bConnection, a));
        refused.SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege);

        await Should.ThrowAsync<PostgresException>(() => ConnectAsAsync(aConnection, b));
    }

    [Fact]
    public async Task A_tenant_role_cannot_read_the_host_databases_tenant_store()
    {
        /* The host's AbpTenantConnectionStrings holds every tenant's credentials. D4 revokes CONNECT on tenant
         * databases only, so a tenant role may open the host database; it must not be able to read anything there. */
        var a = NewName();
        var aConnection = _provisioner.BuildConnectionString(a, "a-password");
        await _provisioner.EnsureDatabaseAsync(aConnection);

        var builder = new NpgsqlConnectionStringBuilder(aConnection) { Database = PostgreSqlTestDatabases.HostDatabase, Pooling = false };
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        try
        {
            await connection.OpenAsync();
        }
        catch (PostgresException)
        {
            return; /* not even connecting: stricter than required */
        }

        await using var command = new NpgsqlCommand("SELECT count(*) FROM \"AbpTenantConnectionStrings\"", connection);
        var refused = await Should.ThrowAsync<PostgresException>(() => command.ExecuteScalarAsync());
        refused.SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
    }

    [Fact]
    public async Task A_password_with_quotes_is_passed_as_a_literal_not_as_sql()
    {
        var name = NewName();
        const string password = "x' SUPERUSER; --\"; DROP ROLE postgres; --";

        await _provisioner.EnsureDatabaseAsync(_provisioner.BuildConnectionString(name, password));

        (await CountAsync("SELECT count(*) FROM pg_roles WHERE rolname = @name AND NOT rolsuper", name)).ShouldBe(1);
        (await RoleCountAsync("postgres")).ShouldBe(1);
    }

    [Fact]
    public async Task Drop_removes_the_database_and_the_role_and_is_idempotent()
    {
        var name = NewName();
        var connectionString = _provisioner.BuildConnectionString(name, "pw");
        await _provisioner.EnsureDatabaseAsync(connectionString);
        /* A live pooled connection must not block the drop (WITH (FORCE)). */
        await using var open = new NpgsqlConnection(connectionString);
        await open.OpenAsync();

        var location = _provisioner.Parse(connectionString)!;
        await _provisioner.DropDatabaseAsync(location);
        await _provisioner.DropDatabaseAsync(location);

        (await DatabaseCountAsync(name)).ShouldBe(0);
        (await RoleCountAsync(name)).ShouldBe(0);
    }

    [Fact]
    public async Task Drop_refuses_the_host_database_and_leaves_it_in_place()
    {
        var host = _provisioner.GetHostLocation();
        host.DatabaseName.ShouldBe(PostgreSqlTestDatabases.HostDatabase);

        await Should.ThrowAsync<InvalidOperationException>(() => _provisioner.DropDatabaseAsync(host));
        await Should.ThrowAsync<InvalidOperationException>(() =>
            _provisioner.DropDatabaseAsync(host with { DatabaseName = "postgres", UserName = "postgres" }));

        (await DatabaseCountAsync(PostgreSqlTestDatabases.HostDatabase)).ShouldBe(1);
        (await RoleCountAsync("postgres")).ShouldBe(1);
    }

    /* ── End to end: create → provision → delete → drop ─────────────────────────────────────── */

    [Fact]
    public async Task A_tenant_created_in_the_app_is_provisioned_invited_and_on_delete_dropped()
    {
        var service = GetRequiredService<ITenantAdministrationAppService>();
        var jobs = GetRequiredService<FakeBackgroundJobManager>();
        var mail = GetRequiredService<CapturingEmailSender>();
        var currentTenant = GetRequiredService<ICurrentTenant>();

        /* As in the host process: the Platform permission seeder runs in the provisioning seed too (the test module
         * removes it for the shared databases; this test's own container puts it back). */
        GetRequiredService<IOptions<AbpDataSeedOptions>>().Value.Contributors.AddIfNotContains(typeof(AdministrationPermissionDataSeedContributor));

        var created = await service.CreateAsync(new TenantCreateDto { Name = "ttest", AdminEmailAddress = "admin@ttest.example" });
        var name = TenantDatabaseNaming.For("ttest", created.Id);
        _created.Add(name);

        var provision = jobs.JobsOf<TenantProvisioningArgs>().ShouldHaveSingleItem();
        (await DatabaseCountAsync(name)).ShouldBe(0, "nothing is created in the request");

        await GetRequiredService<TenantProvisioningJob>().ExecuteAsync(provision);

        var afterFirstRun = await service.GetAsync(created.Id);
        afterFirstRun.ProvisioningError.ShouldBeNull();
        afterFirstRun.ProvisioningState.ShouldBe(TenantProvisioningState.Succeeded);
        afterFirstRun.InvitationSentAt.ShouldNotBeNull();
        (await DatabaseCountAsync(name)).ShouldBe(1);
        (await PublicConnectGrantsAsync(name)).ShouldBe(0);

        /* The schema objects belong to the tenant's own role. */
        var tenantConnectionString = await WithUnitOfWorkAsync(async () =>
            (await GetRequiredService<ITenantRepository>().GetAsync(created.Id)).FindDefaultConnectionString()!);
        await using (var tenantConnection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(tenantConnectionString) { Pooling = false }.ConnectionString))
        {
            await tenantConnection.OpenAsync();
            await using var command = new NpgsqlCommand(
                "SELECT count(*) FROM pg_namespace n JOIN pg_roles r ON r.oid = n.nspowner WHERE n.nspname IN ('platform', 'masterdata') AND r.rolname = current_user",
                tenantConnection);
            Convert.ToInt64(await command.ExecuteScalarAsync()).ShouldBe(2);
        }

        /* The administrator exists in the tenant's own database with the given address; the host has no such user. */
        using (currentTenant.Change(created.Id))
        {
            var admin = await WithUnitOfWorkAsync(() => GetRequiredService<IIdentityUserRepository>().FindByNormalizedUserNameAsync("ADMIN"));
            admin.ShouldNotBeNull().Email.ShouldBe("admin@ttest.example");
            admin.TenantId.ShouldBe(created.Id);
        }

        using (currentTenant.Change(null))
        {
            var hostUser = await WithUnitOfWorkAsync(() => GetRequiredService<IIdentityUserRepository>().FindByNormalizedEmailAsync("ADMIN@TTEST.EXAMPLE"));
            hostUser.ShouldBeNull();
        }

        var invitation = mail.Sent.ShouldHaveSingleItem();
        invitation.To.ShouldBe("admin@ttest.example");
        invitation.Body.ShouldContain("https://auth.opentms.test");
        invitation.Body.ShouldContain("__tenant=" + created.Id);
        invitation.Body.ShouldContain("resetToken=");

        /* The pending address is gone once provisioning succeeded (GDPR note in the design). */
        (await WithUnitOfWorkAsync(async () => (await GetRequiredService<ITenantRepository>().GetAsync(created.Id))
            .GetProperty<string?>(TenantProperties.PendingAdminEmail))).ShouldBeNull();

        /* Idempotency: a second run (Hangfire redelivery, or a retry) changes nothing and mails nobody again. */
        await GetRequiredService<TenantProvisioningJob>().ExecuteAsync(provision);
        (await service.GetAsync(created.Id)).ProvisioningState.ShouldBe(TenantProvisioningState.Succeeded);
        mail.Sent.Count.ShouldBe(1);
        (await DatabaseCountAsync(name)).ShouldBe(1);

        /* Tenant A's database (the fixture's) is untouched by all of this. */
        (await DatabaseCountAsync(PostgreSqlTestDatabases.TenantADatabase)).ShouldBe(1);

        /* Delete → drop. */
        jobs.Clear();
        await service.DeleteAsync(created.Id);
        var drop = jobs.JobsOf<TenantDatabaseDropArgs>().ShouldHaveSingleItem();
        drop.DatabaseName.ShouldBe(name);

        NpgsqlConnection.ClearAllPools();
        await GetRequiredService<TenantDatabaseDropJob>().ExecuteAsync(drop);
        await GetRequiredService<TenantDatabaseDropJob>().ExecuteAsync(drop);

        (await DatabaseCountAsync(name)).ShouldBe(0);
        (await RoleCountAsync(name)).ShouldBe(0);
        (await DatabaseCountAsync(PostgreSqlTestDatabases.HostDatabase)).ShouldBe(1);
        (await DatabaseCountAsync(PostgreSqlTestDatabases.TenantADatabase)).ShouldBe(1);
    }
}
