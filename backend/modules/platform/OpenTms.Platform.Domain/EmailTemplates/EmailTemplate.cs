using System;
using System.Collections.Generic;
using System.Linq;
using OpenTms.Platform.Localization;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.MultiTenancy;

namespace OpenTms.Platform.EmailTemplates;

/// <summary>
/// An e-mail template (design 49; OpenTMS <c>OutboundEMailMessageTemplate</c>): one template per purpose variant with its
/// addresses, and its subject and HTML body per language. Every text may hold Scriban expressions; the service parses
/// them on save (never renders). At most one default per purpose (partial unique index); the service clears the
/// previous default in the same transaction. Sender and reply-to hold company addresses: business data, never logged.
/// </summary>
public class EmailTemplate : PlatformMasterDataAggregateRoot
{
    public string Name { get; private set; } = default!;

    /// <summary>The name in upper case: the unique key (PO 2026-10-16, as vehicles).</summary>
    public string NormalizedName { get; private set; } = default!;

    public EmailTemplatePurpose Purpose { get; private set; }

    public bool IsDefault { get; private set; }

    public string? SenderAddress { get; private set; }

    public string? SenderName { get; private set; }

    public string? ReplyTo { get; private set; }

    /// <summary>Empty: the consumer's recipient.</summary>
    public string? To { get; private set; }

    public string? Cc { get; private set; }

    public string? Bcc { get; private set; }

    public bool SendCopyToSender { get; private set; }

    public virtual ICollection<EmailTemplateContent> Contents { get; private set; } = new List<EmailTemplateContent>();

    protected EmailTemplate()
    {
    }

    public EmailTemplate(Guid id, Guid? tenantId, string name, EmailTemplatePurpose purpose)
        : base(id, tenantId)
    {
        SetName(name);
        SetPurpose(purpose);
    }

    public void SetName(string name)
    {
        Name = NormalizeName(name);
        NormalizedName = Name.ToUpperInvariant();
    }

    public void SetPurpose(EmailTemplatePurpose purpose) => Purpose = Defined(purpose, nameof(purpose));

    /// <summary>The service clears the previous default of the purpose first (the partial unique index remains the truth).</summary>
    public void SetDefault(bool isDefault) => IsDefault = isDefault;

    public void SetAddresses(string? senderAddress, string? senderName, string? replyTo, string? to, string? cc, string? bcc, bool sendCopyToSender)
    {
        const int max = PlatformMasterDataConsts.MaxEmailAddressFieldLength;
        SenderAddress = Optional(senderAddress, nameof(senderAddress), max);
        SenderName = Optional(senderName, nameof(senderName), max);
        ReplyTo = Optional(replyTo, nameof(replyTo), max);
        To = Optional(to, nameof(to), max);
        Cc = Optional(cc, nameof(cc), max);
        Bcc = Optional(bcc, nameof(bcc), max);
        SendCopyToSender = sendCopyToSender;
    }

    /// <summary>
    /// Replaces the content rows: one per language, only the configured languages, the default language
    /// (<see cref="LocalizedName.DefaultCulture"/>) required. Validated before anything changes.
    /// </summary>
    public void SetContents(IReadOnlyList<(string Culture, string Subject, string HtmlBody)> contents, IReadOnlyCollection<string> configuredCultures)
    {
        var normalized = new List<(string Culture, string Subject, string HtmlBody)>();
        foreach (var (culture, subject, htmlBody) in contents)
        {
            var key = LocalizedName.NormalizeCulture(culture);
            if (key.Length == 0 || key.Length > PlatformMasterDataConsts.MaxCultureLength || !configuredCultures.Contains(key))
            {
                throw new BusinessException(PlatformErrorCodes.UnsupportedCulture).WithData("culture", key);
            }

            if (normalized.Any(c => c.Culture == key))
            {
                throw new BusinessException(PlatformErrorCodes.DuplicateKey).WithData("field", "culture").WithData("value", key);
            }

            normalized.Add((
                key,
                Check.NotNullOrWhiteSpace(subject, nameof(subject), PlatformMasterDataConsts.MaxSubjectLength).Trim(),
                Check.Length(Check.NotNullOrWhiteSpace(htmlBody, nameof(htmlBody)), nameof(htmlBody), PlatformMasterDataConsts.MaxHtmlBodyLength)!));
        }

        if (normalized.All(c => c.Culture != LocalizedName.DefaultCulture))
        {
            throw new BusinessException(PlatformErrorCodes.DefaultLanguageContentRequired).WithData("culture", LocalizedName.DefaultCulture);
        }

        var wanted = normalized.Select(c => c.Culture).ToHashSet(StringComparer.Ordinal);
        foreach (var stale in Contents.Where(c => !wanted.Contains(c.Culture)).ToList())
        {
            Contents.Remove(stale);
        }

        foreach (var (culture, subject, htmlBody) in normalized)
        {
            var content = Contents.FirstOrDefault(c => c.Culture == culture);
            if (content == null)
            {
                content = new EmailTemplateContent(TenantId, Id, culture);
                Contents.Add(content);
            }

            content.Set(subject, htmlBody);
        }
    }
}

/// <summary>The subject and HTML body of a template in one language; composite key (template, culture).</summary>
public class EmailTemplateContent : Entity, IMultiTenant
{
    public Guid? TenantId { get; private set; }

    public Guid EmailTemplateId { get; private set; }

    /// <summary>A configured language code ("nl", "en").</summary>
    public string Culture { get; private set; } = default!;

    public string Subject { get; private set; } = default!;

    /// <summary>Up to 200,000 characters of HTML source with Scriban expressions.</summary>
    public string HtmlBody { get; private set; } = default!;

    protected EmailTemplateContent()
    {
    }

    public EmailTemplateContent(Guid? tenantId, Guid emailTemplateId, string culture)
    {
        TenantId = tenantId;
        EmailTemplateId = emailTemplateId;
        Culture = culture;
    }

    public void Set(string subject, string htmlBody)
    {
        Subject = subject;
        HtmlBody = htmlBody;
    }

    public override object[] GetKeys() => [EmailTemplateId, Culture];
}
