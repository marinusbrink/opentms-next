using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using Scriban;
using Scriban.Runtime;

namespace OpenTms.Platform.DocumentRendering;

/// <summary>
/// Composes a document's file name from a report definition's <c>FileNamePattern</c> (design 49: Scriban, e.g.
/// <c>factuur-{{ number }}.pdf</c>, always ending in ".pdf"), as OpenTMS's ComposeFileName did: the pattern is rendered
/// with <c>number</c> = the payload's <c>Entity.EntityNumber</c>, the ".pdf" is dropped and the format's extension
/// added. An empty or unusable result falls back to a random name, as in OpenTMS. Characters a file system or a
/// Content-Disposition header cannot hold become "_".
/// </summary>
public static class ReportFileNameComposer
{
    public const int MaxLength = 255;

    private const string PdfExtension = ".pdf";

    private static readonly char[] Invalid = ['/', '\\', ':', '*', '?', '"', '<', '>', '|'];

    public static string Compose(string? pattern, JsonElement? data, ReportRenderFormat format)
    {
        var extension = ExtensionOf(format);
        var name = Render(pattern, NumberOf(data));
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
    /// <c>Entity.EntityNumber</c> of the catalogue payload; the transport-unit balance nests its entity once more
    /// (<c>Entity.Entity.EntityNumber</c>, design 50).
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

        return entity.TryGetProperty("Entity", out var inner) && inner.ValueKind == JsonValueKind.Object && TryText(inner, "EntityNumber", out number)
            ? number
            : null;
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

    private static string Render(string? pattern, string? number)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return string.Empty;
        }

        try
        {
            var template = Template.Parse(pattern);
            if (template.HasErrors)
            {
                return string.Empty;
            }

            var globals = new ScriptObject { ["number"] = number ?? string.Empty };
            var context = new TemplateContext
            {
                /* A file name never needs a long loop or deep recursion: a pattern that tries is cut short. */
                LoopLimit = 100,
                RecursiveLimit = 10,
                LimitToString = MaxLength * 4
            };
            context.PushGlobal(globals);
            return template.Render(context) ?? string.Empty;
        }
        catch (Exception)
        {
            /* A pattern that fails at render time (it parsed on save) gets the random name, as OpenTMS did. */
            return string.Empty;
        }
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
