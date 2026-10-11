using System;
using Volo.Abp.Domain.Entities;

namespace OpenTms.Platform.Maintenance.Instances;

/// <summary>
/// One running (or recently stopped) process of the application, as the instance registry records
/// it in the host database (docs/designs/maintenance-view.md, PO decision 2026-10-11). Every
/// instance registers itself when it starts, heartbeats on a period with its health result, and
/// marks itself stopped on a graceful shutdown. An instance that was killed never says goodbye:
/// its heartbeat simply goes silent, and the view shows it as unresponsive
/// (<see cref="ApplicationInstanceStatus"/>). Host-owned: not a tenant entity, never filtered.
/// </summary>
public class ApplicationInstance : BasicAggregateRoot<Guid>
{
    /// <summary>The role this process runs (AD-11): Api, Ingest, Jobs or Agents. Today always Api.</summary>
    public string Role { get; private set; } = string.Empty;

    public string MachineName { get; private set; } = string.Empty;

    public int ProcessId { get; private set; }

    public string Version { get; private set; } = string.Empty;

    public string Environment { get; private set; } = string.Empty;

    public string RuntimeVersion { get; private set; } = string.Empty;

    public int ProcessorCount { get; private set; }

    public bool OutboxDispatchEnabled { get; private set; }

    public DateTime StartedAt { get; private set; }

    public DateTime LastHeartbeatAt { get; private set; }

    /// <summary>Set by a graceful shutdown only; a killed instance keeps null and goes silent.</summary>
    public DateTime? StoppedAt { get; private set; }

    /// <summary>Healthy, Degraded or Unhealthy as of the last heartbeat; null before the first health run.</summary>
    public string? HealthStatus { get; private set; }

    public DateTime? HealthCheckedAt { get; private set; }

    /// <summary>The health check entries of the last heartbeat as JSON (document-shaped, AD-06).</summary>
    public string? HealthDetails { get; private set; }

    public long WorkingSetBytes { get; private set; }

    public long ManagedMemoryBytes { get; private set; }

    protected ApplicationInstance()
    {
    }

    public ApplicationInstance(
        Guid id,
        string role,
        string machineName,
        int processId,
        string version,
        string environment,
        string runtimeVersion,
        int processorCount,
        bool outboxDispatchEnabled,
        DateTime startedAt)
        : base(id)
    {
        Role = role;
        MachineName = machineName;
        ProcessId = processId;
        Version = version;
        Environment = environment;
        RuntimeVersion = runtimeVersion;
        ProcessorCount = processorCount;
        OutboxDispatchEnabled = outboxDispatchEnabled;
        StartedAt = startedAt;
        LastHeartbeatAt = startedAt;
    }

    public void Heartbeat(DateTime now, long workingSetBytes, long managedMemoryBytes, string? healthStatus, string? healthDetails)
    {
        LastHeartbeatAt = now;
        WorkingSetBytes = workingSetBytes;
        ManagedMemoryBytes = managedMemoryBytes;
        if (healthStatus != null)
        {
            HealthStatus = healthStatus;
            HealthDetails = healthDetails;
            HealthCheckedAt = now;
        }
    }

    public void MarkStopped(DateTime now)
    {
        StoppedAt = now;
        LastHeartbeatAt = now;
    }
}

public enum ApplicationInstanceStatus
{
    Running,
    /// <summary>No graceful stop recorded and no heartbeat within the allowed silence: presumed dead.</summary>
    Unresponsive,
    Stopped
}

public static class ApplicationInstanceStatusResolver
{
    public static ApplicationInstanceStatus Resolve(ApplicationInstance instance, DateTime now, TimeSpan unresponsiveAfter)
    {
        if (instance.StoppedAt != null)
        {
            return ApplicationInstanceStatus.Stopped;
        }

        return now - instance.LastHeartbeatAt > unresponsiveAfter
            ? ApplicationInstanceStatus.Unresponsive
            : ApplicationInstanceStatus.Running;
    }
}
