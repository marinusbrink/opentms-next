using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using OpenTms.Data;
using OpenTms.EntityFrameworkCore.Platform.Impersonation;
using OpenTms.EntityFrameworkCore.Platform.Tenants;
using OpenTms.Platform.Administration;
using OpenTms.Platform.EntityFrameworkCore.Tenants;
using OpenTms.Platform.Tenants;
using Shouldly;
using Volo.Abp.Data;
using Volo.Abp.Emailing;
using Volo.Abp.Modularity;
using Volo.Abp.TenantManagement;
using Xunit;
using TenantCreateDto = OpenTms.Platform.Tenants.TenantCreateDto;

namespace OpenTms.PostgreSql.Tenants;

/// <summary>A mail server stand-in that can be told to refuse, the way an SMTP server does: with the address in its message.</summary>
public sealed class SwitchableEmailSender : IEmailSender
{
    public List<(string To, string Subject, string Body)> Sent { get; } = new();

    public Exception? Failure { get; set; }

    public Task SendAsync(string to, string? subject, string? body, bool isBodyHtml = true, AdditionalEmailSendingArgs? additionalEmailSendingArgs = null)
    {
        if (Failure != null)
        {
            throw Failure;
        }

        Sent.Add((to, subject ?? "", body ?? ""));
        return Task.CompletedTask;
    }

    public Task SendAsync(string from, string to, string? subject, string? body, bool isBodyHtml = true, AdditionalEmailSendingArgs? additionalEmailSendingArgs = null) =>
        SendAsync(to, subject, body, isBodyHtml, additionalEmailSendingArgs);

    public Task SendAsync(System.Net.Mail.MailMessage mail, bool normalize = true) => SendAsync(mail.To.ToString(), mail.Subject, mail.Body);

    public Task QueueAsync(string to, string subject, string body, bool isBodyHtml = true, AdditionalEmailSendingArgs? additionalEmailSendingArgs = null) =>
        SendAsync(to, subject, body, isBodyHtml, additionalEmailSendingArgs);

    public Task QueueAsync(string from, string to, string subject, string body, bool isBodyHtml = true, AdditionalEmailSendingArgs? additionalEmailSendingArgs = null) =>
        SendAsync(to, subject, body, isBodyHtml, additionalEmailSendingArgs);
}

/// <summary>An SMTP-like refusal whose message carries the recipient's address, as real mail servers' do.</summary>
public sealed class RecipientRejectedException(string message) : Exception(message);

/// <summary>The provisioning test application with a mail sender that can fail and every log line recorded.</summary>
[DependsOn(typeof(TenantProvisioningPostgreSqlTestModule))]
public class TenantReviewPostgreSqlTestModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var mail = new SwitchableEmailSender();
        context.Services.AddSingleton(mail);
        context.Services.Replace(ServiceDescriptor.Singleton<IEmailSender>(mail));

        var logs = new RecordingLoggerProvider();
        context.Services.AddSingleton(logs);
        context.Services.AddLogging(builder => builder.AddProvider(logs).SetMinimumLevel(LogLevel.Trace));
    }
}

