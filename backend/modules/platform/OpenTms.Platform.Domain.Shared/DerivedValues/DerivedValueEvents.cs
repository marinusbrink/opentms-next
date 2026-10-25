using System;
using System.Collections.Generic;

namespace OpenTms.Platform.DerivedValues;

/// <summary>
/// Records' derived values must be recalculated, each for its version (design 49, flow 2 A). Written to the outbox in
/// the same transaction as the change that caused it; ids only (constitution rule 5). One event carries one record for
/// a single save and up to <see cref="DerivedValueConsts.MaxTargetsPerEvent"/> for a bulk request, so a retroactive
/// change of thousands of records costs a handful of outbox rows, not thousands.
/// </summary>
public class DerivedValuesRequestedEto
{
    public string TargetType { get; set; } = string.Empty;

    public List<DerivedValueTargetVersion> Targets { get; set; } = new();

    /// <summary>A short code of what changed ("CarrierChanged"), for logs and maintenance.</summary>
    public string? Reason { get; set; }
}

public class DerivedValueTargetVersion
{
    public Guid TargetId { get; set; }

    public long Version { get; set; }
}

/// <summary>A record's derived values were recalculated and stored for <see cref="Version"/> (flow 2 D).</summary>
public class DerivedValuesRecalculatedEto
{
    public string TargetType { get; set; } = string.Empty;

    public Guid TargetId { get; set; }

    public long Version { get; set; }
}
