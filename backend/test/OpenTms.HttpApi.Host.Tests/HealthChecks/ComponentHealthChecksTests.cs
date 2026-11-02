using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using OpenTms.Maintenance;
using OpenTms.Platform.Events.Outbox;
using OpenTms.Platform.Maintenance;
using OpenTms.Platform.Maintenance.BackgroundJobs;
using OpenTms.Platform.Maintenance.Outbox;
using Shouldly;
using Volo.Abp.Data;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace OpenTms.HealthChecks;

/// <summary>
/// The components of the system itself on the Health tab (docs/designs/maintenance-view.md, "Components"): every check
/// gives the status an administrator can act on and the facts (reason, settings, target, optional) the tab turns into
/// "what is wrong, what depends on it, what to do". Over fakes of the Platform ports; the tenant databases are probed
/// for real against a port nothing listens on. Risk class: Critical (Platform; the checks read every tenant's
/// database and outbox, and the provider must run them in the host's context whatever tenant is current).
/// </summary>
public class ComponentHealthChecksTests
{
    private static HealthCheckContext Context(IHealthCheck check, string name) =>
        new() { Registration = new HealthCheckRegistration(name, check, HealthStatus.Unhealthy, [HealthCheckFacts.CoreTag]) };

    private static string Reason(HealthCheckResult result) => (string)result.Data[HealthCheckFacts.Reason];

    // ── Background jobs ─────────────────────────────────────────────────────────

    private static BackgroundJobStatistics Jobs(long failed, long enqueued, params DateTime?[] heartbeats) =>
        new(enqueued, 0, 0, 10, failed, 0, 2,
            heartbeats.Select((at, i) => new BackgroundJobServer($"server-{i}", 5, ["default"], DateTime.UtcNow.AddHours(-1), at)).ToList(),
            []);

    [Fact]
    public async Task Background_jobs_without_a_live_server_are_unhealthy_and_count_the_waiting_jobs()
    {
        var monitor = new FakeJobMonitor { Statistics = Jobs(failed: 0, enqueued: 7, DateTime.UtcNow.AddMinutes(-20)) };
        var check = new BackgroundJobsHealthCheck(monitor);

        var result = await check.CheckHealthAsync(Context(check, BackgroundJobsHealthCheck.Name));

        result.Status.ShouldBe(HealthStatus.Unhealthy);
        Reason(result).ShouldBe(BackgroundJobsHealthCheck.NoServer);
        result.Data["servers"].ShouldBe(0, "a server silent for 20 minutes no longer counts");
        result.Data["enqueued"].ShouldBe(7L);
    }

    [Fact]
    public async Task Failed_background_jobs_are_degraded_with_their_count()
    {
        var monitor = new FakeJobMonitor { Statistics = Jobs(failed: 3, enqueued: 0, DateTime.UtcNow) };
        var check = new BackgroundJobsHealthCheck(monitor);

        var result = await check.CheckHealthAsync(Context(check, BackgroundJobsHealthCheck.Name));

        result.Status.ShouldBe(HealthStatus.Degraded);
        Reason(result).ShouldBe(BackgroundJobsHealthCheck.FailedJobs);
        result.Data["failed"].ShouldBe(3L);
    }

    [Fact]
    public async Task Background_jobs_with_a_live_server_and_no_failures_are_healthy()
    {
        var monitor = new FakeJobMonitor { Statistics = Jobs(failed: 0, enqueued: 2, DateTime.UtcNow, DateTime.UtcNow.AddSeconds(-30)) };
        var check = new BackgroundJobsHealthCheck(monitor);

        var result = await check.CheckHealthAsync(Context(check, BackgroundJobsHealthCheck.Name));

        result.Status.ShouldBe(HealthStatus.Healthy);
        Reason(result).ShouldBe(HealthCheckFacts.Reasons.Ok);
        result.Data["servers"].ShouldBe(2);
    }

    [Fact]
    public async Task Background_jobs_whose_storage_cannot_be_read_are_unhealthy_with_the_error()
    {
        var monitor = new FakeJobMonitor { Throw = new InvalidOperationException("relation hangfire.server does not exist") };
        var check = new BackgroundJobsHealthCheck(monitor);

        var result = await check.CheckHealthAsync(Context(check, BackgroundJobsHealthCheck.Name));

        result.Status.ShouldBe(HealthStatus.Unhealthy);
        Reason(result).ShouldBe(HealthCheckFacts.Reasons.Unreachable);
        result.Exception!.Message.ShouldContain("hangfire.server");
    }

