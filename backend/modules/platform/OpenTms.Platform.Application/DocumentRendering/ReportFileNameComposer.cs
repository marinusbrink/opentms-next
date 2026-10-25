using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using Scriban;
using Scriban.Parsing;
using Scriban.Runtime;
using Scriban.Syntax;

namespace OpenTms.Platform.DocumentRendering;

/// <summary>
/// Composes a document's file name from a report definition's <c>FileNamePattern</c> (design 54: Scriban, e.g.
/// <c>factuur-{{ number }}.pdf</c>, always ending in ".pdf"), as OpenTMS's ComposeFileName did: the pattern is rendered
/// with <c>number</c> = the payload's <c>Entity.EntityNumber</c> (for the transport-unit balance the period's name,
/// <c>Entity.Entity.Period.Name</c>), the ".pdf" is dropped and the format's extension added. <c>number</c> is the only
/// field a pattern may use: <see cref="FindUnknownField"/> refuses any other on save. An empty or unusable result falls
/// back to a random name, as in OpenTMS. Characters a file system or a Content-Disposition header cannot hold become "_".
/// </summary>
public static class ReportFileNameComposer
{
    public const int MaxLength = 255;

    /// <summary>The number a pattern is tried with on save (<see cref="FindUnknownField"/>).</summary>
    public const string SampleNumber = "12345";

    private const string PdfExtension = ".pdf";

    private static readonly char[] Invalid = ['/', '\\', ':', '*', '?', '"', '<', '>', '|'];

    public static string Compose(string? pattern, JsonElement? data, ReportRenderFormat format) => Compose(pattern, data, format, out _);

    /// <param name="patternFailed">True when a non-empty pattern did not parse or failed at render time (a row saved
    /// before <see cref="FindUnknownField"/> existed): the caller logs it with the definition's id.</param>
    public static string Compose(string? pattern, JsonElement? data, ReportRenderFormat format, out bool patternFailed)
    {
        var extension = ExtensionOf(format);
        var name = Render(pattern, NumberOf(data), out patternFailed);
        if (name.EndsWith(PdfExtension, StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^PdfExtension.Length];
        }

        name = Sanitize(name);
        if (name.Length == 0)
        {
            name = RandomName();
        }

        var maxBase = MaxLength - extension.Length - 1;
        if (name.Length > maxBase)
        {
            name = name[..maxBase];
        }

        return name + "." + extension;
    }

    public static string ExtensionOf(ReportRenderFormat format) => format switch
    {
        ReportRenderFormat.Html => "html",
        ReportRenderFormat.Zip => "zip",
        ReportRenderFormat.Xlsx => "xlsx",
        _ => "pdf"
    };

    public static string ContentTypeOf(ReportRenderFormat format) => format switch
    {
        ReportRenderFormat.Html => "text/html",
        ReportRenderFormat.Zip => "application/zip",
        ReportRenderFormat.Xlsx => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        _ => "application/pdf"
    };

