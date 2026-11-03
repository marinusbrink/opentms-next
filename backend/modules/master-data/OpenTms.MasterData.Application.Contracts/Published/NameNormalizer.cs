using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace OpenTms.MasterData.Published;

/// <summary>
/// Prepares a name for <see cref="TrigramSimilarity"/> (design 72 build note 25): lower case, every character that is not
/// a letter or digit (dots, commas, hyphens, ampersands, …) a space, whitespace collapsed, and the standalone legal-form
/// tokens dropped — "Jansen Transport B.V." and "Jansen Transprot BV" are compared as "jansen transport" and
/// "jansen transprot". The legal forms: B.V./BV, N.V./NV, V.O.F./VOF, C.V./CV, BVBA, SRL, GmbH, GmbH &amp; Co. KG, KG, AG,
/// Ltd, Limited, LLC, Inc, S.A./SA, SARL, SAS, Sp. z o.o. ("Holding" is no legal form and stays). A name that consists of
/// legal-form tokens only keeps them. <see cref="TrigramSimilarity"/> itself stays pg_trgm-identical; the callers score
/// names through <see cref="NameSimilarity"/>, which normalises both sides. Deterministic and culture-invariant.
/// </summary>
public static class NameNormalizer
{
    /// <summary>The legal forms as token sequences after normalisation, longest first so "gmbh co kg" wins over "gmbh".</summary>
    private static readonly string[][] LegalForms = new[]
        {
            "b v", "bv", "n v", "nv", "v o f", "vof", "c v", "cv", "bvba", "srl", "gmbh co kg", "gmbh", "kg", "ag",
            "ltd", "limited", "llc", "inc", "s a", "sa", "sarl", "sas", "sp z o o"
        }
        .Select(f => f.Split(' '))
        .OrderByDescending(f => f.Length)
        .ToArray();

    public static string Normalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var value = name.Length > TrigramSimilarity.MaxLength ? name[..TrigramSimilarity.MaxLength] : name;
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            builder.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        }

        var tokens = builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var kept = new List<string>(tokens.Length);
        for (var i = 0; i < tokens.Length;)
        {
            var form = LegalForms.FirstOrDefault(f => i + f.Length <= tokens.Length && f.Select((t, k) => tokens[i + k] == t).All(same => same));
            if (form != null)
            {
                i += form.Length;
                continue;
            }

            kept.Add(tokens[i]);
            i++;
        }

        return string.Join(' ', kept.Count > 0 ? kept.ToArray() : tokens);
    }
}
