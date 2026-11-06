using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using OpenTms.Data;
using OpenTms.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.Autofac;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Data;
using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.TenantManagement;
using Volo.Abp.Uow;
using Xunit;

namespace OpenTms.HostIntegration;

/// <summary>
/// The real API host (<see cref="OpenTmsHttpApiHostModule"/>, the same module, middleware pipeline, OpenIddict server and
/// validation as <c>Program.Main</c>) on an in-memory <see cref="TestServer"/>, against PostgreSQL databases of its own:
/// a host database, tenants A and C with their own databases (database-per-tenant default) and tenants B and D in the host
/// database (shared tier), so a pair of each tier exists for the isolation tests. The databases are dropped, created and migrated by the production migration service
/// (<see cref="OpenTmsDbMigrationService"/>, as <c>OpenTms.DbMigrator</c> runs it) once per test run, so every run starts
/// clean and the schema under test is the one that ships.
///
/// Differences from <c>Program.Main</c>, all outside the request path: no Serilog sinks (plain logging at Warning), no
/// background workers (outbox dispatcher, instance registry, mailbox sync) and no Hangfire job execution, so nothing
/// runs beside the requests under test.
///
/// Server: localhost:5432 postgres/postgres, or OPENTMS_TEST_PG (a connection string without Database=). Database names
/// carry a per-checkout suffix (SHA-256 of the solution directory, or OPENTMS_TEST_PG_DB_SUFFIX) so two checkouts or a
/// parallel verify gate never share a database; they differ from the EF Core test project's names.
/// </summary>
public sealed class HostIntegrationFixture : IAsyncLifetime
{
    private const string Prefix = "OpenTms_HostTests_";

    public static readonly string Suffix = ComputeSuffix();

    public static readonly string HostDatabase = Prefix + Suffix;

    public static readonly string TenantADatabase = Prefix + Suffix + "_TenantA";

    public static readonly string TenantCDatabase = Prefix + Suffix + "_TenantC";

    public const string TenantAName = "host-test-a";

    public const string TenantBName = "host-test-b";

    public const string TenantCName = "host-test-c";

    public const string TenantDName = "host-test-d";

    /// <summary>The secret of the confidential AI tool (design 74, Gemini): the DbMigrator seeds it only with a secret.</summary>
    public const string GeminiClientSecret = "host-test-gemini-secret-0123456789-0123456789";

    public const string GeminiRedirectUri = "https://vertexaisearch.cloud.google.com/oauth-redirect";

    public const string ClaudeRedirectUri = "https://claude.ai/api/mcp/auth_callback";

    public const string ClaudeComRedirectUri = "https://claude.com/api/mcp/auth_callback";

    public Guid TenantAId { get; private set; }

    public Guid TenantBId { get; private set; }

    public Guid TenantCId { get; private set; }

    public Guid TenantDId { get; private set; }

    /// <summary>What the host logged from Information up: the log-content tests read it. The console stays at Warning.</summary>
    public HostLogCapture Logs { get; } = new();

    public WebApplication App { get; private set; } = null!;

    public IServiceProvider Services => App.Services;

    public static string ServerConnectionString =>
        Environment.GetEnvironmentVariable("OPENTMS_TEST_PG")?.TrimEnd(';')
        ?? "Host=localhost;Port=5432;Username=postgres;Password=postgres";

    public static string ConnectionStringFor(string database) => $"{ServerConnectionString};Database={database}";

    /// <summary>A client of the in-memory server; cookies are not kept (each call stands alone, like a customer's system).</summary>
    public HttpClient CreateClient() => App.GetTestClient();

    public async Task InitializeAsync()
    {
        /* OpenTmsEntityFrameworkCoreModule sets this before the host touches Npgsql; Npgsql reads it once per process, and
         * the drop below is this process's first Npgsql use, so it must be set here first (as the host would have it). */
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        await DropDatabasesAsync();
        var configuration = BuildConfiguration();
        await MigrateAsync(configuration);
        App = await StartHostAsync(configuration, Logs);
    }

    public async Task DisposeAsync()
    {
        if (App != null)
        {
            await App.StopAsync();
            await App.DisposeAsync();
        }

        /* The databases stay until the next run drops them: a failing run can be inspected. */
        NpgsqlConnection.ClearAllPools();
    }

