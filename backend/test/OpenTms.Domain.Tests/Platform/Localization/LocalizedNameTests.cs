using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpenTms.Platform.Localization;
using Shouldly;
using Xunit;

namespace OpenTms.Platform.Localization;

/// <summary>
/// Unit tests for the Platform LocalizedName value object (design 41, "LocalizedName").
/// Risk class: High — the fallback chain decides what every grid, picker and document shows for a
/// name; a wrong fallback shows an empty or foreign-language name on every screen.
/// </summary>
public class LocalizedNameTests
{
    private static LocalizedName Netherlands() => LocalizedName.Create(new Dictionary<string, string?>
    {
        ["nl"] = "Nederland",
        ["en"] = "Netherlands",
        ["de"] = "Niederlande"
    });

    // ── Create ────────────────────────────────────────────────────────────────

    [Fact]
    public void Create_trims_texts_and_drops_empty_or_null_texts()
    {
        var name = LocalizedName.Create(new Dictionary<string, string?>
        {
            ["en"] = "  Euro  ",
            ["nl"] = "   ",
            ["de"] = null,
            ["fr"] = ""
        });

        name.Texts.Count.ShouldBe(1);
        name.Texts["en"].ShouldBe("Euro");
        name.HasText("nl").ShouldBeFalse();
        name.HasText("de").ShouldBeFalse();
    }

    [Fact]
    public void Create_lower_cases_and_trims_culture_keys()
    {
        var name = LocalizedName.Create(new Dictionary<string, string?>
        {
            ["EN"] = "Euro",
            [" Nl-NL "] = "Euro (nl)"
        });

        name.Texts.Keys.ShouldBe(["en", "nl-nl"]);
        name.TextFor("en").ShouldBe("Euro");
        name.TextFor("NL-nl").ShouldBe("Euro (nl)");
    }

    [Fact]
    public void Create_drops_an_empty_culture_key()
    {
        var name = LocalizedName.Create(new Dictionary<string, string?> { [""] = "Nowhere", ["en"] = "Euro" });

        name.Texts.Count.ShouldBe(1);
        name.Texts.ContainsKey("").ShouldBeFalse();
    }

    [Fact]
    public void Create_rejects_a_map_without_any_text()
    {
        Should.Throw<ArgumentException>(() => LocalizedName.Create(new Dictionary<string, string?>()));
        Should.Throw<ArgumentException>(() => LocalizedName.Create(new Dictionary<string, string?> { ["en"] = "  " }));
        Should.Throw<ArgumentNullException>(() => LocalizedName.Create(null!));
    }

    [Fact]
    public void Create_rejects_a_text_longer_than_128_characters_and_accepts_exactly_128()
    {
        LocalizedName.Create("en", new string('a', LocalizedName.MaxTextLength)).Texts["en"].Length.ShouldBe(128);
        Should.Throw<ArgumentException>(() => LocalizedName.Create("en", new string('a', LocalizedName.MaxTextLength + 1)));
    }

    [Fact]
    public void Create_with_a_single_culture_and_text_is_a_shorthand()
    {
        var name = LocalizedName.Create("NL", " Nederland ");

        name.Texts.ShouldBe(new Dictionary<string, string> { ["nl"] = "Nederland" });
    }

    [Fact]
    public void Last_entry_wins_when_two_keys_normalise_to_the_same_culture()
    {
        var name = LocalizedName.Create(new List<KeyValuePair<string, string?>>
        {
            new("en", "First"),
            new("EN", "Second")
        });

        name.TextFor("en").ShouldBe("Second");
    }

    // ── Resolve ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("nl", "Nederland")]
    [InlineData("NL", "Nederland")]
    [InlineData("nl-NL", "Nederland")]
    [InlineData("nl-BE", "Nederland")]
    [InlineData("en", "Netherlands")]
    [InlineData("en-GB", "Netherlands")]
    [InlineData("de-AT", "Niederlande")]
    [InlineData("fr", "Netherlands")]
    [InlineData("fr-FR", "Netherlands")]
    [InlineData("", "Netherlands")]
    [InlineData("   ", "Netherlands")]
    [InlineData(null, "Netherlands")]
    public void Resolve_walks_exact_culture_then_language_then_default_culture(string? culture, string expected)
    {
        Netherlands().Resolve(culture).ShouldBe(expected);
    }

    [Fact]
    public void Resolve_falls_back_to_the_first_text_when_the_default_culture_is_missing()
    {
        var name = LocalizedName.Create(new Dictionary<string, string?> { ["nl"] = "Nederland", ["fr"] = "Pays-Bas" });

        /* Texts are ordered by culture ("fr" < "nl"), so the first text is the French one. */
        name.Resolve("de").ShouldBe("Pays-Bas");
        name.Resolve((string?)null).ShouldBe("Pays-Bas");
        name.Resolve("nl").ShouldBe("Nederland");
    }

    [Fact]
    public void Resolve_accepts_a_CultureInfo()
    {
        Netherlands().Resolve(CultureInfo.GetCultureInfo("nl-NL")).ShouldBe("Nederland");
        Netherlands().Resolve(CultureInfo.InvariantCulture).ShouldBe("Netherlands");
    }

