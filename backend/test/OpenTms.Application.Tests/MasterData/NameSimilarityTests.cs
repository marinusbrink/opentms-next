using System.Linq;
using OpenTms.MasterData.Published;
using OpenTms.Orders.Intake;
using Shouldly;
using Xunit;

namespace OpenTms.MasterData;

/// <summary>
/// The name score of the order intake's fuzzy rule (design 72 build note 28, PO decisions 2026-11-03): the normaliser
/// (lower case, punctuation a space, standalone legal forms dropped, "Holding" kept, a name of legal forms only kept),
/// the trigram similarity (pg_trgm's algorithm; the PostgreSQL comparison is in
/// <c>NameSimilarityPostgreSqlTests</c>), the OSA edit similarity that counts only at a distance of at most 2, and the
/// measured pairs of the build note; plus the resolver's pick rule (threshold, 0.10 margin, tie → Ambiguous). Risk
/// class: High (order chain: a wrong match puts an order on the wrong customer).
/// </summary>
public class NameSimilarityTests
{
    /* ---------- NameNormalizer ---------- */

    [Theory]
    [InlineData("Jansen Transport B.V.", "jansen transport")]
    [InlineData("Jansen Transprot BV", "jansen transprot")]
    [InlineData("Bakker Logistiek Holding BV", "bakker logistiek holding")]
    [InlineData("Heineken Nederland B.V.", "heineken nederland")]
    [InlineData("Müller GmbH & Co. KG", "müller")]
    [InlineData("Müller GmbH", "müller")]
    [InlineData("Kowalski Sp. z o.o.", "kowalski")]
    [InlineData("Transports Dupont S.A.", "transports dupont")]
    [InlineData("De Vries V.O.F.", "de vries")]
    [InlineData("Smith Ltd", "smith")]
    [InlineData("Acme Inc.", "acme")]
    [InlineData("Peeters BVBA", "peeters")]
    [InlineData("  Jansen,   Transport -  Venlo ", "jansen transport venlo")]
    [InlineData("JANSEN&ZONEN", "jansen zonen")]
    [InlineData("Garage 2000 B.V.", "garage 2000")]
    public void The_normaliser_lowers_case_turns_punctuation_into_spaces_and_drops_standalone_legal_forms(string name, string expected)
    {
        NameNormalizer.Normalize(name).ShouldBe(expected);
    }

    [Theory]
    [InlineData("Bavaria Logistics", "bavaria logistics")] /* "bv" only as a whole token, never inside a word */
    [InlineData("Agrico Vof-handel", "agrico handel")] /* "vof" is a token after the hyphen became a space */
    [InlineData("Sabic Holding", "sabic holding")] /* Holding is no legal form */
    public void Legal_forms_are_dropped_only_as_whole_tokens(string name, string expected)
    {
        NameNormalizer.Normalize(name).ShouldBe(expected);
    }

