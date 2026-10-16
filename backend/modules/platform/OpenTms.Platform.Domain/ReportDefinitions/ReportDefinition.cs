using System;
using System.Collections.Generic;
using System.Linq;
using OpenTms.Platform.Localization;
using Volo.Abp;

namespace OpenTms.Platform.ReportDefinitions;

/// <summary>
/// A report definition (design 49; OpenTMS <c>ReportDefinition</c>, UI "Bestandlayouts"): which provider template renders
/// a document of a purpose in a language, and the Scriban pattern of its file name. At most one default per purpose and
/// language (partial unique index); the service clears the previous default in the same transaction. Archived
/// definitions are hidden from pickers and from automatic selection. Generated documents are stored (AD-09), so a
/// changed definition never changes a past document.
/// </summary>
public class ReportDefinition : PlatformMasterDataAggregateRoot
{
    public string Name { get; private set; } = default!;

    /// <summary>The name in upper case: the unique key (PO 2026-10-16).</summary>
    public string NormalizedName { get; private set; } = default!;

    public ReportPurpose Purpose { get; private set; }

    /// <summary>A configured language code ("nl", "en").</summary>
    public string Culture { get; private set; } = default!;

    public ReportRenderingProvider Provider { get; private set; }

    /// <summary>The provider's template id.</summary>
    public string ProviderTemplateId { get; private set; } = default!;

    /// <summary>Scriban, e.g. "factuur-{{ number }}.pdf"; always ends in ".pdf".</summary>
    public string FileNamePattern { get; private set; } = default!;

    public bool IsDefault { get; private set; }

    public bool IsArchived { get; private set; }

    public bool AvailableInCustomerPortal { get; private set; }

    protected ReportDefinition()
    {
    }

    public ReportDefinition(
        Guid id,
        Guid? tenantId,
        string name,
        ReportPurpose purpose,
        string culture,
        IReadOnlyCollection<string> configuredCultures,
        ReportRenderingProvider provider,
        string providerTemplateId,
        string fileNamePattern)
        : base(id, tenantId)
    {
        SetName(name);
        SetPurpose(purpose, culture, configuredCultures);
        SetProvider(provider, providerTemplateId);
        SetFileNamePattern(fileNamePattern);
    }

    public void SetName(string name)
    {
        Name = NormalizeName(name);
        NormalizedName = Name.ToUpperInvariant();
    }

    /// <summary>The purpose and the language together: they make up the default's key.</summary>
    public void SetPurpose(ReportPurpose purpose, string culture, IReadOnlyCollection<string> configuredCultures)
    {
        var key = LocalizedName.NormalizeCulture(culture);
        if (key.Length == 0 || key.Length > PlatformMasterDataConsts.MaxCultureLength || !configuredCultures.Contains(key))
        {
            throw new BusinessException(PlatformErrorCodes.UnsupportedCulture).WithData("culture", key);
        }

        Purpose = Defined(purpose, nameof(purpose));
        Culture = key;
    }

    public void SetProvider(ReportRenderingProvider provider, string providerTemplateId)
    {
        Provider = Defined(provider, nameof(provider));
        ProviderTemplateId = Check.NotNullOrWhiteSpace(providerTemplateId, nameof(providerTemplateId), PlatformMasterDataConsts.MaxProviderTemplateIdLength).Trim();
    }

    /// <summary>The pattern with ".pdf" added when missing; the service has parsed it as Scriban.</summary>
    public void SetFileNamePattern(string fileNamePattern)
        => FileNamePattern = Check.Length(WithPdfExtension(fileNamePattern), nameof(fileNamePattern), PlatformMasterDataConsts.MaxFileNamePatternLength)!;

    public static string WithPdfExtension(string fileNamePattern)
    {
        var pattern = Check.NotNullOrWhiteSpace(fileNamePattern, nameof(fileNamePattern)).Trim();
        return pattern.EndsWith(PlatformMasterDataConsts.PdfExtension, StringComparison.OrdinalIgnoreCase)
            ? pattern
            : pattern + PlatformMasterDataConsts.PdfExtension;
    }

    /// <summary>The service clears the previous default of the purpose and language first (the index remains the truth).</summary>
    public void SetDefault(bool isDefault) => IsDefault = isDefault;

    public void SetAvailableInCustomerPortal(bool available) => AvailableInCustomerPortal = available;

    public void Archive() => IsArchived = true;

    public void Unarchive() => IsArchived = false;
}
