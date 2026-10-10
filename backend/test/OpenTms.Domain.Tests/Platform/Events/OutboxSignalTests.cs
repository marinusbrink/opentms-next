using System;
using System.Threading;
using System.Threading.Tasks;
using OpenTms.Platform.Events.Outbox;
using Shouldly;
using Xunit;

namespace OpenTms.Platform.Events;

/// <summary>Risk class: low — in-process signalling; collapsing bursts matters for the connection budget.</summary>
public class OutboxSignalTests
{
    [Fact]
    public void A_burst_of_notifications_for_one_tenant_collapses_into_a_single_pending_signal()
    {
        var signal = new OutboxSignal();
        var tenantId = Guid.NewGuid();

        signal.Notify(tenantId);
        signal.Notify(tenantId);
        signal.Notify(tenantId);

        signal.TryRead(out var first).ShouldBeTrue();
        first.ShouldBe(tenantId);
        signal.TryRead(out _).ShouldBeFalse();

        // Once read, the tenant can be signalled again.
        signal.Notify(tenantId);
        signal.TryRead(out _).ShouldBeTrue();
    }

    [Fact]
    public void The_host_database_and_tenants_are_signalled_separately()
    {
        var signal = new OutboxSignal();
        var tenantId = Guid.NewGuid();

        signal.Notify(null);
        signal.Notify(tenantId);

        signal.TryRead(out var first).ShouldBeTrue();
        signal.TryRead(out var second).ShouldBeTrue();
        new[] { first, second }.ShouldBe(new Guid?[] { null, tenantId }, ignoreOrder: true);
        signal.TryRead(out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Waiting_completes_when_a_notification_arrives_and_times_out_otherwise()
    {
        var signal = new OutboxSignal();

        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Should.ThrowAsync<OperationCanceledException>(async () => await signal.WaitToReadAsync(timeout.Token));

        signal.Notify(Guid.NewGuid());
        (await signal.WaitToReadAsync(CancellationToken.None)).ShouldBeTrue();
    }
}
