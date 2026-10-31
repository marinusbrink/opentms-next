using System;
using System.Threading.Tasks;

namespace OpenTms.Platform.Emailing;

/// <summary>
/// Puts a mail on the send log (docs/designs/64-email-delivery-postmark.md): the row is written as Queued in the current
/// tenant and the caller's transactional unit of work, together with the outbox event that enqueues the send job once the
/// transaction commits. Published for the modules that send mail next to their fact; not an application service.
/// </summary>
public interface IEmailQueue
{
    /// <returns>The send log id.</returns>
    Task<Guid> QueueAsync(QueuedEmail email);
}

/// <summary>A mail to send. The From is the mail service's verified sender; a sender's own address goes in <see cref="ReplyTo"/>.</summary>
public class QueuedEmail
{
    /// <summary>Comma-separated addresses; required.</summary>
    public string To { get; set; } = string.Empty;

    public string? Cc { get; set; }

    public string? Bcc { get; set; }

    public string? ReplyTo { get; set; }

    public string Subject { get; set; } = string.Empty;

    /// <summary>At least one of the bodies.</summary>
    public string? HtmlBody { get; set; }

    public string? TextBody { get; set; }

    /// <summary>A category for the mail service's statistics ("TenantInvitation", "Invoice").</summary>
    public string? Tag { get; set; }
}
