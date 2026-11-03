using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace OpenTms.Integrations.OrderIntake.Extraction;

/// <summary>
/// The layout fingerprint of design 72 §3, for the trace and the profile lookup: xlsx/csv hash the sheet names and the
/// normalised header row; XML the root's namespace and name plus the sorted distinct element paths; text and PDF the
/// sorted set of label lines (lines whose text without digits and punctuation is 2–40 letters and occurs once). Values
/// never enter the description, only layout words and counts.
/// </summary>
public static class DocumentFingerprinter
{
    public const int MaxDescriptionLength = 400;

    public record Result(string Fingerprint, string Description, IReadOnlyCollection<string> SignatureTokens);

    public static Result ForSheets(IReadOnlyList<ExtractedSheet> sheets)
    {
        var parts = new List<string>();
        var tokens = new List<string>();
        foreach (var sheet in sheets)
        {
            var header = sheet.Rows.FirstOrDefault()?.Cells.Select(c => Normalize(c.Text)).ToList() ?? new List<string>();
            parts.Add(Normalize(sheet.Name) + ":" + string.Join("|", header));
            tokens.AddRange(sheet.Rows.FirstOrDefault()?.Cells.Select(c => c.Text.Trim()) ?? []);
        }

        var firstHeader = sheets.FirstOrDefault()?.Rows.FirstOrDefault()?.Cells.Select(c => c.Text.Trim()) ?? [];
        var description = $"{sheets.Count} sheet(s) ({string.Join(", ", sheets.Select(s => s.Name))}); headers: {string.Join(", ", firstHeader)}";
        return new Result(Hash(string.Join("\n", parts)), Cut(description), tokens.Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    public static Result ForXml(XDocument document)
    {
        var root = document.Root;
        if (root == null)
        {
            return new Result(Hash(string.Empty), "empty XML", []);
        }

        var paths = root.DescendantsAndSelf()
            .Select(e => "/" + string.Join("/", e.AncestorsAndSelf().Reverse().Select(a => a.Name.LocalName)))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
        var key = root.Name.NamespaceName + "|" + root.Name.LocalName + "\n" + string.Join("\n", paths);
        var description = $"XML root {root.Name.LocalName}" + (root.Name.NamespaceName.Length > 0 ? $" ({root.Name.NamespaceName})" : string.Empty) + $", {paths.Count} element paths";
        var tokens = paths.Concat(paths.Select(p => p[(p.LastIndexOf('/') + 1)..])).Distinct(StringComparer.Ordinal).ToList();
        return new Result(Hash(key), Cut(description), tokens);
    }

    public static Result ForText(IReadOnlyList<string> lines)
    {
        var labels = LabelLines(lines);
        var description = $"{labels.Count} label lines" + (labels.Count > 0 ? ": " + string.Join(", ", labels.Take(12)) : string.Empty);
        return new Result(Hash(string.Join("\n", labels)), Cut(description), labels);
    }

    /// <summary>The label lines: the letters of a line (digits and punctuation removed) when 2–40 letters, distinct and occurring once.</summary>
    public static List<string> LabelLines(IReadOnlyList<string> lines)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            if (line == ExtractedDocument.PageBreak)
            {
                continue;
            }

            var letters = LettersOnly(line);
            var letterCount = letters.Count(char.IsLetter);
            if (letterCount is < 2 or > 40)
            {
                continue;
            }

            counts[letters] = counts.GetValueOrDefault(letters) + 1;
        }

        return counts.Where(kv => kv.Value == 1).Select(kv => kv.Key).OrderBy(k => k, StringComparer.Ordinal).ToList();
    }

    private static string LettersOnly(string line)
    {
        var builder = new StringBuilder(line.Length);
        var lastSpace = true;
        foreach (var c in line)
        {
            if (char.IsLetter(c))
            {
                builder.Append(char.ToLowerInvariant(c));
                lastSpace = false;
            }
            else if (!lastSpace)
            {
                builder.Append(' ');
                lastSpace = true;
            }
        }

        return builder.ToString().Trim();
    }

    private static string Normalize(string text) => TextNormalizer.NormalizeLine(text).ToLowerInvariant();

    public static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static string Cut(string text) => text.Length <= MaxDescriptionLength ? text : text[..MaxDescriptionLength] + "…";

    /// <summary>The sender key (design 72 §3): the lowercased From address; <c>api:{clientId}</c> for the API.</summary>
    public static string? SenderKeyFromAddress(string? from)
    {
        if (string.IsNullOrWhiteSpace(from))
        {
            return null;
        }

        var text = from.Trim();
        var open = text.LastIndexOf('<');
        var close = text.LastIndexOf('>');
        if (open >= 0 && close > open)
        {
            text = text[(open + 1)..close];
        }

        text = text.Trim().Trim('"').ToLowerInvariant();
        return text.Length is 0 or > OrderIntakeConsts.MaxSenderKeyLength ? null : text;
    }

    public static string ApiSenderKey(string clientId) => "api:" + clientId.ToLower(CultureInfo.InvariantCulture);
}
