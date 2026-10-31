namespace OpenTms.Platform.Emailing;

/// <summary>The delivery reports the send log records (design 64; PO 2026-10-30).</summary>
public enum EmailDeliveryReportType
{
    Delivered = 0,
    Bounced = 1,
    SpamComplaint = 2,
    Opened = 3,
    Clicked = 4
}
