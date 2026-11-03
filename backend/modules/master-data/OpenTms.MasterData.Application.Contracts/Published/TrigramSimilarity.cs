using System;
using System.Collections.Generic;
using System.Text;

namespace OpenTms.MasterData.Published;

/// <summary>
/// Name similarity as PostgreSQL's pg_trgm <c>similarity()</c> computes it (design 72 build note 25), in C# so the SQLite
/// unit tests and PostgreSQL agree and the score never depends on the database: the text is lower-cased, split into words
/// at every character that is not a letter or digit, each word is padded with two spaces in front and one behind, and its
/// three-character windows form a set; the similarity is the number of trigrams both sets share divided by the number in
/// either (0 when either text has no word, 1 for the same words). Diacritics are not folded, as pg_trgm does not fold them.
/// Deterministic and culture-invariant.
/// </summary>
public static class TrigramSimilarity
{
    /// <summary>The longest text scored; longer texts are cut (the inputs of the match query are bounded the same way).</summary>
    public const int MaxLength = 320;

    public static double Of(string? a, string? b)
    {
        var left = Trigrams(a);
        var right = Trigrams(b);
        if (left.Count == 0 || right.Count == 0)
        {
            return 0;
        }

        var shared = 0;
        foreach (var trigram in left)
        {
            if (right.Contains(trigram))
            {
                shared++;
            }
        }

        return (double)shared / (left.Count + right.Count - shared);
    }

    /// <summary>The best similarity of <paramref name="text"/> to any of <paramref name="names"/> (a relation's name and full name).</summary>
    public static double Best(string? text, params string?[] names)
    {
        var best = 0d;
        foreach (var name in names)
        {
            best = Math.Max(best, Of(text, name));
        }

        return best;
    }

    /// <summary>The trigram set of a text, as pg_trgm's <c>show_trgm()</c> lists it.</summary>
    public static HashSet<string> Trigrams(string? text)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(text))
        {
            return set;
        }

        var value = text.Length > MaxLength ? text[..MaxLength] : text;
        var word = new StringBuilder();
        foreach (var c in value)
        {
            if (char.IsLetterOrDigit(c))
            {
                word.Append(char.ToLowerInvariant(c));
            }
            else
            {
                AddWord(set, word);
            }
        }

        AddWord(set, word);
        return set;
    }

    private static void AddWord(HashSet<string> set, StringBuilder word)
    {
        if (word.Length == 0)
        {
            return;
        }

        var padded = "  " + word + " ";
        for (var i = 0; i + 3 <= padded.Length; i++)
        {
            set.Add(padded.Substring(i, 3));
        }

        word.Clear();
    }
}
