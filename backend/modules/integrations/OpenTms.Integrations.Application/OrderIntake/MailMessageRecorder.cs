using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MimeKit;
using OpenTms.Integrations.OrderIntake.Extraction;
using Volo.Abp.DependencyInjection;

namespace OpenTms.Integrations.OrderIntake;

/// <summary>
/// Turns a raw RFC 822 mail into an inbound message (design 72 §2) with MimeKit: From, Subject and Message-ID from the
/// headers; the text part (or the HTML part) is a MailBody document when it has more than 40 non-whitespace characters
/// once the quoted reply and the signature are removed; every attachment is a document when its format (by content and
/// extension) is pdf, xlsx, csv, txt, xml or html, otherwise a Skipped document listed by name; inline images are
/// ignored. The external id is the channel's (Gmail's message id, the Message-ID header for Microsoft 365), else the
/// Message-ID header, else a hash of the raw mail.
/// </summary>
public class MailMessageRecorder : IMailMessageRecorder, ITransientDependency
{
    private readonly IInboundMessageRecorder _recorder;

    public MailMessageRecorder(IInboundMessageRecorder recorder)
    {
        _recorder = recorder;
    }

    public async Task<InboundMessageRecordResult> RecordMailAsync(
        InboundChannel channel, Guid mailboxConnectionId, string externalId, byte[] rawMessage, DateTime receivedAt, CancellationToken cancellationToken = default)
    {
        var recording = Parse(channel, mailboxConnectionId, externalId, rawMessage, receivedAt, cancellationToken);
        return await _recorder.RecordAsync(recording, cancellationToken);
    }

    /// <summary>The parse alone (pure): the recording the recorder stores.</summary>
    public static InboundMessageRecording Parse(InboundChannel channel, Guid sourceId, string? externalId, byte[] rawMessage, DateTime receivedAt, CancellationToken cancellationToken = default)
    {
        var recording = new InboundMessageRecording
        {
            Channel = channel,
            SourceId = sourceId,
            ReceivedAt = receivedAt,
            RawContent = rawMessage
        };

        MimeMessage message;
        try
        {
            using var stream = new MemoryStream(rawMessage, writable: false);
            message = MimeMessage.Load(stream, cancellationToken);
        }
        catch (FormatException)
        {
            /* Unreadable as a mail: the message is stored with its raw content and no documents (it rolls up to NotAnOrder). */
            recording.ExternalId = ExternalIdOf(externalId, null, rawMessage);
            return recording;
        }

        recording.ExternalId = ExternalIdOf(externalId, message.MessageId, rawMessage);
        recording.From = message.From.Mailboxes.FirstOrDefault()?.Address;
        recording.Subject = message.Subject;

        var body = BodyDocument(message);
        if (body != null)
        {
            recording.Documents.Add(body);
        }

        foreach (var entity in message.BodyParts)
        {
            var document = AttachmentDocument(message, entity, cancellationToken);
            if (document != null)
            {
                recording.Documents.Add(document);
            }
        }

        return recording;
    }

    private static string ExternalIdOf(string? channelId, string? messageIdHeader, byte[] raw)
    {
        var id = !string.IsNullOrWhiteSpace(channelId) ? channelId.Trim()
            : !string.IsNullOrWhiteSpace(messageIdHeader) ? messageIdHeader.Trim()
            : "sha256:" + Convert.ToHexStringLower(SHA256.HashData(raw));
        return id.Length <= OrderIntakeConsts.MaxExternalIdLength ? id : "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(id)));
    }

    private static InboundDocumentRecording? BodyDocument(MimeMessage message)
    {
        var text = message.TextBody;
        var format = DocumentFormat.Text;
        var stored = text;
        var plain = text;
        if (string.IsNullOrWhiteSpace(text))
        {
            var html = message.HtmlBody;
            if (string.IsNullOrWhiteSpace(html))
            {
                return null;
            }

            format = DocumentFormat.Html;
            stored = html;
            plain = TextNormalizer.HtmlToText(html);
        }

        if (TextNormalizer.CountNonWhitespace(TextNormalizer.StripQuotedReplyAndSignature(plain!)) <= OrderIntakeConsts.MinMailBodyCharacters)
        {
            return null;
        }

        return new InboundDocumentRecording
        {
            Kind = InboundDocumentKind.MailBody,
            FileName = null,
            ContentType = format == DocumentFormat.Html ? "text/html; charset=utf-8" : "text/plain; charset=utf-8",
            Content = Encoding.UTF8.GetBytes(stored!),
            Format = format
        };
    }

    private static InboundDocumentRecording? AttachmentDocument(MimeMessage message, MimeEntity entity, CancellationToken cancellationToken)
    {
        if (entity is MessagePart attachedMail)
        {
            /* An attached mail (.eml) is listed, never read. */
            using var buffer = new MemoryStream();
            attachedMail.Message?.WriteTo(buffer, cancellationToken);
            return Skipped(attachedMail.ContentDisposition?.FileName ?? (attachedMail.Message?.Subject is { } s ? s + ".eml" : "message.eml"), "message/rfc822", buffer.ToArray());
        }

        if (entity is not MimePart part)
        {
            return null;
        }

        /* The body parts themselves, and inline images, are not attachments. */
        if (part is TextPart && !part.IsAttachment && part.FileName == null)
        {
            return null;
        }

        var isInline = part.ContentDisposition == null || part.ContentDisposition.Disposition.Equals(ContentDisposition.Inline, StringComparison.OrdinalIgnoreCase);
        if (part.ContentType.MediaType.Equals("image", StringComparison.OrdinalIgnoreCase) && (isInline || part.ContentId != null) && !part.IsAttachment)
        {
            return null;
        }

        if (!part.IsAttachment && part.FileName == null)
        {
            return null;
        }

        using var content = new MemoryStream();
        part.Content?.DecodeTo(content, cancellationToken);
        var bytes = content.ToArray();
        var fileName = part.FileName;
        var contentType = part.ContentType.MimeType;
        var format = DocumentFormatDetector.Detect(bytes, fileName, contentType);
        if (format == DocumentFormat.Unknown)
        {
            return Skipped(fileName ?? "attachment", contentType, bytes);
        }

        return new InboundDocumentRecording
        {
            Kind = InboundDocumentKind.Attachment,
            FileName = fileName,
            ContentType = contentType,
            Content = bytes,
            Format = format
        };
    }

    private static InboundDocumentRecording Skipped(string fileName, string contentType, byte[] content)
        => new()
        {
            Kind = InboundDocumentKind.Attachment,
            FileName = fileName,
            ContentType = contentType,
            Content = content,
            Format = DocumentFormat.Unknown,
            Skipped = true
        };
}
