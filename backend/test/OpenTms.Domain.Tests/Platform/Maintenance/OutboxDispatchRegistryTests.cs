using System;
using OpenTms.Platform.Maintenance.Outbox;
using Shouldly;
using Xunit;

namespace OpenTms.Platform.Maintenance;

/// <summary>
/// Unit tests for the in-memory record of the dispatcher's last visits. Risk class: Medium — it
/// feeds the Outbox tab's "last error" column, the one place a blocking event (AO-10) is visible.
/// </summary>
public class OutboxDispatchRegistryTests
{
    private readonly OutboxDispatchRegistry _registry = new();

    [Fact]
    public void Unknown_databases_have_no_record()
    {
        _registry.Find(null).ShouldBeNull();
        _registry.Find(Guid.NewGuid()).ShouldBeNull();
    }

    [Fact]
    public void A_run_is_recorded_per_database_and_the_host_is_kept_apart_from_tenants()
    {
        var tenantId = Guid.NewGuid();

        _registry.RecordRun(null, 3, null);
        _registry.RecordRun(tenantId, 1, "Boom");

        var host = _registry.Find(null)!;
        host.LastPublishedCount.ShouldBe(3);
        host.LastError.ShouldBeNull();
        host.LastErrorAt.ShouldBeNull();

        var tenant = _registry.Find(tenantId)!;
        tenant.LastPublishedCount.ShouldBe(1);
        tenant.LastError.ShouldBe("Boom");
        tenant.LastErrorAt.ShouldNotBeNull();
    }

    [Fact]
    public void A_successful_run_keeps_the_last_error_visible_but_updates_the_run()
    {
        var tenantId = Guid.NewGuid();

        _registry.RecordRun(tenantId, 0, "Boom");
        var failed = _registry.Find(tenantId)!;

        _registry.RecordRun(tenantId, 5, null);
        var later = _registry.Find(tenantId)!;

        later.LastPublishedCount.ShouldBe(5);
        later.LastRunAt.ShouldBeGreaterThanOrEqualTo(failed.LastRunAt);
        later.LastError.ShouldBe("Boom");
        later.LastErrorAt.ShouldBe(failed.LastErrorAt);
    }
}
