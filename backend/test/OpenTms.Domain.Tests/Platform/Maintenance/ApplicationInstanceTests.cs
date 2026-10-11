using System;
using OpenTms.Platform.Auditing;
using OpenTms.Platform.Maintenance.Instances;
using Shouldly;
using Xunit;

namespace OpenTms.Platform.Maintenance;

/// <summary>
/// Unit tests for the instance entity and its status rule. Risk class: High — the status is what
/// an operator reads during an autoscaling incident; a wrong threshold hides a dead instance or
/// cries wolf on a live one.
/// </summary>
public class ApplicationInstanceTests
{
    private static readonly DateTime Start = new(2026, 10, 11, 10, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Silence = TimeSpan.FromSeconds(90);

    private static ApplicationInstance NewInstance() =>
        new(Guid.NewGuid(), "Api", "box-1", 4242, "1.0.0", "Development", ".NET 10", 8, outboxDispatchEnabled: true, Start);

    [Fact]
    public void A_fresh_instance_is_running_and_its_heartbeat_equals_its_start()
    {
        var instance = NewInstance();

        instance.LastHeartbeatAt.ShouldBe(Start);
        instance.StoppedAt.ShouldBeNull();
        instance.HealthStatus.ShouldBeNull();
        ApplicationInstanceStatusResolver.Resolve(instance, Start.AddSeconds(10), Silence).ShouldBe(ApplicationInstanceStatus.Running);
    }

    [Fact]
    public void Heartbeat_records_memory_and_health_and_keeps_the_last_health_when_none_is_given()
    {
        var instance = NewInstance();

        instance.Heartbeat(Start.AddSeconds(30), 1000, 500, "Healthy", "[]");
        instance.LastHeartbeatAt.ShouldBe(Start.AddSeconds(30));
        instance.WorkingSetBytes.ShouldBe(1000);
        instance.HealthStatus.ShouldBe("Healthy");
        instance.HealthCheckedAt.ShouldBe(Start.AddSeconds(30));

        instance.Heartbeat(Start.AddSeconds(60), 1100, 600, null, null);
        instance.LastHeartbeatAt.ShouldBe(Start.AddSeconds(60));
        instance.HealthStatus.ShouldBe("Healthy");
        instance.HealthCheckedAt.ShouldBe(Start.AddSeconds(30));
    }

    [Fact]
    public void Silence_beyond_the_threshold_means_unresponsive_and_a_graceful_stop_means_stopped()
    {
        var instance = NewInstance();
        instance.Heartbeat(Start.AddMinutes(1), 1, 1, "Healthy", "[]");

        ApplicationInstanceStatusResolver.Resolve(instance, Start.AddMinutes(1).Add(Silence), Silence).ShouldBe(ApplicationInstanceStatus.Running);
        ApplicationInstanceStatusResolver.Resolve(instance, Start.AddMinutes(1).Add(Silence).AddSeconds(1), Silence).ShouldBe(ApplicationInstanceStatus.Unresponsive);

        instance.MarkStopped(Start.AddMinutes(5));
        instance.StoppedAt.ShouldBe(Start.AddMinutes(5));
        instance.LastHeartbeatAt.ShouldBe(Start.AddMinutes(5));
        ApplicationInstanceStatusResolver.Resolve(instance, Start.AddDays(1), Silence).ShouldBe(ApplicationInstanceStatus.Stopped);
    }

    [Fact]
    public void Heartbeats_are_kept_out_of_the_functional_logbook()
    {
        FunctionalLogbookEntitySelector.IsSelected(typeof(ApplicationInstance)).ShouldBeFalse();
    }
}