    // ── Outbox ──────────────────────────────────────────────────────────────────

    private static readonly Guid Acme = Guid.NewGuid();
    private static readonly Guid Shared = Guid.NewGuid();

    private static FakeTenantStore Tenants()
    {
        var store = new FakeTenantStore();
        store.Add(Acme, "acme", "Host=db;Database=t_acme");
        store.Add(Shared, "shared", connectionString: null);
        return store;
    }

    private static OutboxHealthCheck Outbox(RunningInstances? running, FakeOutboxInspector inspector, OutboxDispatchRegistry? registry = null) =>
        new(new FakeFleet(running), inspector, registry ?? new OutboxDispatchRegistry(), Tenants(),
            Options.Create(new OutboxOptions { SweepPeriod = TimeSpan.FromMinutes(5) }));

    [Fact]
    public async Task An_outbox_without_any_dispatching_instance_is_unhealthy()
    {
        var check = Outbox(new RunningInstances(Count: 2, Dispatching: 0), new FakeOutboxInspector());

        var result = await check.CheckHealthAsync(Context(check, OutboxHealthCheck.Name));

        result.Status.ShouldBe(HealthStatus.Unhealthy);
        Reason(result).ShouldBe(OutboxHealthCheck.NoDispatcher);
        result.Data[HealthCheckFacts.Settings].ShouldBe("Outbox:DispatchEnabled,Outbox:SweepPeriod");
    }

    [Fact]
    public async Task A_failed_last_dispatch_is_degraded_and_names_the_tenant_and_the_error()
    {
        var registry = new OutboxDispatchRegistry();
        registry.RecordRun(null, 4, null);
        registry.RecordRun(Acme, 0, "handler threw: no such column");
        var check = Outbox(new RunningInstances(1, 1), new FakeOutboxInspector(), registry);

        var result = await check.CheckHealthAsync(Context(check, OutboxHealthCheck.Name));

        result.Status.ShouldBe(HealthStatus.Degraded);
        Reason(result).ShouldBe(OutboxHealthCheck.DispatchError);
        result.Data["database"].ShouldBe("acme");
        result.Data["error"].ShouldBe("handler threw: no such column");
    }

    [Fact]
    public async Task A_dispatch_that_failed_before_and_succeeded_since_is_no_longer_a_finding()
    {
        var registry = new OutboxDispatchRegistry();
        registry.RecordRun(Acme, 0, "temporary");
        await Task.Delay(5);
        registry.RecordRun(Acme, 3, null);
        var check = Outbox(new RunningInstances(1, 1), new FakeOutboxInspector(), registry);

        var result = await check.CheckHealthAsync(Context(check, OutboxHealthCheck.Name));

        result.Status.ShouldBe(HealthStatus.Healthy);
    }

    [Fact]
    public async Task Events_waiting_longer_than_two_sweeps_are_a_degraded_backlog_with_the_database_and_minutes()
    {
        var inspector = new FakeOutboxInspector();
        inspector.Snapshots[Acme] = new OutboxSnapshot(12, DateTime.UtcNow.AddMinutes(-31));
        inspector.Snapshots[Guid.Empty] = new OutboxSnapshot(1, DateTime.UtcNow.AddMinutes(-1));
        var check = Outbox(new RunningInstances(1, 1), inspector);

        var result = await check.CheckHealthAsync(Context(check, OutboxHealthCheck.Name));

        result.Status.ShouldBe(HealthStatus.Degraded);
        Reason(result).ShouldBe(OutboxHealthCheck.Backlog);
        result.Data["database"].ShouldBe("acme");
        ((int)result.Data["minutes"]).ShouldBeGreaterThanOrEqualTo(30);
        result.Data["pending"].ShouldBe(13L);
    }

    [Fact]
    public async Task The_outbox_inspects_the_host_and_every_own_database_but_never_a_shared_tier_tenant()
    {
        var inspector = new FakeOutboxInspector();
        var check = Outbox(new RunningInstances(1, 1), inspector);

        var result = await check.CheckHealthAsync(Context(check, OutboxHealthCheck.Name));

        result.Status.ShouldBe(HealthStatus.Healthy);
        inspector.Inspected.ShouldBe([null, Acme], ignoreOrder: true);
    }