    private static string HostProjectDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OpenTms.slnx")))
            {
                return Path.Combine(directory.FullName, "src", "OpenTms.HttpApi.Host");
            }
        }

        throw new InvalidOperationException("OpenTms.slnx not found above " + AppContext.BaseDirectory);
    }

    private static string ComputeSuffix()
    {
        var configured = Environment.GetEnvironmentVariable("OPENTMS_TEST_PG_DB_SUFFIX");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var cleaned = new string(configured.Trim().Select(c => char.IsAsciiLetterOrDigit(c) || c == '_' ? c : '_').ToArray());
            return cleaned.Length > 30 ? cleaned[..30] : cleaned;
        }

        var solution = Path.GetFullPath(Path.Combine(HostProjectDirectory(), "..", ".."));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Path.TrimEndingDirectorySeparator(solution))))[..8];
    }

    /// <summary>The host's own appsettings (Development, as local runs), with the test databases and the quiet switches.</summary>
    private static IConfiguration BuildConfiguration()
    {
        var hostDirectory = HostProjectDirectory();
        return new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(hostDirectory, "appsettings.json"), optional: false)
            .AddJsonFile(Path.Combine(hostDirectory, "appsettings.Development.json"), optional: true)
            .AddInMemoryCollection(Overrides())
            .Build();
    }

    private static Dictionary<string, string?> Overrides() => new()
    {
        ["ConnectionStrings:Default"] = ConnectionStringFor(HostDatabase),
        ["Outbox:DispatchEnabled"] = "false",
        ["InstanceRegistry:Enabled"] = "false",
        ["Integrations:OrderIntake:SyncEnabled"] = "false",
        ["OpenIddict:Applications:OpenTms_App:ClientId"] = "OpenTms_App",
        ["OpenIddict:Applications:OpenTms_App:RootUrl"] = "http://localhost:5173",
        ["OpenIddict:Applications:OpenTms_Swagger:ClientId"] = "OpenTms_Swagger",
        ["OpenIddict:Applications:OpenTms_Swagger:RootUrl"] = "http://localhost:44301/",
        /* As the DbMigrator's appsettings (design 74 "Approved tools"), with a secret for the confidential tool. */
        ["OpenIddict:Applications:OpenTms_Mcp_Claude:ClientId"] = "OpenTms_Mcp_Claude",
        ["OpenIddict:Applications:OpenTms_Mcp_Claude:DisplayName"] = "Claude",
        ["OpenIddict:Applications:OpenTms_Mcp_Claude:RedirectUris:0"] = ClaudeRedirectUri,
        ["OpenIddict:Applications:OpenTms_Mcp_Claude:RedirectUris:1"] = ClaudeComRedirectUri,
        ["OpenIddict:Applications:OpenTms_Mcp_Gemini:ClientId"] = "OpenTms_Mcp_Gemini",
        ["OpenIddict:Applications:OpenTms_Mcp_Gemini:DisplayName"] = "Gemini",
        ["OpenIddict:Applications:OpenTms_Mcp_Gemini:ClientType"] = "confidential",
        ["OpenIddict:Applications:OpenTms_Mcp_Gemini:ClientSecret"] = GeminiClientSecret,
        ["OpenIddict:Applications:OpenTms_Mcp_Gemini:RedirectUris:0"] = GeminiRedirectUri,
        ["Serilog:MinimumLevel:Default"] = "Warning"
    };

    private static async Task DropDatabasesAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionStringFor("postgres"));
        await connection.OpenAsync();
        foreach (var database in new[] { HostDatabase, TenantADatabase, TenantCDatabase })
        {
            await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{database}\" WITH (FORCE)", connection);
            await command.ExecuteNonQueryAsync();
        }

        NpgsqlConnection.ClearAllPools();
    }

    /// <summary>What OpenTms.DbMigrator does, twice: the host database, then the tenants (A gets its own database).</summary>
    private async Task MigrateAsync(IConfiguration configuration)
    {
        using var application = await AbpApplicationFactory.CreateAsync<HostTestMigratorModule>(options =>
        {
            options.Services.ReplaceConfiguration(configuration);
            options.UseAutofac();
            options.Services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
            options.AddDataMigrationEnvironment();
        });
        await application.InitializeAsync();

        var migrator = application.ServiceProvider.GetRequiredService<OpenTmsDbMigrationService>();
        ThrowIfFailed(await migrator.MigrateAsync());

        var unitOfWorkManager = application.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        var tenantManager = application.ServiceProvider.GetRequiredService<ITenantManager>();
        var tenants = application.ServiceProvider.GetRequiredService<ITenantRepository>();
        using (var unitOfWork = unitOfWorkManager.Begin(new AbpUnitOfWorkOptions { IsTransactional = true }, requiresNew: true))
        {
            var tenantA = await tenantManager.CreateAsync(TenantAName);
            tenantA.SetDefaultConnectionString(ConnectionStringFor(TenantADatabase));
            await tenants.InsertAsync(tenantA);
            var tenantB = await tenantManager.CreateAsync(TenantBName);
            await tenants.InsertAsync(tenantB);
            var tenantC = await tenantManager.CreateAsync(TenantCName);
            tenantC.SetDefaultConnectionString(ConnectionStringFor(TenantCDatabase));
            await tenants.InsertAsync(tenantC);
            var tenantD = await tenantManager.CreateAsync(TenantDName);
            await tenants.InsertAsync(tenantD);
            await unitOfWork.CompleteAsync();
            TenantAId = tenantA.Id;
            TenantBId = tenantB.Id;
            TenantCId = tenantC.Id;
            TenantDId = tenantD.Id;
        }

        ThrowIfFailed(await migrator.MigrateAsync());
        await application.ShutdownAsync();
    }

    private static void ThrowIfFailed(IReadOnlyList<OpenTmsDbMigrationService.MigrationResult> results)
    {
        var failed = results.Where(r => !r.Succeeded).ToList();
        if (failed.Count > 0)
        {
            throw new InvalidOperationException("Host test database migration failed: " + string.Join("; ", failed.Select(r => r.Target + ": " + r.Error)));
        }
    }

    /// <summary>Program.Main's sequence (AddApplicationAsync, Build, InitializeApplicationAsync, start) on a TestServer.</summary>
    private static async Task<WebApplication> StartHostAsync(IConfiguration configuration, HostLogCapture logs)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development,
            ContentRootPath = HostProjectDirectory(),
            ApplicationName = typeof(OpenTmsHttpApiHostModule).Assembly.GetName().Name
        });
        builder.Configuration.AddInMemoryCollection(Overrides());
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole().SetMinimumLevel(LogLevel.Information);
        builder.Logging.AddFilter<Microsoft.Extensions.Logging.Console.ConsoleLoggerProvider>(null, LogLevel.Warning);
        builder.Logging.AddProvider(logs);
        builder.Host.UseAutofac();
        await builder.AddApplicationAsync<OpenTmsHttpApiHostModule>();

        /* After the modules: nothing runs beside the requests under test. */
        builder.Services.Configure<AbpBackgroundWorkerOptions>(o => o.IsEnabled = false);
        builder.Services.Configure<AbpBackgroundJobOptions>(o => o.IsJobExecutionEnabled = false);

        var app = builder.Build();
        await app.InitializeApplicationAsync();
        await app.StartAsync();
        return app;
    }
}

