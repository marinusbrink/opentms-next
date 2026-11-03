using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace OpenTms.Integrations.OrderIntake.Extraction;

/// <summary>
/// The normalisation every text format shares (design 72 §3 "Html/Text: normalised text lines"): tabs become two spaces,
/// runs of three or more spaces become two (two spaces separate columns), trailing whitespace goes, empty lines go.
/// Also the HTML → text conversion and the removal of a mail's quoted reply and signature.
/// </summary>
public static partial class TextNormalizer
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

    public static List<string> NormalizeLines(string text)
    {
        var result = new List<string>();
        foreach (var raw in text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
        {
            if (raw == ExtractedDocument.PageBreak)
            {
                result.Add(ExtractedDocument.PageBreak);
                continue;
            }

            var line = NormalizeLine(raw);
            if (line.Length > 0)
            {
                result.Add(line);
            }
        }

        return result;
    }

    public static string NormalizeLine(string raw)
    {
        var builder = new StringBuilder(raw.Length);
        foreach (var c in raw)
        {
            if (c == '\t')
            {
                builder.Append("  ");
            }
            else if (c == ' ' || (char.IsWhiteSpace(c) && c != '\n'))
            {
                builder.Append(' ');
            }
            else if (!char.IsControl(c))
            {
                builder.Append(c);
            }
        }

        return ManySpaces().Replace(builder.ToString().Trim(), "  ");
    }

    /// <summary>The visible text of an HTML document, one block element per line, table cells separated by two spaces.</summary>
    public static string HtmlToText(string html)
    {
        var text = ScriptOrStyle().Replace(html, " ");
        text = Comment().Replace(text, " ");
        text = LineBreakTag().Replace(text, "\n");
        text = CellEndTag().Replace(text, "  ");
        text = Tag().Replace(text, string.Empty);
        return WebUtility.HtmlDecode(text);
    }

    /// <summary>
    /// A mail body without its quoted reply (lines starting with "&gt;", and everything after a reply header such as
    /// "-----Original Message-----") and without the signature (everything after a "-- " separator line).
    /// </summary>
    public static string StripQuotedReplyAndSignature(string body)
    {
        var kept = new List<string>();
        foreach (var raw in body.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line == "-- " || line == "--")
            {
                break;
            }

            if (ReplyHeader().IsMatch(line))
            {
                break;
            }

            if (line.TrimStart().StartsWith('>'))
            {
                continue;
            }

            kept.Add(line);
        }

        return string.Join('\n', kept);
    }

    public static int CountNonWhitespace(string text) => text.Count(c => !char.IsWhiteSpace(c));

    [GeneratedRegex(" {3,}", RegexOptions.None, 2000)]
    private static partial Regex ManySpaces();

    [GeneratedRegex(@"<(script|style|head)\b[^>]*>.*?</\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline, 2000)]
    private static partial Regex ScriptOrStyle();

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline, 2000)]
    private static partial Regex Comment();

    [GeneratedRegex(@"<\s*(br|/p|/div|/tr|/li|/h[1-6]|/table|/blockquote|p|div|tr|li|h[1-6])\b[^>]*>", RegexOptions.IgnoreCase, 2000)]
    private static partial Regex LineBreakTag();

    [GeneratedRegex(@"<\s*/\s*(td|th)\s*>", RegexOptions.IgnoreCase, 2000)]
    private static partial Regex CellEndTag();

    [GeneratedRegex(@"<[^>]*>", RegexOptions.None, 2000)]
    private static partial Regex Tag();

    [GeneratedRegex(@"^\s*(-{2,}\s*(Original Message|Oorspronkelijk bericht|Forwarded message|Doorgestuurd bericht)\s*-{2,}|_{20,})\s*$", RegexOptions.IgnoreCase, 2000)]
    private static partial Regex ReplyHeader();
}