    [Fact]
    public async Task An_unreachable_tenant_database_is_left_to_the_tenant_databases_check()
    {
        var inspector = new FakeOutboxInspector { ThrowFor = Acme };
        var check = Outbox(new RunningInstances(1, 1), inspector);

        var result = await check.CheckHealthAsync(Context(check, OutboxHealthCheck.Name));

        result.Status.ShouldBe(HealthStatus.Healthy);
    }

    [Fact]
    public async Task Without_the_registry_the_own_dispatch_setting_decides()
    {
        var check = new OutboxHealthCheck(new FakeFleet(null), new FakeOutboxInspector(), new OutboxDispatchRegistry(), Tenants(),
            Options.Create(new OutboxOptions { DispatchEnabled = false }));

        var result = await check.CheckHealthAsync(Context(check, OutboxHealthCheck.Name));

        Reason(result).ShouldBe(OutboxHealthCheck.NoDispatcher);
    }

    // ── Cache and live updates: one instance is fine, more need shared state ─────

    [Theory]
    [InlineData(1, HealthStatus.Healthy, HealthCheckFacts.Reasons.Ok)]
    [InlineData(2, HealthStatus.Degraded, CacheHealthCheck.PerInstance)]
    public async Task The_in_memory_cache_is_fine_for_one_instance_and_degraded_for_more(int instances, HealthStatus status, string reason)
    {
        var check = new CacheHealthCheck(new FakeFleet(new RunningInstances(instances, 1)));

        var result = await check.CheckHealthAsync(Context(check, CacheHealthCheck.Name));

        result.Status.ShouldBe(status);
        Reason(result).ShouldBe(reason);
        result.Data["instances"].ShouldBe(instances);
    }

    [Theory]
    [InlineData(1, HealthStatus.Healthy, HealthCheckFacts.Reasons.NotConfigured)]
    [InlineData(3, HealthStatus.Degraded, LiveUpdatesHealthCheck.NoBackplane)]
    public async Task Live_updates_without_a_backplane_are_optional_for_one_instance_and_degraded_for_more(int instances, HealthStatus status, string reason)
    {
        var check = new LiveUpdatesHealthCheck(new ConfigurationBuilder().Build(), new FakeFleet(new RunningInstances(instances, 1)), new LiveUpdatesRedisConnection());

        var result = await check.CheckHealthAsync(Context(check, LiveUpdatesHealthCheck.Name));

        result.Status.ShouldBe(status);
        Reason(result).ShouldBe(reason);
        result.Data.ContainsKey(HealthCheckFacts.Optional).ShouldBe(instances == 1);
        result.Data[HealthCheckFacts.Settings].ShouldBe(LiveUpdatesHealthCheck.SettingName);
    }

