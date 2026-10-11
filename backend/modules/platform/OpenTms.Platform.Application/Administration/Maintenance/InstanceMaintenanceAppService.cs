using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using OpenTms.Platform.Grid;
using OpenTms.Platform.Maintenance.Health;
using OpenTms.Platform.Maintenance.Instances;
using OpenTms.Platform.Permissions;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;

namespace OpenTms.Platform.Administration.Maintenance;

/* The instance registry on the Health tab. Host only: the rows describe processes, not tenant data,
 * and live in the host database. The list is read from the host context (the guard makes sure of
 * it), newest heartbeat first. The registry is small (one row per instance, retention a week), so
 * the page is cut in memory after one query. */
[RemoteService(IsEnabled = false)]
[Authorize(PlatformPermissions.Administration.Maintenance.Health)]
public class InstanceMaintenanceAppService : MaintenanceAppServiceBase, IInstanceMaintenanceAppService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IRepository<ApplicationInstance, Guid> _repository;
    private readonly CurrentApplicationInstance _current;
    private readonly InstanceRegistryOptions _options;

    public InstanceMaintenanceAppService(
        IRepository<ApplicationInstance, Guid> repository,
        CurrentApplicationInstance current,
        IOptions<InstanceRegistryOptions> options)
    {
        _repository = repository;
        _current = current;
        _options = options.Value;
    }

    public async Task<InstancesOverviewDto> GetOverviewAsync()
    {
        EnsureHostSide();
        var now = Clock.Now.ToUniversalTime();
        var instances = await _repository.GetListAsync();
        var statuses = instances.Select(i => ApplicationInstanceStatusResolver.Resolve(i, now, _options.UnresponsiveAfter)).ToList();

        return new InstancesOverviewDto
        {
            Running = statuses.Count(s => s == ApplicationInstanceStatus.Running),
            Unresponsive = statuses.Count(s => s == ApplicationInstanceStatus.Unresponsive),
            Stopped = statuses.Count(s => s == ApplicationInstanceStatus.Stopped),
            HeartbeatPeriod = _options.HeartbeatPeriod,
            UnresponsiveAfter = _options.UnresponsiveAfter,
            RegistryEnabled = _options.Enabled,
            CurrentInstanceId = _current.Id
        };
    }

    public async Task<GridResponse<ApplicationInstanceRowDto>> GetListAsync(GridRequest request)
    {
        EnsureHostSide();
        var now = Clock.Now.ToUniversalTime();
        var instances = await _repository.GetListAsync();
        IReadOnlyList<ApplicationInstanceRowDto> all = instances
            .Select(i => ToRow(i, now))
            .OrderBy(r => StatusOrder(r.Status))
            .ThenByDescending(r => r.LastHeartbeatAt)
            .ToList();

        IReadOnlyList<ApplicationInstanceRowDto> filtered = all;
        if (!string.IsNullOrWhiteSpace(request.WildcardSearch))
        {
            var search = request.WildcardSearch.Trim();
            filtered = all.Where(r => Matches(r.MachineName, search) || Matches(r.Role, search) || Matches(r.Version, search) || Matches(r.Environment, search)).ToList();
        }

        var sort = request.SortModels.FirstOrDefault();
        if (sort != null)
        {
            filtered = sort.ColId switch
            {
                "role" => Sort(filtered, r => r.Role, sort.Sort),
                "machineName" => Sort(filtered, r => r.MachineName, sort.Sort),
                "version" => Sort(filtered, r => r.Version, sort.Sort),
                "status" => Sort(filtered, r => StatusOrder(r.Status), sort.Sort),
                "startedAt" => Sort(filtered, r => r.StartedAt, sort.Sort),
                "lastHeartbeatAt" => Sort(filtered, r => r.LastHeartbeatAt, sort.Sort),
                "stoppedAt" => Sort(filtered, r => r.StoppedAt ?? DateTime.MinValue, sort.Sort),
                "workingSetBytes" => Sort(filtered, r => r.WorkingSetBytes, sort.Sort),
                _ => filtered
            };
        }

        return Page(all, filtered, request);
    }

    public async Task<ApplicationInstanceDetailDto> GetAsync(Guid id)
    {
        EnsureHostSide();
        var instance = await _repository.FindAsync(id);
        if (instance == null)
        {
            throw new EntityNotFoundException(typeof(ApplicationInstance), id);
        }

        var row = ToRow(instance, Clock.Now.ToUniversalTime());
        var entries = new List<HealthCheckEntryDto>();
        if (!string.IsNullOrEmpty(instance.HealthDetails))
        {
            try
            {
                entries = (JsonSerializer.Deserialize<List<HealthCheckEntry>>(instance.HealthDetails, JsonOptions) ?? [])
                    .Select(e => new HealthCheckEntryDto
                    {
                        Name = e.Name,
                        Status = e.Status,
                        Description = e.Description,
                        DurationMilliseconds = e.Duration.TotalMilliseconds,
                        Tags = e.Tags.ToList(),
                        Error = e.Error
                    })
                    .ToList();
            }
            catch (JsonException)
            {
                /* A row written by a newer or older build: the status is still shown, the entries are not. */
            }
        }

        return new ApplicationInstanceDetailDto
        {
            Id = row.Id,
            Role = row.Role,
            MachineName = row.MachineName,
            ProcessId = row.ProcessId,
            Version = row.Version,
            Environment = row.Environment,
            Status = row.Status,
            IsCurrent = row.IsCurrent,
            StartedAt = row.StartedAt,
            LastHeartbeatAt = row.LastHeartbeatAt,
            StoppedAt = row.StoppedAt,
            UptimeSeconds = row.UptimeSeconds,
            HealthStatus = row.HealthStatus,
            HealthCheckedAt = row.HealthCheckedAt,
            WorkingSetBytes = row.WorkingSetBytes,
            ManagedMemoryBytes = row.ManagedMemoryBytes,
            OutboxDispatchEnabled = row.OutboxDispatchEnabled,
            RuntimeVersion = instance.RuntimeVersion,
            ProcessorCount = instance.ProcessorCount,
            HealthEntries = entries
        };
    }

    private ApplicationInstanceRowDto ToRow(ApplicationInstance i, DateTime now)
    {
        var status = ApplicationInstanceStatusResolver.Resolve(i, now, _options.UnresponsiveAfter);
        var until = i.StoppedAt ?? (status == ApplicationInstanceStatus.Unresponsive ? i.LastHeartbeatAt : now);
        return new ApplicationInstanceRowDto
        {
            Id = i.Id,
            Role = i.Role,
            MachineName = i.MachineName,
            ProcessId = i.ProcessId,
            Version = i.Version,
            Environment = i.Environment,
            Status = status.ToString(),
            IsCurrent = i.Id == _current.Id,
            StartedAt = i.StartedAt,
            LastHeartbeatAt = i.LastHeartbeatAt,
            StoppedAt = i.StoppedAt,
            UptimeSeconds = Math.Max(0, (until - i.StartedAt).TotalSeconds),
            HealthStatus = i.HealthStatus,
            HealthCheckedAt = i.HealthCheckedAt,
            WorkingSetBytes = i.WorkingSetBytes,
            ManagedMemoryBytes = i.ManagedMemoryBytes,
            OutboxDispatchEnabled = i.OutboxDispatchEnabled
        };
    }

    private static int StatusOrder(string status) => status switch
    {
        ApplicationInstanceStatuses.Running => 0,
        ApplicationInstanceStatuses.Unresponsive => 1,
        _ => 2
    };
}
