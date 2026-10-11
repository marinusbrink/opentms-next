using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpenTms.Platform.Events.Outbox;
using OpenTms.Platform.Maintenance.Health;
using OpenTms.Platform.Permissions;
using Volo.Abp;

namespace OpenTms.Platform.Administration.Maintenance;

/* The Health tab. Host only: the checks and the process facts are about this instance, not about a tenant. */
[RemoteService(IsEnabled = false)]
[Authorize(PlatformPermissions.Administration.Maintenance.Health)]
public class HealthMaintenanceAppService : MaintenanceAppServiceBase, IHealthMaintenanceAppService
{
    private static readonly DateTime ProcessStartedAt = Process.GetCurrentProcess().StartTime.ToUniversalTime();

    private readonly IHealthReportProvider _healthReportProvider;
    private readonly OutboxOptions _outboxOptions;

    public HealthMaintenanceAppService(
        IHealthReportProvider healthReportProvider,
        IOptions<OutboxOptions> outboxOptions)
    {
        _healthReportProvider = healthReportProvider;
        _outboxOptions = outboxOptions.Value;
    }

    public async Task<HealthReportDto> GetReportAsync()
    {
        EnsureHostSide();
        var report = await _healthReportProvider.GetReportAsync();
        var entryAssembly = Assembly.GetEntryAssembly();
        /* The hosting environment exists in a real host only; the tests run without one. */
        var hostEnvironment = LazyServiceProvider.LazyGetService<IHostEnvironment>();

        return new HealthReportDto
        {
            Status = report.Status,
            TotalDurationMilliseconds = report.TotalDuration.TotalMilliseconds,
            Entries = report.Entries.Select(e => new HealthCheckEntryDto
            {
                Name = e.Name,
                Status = e.Status,
                Description = e.Description,
                DurationMilliseconds = e.Duration.TotalMilliseconds,
                Tags = e.Tags.ToList(),
                Error = e.Error
            }).ToList(),
            Process = new ProcessInfoDto
            {
                ApplicationName = hostEnvironment?.ApplicationName ?? entryAssembly?.GetName().Name ?? string.Empty,
                Version = entryAssembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                          ?? entryAssembly?.GetName().Version?.ToString()
                          ?? string.Empty,
                Environment = hostEnvironment?.EnvironmentName ?? string.Empty,
                MachineName = Environment.MachineName,
                StartedAt = ProcessStartedAt,
                UptimeSeconds = (DateTime.UtcNow - ProcessStartedAt).TotalSeconds,
                RuntimeVersion = RuntimeInformation.FrameworkDescription,
                WorkingSetBytes = Environment.WorkingSet,
                ManagedMemoryBytes = GC.GetTotalMemory(forceFullCollection: false),
                ProcessorCount = Environment.ProcessorCount,
                OutboxDispatchEnabled = _outboxOptions.DispatchEnabled
            }
        };
    }
}