    [Fact]
    public async Task An_unreachable_redis_is_degraded_and_its_target_never_carries_the_password()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [LiveUpdatesHealthCheck.SettingName] = "127.0.0.1:1,password=s3cret,connectTimeout=500" })
            .Build();
        using var connection = new LiveUpdatesRedisConnection();
        var check = new LiveUpdatesHealthCheck(configuration, new FakeFleet(new RunningInstances(2, 1)), connection);

        var result = await check.CheckHealthAsync(Context(check, LiveUpdatesHealthCheck.Name));

        result.Status.ShouldBe(HealthStatus.Degraded);
        Reason(result).ShouldBe(HealthCheckFacts.Reasons.Unreachable);
        result.Data[HealthCheckFacts.Target].ShouldBe("127.0.0.1:1");
        string.Join(" ", result.Data.Values).ShouldNotContain("s3cret");
        (result.Description ?? string.Empty).ShouldNotContain("s3cret");
    }

    // ── Tenant databases ────────────────────────────────────────────────────────

    [Fact]
    public async Task An_unreachable_tenant_database_is_unhealthy_and_named_while_shared_tier_tenants_are_not_probed()
    {
        var store = new FakeTenantStore();
        store.Add(Guid.NewGuid(), "acme", "Host=127.0.0.1;Port=1;Database=t_acme;Username=u;Password=s3cret");
        store.Add(Guid.NewGuid(), "shared", connectionString: null);
        var check = new TenantDatabasesHealthCheck(store);

        var result = await check.CheckHealthAsync(Context(check, TenantDatabasesHealthCheck.Name));

        result.Status.ShouldBe(HealthStatus.Unhealthy);
        Reason(result).ShouldBe(HealthCheckFacts.Reasons.Unreachable);
        result.Data["total"].ShouldBe(1, "the shared-tier tenant lives in the host database");
        result.Data["count"].ShouldBe(1);
        result.Data["tenants"].ShouldBe("acme");
        result.Description!.ShouldNotContain("s3cret");
    }

    [Fact]
    public async Task Without_tenant_databases_the_check_is_healthy()
    {
        var check = new TenantDatabasesHealthCheck(new FakeTenantStore());

        var result = await check.CheckHealthAsync(Context(check, TenantDatabasesHealthCheck.Name));

        result.Status.ShouldBe(HealthStatus.Healthy);
        result.Data["total"].ShouldBe(0);
    }

    // ── The provider: host context, shared result, facts as text ────────────────

    /// <summary>A stand-in for ABP's ambient unit of work: an AsyncLocal the caller has set.</summary>
    private static readonly AsyncLocal<string?> AmbientUnitOfWork = new();

    private sealed class TenantProbe(FakeCurrentTenant tenant, List<Guid?> seen, List<string?> ambient) : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            seen.Add(tenant.Id);
            ambient.Add(AmbientUnitOfWork.Value);
            return Task.FromResult(HealthCheckResult.Degraded("probe", data: HealthCheckFacts.Data("Backlog", ["A:B", "C:D"], "db @ host:5432", optional: true,
                new Dictionary<string, object> { ["minutes"] = 1.5 })));
        }
    }

    private static (HostHealthReportProvider Provider, FakeCurrentTenant Tenant, List<Guid?> Seen, List<string?> Ambient) Provider()
    {
        var tenant = new FakeCurrentTenant();
        var seen = new List<Guid?>();
        var ambient = new List<string?>();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHealthChecks().AddCheck("probe", new TenantProbe(tenant, seen, ambient), tags: [HealthCheckFacts.CoreTag]);
        var health = services.BuildServiceProvider().GetRequiredService<HealthCheckService>();
        return (new HostHealthReportProvider(health, tenant), tenant, seen, ambient);
    }

    [Fact]
    public async Task The_provider_runs_the_checks_in_the_hosts_context_whatever_tenant_asks()
    {
        var (provider, tenant, seen, _) = Provider();

        using (tenant.Change(Guid.NewGuid()))
        {
            await provider.GetReportAsync();
            tenant.Id.ShouldNotBeNull("the caller's tenant is restored afterwards");
        }

        seen.ShouldBe([null]);
    }

    [Fact]
    public async Task Callers_within_a_few_seconds_share_one_run_and_the_facts_arrive_as_invariant_text()
    {
        var (provider, _, seen, _) = Provider();

        var first = await provider.GetReportAsync();
        var second = await provider.GetReportAsync();

        seen.Count.ShouldBe(1);
        second.ShouldBeSameAs(first);
        var data = first.Entries.Single().Data!;
        data[HealthCheckFacts.Reason].ShouldBe("Backlog");
        data[HealthCheckFacts.Settings].ShouldBe("A:B,C:D");
        data[HealthCheckFacts.Target].ShouldBe("db @ host:5432");
        data[HealthCheckFacts.Optional].ShouldBe("true");
        data["minutes"].ShouldBe("1.5");
    }

    [Fact]
    public async Task The_checks_never_see_the_callers_ambient_context_so_parallel_checks_share_no_unit_of_work()
    {
        /* Regression: the checks run in parallel; sharing the caller's unit of work made them use one DbContext at once
         * ("A second operation was started on this context instance"). */
        var (provider, _, _, ambient) = Provider();

        AmbientUnitOfWork.Value = "the request's unit of work";
        await provider.GetReportAsync();

        ambient.ShouldBe([null]);
        AmbientUnitOfWork.Value.ShouldBe("the request's unit of work");
    }

    // ── Registration ────────────────────────────────────────────────────────────

    [Fact]
    public void Every_system_component_is_registered_as_core_and_only_the_host_database_decides_readiness()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddObjectAccessor<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddOpenTmsHealthChecks();
        var registrations = services.BuildServiceProvider().GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;

        var core = registrations.Where(r => r.Tags.Contains(HealthCheckFacts.CoreTag)).Select(r => r.Name).ToList();
        core.ShouldBe(
            [OpenTmsDatabaseCheck.Name, TenantDatabasesHealthCheck.Name, BackgroundJobsHealthCheck.Name, OutboxHealthCheck.Name, CacheHealthCheck.Name, LiveUpdatesHealthCheck.Name],
            ignoreOrder: true);
        registrations.Where(r => r.Tags.Contains("ready")).Select(r => r.Name).ShouldBe([OpenTmsDatabaseCheck.Name]);
    }

    // ── Fakes ───────────────────────────────────────────────────────────────────

    private sealed class FakeFleet(RunningInstances? running) : IInstanceFleet
    {
        public Task<RunningInstances?> GetRunningAsync(CancellationToken cancellationToken = default) => Task.FromResult(running);
    }

    private sealed class FakeJobMonitor : IBackgroundJobMonitor
    {
        public BackgroundJobStatistics Statistics { get; set; } = Jobs(0, 0);

        public Exception? Throw { get; set; }

        public bool IsAvailable => true;

        public Task<BackgroundJobStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default) =>
            Throw != null ? Task.FromException<BackgroundJobStatistics>(Throw) : Task.FromResult(Statistics);

        public Task<BackgroundJobPage> GetJobsAsync(BackgroundJobState state, int skip, int take, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> RequeueAsync(string jobId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> DeleteAsync(string jobId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeOutboxInspector : IOutboxInspector
    {
        /// <summary>Keyed by tenant id; <see cref="Guid.Empty"/> is the host database.</summary>
        public Dictionary<Guid, OutboxSnapshot> Snapshots { get; } = new();

        public List<Guid?> Inspected { get; } = new();

        public Guid? ThrowFor { get; set; }

        public Task<OutboxSnapshot> InspectAsync(Guid? tenantId, CancellationToken cancellationToken = default)
        {
            lock (Inspected)
            {
                Inspected.Add(tenantId);
            }

            if (tenantId != null && tenantId == ThrowFor)
            {
                throw new InvalidOperationException("database t_acme does not exist");
            }

            return Task.FromResult(Snapshots.GetValueOrDefault(tenantId ?? Guid.Empty, new OutboxSnapshot(0, null)));
        }
    }

    private sealed class FakeTenantStore : ITenantStore
    {
        private readonly Dictionary<Guid, TenantConfiguration> _tenants = new();

        public void Add(Guid id, string name, string? connectionString)
        {
            var tenant = new TenantConfiguration(id, name) { ConnectionStrings = new ConnectionStrings() };
            if (connectionString != null)
            {
                tenant.ConnectionStrings.Default = connectionString;
            }

            _tenants[id] = tenant;
        }

        public Task<TenantConfiguration?> FindAsync(string normalizedName) =>
            Task.FromResult(_tenants.Values.FirstOrDefault(t => t.NormalizedName == normalizedName));

        public Task<TenantConfiguration?> FindAsync(Guid id) => Task.FromResult(_tenants.GetValueOrDefault(id));

        public Task<IReadOnlyList<TenantConfiguration>> GetListAsync(bool includeDetails = false) =>
            Task.FromResult<IReadOnlyList<TenantConfiguration>>(_tenants.Values.ToList());

        public TenantConfiguration? Find(string normalizedName) => FindAsync(normalizedName).Result;

        public TenantConfiguration? Find(Guid id) => FindAsync(id).Result;
    }

    /// <summary>Like ABP's: the tenant lives in an AsyncLocal and flows with the execution context.</summary>
    private sealed class FakeCurrentTenant : ICurrentTenant
    {
        private readonly AsyncLocal<(Guid? Id, string? Name)> _current = new();

        public bool IsAvailable => Id.HasValue;

        public Guid? Id => _current.Value.Id;

        public string? Name => _current.Value.Name;

        public IDisposable Change(Guid? id, string? name = null)
        {
            var previous = _current.Value;
            _current.Value = (id, name);
            return new Restore(() => _current.Value = previous);
        }

        private sealed class Restore(Action restore) : IDisposable
        {
            public void Dispose() => restore();
        }
    }
}
