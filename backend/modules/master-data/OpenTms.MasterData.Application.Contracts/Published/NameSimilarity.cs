using System;

namespace OpenTms.MasterData.Published;

/// <summary>
/// The name score of the order intake's fuzzy rule (design 72 build note 25, PO decision 2026-11-03): both names through
/// <see cref="NameNormalizer"/>, then the higher of the trigram similarity (<see cref="TrigramSimilarity"/>, pg_trgm's) and
/// the edit similarity, 1 − the optimal-string-alignment distance (Damerau-Levenshtein where a swap of two adjacent
/// characters is one edit) ÷ the longer length — the latter only for a fat-finger, a distance of at most
/// <see cref="MaxTypoDistance"/>; a larger distance scores by trigrams alone. Trigrams forgive missing and extra words; the
/// edit similarity forgives a swapped, missing or wrong letter ("Transprot", "Janssen"), not a different word ("Venlo" vs
/// "Venray", distance 3). 0 when either name is empty. Deterministic.
/// </summary>
public static class NameSimilarity
{
    /// <summary>The largest OSA distance the edit similarity counts for (PO decision 2026-11-03).</summary>
    public const int MaxTypoDistance = 2;

    public static double Score(string? a, string? b) => ScoreNormalized(NameNormalizer.Normalize(a), NameNormalizer.Normalize(b));

    /// <summary>The best <see cref="Score"/> of <paramref name="text"/> to any of <paramref name="names"/> (a relation's name and full name).</summary>
    public static double Best(string? text, params string?[] names)
    {
        var normalized = NameNormalizer.Normalize(text);
        var best = 0d;
        foreach (var name in names)
        {
            best = Math.Max(best, ScoreNormalized(normalized, NameNormalizer.Normalize(name)));
        }

        return best;
    }

    /// <summary>1 − OSA distance ÷ the longer length, on texts as given (callers pass normalised names).</summary>
    public static double EditSimilarity(string a, string b)
    {
        var longest = Math.Max(a.Length, b.Length);
        return longest == 0 ? 0 : 1d - (double)OsaDistance(a, b) / longest;
    }

    /// <summary>Optimal string alignment distance: insertions, deletions, substitutions and adjacent transpositions, each 1.</summary>
    public static int OsaDistance(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++)
        {
            d[i, 0] = i;
        }

        for (var j = 0; j <= b.Length; j++)
        {
            d[0, j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                var value = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                {
                    value = Math.Min(value, d[i - 2, j - 2] + 1);
                }

                d[i, j] = value;
            }
        }

        return d[a.Length, b.Length];
    }

    private static double ScoreNormalized(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0)
        {
            return 0;
        }

        var trigram = TrigramSimilarity.Of(a, b);
        /* Two names whose lengths differ by more than the typo distance can never be within it: no matrix needed. */
        if (Math.Abs(a.Length - b.Length) > MaxTypoDistance)
        {
            return trigram;
        }

        var distance = OsaDistance(a, b);
        return distance <= MaxTypoDistance ? Math.Max(trigram, 1d - (double)distance / Math.Max(a.Length, b.Length)) : trigram;
    }
}
