using System;
using System.Collections.Generic;
using System.Linq;
using OpenTms.Platform.Localization;
using Volo.Abp;

namespace OpenTms.Platform.EmailTemplates;

/// <summary>
/// An e-mail template (design 54; OpenTMS <c>OutboundEMailMessageTemplate</c>, PO 2026-10-18): one template per language
/// with the mail fields on the template itself. A null <see cref="Culture"/> serves all languages. Every text field may
/// hold Scriban expressions; the service parses them on save (never renders). At most one default per purpose (partial
/// unique index); the service clears the previous default in the same transaction. A sender picks the template of the
/// purpose whose language matches the recipient, else the purpose's default. The address fields hold company
/// addresses: business data, never logged.
/// </summary>
public class EmailTemplate : PlatformMasterDataAggregateRoot
{
    public string Name { get; private set; } = default!;

    /// <summary>The name in upper case: the unique key (PO 2026-10-16, as vehicles).</summary>
    public string NormalizedName { get; private set; } = default!;

    public EmailTemplatePurpose Purpose { get; private set; }

    /// <summary>A configured language code ("nl", "en"); null: all languages (OpenTMS <c>Language</c> empty).</summary>
    public string? Culture { get; private set; }

    public bool IsDefault { get; private set; }

    /// <summary>"From" in the editor.</summary>
    public string? SenderAddress { get; private set; }

    public string? ReplyTo { get; private set; }

    /// <summary>Empty: the consumer's recipient.</summary>
    public string? To { get; private set; }

    public string? Cc { get; private set; }

    public string? Bcc { get; private set; }

    /// <summary>"Send me a copy" in the editor.</summary>
    public bool SendCopyToSender { get; private set; }

    public string Subject { get; private set; } = default!;

    /// <summary>Up to 200,000 characters of HTML source with Scriban expressions.</summary>
    public string HtmlBody { get; private set; } = default!;

    protected EmailTemplate()
    {
    }

    public EmailTemplate(
        Guid id,
        Guid? tenantId,
        string name,
        EmailTemplatePurpose purpose,
        string? culture,
        IReadOnlyCollection<string> configuredCultures,
        string subject,
        string htmlBody)
        : base(id, tenantId)
    {
        SetName(name);
        SetPurpose(purpose);
        SetCulture(culture, configuredCultures);
        SetContent(subject, htmlBody);
    }

    public void SetName(string name)
    {
        Name = NormalizeName(name);
        NormalizedName = Name.ToUpperInvariant();
    }

    public void SetPurpose(EmailTemplatePurpose purpose) => Purpose = Defined(purpose, nameof(purpose));

    /// <summary>Empty is all languages; anything else must be a configured language (UnsupportedCulture).</summary>
    public void SetCulture(string? culture, IReadOnlyCollection<string> configuredCultures)
    {
        var key = LocalizedName.NormalizeCulture(culture);
        if (key.Length == 0)
        {
            Culture = null;
            return;
        }

        if (key.Length > PlatformMasterDataConsts.MaxCultureLength || !configuredCultures.Contains(key))
        {
            throw new BusinessException(PlatformErrorCodes.UnsupportedCulture).WithData("culture", key);
        }

        Culture = key;
    }

    /// <summary>The service clears the previous default of the purpose first (the partial unique index remains the truth).</summary>
    public void SetDefault(bool isDefault) => IsDefault = isDefault;

    public void SetAddresses(string? senderAddress, string? replyTo, string? to, string? cc, string? bcc, bool sendCopyToSender)
    {
        const int max = PlatformMasterDataConsts.MaxEmailAddressFieldLength;
        SenderAddress = Optional(senderAddress, nameof(senderAddress), max);
        ReplyTo = Optional(replyTo, nameof(replyTo), max);
        To = Optional(to, nameof(to), max);
        Cc = Optional(cc, nameof(cc), max);
        Bcc = Optional(bcc, nameof(bcc), max);
        SendCopyToSender = sendCopyToSender;
    }

    /// <summary>The subject (trimmed) and the HTML body (kept as typed), both required.</summary>
    public void SetContent(string subject, string htmlBody)
    {
        Subject = Check.NotNullOrWhiteSpace(subject, nameof(subject), PlatformMasterDataConsts.MaxSubjectLength).Trim();
        HtmlBody = Check.Length(Check.NotNullOrWhiteSpace(htmlBody, nameof(htmlBody)), nameof(htmlBody), PlatformMasterDataConsts.MaxHtmlBodyLength)!;
    }
}
