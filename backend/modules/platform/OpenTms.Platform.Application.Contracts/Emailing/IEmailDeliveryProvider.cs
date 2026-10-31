using System;
using System.Threading;
using System.Threading.Tasks;

namespace OpenTms.Platform.Emailing;

/// <summary>
/// A mail service (design 64): Postmark today, implemented in Integrations. One attempt per call; the send job retries.
/// Throws <see cref="EmailRejectedException"/> when the service refuses the mail for good and
/// <see cref="EmailDeliveryUnavailableException"/> when it could not be reached or was busy.
/// </summary>
public interface IEmailDeliveryProvider
{
    /// <summary>The effective configuration; not configured (no token or From) means mail is logged, not sent.</summary>
    Task<EmailDeliveryStatus> GetStatusAsync();

    /// <returns>The service's id of the sent mail.</returns>
    Task<string> SendAsync(OutgoingEmail email, CancellationToken cancellationToken);
}

/// <summary>One mail as the send job hands it to the provider; the provider sets the From.</summary>
public class OutgoingEmail
{
    public Guid EmailMessageId { get; set; }

    public Guid? TenantId { get; set; }

    public string To { get; set; } = string.Empty;

    public string? Cc { get; set; }

    public string? Bcc { get; set; }

    public string? ReplyTo { get; set; }

    public string Subject { get; set; } = string.Empty;

    public string? HtmlBody { get; set; }

    public string? TextBody { get; set; }

    public string? Tag { get; set; }
}

/// <summary>The mail service refused the mail for good (invalid or inactive recipient, unconfirmed sender): no retry.</summary>
public class EmailRejectedException : Exception
{
    public EmailRejectedException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    /// <summary>The service's own error code.</summary>
    public string ErrorCode { get; }
}

/// <summary>The mail service could not take the mail now (busy, down, unreachable, not authorized): retry later.</summary>
public class EmailDeliveryUnavailableException : Exception
{
    public EmailDeliveryUnavailableException(string message, bool outcomeUnknown = false, Exception? innerException = null)
        : base(message, innerException)
    {
        OutcomeUnknown = outcomeUnknown;
    }

    /// <summary>The request may have reached the service (a timeout after it left): the mail may have been sent.</summary>
    public bool OutcomeUnknown { get; }
}

/// <summary>What the mail service will use, after the host's settings and the deployment's configuration are combined.</summary>
public class EmailDeliveryStatus
{
    public bool IsConfigured { get; set; }

    /// <summary>Where the server token comes from.</summary>
    public EmailSettingSource ServerTokenSource { get; set; }

    public string? FromAddress { get; set; }

    public string? FromName { get; set; }

    public bool TrackOpens { get; set; }

    public string? TrackLinks { get; set; }

    public bool IsWebhookConfigured { get; set; }
}

public enum EmailSettingSource
{
    None = 0,

    /// <summary>The host's e-mail settings.</summary>
    Settings = 1,

    /// <summary>The deployment's configuration (secret store, appsettings).</summary>
    Configuration = 2
}