/// <summary>One line the host logged, with what a rendered line does not say: its level and its exception.</summary>
public sealed record HostLogEntry(LogLevel Level, string Category, string Message, Exception? Exception);

/// <summary>Keeps every rendered log line and its structured values, for the tests of what a log line may hold (constitution rule 5).</summary>
public sealed class HostLogCapture : ILoggerProvider
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _lines = new();
    private readonly System.Collections.Concurrent.ConcurrentQueue<HostLogEntry> _entries = new();

    public IReadOnlyCollection<string> Lines => _lines;

    public IReadOnlyCollection<HostLogEntry> Entries => _entries;

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _lines, _entries);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(string category, System.Collections.Concurrent.ConcurrentQueue<string> lines, System.Collections.Concurrent.ConcurrentQueue<HostLogEntry> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel) && (category.StartsWith("OpenTms", StringComparison.Ordinal) || category.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) == false))
            {
                var message = formatter(state, exception);
                lines.Enqueue(category + ": " + message);
                entries.Enqueue(new HostLogEntry(logLevel, category, message, exception));
            }
        }
    }
}

/// <summary>The modules of OpenTms.DbMigrator (OpenTmsDbMigratorModule): EF Core, the contracts, Autofac.</summary>
[DependsOn(
    typeof(AbpAutofacModule),
    typeof(OpenTmsEntityFrameworkCoreModule),
    typeof(OpenTmsApplicationContractsModule))]
public class HostTestMigratorModule : AbpModule
{
    /* This module lives in the test assembly: without this, ABP would register the test assembly's own classes (fakes). */
    public override void PreConfigureServices(ServiceConfigurationContext context)
    {
        SkipAutoServiceRegistration = true;
    }
}

/// <summary>Runs after every parallel collection, one class at a time: the host owns a server and its databases.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public class HostIntegrationCollection : ICollectionFixture<HostIntegrationFixture>
{
    public const string Name = "Host integration (PostgreSQL)";
}