    [Theory]
    [InlineData("B.V.", "b v")]
    [InlineData("BV", "bv")]
    [InlineData("GmbH", "gmbh")]
    public void A_name_of_legal_form_tokens_only_keeps_them(string name, string expected)
    {
        NameNormalizer.Normalize(name).ShouldBe(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("...")]
    public void An_empty_name_normalises_to_nothing(string? name)
    {
        NameNormalizer.Normalize(name).ShouldBe(string.Empty);
    }

    [Fact]
    public void The_normaliser_bounds_its_input()
    {
        NameNormalizer.Normalize(new string('a', 5000)).Length.ShouldBe(TrigramSimilarity.MaxLength);
    }

    /* ---------- TrigramSimilarity (pg_trgm's algorithm) ---------- */

    [Fact]
    public void The_trigram_set_is_pg_trgms_show_trgm()
    {
        /* SELECT show_trgm('Venlo') → {"  v"," ve",enl,lo ,nlo,ven} */
        TrigramSimilarity.Trigrams("Venlo").OrderBy(t => t, System.StringComparer.Ordinal).ShouldBe(["  v", " ve", "enl", "lo ", "nlo", "ven"]);
        TrigramSimilarity.Trigrams("a-b").ShouldBe(["  a", " a ", "  b", " b "], ignoreOrder: true);
        TrigramSimilarity.Trigrams("").ShouldBeEmpty();
    }

    [Theory]
    [InlineData("jansen transprot", "jansen transport", 0.6)]
    [InlineData("jansen transport venlo", "jansen transport venray", 0.7307692)]
    [InlineData("heinekn nederland", "heineken nederland", 0.7619048)]
    [InlineData("klant foods inkoop", "klant foods", 0.6315789)]
    [InlineData("jansen transprot", "janssen transport", 0.5)]
    public void The_trigram_similarity_has_pg_trgms_values(string a, string b, double pgTrgm)
    {
        TrigramSimilarity.Of(a, b).ShouldBe(pgTrgm, 1e-6);
        TrigramSimilarity.Of(b, a).ShouldBe(pgTrgm, 1e-6, "symmetric");
    }

    [Fact]
    public void The_trigram_similarity_of_the_same_words_is_one_and_of_nothing_is_zero()
    {
        TrigramSimilarity.Of("DC Venlo", "dc  venlo!").ShouldBe(1d);
        TrigramSimilarity.Of("", "Venlo").ShouldBe(0d);
        TrigramSimilarity.Of(null, null).ShouldBe(0d);
        TrigramSimilarity.Of("!!!", "abc").ShouldBe(0d);
    }

    /* ---------- NameSimilarity ---------- */

    [Theory]
    [InlineData("transprot", "transport", 1)]
    [InlineData("janssen", "jansen", 1)]
    [InlineData("heinekn", "heineken", 1)]
    [InlineData("venlo", "venray", 3)]
    [InlineData("abc", "abc", 0)]
    [InlineData("", "abc", 3)]
    [InlineData("ca", "abc", 3)] /* OSA, not unrestricted Damerau-Levenshtein (that would be 2) */
    public void The_edit_distance_is_the_optimal_string_alignment_distance(string a, string b, int expected)
    {
        NameSimilarity.OsaDistance(a, b).ShouldBe(expected);
        NameSimilarity.OsaDistance(b, a).ShouldBe(expected);
    }

    [Theory]
    /* The measured pairs of design 72 build note 28 (distance, trigram / edit / score). */
    [InlineData("Jansen Transprot BV", "Jansen Transport B.V.", 0.94)]
    [InlineData("Heinekn Nederland", "Heineken Nederland B.V.", 0.94)]
    [InlineData("Bakker Logistiek Holding BV", "Bakker Logistiek Holding", 1.00)]
    [InlineData("Jansen Transport", "Janssen Transport", 0.94)]
    [InlineData("Jansen Transport Venlo", "Jansen Transport Venray", 0.73)]
    public void The_measured_pairs_score_as_the_design_states(string received, string known, double score)
    {
        System.Math.Round(NameSimilarity.Score(received, known), 2).ShouldBe(score);
        System.Math.Round(NameSimilarity.Score(known, received), 2).ShouldBe(score, "symmetric");
    }

    [Fact]
    public void Another_place_is_not_a_typo_and_stays_below_the_default_threshold()
    {
        /* Venlo/Venray: distance 3, the edit similarity (0.87) is not counted, the trigram one (0.73) is below 0.75. */
        NameSimilarity.Score("Jansen Transport Venlo", "Jansen Transport Venray").ShouldBeLessThan((double)IntakeConsts.DefaultNameSimilarityThreshold);
        NameSimilarity.EditSimilarity("jansen transport venlo", "jansen transport venray").ShouldBeGreaterThan(0.85, "would have passed if counted");
    }

    [Fact]
    public void Normalising_lifts_a_legal_form_difference_that_trigrams_alone_punish()
    {
        TrigramSimilarity.Of("Jansen Transprot BV", "Jansen Transport B.V.").ShouldBeLessThan(0.55, "0.50 before normalising");
        NameSimilarity.Score("Jansen Transprot BV", "Jansen Transport B.V.").ShouldBeGreaterThan(0.9);
    }

    [Fact]
    public void A_distance_of_two_still_counts_and_three_does_not()
    {
        /* "jansen transport" → "jamsen tarnsport": a substitution and a swap (2) */
        NameSimilarity.OsaDistance("jansen transport", "jamsen tarnsport").ShouldBe(2);
        NameSimilarity.Score("jansen transport", "jamsen tarnsport").ShouldBe(1 - 2d / 16, 1e-9);
        /* three edits: the trigram similarity alone */
        NameSimilarity.OsaDistance("jansen transport", "jamsen tarnsporr").ShouldBe(3);
        NameSimilarity.Score("jansen transport", "jamsen tarnsporr").ShouldBe(TrigramSimilarity.Of("jansen transport", "jamsen tarnsporr"), 1e-9);
    }

    [Theory]
    [InlineData(null, "Jansen")]
    [InlineData("", "Jansen")]
    [InlineData("Jansen", null)]
    [InlineData("...", "Jansen")]
    public void An_empty_side_scores_zero(string? a, string? b)
    {
        NameSimilarity.Score(a, b).ShouldBe(0d);
    }

    [Fact]
    public void Best_takes_the_higher_of_name_and_full_name()
    {
        NameSimilarity.Best("Jansen Transprot", "JT", "Jansen Transport B.V.").ShouldBe(NameSimilarity.Score("Jansen Transprot", "Jansen Transport B.V."));
        NameSimilarity.Best("Jansen Transprot", null, null).ShouldBe(0d);
    }

    /* ---------- The pick rule ---------- */

    private static FuzzyPick<string> Pick(decimal threshold, params (string Item, double Score)[] scored) => OrderIntakeResolver.PickFuzzy(scored, threshold);

    [Fact]
    public void A_single_candidate_at_or_above_the_threshold_is_chosen_and_below_it_nothing()
    {
        Pick(0.75m, ("a", 0.75)).Chosen!.Value.Item.ShouldBe("a", "at the threshold counts");
        Pick(0.75m, ("a", 0.7499)).Chosen.ShouldBeNull();
        Pick(0.75m, ("a", 0.7499)).Ambiguous.ShouldBeEmpty("none passing chooses nothing and is no tie");
        Pick(0.75m).Chosen.ShouldBeNull();
    }

    [Fact]
    public void The_best_is_chosen_when_it_leads_the_runner_up_by_the_margin()
    {
        Pick(0.75m, ("b", 0.75), ("a", 0.85)).Chosen!.Value.Item.ShouldBe("a", "0.85 − 0.75 is the margin itself (floating point)");
        Pick(0.75m, ("b", 0.80), ("a", 0.94), ("c", 0.5)).Chosen!.Value.Item.ShouldBe("a");
        Pick(0.75m, ("b", 0.70), ("a", 0.76)).Chosen!.Value.Item.ShouldBe("a", "a runner-up below the threshold does not count");
    }

    [Fact]
    public void Two_passing_candidates_within_the_margin_are_a_tie()
    {
        var tie = Pick(0.75m, ("a", 0.94), ("b", 0.88), ("c", 0.80), ("d", 0.60));
        tie.Chosen.ShouldBeNull();
        tie.Ambiguous.Select(p => p.Item).ShouldBe(["a", "b"], "only the candidates within the margin of the best");
    }

    [Fact]
    public void The_threshold_decides_which_candidates_pass()
    {
        Pick(0.95m, ("a", 0.94)).Chosen.ShouldBeNull();
        Pick(0.90m, ("a", 0.94)).Chosen!.Value.Item.ShouldBe("a");
        Pick(0.90m, ("a", 0.94), ("b", 0.88)).Chosen!.Value.Item.ShouldBe("a", "b fails 0.90, so a is the only one passing");
    }
}
