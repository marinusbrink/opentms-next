using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using OpenTms.Platform.Events.Outbox;
using OpenTms.Platform.Maintenance;
using OpenTms.Platform.Maintenance.Outbox;
using Volo.Abp.DependencyInjection;
using Volo.Abp.MultiTenancy;

namespace OpenTms.HealthChecks;

/// <summary>
/// The transactional outbox (docs/designs/outbox-per-tenant.md): some running instance dispatches, the last visit of this
/// instance's dispatcher to each database succeeded, and no database holds an event older than
/// <see cref="BacklogAfter"/> (two sweeps). In that order: no dispatcher means no event is delivered anywhere
/// (Unhealthy); a failing visit or a backlog means some events wait (Degraded). One short count query per database, the
/// host database and every tenant database, at most <see cref="Parallelism"/> at a time; a database that cannot be
/// reached is the tenant-databases check's finding, not this one's.
/// </summary>
public class OutboxHealthCheck : IHealthCheck, ITransientDependency
{
    public const string Name = "outbox";

    public const string NoDispatcher = "NoDispatcher";

    public const string DispatchError = "DispatchError";

    public const string Backlog = "Backlog";

    public const int Parallelism = 4;

    private const string HostName = "host";

    private static readonly string[] Settings = ["Outbox:DispatchEnabled", "Outbox:SweepPeriod"];

    private readonly IInstanceFleet _fleet;
    private readonly IOutboxInspector _inspector;
    private readonly IOutboxDispatchRegistry _registry;
    private readonly ITenantStore _tenantStore;
    private readonly OutboxOptions _options;

    public OutboxHealthCheck(
        IInstanceFleet fleet,
        IOutboxInspector inspector,
        IOutboxDispatchRegistry registry,
        ITenantStore tenantStore,
        IOptions<OutboxOptions> options)
    {
        _fleet = fleet;
        _inspector = inspector;
        _registry = registry;
        _tenantStore = tenantStore;
        _options = options.Value;
    }

    public TimeSpan BacklogAfter => _options.SweepPeriod * 2;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var running = await _fleet.GetRunningAsync(cancellationToken);
        var dispatchers = running?.Dispatching ?? (_options.DispatchEnabled ? 1 : 0);
        if (dispatchers == 0)
        {
            return HealthCheckResult.Unhealthy(
                "outbox: no running instance dispatches (Outbox:DispatchEnabled is off everywhere).",
                data: HealthCheckFacts.Data(NoDispatcher, Settings));
        }

        var tenants = (await _tenantStore.GetListAsync(includeDetails: true))
            .Where(t => !string.IsNullOrWhiteSpace(t.ConnectionStrings?.Default))
            .Select(t => ((Guid?)t.Id, t.Name))
            .ToList();
        var databases = new List<(Guid? TenantId, string Name)> { (null, HostName) };
        databases.AddRange(tenants);

        var failedVisit = databases
            .Select(d => (d.Name, Record: _registry.Find(d.TenantId)))
            .FirstOrDefault(d => d.Record is { LastError: not null } r && r.LastErrorAt == r.LastRunAt);
        if (failedVisit.Record != null)
        {
            return HealthCheckResult.Degraded(
                $"outbox: the last dispatch of {failedVisit.Name} failed: {failedVisit.Record.LastError}",
                data: HealthCheckFacts.Data(DispatchError, Settings, failedVisit.Name, extra: new Dictionary<string, object>
                {
                    ["database"] = failedVisit.Name,
                    ["error"] = failedVisit.Record.LastError!
                }));
        }

        var now = DateTime.UtcNow;
        using var gate = new SemaphoreSlim(Parallelism);
        var snapshots = await Task.WhenAll(databases.Select(async d =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                return (d.Name, Snapshot: (OutboxSnapshot?)await _inspector.InspectAsync(d.TenantId, cancellationToken));
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                return (d.Name, Snapshot: (OutboxSnapshot?)null);
            }
            finally
            {
                gate.Release();
            }
        }));

        var waiting = snapshots
            .Where(s => s.Snapshot?.OldestPendingAt is { } at && now - DateTime.SpecifyKind(at, DateTimeKind.Utc) > BacklogAfter)
            .OrderBy(s => s.Snapshot!.OldestPendingAt)
            .ToList();
        var extra = new Dictionary<string, object>
        {
            ["dispatchers"] = dispatchers,
            ["pending"] = snapshots.Sum(s => s.Snapshot?.PendingCount ?? 0)
        };

        if (waiting.Count > 0)
        {
            var oldest = waiting[0];
            extra["database"] = string.Join(", ", waiting.Select(w => w.Name));
            extra["minutes"] = (int)(now - DateTime.SpecifyKind(oldest.Snapshot!.OldestPendingAt!.Value, DateTimeKind.Utc)).TotalMinutes;
            return HealthCheckResult.Degraded(
                $"outbox: events wait longer than {BacklogAfter.TotalMinutes} minutes in {extra["database"]}.",
                data: HealthCheckFacts.Data(Backlog, Settings, extra: extra));
        }

        return HealthCheckResult.Healthy(
            $"outbox: {dispatchers} dispatchers, {extra["pending"]} events waiting, none overdue.",
            HealthCheckFacts.Data(HealthCheckFacts.Reasons.Ok, Settings, extra: extra));
    }
}