    /// <summary>
    /// <c>Entity.EntityNumber</c> of the catalogue payload. The transport-unit balance has no entity number (OpenTMS's
    /// <c>GetSingleOutput&lt;TransportUnitTransactionPeriodOverview&gt;</c>, design 55): its number is the period's name,
    /// <c>Entity.Entity.Period.Name</c>. <c>Entity.EntityNumber</c> wins when both exist.
    /// </summary>
    public static string? NumberOf(JsonElement? data)
    {
        if (data is not { ValueKind: JsonValueKind.Object } root || !root.TryGetProperty("Entity", out var entity) || entity.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (TryText(entity, "EntityNumber", out var number))
        {
            return number;
        }

        return entity.TryGetProperty("Entity", out var overview) && overview.ValueKind == JsonValueKind.Object
               && overview.TryGetProperty("Period", out var period) && period.ValueKind == JsonValueKind.Object
               && TryText(period, "Name", out number)
            ? number
            : null;
    }

    /// <summary>
    /// The save-time check of a pattern (design 54/50): renders it against a model holding only <c>number</c>
    /// (<see cref="SampleNumber"/>) with strict variables and strict member access, under the render limits. Returns the
    /// first name the pattern uses that does not exist (<c>invoice.number</c>, <c>number.foo</c>), or null when every
    /// name exists. A pattern that does not parse is TemplateSyntaxError's (null here); any other render failure (a loop
    /// beyond the limit) is left to the render-time fallback (null here as well). Scriban's built-in functions
    /// (<c>string.upcase</c>, <c>date.now</c>) are not fields and stay available.
    /// </summary>
    public static string? FindUnknownField(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return null;
        }

        var template = Template.Parse(pattern);
        if (template.HasErrors)
        {
            return null;
        }

        var context = CreateContext(SampleNumber);
        context.StrictVariables = true;
        context.EnableRelaxedMemberAccess = false;
        try
        {
            template.Render(context);
            return null;
        }
        catch (ScriptRuntimeException e) when (IsUnknownName(e))
        {
            return NameAt(pattern, e.Span);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /* Scriban 7.2's messages for an unknown variable or function ("The variable or function `x` was not found", "The
     * function `string.x` was not found") and an unknown member under strict member access ("Cannot get member with
     * name x."). Re-check them on a Scriban upgrade: reworded, an unknown field would pass the save again. */
    private static bool IsUnknownName(ScriptRuntimeException e)
        => e.OriginalMessage.Contains("was not found", StringComparison.Ordinal)
           || e.OriginalMessage.StartsWith("Cannot get member with name", StringComparison.Ordinal);

    /// <summary>The dotted name around the failing span ("invoice" → "invoice.number", "foo" → "number.foo").</summary>
    private static string NameAt(string pattern, SourceSpan span)
    {
        var start = Math.Clamp(span.Start.Offset, 0, pattern.Length);
        var end = Math.Clamp(span.End.Offset + 1, start, pattern.Length);
        static bool IsNameChar(char c) => char.IsLetterOrDigit(c) || c is '_' or '.';
        while (start > 0 && IsNameChar(pattern[start - 1]))
        {
            start--;
        }

        while (end < pattern.Length && IsNameChar(pattern[end]))
        {
            end++;
        }

        var name = pattern[start..end].Trim('.');
        return name.Length > 0 ? name : pattern.Trim();
    }

    private static bool TryText(JsonElement element, string property, out string? text)
    {
        text = null;
        if (!element.TryGetProperty(property, out var value))
        {
            return false;
        }

        text = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
        return text != null;
    }

    private static string Render(string? pattern, string? number, out bool failed)
    {
        failed = false;
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return string.Empty;
        }

        try
        {
            var template = Template.Parse(pattern);
            if (template.HasErrors)
            {
                failed = true;
                return string.Empty;
            }

            return template.Render(CreateContext(number ?? string.Empty)) ?? string.Empty;
        }
        catch (Exception)
        {
            /* A pattern that fails at render time (it parsed on save) gets the random name, as OpenTMS did. */
            failed = true;
            return string.Empty;
        }
    }

    /// <summary>The model (only <c>number</c>) and the limits shared by the render and the save-time check.</summary>
    private static TemplateContext CreateContext(string number)
    {
        var context = new TemplateContext
        {
            /* A file name never needs a long loop or deep recursion: a pattern that tries is cut short. */
            LoopLimit = 100,
            RecursiveLimit = 10,
            LimitToString = MaxLength * 4
        };
        context.PushGlobal(new ScriptObject { ["number"] = number });
        return context;
    }

    private static string Sanitize(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            builder.Append(char.IsControl(c) || Invalid.Contains(c) ? '_' : c);
        }

        return builder.ToString().Trim().Trim('.').Trim();
    }

    private static string RandomName() => Guid.NewGuid().ToString("N")[20..].ToLower(CultureInfo.InvariantCulture);
}
