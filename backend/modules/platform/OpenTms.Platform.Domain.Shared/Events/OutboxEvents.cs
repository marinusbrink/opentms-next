using System;

namespace OpenTms.Platform.Events;

/// <summary>
/// Conventions for events that travel through the transactional outbox
/// (design: docs/designs/outbox-per-tenant.md, architecture decision AD-16).
/// </summary>
public static class OutboxEvents
{
    /// <summary>
    /// Only events whose type lives in an <c>OpenTms.*</c> namespace use the outbox. ABP's own
    /// distributed events (identity, tenant management, entity-change events) keep their direct
    /// in-process delivery.
    /// </summary>
    public const string NamespacePrefix = "OpenTms.";

    /// <summary>
    /// Extra-property key under which a stored event carries the tenant it was raised for. A
    /// shared-tier tenant's events live in the host database, so the row itself must say which
    /// tenant the handlers run for.
    /// </summary>
    public const string TenantIdPropertyName = "OpenTms.TenantId";

    public static bool UsesOutbox(Type eventType)
    {
        return eventType.Namespace?.StartsWith(NamespacePrefix, StringComparison.Ordinal) == true;
    }
}