    [Fact]
    public void The_default_culture_is_english()
    {
        LocalizedName.DefaultCulture.ShouldBe("en");
        Netherlands().ToString().ShouldBe("Netherlands");
    }

    [Fact]
    public void Resolve_prefers_a_region_specific_text_over_the_language_text()
    {
        var name = LocalizedName.Create(new Dictionary<string, string?> { ["nl"] = "Nederland", ["nl-be"] = "Nederland (BE)", ["en"] = "Netherlands" });

        name.Resolve("nl-BE").ShouldBe("Nederland (BE)");
        name.Resolve("nl-NL").ShouldBe("Nederland");
    }

    // ── With ──────────────────────────────────────────────────────────────────

    [Fact]
    public void With_returns_a_copy_with_the_culture_added_or_replaced_and_leaves_the_original_alone()
    {
        var original = Netherlands();

        var replaced = original.With("NL", " Holland ");
        var added = original.With("fr", "Pays-Bas");

        replaced.TextFor("nl").ShouldBe("Holland");
        replaced.TextFor("en").ShouldBe("Netherlands");
        added.TextFor("fr").ShouldBe("Pays-Bas");
        added.Texts.Count.ShouldBe(4);
        original.TextFor("nl").ShouldBe("Nederland");
        original.Texts.Count.ShouldBe(3);
    }

    [Fact]
    public void With_an_empty_text_removes_the_culture()
    {
        var name = Netherlands().With("de", "").With("nl", null);

        name.Texts.Keys.ShouldBe(["en"]);
    }

    [Fact]
    public void With_cannot_remove_the_last_text()
    {
        var name = LocalizedName.Create("en", "Euro");

        Should.Throw<ArgumentException>(() => name.With("en", ""));
    }

    // ── JSON ──────────────────────────────────────────────────────────────────

    [Fact]
    public void ToJson_is_deterministic_and_ordered_by_culture()
    {
        var a = LocalizedName.Create(new Dictionary<string, string?> { ["nl"] = "Nederland", ["en"] = "Netherlands" });
        var b = LocalizedName.Create(new Dictionary<string, string?> { ["en"] = "Netherlands", ["nl"] = "Nederland" });

        a.ToJson().ShouldBe("{\"en\":\"Netherlands\",\"nl\":\"Nederland\"}");
        b.ToJson().ShouldBe(a.ToJson());
    }

    [Fact]
    public void FromJson_round_trips_ToJson()
    {
        var original = Netherlands();

        var parsed = LocalizedName.FromJson(original.ToJson());

        parsed.ShouldBe(original);
        parsed.Texts.ShouldBe(original.Texts);
    }

    [Fact]
    public void FromJson_normalises_like_Create()
    {
        var parsed = LocalizedName.FromJson("{\"EN\":\" Euro \",\"nl\":\"\"}");

        parsed.Texts.ShouldBe(new Dictionary<string, string> { ["en"] = "Euro" });
    }

    [Fact]
    public void FromJson_rejects_json_that_is_not_a_name()
    {
        Should.Throw<ArgumentException>(() => LocalizedName.FromJson("null"));
        Should.Throw<ArgumentException>(() => LocalizedName.FromJson("{}"));
    }

    [Fact]
    public void Json_keeps_non_ascii_text_intact()
    {
        var name = LocalizedName.Create(new Dictionary<string, string?> { ["nl"] = "Curaçao", ["en"] = "Curaçao" });

        LocalizedName.FromJson(name.ToJson()).TextFor("nl").ShouldBe("Curaçao");
    }

    // ── Equality ──────────────────────────────────────────────────────────────

    [Fact]
    public void Names_with_the_same_texts_are_equal_and_hash_equal()
    {
        var a = Netherlands();
        var b = LocalizedName.Create(new Dictionary<string, string?> { ["en"] = "Netherlands ", ["DE"] = "Niederlande", ["nl"] = "Nederland" });

        a.Equals(b).ShouldBeTrue();
        a.Equals((object)b).ShouldBeTrue();
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Fact]
    public void Names_with_different_texts_or_cultures_are_not_equal()
    {
        var a = Netherlands();

        a.Equals(a.With("nl", "Holland")).ShouldBeFalse();
        a.Equals(a.With("fr", "Pays-Bas")).ShouldBeFalse();
        a.Equals(null).ShouldBeFalse();
        a.Equals((object?)"Netherlands").ShouldBeFalse();
    }

    [Fact]
    public void Text_comparison_is_case_sensitive()
    {
        LocalizedName.Create("en", "Euro").Equals(LocalizedName.Create("en", "euro")).ShouldBeFalse();
    }

    // ── Query helper ──────────────────────────────────────────────────────────

    [Fact]
    public void LocalizedNameQuery_Text_reads_the_map_in_memory_and_tolerates_null()
    {
        LocalizedNameQuery.Text(Netherlands(), "nl").ShouldBe("Nederland");
        LocalizedNameQuery.Text(Netherlands(), "fr").ShouldBeNull();
        LocalizedNameQuery.Text(null, "nl").ShouldBeNull();
    }
}