/// <summary>
/// The review fixes of 2026-10-14 that need real PostgreSQL (docs/designs/tenants-and-editions.md):
/// <list type="bullet">
/// <item>B2 — the DbMigrator (OpenTmsDbMigrationService) leaves a tenant that is still being provisioned (queued, running,
/// failed) to its job: it must never seed the default administrator password into a half-made database.</item>
/// <item>M1/M6 — an invitation that cannot be sent does not fail provisioning, and neither a provisioning error nor a log
/// line ever carries the mail server's message (which names the address); the invitation job never rethrows.</item>
/// <item>M4 — one warning per process when the provisioning login is not configured.</item>
/// </list>
/// Every database and role is <c>t_ttestrev…</c> and is dropped afterwards; the tenant rows are removed from the shared
/// host test database. Risk class: Critical — Platform (default credentials in a tenant database; PII in logs; a mail
/// sent twice).
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class TenantReviewFixesPostgreSqlTests : OpenTmsTestBase<TenantReviewPostgreSqlTestModule>, IAsyncLifetime
{
    private const string Address = "planner.admin@ttestrev.example";

    private readonly ITenantAdministrationAppService _tenants;
    private readonly ITenantDatabaseProvisioner _provisioner;
    private readonly SwitchableEmailSender _mail;
    private readonly RecordingLoggerProvider _logs;
    private readonly FakeBackgroundJobManager _jobs;
    private readonly List<Guid> _createdTenants = new();
    private readonly List<string> _createdDatabases = new();

    public TenantReviewFixesPostgreSqlTests()
    {
        _tenants = GetRequiredService<ITenantAdministrationAppService>();
        _provisioner = GetRequiredService<ITenantDatabaseProvisioner>();
        _mail = GetRequiredService<SwitchableEmailSender>();
        _logs = GetRequiredService<RecordingLoggerProvider>();
        _jobs = GetRequiredService<FakeBackgroundJobManager>();

        /* As in the host process (see TenantProvisioningPostgreSqlTests). */
        GetRequiredService<IOptions<AbpDataSeedOptions>>().Value.Contributors.AddIfNotContains(typeof(AdministrationPermissionDataSeedContributor));
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        await using (var server = new NpgsqlConnection(PostgreSqlTestDatabases.ConnectionStringFor("postgres") + ";Pooling=false"))
        {
            await server.OpenAsync();
            foreach (var name in _createdDatabases.Distinct())
            {
                TenantDatabaseNaming.IsGenerated(name).ShouldBeTrue();
                name.ShouldStartWith("t_ttestrev");
                await ExecuteAsync(server, $"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)");
                await ExecuteAsync(server, $"DROP ROLE IF EXISTS \"{name}\"");
            }
        }

        await using var host = new NpgsqlConnection(PostgreSqlTestDatabases.HostConnectionString + ";Pooling=false");
        await host.OpenAsync();
        foreach (var id in _createdTenants)
        {
            await ExecuteAsync(host, $"DELETE FROM \"AbpTenantConnectionStrings\" WHERE \"TenantId\" = '{id}'");
            await ExecuteAsync(host, $"DELETE FROM \"AbpTenants\" WHERE \"Id\" = '{id}'");
        }
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> ScalarAsync(string connectionString, string sql, string? name = null)
    {
        await using var connection = new NpgsqlConnection(connectionString + ";Pooling=false");
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        if (name != null)
        {
            command.Parameters.AddWithValue("name", name);
        }

        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static Task<long> DatabaseCountAsync(string name) =>
        ScalarAsync(PostgreSqlTestDatabases.ConnectionStringFor("postgres"), "SELECT count(*) FROM pg_database WHERE datname = @name", name);

    private async Task<(Guid Id, string Database, string ConnectionString)> CreateTenantAsync(string name)
    {
        var created = await _tenants.CreateAsync(new TenantCreateDto { Name = name, AdminEmailAddress = Address });
        _createdTenants.Add(created.Id);
        var database = TenantDatabaseNaming.For(name, created.Id);
        _createdDatabases.Add(database);
        var connectionString = await WithUnitOfWorkAsync(async () =>
            (await GetRequiredService<ITenantRepository>().GetAsync(created.Id)).FindDefaultConnectionString()!);
        return (created.Id, database, connectionString);
    }

    private Task SetStateAsync(Guid id, string state) => GetRequiredService<TenantStateWriter>().SetStateAsync(id, state);

    private IEnumerable<string> LogText() => _logs.Entries.Select(e => e.Message + " | " + e.Values);

    // ── B2: the DbMigrator leaves tenants in provisioning to their job ───────

    [Theory]
    [InlineData(TenantProvisioningState.Failed)]
    [InlineData(TenantProvisioningState.Queued)]
    [InlineData(TenantProvisioningState.Running)]
    public async Task The_migrator_does_not_migrate_or_seed_a_tenant_whose_provisioning_has_not_succeeded(string state)
    {
        /* A failed run left the role and an empty database behind, without its administrator. */
        var (id, database, connectionString) = await CreateTenantAsync("ttestrevmig");
        await _provisioner.EnsureDatabaseAsync(connectionString);
        await SetStateAsync(id, state);
        (await DatabaseCountAsync(database)).ShouldBe(1);

        var results = await GetRequiredService<OpenTmsDbMigrationService>().MigrateAsync();

        results.ShouldAllBe(r => r.Succeeded, string.Join(", ", results.Where(r => !r.Succeeded).Select(r => r.Target + ": " + r.Error)));
        results.ShouldContain(r => r.Target == "ttestrevmig");

        /* Nothing in the tenant's database: no schema, so no administrator with the default password. */
        (await ScalarAsync(connectionString, "SELECT count(*) FROM information_schema.tables WHERE table_name = 'AbpUsers'")).ShouldBe(0);
        (await ScalarAsync(connectionString, "SELECT count(*) FROM pg_namespace WHERE nspname IN ('platform', 'masterdata')")).ShouldBe(0);
        (await ScalarAsync(connectionString, "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'public'")).ShouldBe(0);

        /* The skip is logged with the tenant id. */
        LogText().ShouldContain(line => line.Contains(id.ToString()) && line.Contains(state), "the skip is logged with the tenant id and state");
    }

    [Fact]
    public async Task The_migrator_does_not_create_the_database_of_a_tenant_that_is_still_queued()
    {
        var (_, database, _) = await CreateTenantAsync("ttestrevnew");
        (await DatabaseCountAsync(database)).ShouldBe(0);

        var results = await GetRequiredService<OpenTmsDbMigrationService>().MigrateAsync();

        results.ShouldAllBe(r => r.Succeeded);
        (await DatabaseCountAsync(database)).ShouldBe(0, "the provisioning job creates it, with its own role");
    }

    [Fact]
    public async Task The_migrator_still_migrates_a_tenant_whose_provisioning_succeeded()
    {
        var (id, _, connectionString) = await CreateTenantAsync("ttestrevok");
        await _provisioner.EnsureDatabaseAsync(connectionString);
        await SetStateAsync(id, TenantProvisioningState.Succeeded);

        var results = await GetRequiredService<OpenTmsDbMigrationService>().MigrateAsync();

        results.ShouldAllBe(r => r.Succeeded, string.Join(", ", results.Where(r => !r.Succeeded).Select(r => r.Target + ": " + r.Error)));
        (await ScalarAsync(connectionString, "SELECT count(*) FROM information_schema.tables WHERE table_name = 'AbpUsers'")).ShouldBe(1);
        (await ScalarAsync(connectionString, "SELECT count(*) FROM pg_namespace WHERE nspname IN ('platform', 'masterdata')")).ShouldBe(2);
    }

    // ── M1: a failed invitation does not fail provisioning, and leaks no address ──

    [Fact]
    public async Task An_invitation_the_mail_server_refuses_leaves_the_tenant_ready_with_the_exception_type_only()
    {
        var (id, database, _) = await CreateTenantAsync("ttestrevmail");
        _mail.Failure = new RecipientRejectedException($"5.1.1 <{Address}>: Recipient address rejected: User unknown in virtual mailbox table");

        await GetRequiredService<TenantProvisioningJob>().ExecuteAsync(new TenantProvisioningArgs { TenantId = id });

        var tenant = await _tenants.GetAsync(id);
        tenant.ProvisioningState.ShouldBe(TenantProvisioningState.Succeeded);
        tenant.ProvisioningError.ShouldBe("InvitationNotSent: RecipientRejectedException");
        tenant.InvitationSentAt.ShouldBeNull();
        (await DatabaseCountAsync(database)).ShouldBe(1);

        var stored = await WithUnitOfWorkAsync(() => GetRequiredService<ITenantRepository>().GetAsync(id));
        stored.GetProperty<string?>(TenantProperties.PendingAdminEmail).ShouldBeNull("the address only waits for provisioning");
        stored.GetProperty<string?>(TenantProperties.ProvisioningError)!.ShouldNotContain("@");

        var lines = LogText().ToList();
        lines.ShouldContain(line => line.Contains(id.ToString()) && line.Contains("RecipientRejectedException"));
        lines.ShouldNotContain(line => line.Contains(Address, StringComparison.OrdinalIgnoreCase), "no log line or logged exception may carry the address");
        lines.ShouldNotContain(line => line.Contains("Recipient address rejected"), "the mail server's message never reaches the logs");
        _mail.Sent.ShouldBeEmpty();
    }

    // ── M6: the invitation job ────────────────────────────────────────────────

    private async Task<Guid> ReadyTenantWithoutInvitationAsync(string name)
    {
        var (id, _, _) = await CreateTenantAsync(name);
        _mail.Failure = new RecipientRejectedException($"550 {Address} mailbox unavailable");
        await GetRequiredService<TenantProvisioningJob>().ExecuteAsync(new TenantProvisioningArgs { TenantId = id });
        (await _tenants.GetAsync(id)).ProvisioningState.ShouldBe(TenantProvisioningState.Succeeded);
        return id;
    }

    [Fact]
    public async Task A_failing_invitation_job_neither_rethrows_nor_logs_the_address_and_can_run_twice()
    {
        var id = await ReadyTenantWithoutInvitationAsync("ttestrevinv");
        while (_logs.Entries.TryDequeue(out _))
        {
        }

        var job = GetRequiredService<TenantInvitationJob>();
        await Should.NotThrowAsync(() => job.ExecuteAsync(new TenantInvitationArgs { TenantId = id }));
        await Should.NotThrowAsync(() => job.ExecuteAsync(new TenantInvitationArgs { TenantId = id }));

        (await _tenants.GetAsync(id)).InvitationSentAt.ShouldBeNull();
        var lines = LogText().ToList();
        lines.Count(line => line.Contains(id.ToString()) && line.Contains("RecipientRejectedException")).ShouldBe(2);
        lines.ShouldNotContain(line => line.Contains(Address, StringComparison.OrdinalIgnoreCase));
        lines.ShouldNotContain(line => line.Contains("mailbox unavailable"));
    }

    [Fact]
    public async Task The_invitation_job_run_twice_sends_one_mail()
    {
        /* Constitution rule 3: a job tolerates double execution without double effects — "no duplicate mails".
         * Hangfire delivers at least once (a worker that dies after sending redelivers the same job). */
        var id = await ReadyTenantWithoutInvitationAsync("ttestrevtwice");
        _mail.Failure = null;
        var args = new TenantInvitationArgs { TenantId = id };

        await GetRequiredService<TenantInvitationJob>().ExecuteAsync(args);
        await GetRequiredService<TenantInvitationJob>().ExecuteAsync(args);

        (await _tenants.GetAsync(id)).InvitationSentAt.ShouldNotBeNull();
        _mail.Sent.Count.ShouldBe(1, "the same job delivered twice must mail once");
    }

    [Fact]
    public async Task Resend_after_a_refused_invitation_sends_it_and_marks_it()
    {
        var id = await ReadyTenantWithoutInvitationAsync("ttestrevresend");
        _mail.Failure = null;
        _jobs.Clear();

        await _tenants.ResendInvitationAsync(id);
        await GetRequiredService<TenantInvitationJob>().ExecuteAsync(_jobs.JobsOf<TenantInvitationArgs>().ShouldHaveSingleItem());

        _mail.Sent.ShouldHaveSingleItem().To.ShouldBe(Address);
        (await _tenants.GetAsync(id)).InvitationSentAt.ShouldNotBeNull();
    }

    // ── M4: one warning when the provisioning login is not configured ─────────

    private sealed class ListLogger : ILogger<PostgreSqlTenantDatabaseProvisioner>
    {
        public List<(LogLevel Level, string Message)> Lines { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add((logLevel, formatter(state, exception)));
    }

    private static void ResetWarnedOnce() =>
        typeof(PostgreSqlTenantDatabaseProvisioner).GetField("_hostLoginWarned", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, 0);

    [Fact]
    public async Task Without_a_provisioning_login_the_provisioner_warns_once_per_process()
    {
        ResetWarnedOnce();
        var logger = new ListLogger();
        var provisioner = FakeTenantDatabaseProvisioner.Create(PostgreSqlTestDatabases.HostConnectionString);
        provisioner.Logger = logger;
        var first = provisioner.BuildConnectionString(TenantDatabaseNaming.For("ttestrevwarn", Guid.NewGuid()), "pw-1");
        var second = provisioner.BuildConnectionString(TenantDatabaseNaming.For("ttestrevwarn", Guid.NewGuid()), "pw-2");
        _createdDatabases.Add(new NpgsqlConnectionStringBuilder(first).Database!);
        _createdDatabases.Add(new NpgsqlConnectionStringBuilder(second).Database!);

        await provisioner.EnsureDatabaseAsync(first);
        await provisioner.EnsureDatabaseAsync(first);
        await provisioner.EnsureDatabaseAsync(second);

        var warnings = logger.Lines.Where(l => l.Level == LogLevel.Warning).ToList();
        warnings.ShouldHaveSingleItem().Message.ShouldContain("Tenancy:Provisioning:ConnectionString");
        warnings[0].Message.ShouldNotContain("Password", Case.Insensitive);
    }

    [Fact]
    public async Task A_provisioning_login_of_its_own_that_is_no_superuser_creates_and_drops_a_tenant_database_without_a_warning()
    {
        /* Production's shape (constitution rule 7): a login with CREATEDB and CREATEROLE only, as Cloud SQL's logins are.
         * PostgreSQL 16+ lets it own-assign a database to the tenant role, and drop it, only as a member of that role. */
        ResetWarnedOnce();
        var login = "ttestrev_prov_" + Guid.NewGuid().ToString("N")[..8];
        var server = PostgreSqlTestDatabases.ConnectionStringFor("postgres") + ";Pooling=false";
        await using (var admin = new NpgsqlConnection(server))
        {
            await admin.OpenAsync();
            await ExecuteAsync(admin, $"CREATE ROLE \"{login}\" LOGIN NOSUPERUSER CREATEDB CREATEROLE PASSWORD 'prov-pw'");
        }

        try
        {
            var logger = new ListLogger();
            var options = new TenantDatabaseOptions();
            options.Provisioning.ConnectionString = new NpgsqlConnectionStringBuilder(PostgreSqlTestDatabases.ConnectionStringFor("postgres"))
            {
                Username = login,
                Password = "prov-pw"
            }.ConnectionString;
            var provisioner = FakeTenantDatabaseProvisioner.Create(PostgreSqlTestDatabases.HostConnectionString, options);
            provisioner.Logger = logger;
            var name = TenantDatabaseNaming.For("ttestrevlogin", Guid.NewGuid());
            _createdDatabases.Add(name);
            var connectionString = provisioner.BuildConnectionString(name, "pw-3");

            await provisioner.EnsureDatabaseAsync(connectionString);
            await provisioner.EnsureDatabaseAsync(connectionString);

            (await DatabaseCountAsync(name)).ShouldBe(1);
            (await ScalarAsync(connectionString, "SELECT count(*) FROM pg_database WHERE datname = current_database() AND pg_get_userbyid(datdba) = current_user"))
                .ShouldBe(1, "the tenant's own role owns its database");

            await provisioner.DropDatabaseAsync(provisioner.Parse(connectionString)!);

            (await DatabaseCountAsync(name)).ShouldBe(0);
            (await ScalarAsync(server, "SELECT count(*) FROM pg_roles WHERE rolname = @name", name)).ShouldBe(0);
            logger.Lines.ShouldNotContain(l => l.Level == LogLevel.Warning);
        }
        finally
        {
            await using var admin = new NpgsqlConnection(server);
            await admin.OpenAsync();
            await ExecuteAsync(admin, $"DROP ROLE IF EXISTS \"{login}\"");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Without_a_provisioning_login_of_its_own_provisioning_is_refused_unless_the_host_login_is_allowed(bool namedExplicitly)
    {
        /* Outside development AllowHostLogin is off: neither an empty setting nor one naming the host database's own
         * login may create a database, so the login every request uses never needs CREATEDB or CREATEROLE. */
        var options = new TenantDatabaseOptions();
        if (namedExplicitly)
        {
            options.Provisioning.ConnectionString = PostgreSqlTestDatabases.ConnectionStringFor("postgres");
        }

        var provisioner = FakeTenantDatabaseProvisioner.Create(PostgreSqlTestDatabases.HostConnectionString, options);
        var name = TenantDatabaseNaming.For("ttestrevrefused", Guid.NewGuid());
        _createdDatabases.Add(name);
        var connectionString = provisioner.BuildConnectionString(name, "pw-4");

        var refused = await Should.ThrowAsync<InvalidOperationException>(() => provisioner.EnsureDatabaseAsync(connectionString));
        refused.Message.ShouldContain("Tenancy:Provisioning:ConnectionString");
        refused.Message.ShouldNotContain("postgres", Case.Insensitive, "no login name or password in the stored failure");
        await Should.ThrowAsync<InvalidOperationException>(() => provisioner.DropDatabaseAsync(provisioner.Parse(connectionString)!));
        (await DatabaseCountAsync(name)).ShouldBe(0);

        options.Provisioning.AllowHostLogin = true;
        await provisioner.EnsureDatabaseAsync(connectionString);
        (await DatabaseCountAsync(name)).ShouldBe(1);
    }
}
