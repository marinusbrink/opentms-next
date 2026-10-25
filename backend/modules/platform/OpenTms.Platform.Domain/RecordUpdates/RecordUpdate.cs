using System;
using OpenTms.Platform.DerivedValues;

namespace OpenTms.Platform.RecordUpdates;

/// <summary>
/// What a screen that shows a record is told (design 49): the record's type, id and version, and the calculation status
/// when it is a derived-value update (null for a plain "changed by someone else" notice). Ids only, never data.
/// </summary>
public sealed record RecordUpdate(Guid? TenantId, string TargetType, Guid TargetId, long? Version, DerivedValueStatus? Status)
{
    /// <summary>The SignalR group of one record: "&lt;tenant id or host&gt;:&lt;type&gt;:&lt;id&gt;".</summary>
    public static string GroupName(Guid? tenantId, string targetType, Guid targetId)
        => $"{(tenantId.HasValue ? tenantId.Value.ToString("N") : "host")}:{targetType}:{targetId:N}";

    public string Group => GroupName(TenantId, TargetType, TargetId);
}
