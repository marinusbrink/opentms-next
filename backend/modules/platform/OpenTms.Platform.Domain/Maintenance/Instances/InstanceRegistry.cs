using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenTms.Platform.Events.Outbox;
using OpenTms.Platform.Maintenance.Health;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;

namespace OpenTms.Platform.Maintenance.Instances;

/// <summary>The identity of this process in the registry: one id per process lifetime.</summary>
public class CurrentApplicationInstance : ISingletonDependency
{
    public Guid Id { get; } = Guid.NewGuid();

    public DateTime StartedAt { get; } = Process.GetCurrentProcess().StartTime.ToUniversalTime();

    /// <summary>True once the row exists in the host database.</summary>
    public bool IsRegistered { get; internal set; }
}

/// <summary>The three things an instance tells the registry: it is born, it is alive, it is going.</summary>
public interface IInstanceRegistry
{
    Task RegisterAsync(CancellationToken cancellationToken = default);

    /// <summary>Updates the heartbeat with the current health result and memory, and removes rows past retention.</summary>
    Task HeartbeatAsync(CancellationToken cancellationToken = default);

    Task MarkStoppedAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Writes this instance's row in the <b>host</b> database, whatever tenant is current: the registry
/// is host-owned operations data (04-data-and-tenancy: host-owned rows, no tenant filter bypass
/// needed because the entity is not a tenant entity).
/// </summary>
public class InstanceRegistry : IInstanceRegistry, ITransientDependency
{
    public ILogger<InstanceRegistry> Logger { get; set; } = NullLogger<InstanceRegistry>.Instance;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IRepository<ApplicationInstance, Guid> _repository;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly ICurrentTenant _currentTenant;
    private readonly CurrentApplicationInstance _current;
    private readonly InstanceRegistryOptions _options;
    private readonly OutboxOptions _outboxOptions;
    private readonly IServiceProvider _serviceProvider;

    public InstanceRegistry(
        IRepository<ApplicationInstance, Guid> repository,
        IUnitOfWorkManager unitOfWorkManager,
        ICurrentTenant currentTenant,
        CurrentApplicationInstance current,
        IOptions<InstanceRegistryOptions> options,
        IOptions<OutboxOptions> outboxOptions,
        IServiceProvider serviceProvider)
    {
        _repository = repository;
        _unitOfWorkManager = unitOfWorkManager;
        _currentTenant = currentTenant;
        _current = current;
        _options = options.Value;
        _outboxOptions = outboxOptions.Value;
        _serviceProvider = serviceProvider;
    }

    public async Task RegisterAsync(CancellationToken cancellationToken = default)
    {
        var entryAssembly = Assembly.GetEntryAssembly();
        var hostEnvironment = _serviceProvider.GetService<IHostEnvironment>();
        var instance = new ApplicationInstance(
            _current.Id,
            _options.Role,
            Environment.MachineName,
            Environment.ProcessId,
            entryAssembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? entryAssembly?.GetName().Version?.ToString() ?? string.Empty,
            hostEnvironment?.EnvironmentName ?? string.Empty,
            RuntimeInformation.FrameworkDescription,
            Environment.ProcessorCount,
            _outboxOptions.DispatchEnabled,
            _current.StartedAt);

        await InHostDatabaseAsync(async () =>
        {
            if (await _repository.FindAsync(_current.Id, includeDetails: false, cancellationToken) == null)
            {
                await _repository.InsertAsync(instance, autoSave: true, cancellationToken);
            }
        }, cancellationToken);

        _current.IsRegistered = true;
        Logger.LogInformation("Instance {InstanceId} ({Role} on {MachineName}) registered.", _current.Id, _options.Role, Environment.MachineName);
    }

    public async Task HeartbeatAsync(CancellationToken cancellationToken = default)
    {
        if (!_current.IsRegistered)
        {
            await RegisterAsync(cancellationToken);
        }

        string? healthStatus = null;
        string? healthDetails = null;
        var healthProvider = _serviceProvider.GetService<IHealthReportProvider>();
        if (healthProvider != null)
        {
            try
            {
                var report = await healthProvider.GetReportAsync(cancellationToken);
                healthStatus = report.Status;
                healthDetails = JsonSerializer.Serialize(report.Entries, JsonOptions);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                /* A health run that throws is itself the finding. */
                healthStatus = "Unhealthy";
                healthDetails = JsonSerializer.Serialize(new[] { new HealthCheckEntry("health", "Unhealthy", null, TimeSpan.Zero, [], ex.Message) }, JsonOptions);
            }
        }

        var now = DateTime.UtcNow;
        await InHostDatabaseAsync(async () =>
        {
            var instance = await _repository.FindAsync(_current.Id, includeDetails: false, cancellationToken);
            if (instance == null)
            {
                /* The row was removed (housekeeping of another instance after a long pause, or a restore). */
                _current.IsRegistered = false;
                return;
            }

            instance.Heartbeat(now, Environment.WorkingSet, GC.GetTotalMemory(forceFullCollection: false), healthStatus, healthDetails);
            await _repository.UpdateAsync(instance, autoSave: true, cancellationToken);

            /* Housekeeping: rows past retention that are stopped or long silent. */
            var cutoff = now - _options.Retention;
            await _repository.DeleteDirectAsync(i => i.LastHeartbeatAt < cutoff, cancellationToken);
        }, cancellationToken);
    }

    public async Task MarkStoppedAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        await InHostDatabaseAsync(async () =>
        {
            var instance = await _repository.FindAsync(_current.Id, includeDetails: false, cancellationToken);
            if (instance == null)
            {
                return;
            }

            instance.MarkStopped(now);
            await _repository.UpdateAsync(instance, autoSave: true, cancellationToken);
        }, cancellationToken);
        Logger.LogInformation("Instance {InstanceId} marked stopped.", _current.Id);
    }

    private async Task InHostDatabaseAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        using (_currentTenant.Change(null))
        using (var unitOfWork = _unitOfWorkManager.Begin(new AbpUnitOfWorkOptions { IsTransactional = true }, requiresNew: true))
        {
            await action();
            await unitOfWork.CompleteAsync(cancellationToken);
        }
    }
}
