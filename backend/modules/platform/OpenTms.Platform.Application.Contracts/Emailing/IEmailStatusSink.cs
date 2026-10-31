using System;
using System.Threading.Tasks;

namespace OpenTms.Platform.Emailing;

/// <summary>
/// The e-mail status sink (design 64; AP-14): the only way a delivery report reaches the send log. Ingest (the mail
/// service's webhook in Integrations) hands over a verified report; the sink finds the mail's tenant in Platform's own
/// route, applies the report idempotently and raises the events of the fixed list on a transition. It applies no other
/// business rule.
/// </summary>
public interface IEmailStatusSink
{
    /// <returns>False when no mail of this system has the report's message id (another system's mail): nothing changed.</returns>
    Task<bool> RecordAsync(EmailDeliveryReport report);
}

/// <summary>One delivery report, in Platform's terms.</summary>
public class EmailDeliveryReport
{
    /// <summary>The mail service's id of the mail (Postmark's MessageID).</summary>
    public string ProviderMessageId { get; set; } = string.Empty;

    public EmailDeliveryReportType Type { get; set; }

    /// <summary>When it happened, in UTC.</summary>
    public DateTime OccurredAt { get; set; }

    /// <summary>For a bounce: the service's bounce type ("HardBounce").</summary>
    public string? BounceType { get; set; }

    /// <summary>For a bounce: the service's description.</summary>
    public string? Description { get; set; }
}
