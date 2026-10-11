using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTms.Platform.Maintenance.Health;
using HealthReport = OpenTms.Platform.Maintenance.Health.HealthReport;
using Volo.Abp.DependencyInjection;

namespace OpenTms.Maintenance;

/// <summary>
/// The Health tab's source: the same registered checks that answer <c>/health/live</c> and
/// <c>/health/ready</c>, run once on request.
/// </summary>
public class HostHealthReportProvider : IHealthReportProvider, ITransientDependency
{
    private readonly HealthCheckService _healthCheckService;

    public HostHealthReportProvider(HealthCheckService healthCheckService)
    {
        _healthCheckService = healthCheckService;
    }

    public async Task<HealthReport> GetReportAsync(CancellationToken cancellationToken = default)
    {
        var report = await _healthCheckService.CheckHealthAsync(cancellationToken);
        return new HealthReport(
            report.Status.ToString(),
            report.TotalDuration,
            report.Entries
                .Select(e => new HealthCheckEntry(
                    e.Key,
                    e.Value.Status.ToString(),
                    e.Value.Description,
                    e.Value.Duration,
                    e.Value.Tags.ToList(),
                    e.Value.Exception?.Message))
                .ToList());
    }
}
