using Microsoft.Extensions.Localization;
using System.Linq;
using Scriban;
using Scriban.Parsing;
using Volo.Abp;

namespace OpenTms.Platform;

/// <summary>
/// Parses a Scriban text on save (design 49, assessment §3.10): a syntax error is refused with TemplateSyntaxError naming
/// the field, the language and the line. Only <c>Template.Parse</c>: a template is never rendered here. The template text
/// itself never goes into the exception (it may hold addresses).
/// </summary>
public static class ScribanTemplateValidator
{
    /// <summary>The language value for a field that is not per language.</summary>
    public const string AllLanguages = "-";

    /// <param name="localizer">Builds the readable "fieldLabel" of the message ("HTML body (English)"); <c>field</c> and
    /// <c>language</c> stay in the data for clients that act on them.</param>
    public static void EnsureValid(string? text, string field, IStringLocalizer localizer, string? culture = null)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var template = Template.Parse(text);
        if (!template.HasErrors)
        {
            return;
        }

        var error = template.Messages.FirstOrDefault(m => m.Type == ParserMessageType.Error) ?? template.Messages.First();
        var label = localizer["Platform:" + char.ToUpperInvariant(field[0]) + field[1..]].Value;
        var languageName = string.IsNullOrEmpty(culture) ? null : localizer["Platform:Language." + culture];
        var fieldLabel = languageName == null ? label : $"{label} ({(languageName.ResourceNotFound ? culture : languageName.Value)})";
        throw new BusinessException(PlatformErrorCodes.TemplateSyntaxError)
            .WithData("field", field)
            .WithData("fieldLabel", fieldLabel)
            .WithData("language", string.IsNullOrEmpty(culture) ? AllLanguages : culture)
            .WithData("line", error.Span.Start.Line + 1);
    }
}
