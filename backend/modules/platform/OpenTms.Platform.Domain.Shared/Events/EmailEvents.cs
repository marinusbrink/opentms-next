using System;

namespace OpenTms.Platform.Events;

/// <summary>
/// A mail was put on the send log (design 64). Written to the outbox in the same transaction as the row, so the send job
/// is only enqueued once the fact that caused the mail is committed. Platform's own handler enqueues the job; ids only.
/// </summary>
public class EmailQueuedEto
{
    public Guid EmailMessageId { get; set; }
}

/// <summary>
/// A sent mail bounced (design 64; on the fixed list of 07-ingest). Raised once, on the transition to Bounced. A module
/// or agent applies the rule ("block this address for invoices"); none does yet.
/// </summary>
public class EmailBouncedEto
{
    public Guid EmailMessageId { get; set; }

    /// <summary>The mail service's bounce type ("HardBounce", "SoftBounce", ...).</summary>
    public string? BounceType { get; set; }
}

/// <summary>The recipient of a sent mail marked it as spam (design 64; on the fixed list). Raised once.</summary>
public class EmailSpamComplaintReceivedEto
{
    public Guid EmailMessageId { get; set; }
}
