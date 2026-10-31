using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace OpenTms.Platform.Emailing;

/// <summary>
/// One mail of the send log (docs/designs/64-email-delivery-postmark.md, AP-11): written as Queued in the transaction of
/// the fact that caused it, sent by <c>EmailSendJob</c>, and moved on by the mail service's delivery reports. Every
/// report applies idempotently and says whether it was a transition, so an event is raised once per transition.
/// </summary>
public class EmailMessage : CreationAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }

    /// <summary>Comma-separated addresses.</summary>
    public string To { get; private set; } = default!;

    public string? Cc { get; private set; }

    public string? Bcc { get; private set; }

    public string? ReplyTo { get; private set; }

    public string Subject { get; private set; } = default!;

    public string? HtmlBody { get; private set; }

    public string? TextBody { get; private set; }

    /// <summary>A category for the mail service's statistics ("TenantInvitation").</summary>
    public string? Tag { get; private set; }

    public EmailMessageStatus Status { get; private set; }

    public int Attempts { get; private set; }

    /// <summary>The last error code and message; never an address or a body.</summary>
    public string? LastError { get; private set; }

    /// <summary>The mail service's id of the sent mail (Postmark's MessageID).</summary>
    public string? ProviderMessageId { get; private set; }

    public DateTime? SentAt { get; private set; }

    public DateTime? DeliveredAt { get; private set; }

    public DateTime? BouncedAt { get; private set; }

    public string? BounceType { get; private set; }

    public string? BounceDescription { get; private set; }

    public DateTime? SpamComplaintAt { get; private set; }

    public DateTime? FirstOpenedAt { get; private set; }

    public DateTime? LastOpenedAt { get; private set; }

    public DateTime? FirstClickedAt { get; private set; }

    public DateTime? LastClickedAt { get; private set; }

    protected EmailMessage()
    {
    }

    public EmailMessage(
        Guid id,
        Guid? tenantId,
        string to,
        string? cc,
        string? bcc,
        string? replyTo,
        string subject,
        string? htmlBody,
        string? textBody,
        string? tag)
        : base(id)
    {
        TenantId = tenantId;
        To = Check.NotNullOrWhiteSpace(to, nameof(to), EmailMessageConsts.MaxAddressListLength);
        Cc = Check.Length(Blank(cc), nameof(cc), EmailMessageConsts.MaxAddressListLength);
        Bcc = Check.Length(Blank(bcc), nameof(bcc), EmailMessageConsts.MaxAddressListLength);
        ReplyTo = Check.Length(Blank(replyTo), nameof(replyTo), EmailMessageConsts.MaxReplyToLength);
        Subject = Check.Length(subject ?? string.Empty, nameof(subject), EmailMessageConsts.MaxSubjectLength)!;
        if (string.IsNullOrWhiteSpace(htmlBody) && string.IsNullOrWhiteSpace(textBody))
        {
            throw new ArgumentException("A mail needs an HTML or a text body.", nameof(htmlBody));
        }

        HtmlBody = Blank(htmlBody);
        TextBody = Blank(textBody);
        Tag = Check.Length(Blank(tag), nameof(tag), EmailMessageConsts.MaxTagLength);
        Status = EmailMessageStatus.Queued;
    }

    /// <summary>Before the call to the mail service; committed on its own, so a crash leaves Sending behind.</summary>
    public void MarkSending()
    {
        EnsureStatus(EmailMessageStatus.Queued);
        Status = EmailMessageStatus.Sending;
        Attempts++;
    }

    /// <summary>
    /// The mail service took the mail. A report can arrive before this is saved (the route is written first); then the
    /// report's status stays and only the id and the time are recorded.
    /// </summary>
    public void MarkSent(string providerMessageId, DateTime sentAt)
    {
        if (Status is not (EmailMessageStatus.Sending or EmailMessageStatus.Delivered or EmailMessageStatus.Bounced or EmailMessageStatus.SpamComplaint))
        {
            throw new InvalidOperationException($"A {Status} mail cannot be marked sent.");
        }

        ProviderMessageId = Check.NotNullOrWhiteSpace(providerMessageId, nameof(providerMessageId), EmailMessageConsts.MaxProviderMessageIdLength);
        SentAt = sentAt;
        LastError = null;
        if (Status == EmailMessageStatus.Sending)
        {
            Status = EmailMessageStatus.Sent;
        }
    }

    /// <summary>The mail service certainly did not take the mail: it goes back to the queue for the next attempt.</summary>
    public void MarkQueuedAgain(string error)
    {
        EnsureStatus(EmailMessageStatus.Sending);
        Status = EmailMessageStatus.Queued;
        LastError = Truncate(error);
    }

    /// <summary>The mail never left, or may have left and is not tried again (at most once on doubt).</summary>
    public void MarkFailed(string error)
    {
        if (Status is not (EmailMessageStatus.Queued or EmailMessageStatus.Sending))
        {
            throw new InvalidOperationException($"A {Status} mail cannot fail.");
        }

        Status = EmailMessageStatus.Failed;
        LastError = Truncate(error);
    }

    /// <summary>Sent (or Sending: the report beat the job's save) → Delivered. True on the transition only.</summary>
    public bool ApplyDelivered(DateTime deliveredAt)
    {
        if (Status is not (EmailMessageStatus.Sent or EmailMessageStatus.Sending))
        {
            /* A late or repeated delivery report: keep the first time if it was never recorded. */
            if (DeliveredAt == null && IsSent)
            {
                DeliveredAt = deliveredAt;
            }

            return false;
        }

        DeliveredAt = deliveredAt;
        Status = EmailMessageStatus.Delivered;
        return true;
    }

    /// <summary>Sending, Sent or Delivered → Bounced. True on the transition only.</summary>
    public bool ApplyBounced(DateTime bouncedAt, string? bounceType, string? description)
    {
        if (Status is not (EmailMessageStatus.Sending or EmailMessageStatus.Sent or EmailMessageStatus.Delivered))
        {
            return false;
        }

        BouncedAt = bouncedAt;
        BounceType = Truncate(bounceType, EmailMessageConsts.MaxBounceTypeLength);
        BounceDescription = Truncate(description, EmailMessageConsts.MaxBounceDescriptionLength);
        Status = EmailMessageStatus.Bounced;
        return true;
    }

    /// <summary>Sending, Sent, Delivered or Bounced → SpamComplaint. True on the transition only.</summary>
    public bool ApplySpamComplaint(DateTime complainedAt)
    {
        if (Status is not (EmailMessageStatus.Sending or EmailMessageStatus.Sent or EmailMessageStatus.Delivered or EmailMessageStatus.Bounced))
        {
            return false;
        }

        SpamComplaintAt = complainedAt;
        Status = EmailMessageStatus.SpamComplaint;
        return true;
    }

    /// <summary>Keeps the earliest and the latest open; never a status change.</summary>
    public void ApplyOpened(DateTime openedAt)
    {
        if (!IsSent)
        {
            return;
        }

        FirstOpenedAt = Min(FirstOpenedAt, openedAt);
        LastOpenedAt = Max(LastOpenedAt, openedAt);
    }

    /// <summary>Keeps the earliest and the latest click; never a status change.</summary>
    public void ApplyClicked(DateTime clickedAt)
    {
        if (!IsSent)
        {
            return;
        }

        FirstClickedAt = Min(FirstClickedAt, clickedAt);
        LastClickedAt = Max(LastClickedAt, clickedAt);
    }

    /// <summary>The mail service has (or, while Sending, may have) taken it: reports apply.</summary>
    private bool IsSent => Status is EmailMessageStatus.Sending or EmailMessageStatus.Sent or EmailMessageStatus.Delivered
        or EmailMessageStatus.Bounced or EmailMessageStatus.SpamComplaint;

    private void EnsureStatus(EmailMessageStatus expected)
    {
        if (Status != expected)
        {
            throw new InvalidOperationException($"The mail is {Status}, not {expected}.");
        }
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Truncate(string? value, int max = EmailMessageConsts.MaxLastErrorLength)
        => value == null ? null : value.Length <= max ? value : value[..max];

    private static DateTime Min(DateTime? current, DateTime value) => current is { } c && c <= value ? c : value;

    private static DateTime Max(DateTime? current, DateTime value) => current is { } c && c >= value ? c : value;
}
