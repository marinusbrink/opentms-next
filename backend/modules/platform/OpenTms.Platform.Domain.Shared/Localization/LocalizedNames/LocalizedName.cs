using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace OpenTms.Platform.Localization;

/// <summary>
/// A culture → text map for a name that tenants maintain per language (docs/designs/41-master-data-first-run.md,
/// PO approved 2026-10-11). Stored as one <c>jsonb</c> column (AD-06), e.g. <c>{"en":"Netherlands","nl":"Nederland"}</c>.
/// Immutable; <see cref="Resolve(string?)"/> falls back from the requested culture to its language, then to
/// <see cref="DefaultCulture"/>, then to the first text, so a resolved name is never empty.
/// </summary>
public sealed class LocalizedName : IEquatable<LocalizedName>
{
    /// <summary>The fallback language: ABP's default resource culture in this product (gate 1, assumption 2).</summary>
    public const string DefaultCulture = "en";

    public const int MaxTextLength = 128;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SortedDictionary<string, string> _texts;

    private LocalizedName(SortedDictionary<string, string> texts)
    {
        _texts = texts;
    }

    /// <summary>Culture code (lower-case, e.g. "nl") → text. Ordered by culture so the JSON form is deterministic.</summary>
    public IReadOnlyDictionary<string, string> Texts => _texts;

    public static LocalizedName Create(IEnumerable<KeyValuePair<string, string?>> texts)
    {
        ArgumentNullException.ThrowIfNull(texts);

        var map = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (culture, text) in texts)
        {
            var key = NormalizeCulture(culture);
            var value = text?.Trim();
            if (key.Length == 0 || string.IsNullOrEmpty(value))
            {
                continue;
            }

            if (value.Length > MaxTextLength)
            {
                throw new ArgumentException($"A localized name text may not exceed {MaxTextLength} characters (culture '{key}').", nameof(texts));
            }

            map[key] = value;
        }

        if (map.Count == 0)
        {
            throw new ArgumentException("A localized name needs at least one non-empty text.", nameof(texts));
        }

        return new LocalizedName(map);
    }

    public static LocalizedName Create(string culture, string text)
        => Create([new KeyValuePair<string, string?>(culture, text)]);

    public bool HasText(string? culture) => _texts.ContainsKey(NormalizeCulture(culture));

    public string? TextFor(string? culture) => _texts.TryGetValue(NormalizeCulture(culture), out var text) ? text : null;

    /// <summary>The text for <paramref name="culture"/>, its language ("nl-NL" → "nl"), the default culture, or the first text.</summary>
    public string Resolve(string? culture)
    {
        if (!string.IsNullOrWhiteSpace(culture))
        {
            var key = NormalizeCulture(culture);
            if (_texts.TryGetValue(key, out var exact))
            {
                return exact;
            }

            var dash = key.IndexOf('-');
            if (dash > 0 && _texts.TryGetValue(key[..dash], out var language))
            {
                return language;
            }
        }

        return _texts.TryGetValue(DefaultCulture, out var fallback) ? fallback : _texts.Values.First();
    }

    public string Resolve(CultureInfo culture) => Resolve(culture?.Name);

    /// <summary>A copy with <paramref name="culture"/> set to <paramref name="text"/>; an empty text removes the culture.</summary>
    public LocalizedName With(string culture, string? text)
    {
        var map = new Dictionary<string, string?>(_texts!, StringComparer.Ordinal)
        {
            [NormalizeCulture(culture)] = text
        };
        return Create(map);
    }

    public static string NormalizeCulture(string? culture) => (culture ?? string.Empty).Trim().ToLowerInvariant();

    public string ToJson() => JsonSerializer.Serialize(_texts, JsonOptions);

    public static LocalizedName FromJson(string json)
    {
        var texts = JsonSerializer.Deserialize<Dictionary<string, string?>>(json, JsonOptions)
                    ?? throw new ArgumentException("The JSON does not hold a localized name.", nameof(json));
        return Create(texts);
    }

    public bool Equals(LocalizedName? other)
    {
        if (other is null)
        {
            return false;
        }

        return ReferenceEquals(this, other) || _texts.SequenceEqual(other._texts);
    }

    public override bool Equals(object? obj) => obj is LocalizedName other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var (culture, text) in _texts)
        {
            hash.Add(culture, StringComparer.Ordinal);
            hash.Add(text, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }

    public override string ToString() => Resolve(DefaultCulture);
}
