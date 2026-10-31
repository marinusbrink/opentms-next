namespace OpenTms.Platform.Emailing;

/// <summary>
/// Where one mail of the send log stands (docs/designs/64-email-delivery-postmark.md). Queued → Sending → Sent →
/// Delivered; a delivery report can move a sent mail on to Bounced or SpamComplaint; Failed is a mail that never left.
/// </summary>
public enum EmailMessageStatus
{
    Queued = 0,
    Sending = 1,
    Sent = 2,
    Delivered = 3,
    Bounced = 4,
    SpamComplaint = 5,
    Failed = 6
}
