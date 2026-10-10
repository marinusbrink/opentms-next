using System;
using Volo.Abp.Data;
using Volo.Abp.EventBus.Distributed;

namespace OpenTms.Platform.Events.Outbox;

public static class OutgoingEventInfoExtensions
{
    /// <summary>Stamps the tenant the event was raised for onto the stored event.</summary>
    public static void SetTenantId(this OutgoingEventInfo outgoingEvent, Guid? tenantId)
    {
        outgoingEvent.SetProperty(OutboxEvents.TenantIdPropertyName, tenantId?.ToString("D"));
    }

    /// <summary>
    /// The tenant stamped onto the stored event. Returns <paramref name="fallback"/> for rows written
    /// before the stamp existed or without one.
    /// </summary>
    public static Guid? GetTenantIdOrDefault(this OutgoingEventInfo outgoingEvent, Guid? fallback)
    {
        if (!outgoingEvent.HasProperty(OutboxEvents.TenantIdPropertyName))
        {
            return fallback;
        }

        var raw = outgoingEvent.GetProperty(OutboxEvents.TenantIdPropertyName)?.ToString();
        if (string.IsNullOrWhiteSpace(raw))
        {
            // An explicit empty stamp means "raised in the host context".
            return null;
        }

        return Guid.TryParse(raw, out var tenantId) ? tenantId : fallback;
    }
}
