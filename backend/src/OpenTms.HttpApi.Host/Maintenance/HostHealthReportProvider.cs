using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTms.Platform.Maintenance.Health;
using HealthReport = OpenTms.Platform.Maintenance.Health.HealthReport;
using Volo.Abp.DependencyInjection;
using Volo.Abp.MultiTenancy;

namespace OpenTms.Maintenance;

/// <summary>
/// The Health tab's source: the same registered checks that answer <c>/health/live</c> and
/// <c>/health/ready</c>, run once on request. Callers within <see cref="ReuseFor"/> of a run share its result (the
/// tab reads the report and the component list at once, the heartbeat runs alongside), so an external service is
/// asked once, not once per caller. Always in the host's context: the checks describe the system, not a tenant.
/// </summary>
public class HostHealthReportProvider : IHealthReportProvider, ISingletonDependency
{
    public static readonly TimeSpan ReuseFor = TimeSpan.FromSeconds(5);

    private readonly HealthCheckService _healthCheckService;
    private readonly ICurrentTenant _currentTenant;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private (DateTime At, HealthReport Report)? _last;

    public HostHealthReportProvider(HealthCheckService healthCheckService, ICurrentTenant currentTenant)
    {
        _healthCheckService = healthCheckService;
        _currentTenant = currentTenant;
    }

    public async Task<HealthReport> GetReportAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_last is { } last && DateTime.UtcNow - last.At < ReuseFor)
            {
                return last.Report;
            }

            /* HealthCheckService runs the checks in parallel. Started without the caller's execution context, they share
             * no ambient unit of work (and so no DbContext): each repository call opens its own. The caller's tenant
             * does not flow either; the change to the host makes that explicit. */
            Task<Microsoft.Extensions.Diagnostics.HealthChecks.HealthReport> run;
            var activity = Activity.Current;
            using (_currentTenant.Change(null))
            using (ExecutionContext.SuppressFlow())
            {
                run = Task.Run(() =>
                {
                    /* The trace goes along, so the outbound calls and their logs keep the request's correlation id. */
                    Activity.Current = activity;
                    return _healthCheckService.CheckHealthAsync(cancellationToken);
                }, cancellationToken);
            }

            var report = await run;

            var result = new HealthReport(
                report.Status.ToString(),
                report.TotalDuration,
                report.Entries
                    .Select(e => new HealthCheckEntry(
                        e.Key,
                        e.Value.Status.ToString(),
                        e.Value.Description,
                        e.Value.Duration,
                        e.Value.Tags.ToList(),
                        e.Value.Exception?.Message,
                        ToStrings(e.Value.Data)))
                    .ToList());
            _last = (DateTime.UtcNow, result);
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static IReadOnlyDictionary<string, string>? ToStrings(IReadOnlyDictionary<string, object> data) =>
        data.Count == 0
            ? null
            : data.ToDictionary(kv => kv.Key, kv => Convert.ToString(kv.Value, CultureInfo.InvariantCulture) ?? string.Empty);
}
