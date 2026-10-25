using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;

namespace OpenTms.Platform.EmailTemplates;

/// <summary>
/// The default content of a new e-mail template per purpose and language (design 54, "Tags and default content": the
/// OpenTMS seed texts converted to Scriban). The texts are data, not code: the HTML bodies are the embedded resources
/// <c>EmailTemplates/Defaults/&lt;Purpose&gt;.&lt;culture&gt;.html</c> of this assembly, the subjects the localization
/// texts <c>Platform:DefaultSubject.&lt;Purpose&gt;</c>. Only catalogued tags are used
/// (<see cref="EmailTemplateTagCatalogue"/>).
/// </summary>
public static class EmailTemplateDefaults
{
    public const string DefaultCulture = "en";

    /// <summary>The languages the defaults exist in.</summary>
    public static IReadOnlyList<string> Cultures { get; } = ["nl", "en"];

    private const string SubjectKeyPrefix = "Platform:DefaultSubject.";
    private const string CompanyEmail = "{{ company.email }}";
    private const string CompanyInvoiceEmail = "{{ company.invoice_email }}";

    private static readonly ConcurrentDictionary<string, string> Bodies = new(StringComparer.Ordinal);

    /// <summary>
    /// "nl" or "en" (trimmed, any case), else the UI culture's language when the defaults exist in it, else
    /// <see cref="DefaultCulture"/>.
    /// </summary>
    public static string ResolveCulture(string? culture, CultureInfo? uiCulture = null)
    {
        var requested = culture?.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(requested) && Cultures.Contains(requested))
        {
            return requested;
        }

        var ui = (uiCulture ?? CultureInfo.CurrentUICulture).TwoLetterISOLanguageName.ToLowerInvariant();
        return Cultures.Contains(ui) ? ui : DefaultCulture;
    }

    /// <summary>"From" and "Reply-to": the company's invoice address for the invoice purposes, else its e-mail address.</summary>
    public static string SenderAddressFor(EmailTemplatePurpose purpose) => purpose switch
    {
        EmailTemplatePurpose.Invoice or EmailTemplatePurpose.InvoiceReminder or EmailTemplatePurpose.ProformaOverview => CompanyInvoiceEmail,
        _ => CompanyEmail
    };

    /// <summary>The localization key of the purpose's default subject (one text per language in the resource).</summary>
    public static string SubjectKeyOf(EmailTemplatePurpose purpose) => SubjectKeyPrefix + purpose;

    /// <summary>The default HTML body of a purpose in a resolved culture, read once from the embedded resource.</summary>
    public static string HtmlBodyFor(EmailTemplatePurpose purpose, string culture)
        => Bodies.GetOrAdd(purpose + "." + culture, ReadBody);

    /// <summary>Every localization key the defaults need, for the parity test.</summary>
    public static IEnumerable<string> AllLocalizationKeys()
        => Enum.GetValues<EmailTemplatePurpose>().Select(SubjectKeyOf);

    private static string ReadBody(string purposeAndCulture)
    {
        var assembly = typeof(EmailTemplateDefaults).Assembly;
        var suffix = ".EmailTemplates.Defaults." + purposeAndCulture + ".html";
        var name = assembly.GetManifestResourceNames().SingleOrDefault(n => n.EndsWith(suffix, StringComparison.Ordinal))
                   ?? throw new InvalidOperationException($"No default e-mail body is embedded for {purposeAndCulture}.");
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Trim();
    }
}
