using OpenTms.Platform.Events;
using Shouldly;
using Volo.Abp.Domain.Entities.Events.Distributed;
using Volo.Abp.EventBus;
using Volo.Abp.EventBus.Distributed;
using Xunit;

namespace OpenTms.Platform.Events;

/// <summary>Risk class: low — pure selector logic; the behaviour it drives is covered by the PostgreSQL tests.</summary>
public class OutboxEventsTests
{
    [Fact]
    public void OpenTms_events_use_the_outbox()
    {
        OutboxEvents.UsesOutbox(typeof(OutboxEventsTests)).ShouldBeTrue();
    }

    [Fact]
    public void Abp_framework_events_keep_direct_delivery()
    {
        OutboxEvents.UsesOutbox(typeof(EntityCreatedEto<object>)).ShouldBeFalse();
        OutboxEvents.UsesOutbox(typeof(DynamicEventData)).ShouldBeFalse();
    }

    [Fact]
    public void The_prefix_is_a_namespace_prefix_not_a_substring()
    {
        OutboxEvents.UsesOutbox(typeof(NotOpenTms.OpenTmsLookalikeEto)).ShouldBeFalse();
    }
}
