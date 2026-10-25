using System;

namespace OpenTms.Financial.Events;

/// <summary>
/// The rate lines of an order or a trip were (re)calculated or edited (design 57; OpenTms.Financial.RateLinesChanged per
/// AP-06). Exactly one of <see cref="OrderId"/> and <see cref="TripId"/> is set. Orders stores its revenue and margin,
/// Planning allocates cost and revenue over the legs (AD-20). Ids and a version only.
/// </summary>
public class RateLinesChangedEto
{
    public Guid? OrderId { get; set; }

    public Guid? TripId { get; set; }

    /// <summary>The derived-value version the lines were written for; a handler ignores an older one it has seen.</summary>
    public long Version { get; set; }
}
