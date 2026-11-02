using System;
using System.Collections.Generic;
using OpenTms.Platform.DerivedValues;

namespace OpenTms.Platform.RecordUpdates;

/// <summary>
/// What a screen that shows a record is told (design 49): the record's type, id and version, and the calculation status
/// when it is a derived-value update (null for a plain "changed by someone else" notice). Ids only, never data.
/// <see cref="Hints"/> (design 67) tells a screen that watches the whole type whether the record concerns it.
/// </summary>
public sealed record RecordUpdate(Guid? TenantId, string TargetType, Guid TargetId, long? Version, DerivedValueStatus? Status, RecordUpdateHints? Hints = null)
{
    /// <summary>The SignalR group of one record: "&lt;tenant id or host&gt;:&lt;type&gt;:&lt;id&gt;".</summary>
    public static string GroupName(Guid? tenantId, string targetType, Guid targetId)
        => $"{TenantPart(tenantId)}:{targetType}:{targetId:N}";

    /// <summary>
    /// The SignalR group of every record of a type (design 67): "&lt;tenant id or host&gt;:&lt;type&gt;:*". The tenant part
    /// always comes from the server's <c>ICurrentTenant</c>, never from the client.
    /// </summary>
    public static string TypeGroupName(Guid? tenantId, string targetType)
        => $"{TenantPart(tenantId)}:{targetType}:*";

    public string Group => GroupName(TenantId, TargetType, TargetId);

    public string TypeGroup => TypeGroupName(TenantId, TargetType);

    private static string TenantPart(Guid? tenantId) => tenantId.HasValue ? tenantId.Value.ToString("N") : "host";
}

/// <summary>
/// Routing hints of a record update (design 67): which dates, organisation unit and resources the change touches, and
/// whether the record left the type's set (deleted, or no longer matching what a pane shows). Ids and dates only,
/// never data (constitution rule 5).
/// </summary>
public sealed record RecordUpdateHints(
    DateOnly? From = null,
    DateOnly? Till = null,
    Guid? OrganizationUnitId = null,
    IReadOnlyList<Guid>? ResourceIds = null,
    bool Removed = false);
