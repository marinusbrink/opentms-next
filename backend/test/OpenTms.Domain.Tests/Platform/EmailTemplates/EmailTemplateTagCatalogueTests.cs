using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Shouldly;
using Xunit;

namespace OpenTms.Platform.EmailTemplates;

/// <summary>
/// EmailTemplateTagCatalogue against the design table (docs/designs/49-platform-master-data.md, "Tags and default
/// content", PO 2026-10-20): the sixteen rows written out as the expected set (group, purposes, tags; the four
/// collections with their children), GroupsFor/TagsFor per purpose, unique snake_case paths, the inserted snippets, the
/// localization keys the parity test checks, and the rule that a collection's loop variable is never a group key of
/// the same purpose (inside the block it would hide that group: the old "trip" variable of "trips" fails here).
/// Risk class: Low (static catalogue data) — unit tests; the senders' typed models get their own contract tests.
/// </summary>
public class EmailTemplateTagCatalogueTests
{
    private static readonly Regex SnakeCasePath = new("^[a-z][a-z0-9_]*(\\.[a-z][a-z0-9_]*)*$", RegexOptions.CultureInvariant);
    private static readonly Regex LoopHeader = new("^\\{\\{ for ([a-z][a-z0-9_]*) in ([a-z0-9_.]+) \\}\\}$", RegexOptions.Multiline | RegexOptions.CultureInvariant);

    private static readonly EmailTemplatePurpose[] All = Enum.GetValues<EmailTemplatePurpose>();

    /// <summary>A row of the design table: a group, its purposes, its tags; <c>Item</c> is the loop variable of a collection row.</summary>
    private sealed record Row(string Group, EmailTemplatePurpose[] Purposes, string[] Tags, string? Item = null)
    {
        public bool IsCollection => Item != null;
    }

    private static readonly Row[] Design =
    [
        new("company", All, ["name", "address", "postal_code", "city", "country", "phone", "email", "invoice_email", "website", "logo_url", "brand_color"]),
        new("user", All, ["full_name", "email"]),
        new("recipient", All, ["name", "email", "invoice_email", "contact_person", "contact_person_email", "salutation"]),
        new("general", All, ["today", "now", "year"]),
        new("order", [EmailTemplatePurpose.TransportOrder, EmailTemplatePurpose.EtaNotification, EmailTemplatePurpose.DigitalArchive],
        [
            "number", "date", "reference", "customer_reference", "status", "loading_date", "unloading_date", "remarks",
            "origin.name", "origin.address", "origin.postal_code", "origin.city", "origin.country",
            "destination.name", "destination.address", "destination.postal_code", "destination.city", "destination.country",
            "shipper", "type_name"
        ]),
        new("freight", [EmailTemplatePurpose.TransportOrder, EmailTemplatePurpose.EtaNotification],
        [
            "product", "transport_unit", "quantity", "colli", "weight_kg", "volume_m3", "length_cm", "width_cm", "height_cm",
            "loading_meters", "container_number", "pincode", "invoice_reference", "origin_reference", "destination_reference", "shipper", "remarks"
        ], Item: "line"),
        new("trip", [EmailTemplatePurpose.TransportFile, EmailTemplatePurpose.DriverInstructions, EmailTemplatePurpose.EtaNotification],
            ["number", "date", "reference", "vehicle", "licence_plate", "driver", "driver_email", "carrier", "departure_date", "type_name"]),
        new("eta", [EmailTemplatePurpose.EtaNotification], ["day_of_arrival", "time_of_arrival", "margin_from", "margin_till", "margin", "track_and_trace_url"]),
        new("invoice", [EmailTemplatePurpose.Invoice, EmailTemplatePurpose.InvoiceReminder],
            ["number", "date", "due_date", "reference", "period", "total_amount", "vat_amount", "currency", "open_amount", "days_overdue", "type_name"]),
        new("invoice.lines", [EmailTemplatePurpose.Invoice, EmailTemplatePurpose.InvoiceReminder], ["description", "quantity", "amount"], Item: "line"),
        new("purchase_order", [EmailTemplatePurpose.PurchaseOrder], ["number", "date", "reference", "carrier", "total_amount", "currency", "type_name"]),
        new("driver", [EmailTemplatePurpose.DriverInstructions], ["name", "email", "instructions_url"]),
        new("trips", [EmailTemplatePurpose.DriverInstructions], ["number", "date", "vehicle", "first_stop", "last_stop"], Item: "planned_trip"),
        new("document", [EmailTemplatePurpose.DigitalArchive], ["file_name", "document_type", "received_at"]),
        new("proforma", [EmailTemplatePurpose.ProformaOverview], ["period_from", "period_till", "total_amount", "currency"]),
        new("proforma.orders", [EmailTemplatePurpose.ProformaOverview], ["number", "date", "reference", "amount"], Item: "order"),
    ];

    public static IEnumerable<object[]> Purposes() => All.Select(p => new object[] { p });

    /// <summary>The full paths of a row, the collection tag itself included (as the catalogue flattens them).</summary>
    private static IEnumerable<string> PathsOf(Row row)
    {
        if (row.IsCollection)
        {
            yield return row.Group;
        }

        foreach (var tag in row.Tags)
        {
            yield return row.Group + "." + tag;
        }
    }

    private static IEnumerable<EmailTemplateTag> Flatten(EmailTemplateTag tag) => new[] { tag }.Concat(tag.Children);

    // ── the design table ──────────────────────────────────────────────────────

    [Fact]
    public void The_catalogue_is_the_sixteen_rows_of_the_design_table_in_order()
    {
        Design.Length.ShouldBe(16, "the design table has sixteen rows");
        EmailTemplateTagCatalogue.Groups.Select(g => g.Key).ShouldBe(Design.Select(r => r.Group));

        foreach (var row in Design)
        {
            var group = EmailTemplateTagCatalogue.Groups.Single(g => g.Key == row.Group);
            group.Purposes.ShouldBe(row.Purposes, $"purposes of {row.Group}");
            group.LabelKey.ShouldBe("Platform:TagGroup." + row.Group);

            if (row.IsCollection)
            {
                var collection = group.Tags.ShouldHaveSingleItem($"a collection row is one collection tag ({row.Group})");
                collection.Path.ShouldBe(row.Group);
                collection.Kind.ShouldBe(EmailTemplateTagKind.Collection);
                collection.Children.Select(c => c.Path).ShouldBe(row.Tags.Select(t => row.Group + "." + t));
                collection.Children.ShouldAllBe(c => c.Kind == EmailTemplateTagKind.Value);
                collection.Children.ShouldAllBe(c => c.Children.Count == 0);
            }
            else
            {
                group.Tags.Select(t => t.Path).ShouldBe(row.Tags.Select(t => row.Group + "." + t), $"tags of {row.Group}");
                group.Tags.ShouldAllBe(t => t.Kind == EmailTemplateTagKind.Value);
                group.Tags.ShouldAllBe(t => t.Children.Count == 0);
            }
        }
    }

    [Fact]
    public void The_catalogue_has_four_collections_and_one_hundred_nineteen_tags()
    {
        var tags = EmailTemplateTagCatalogue.Groups.SelectMany(g => g.Tags).SelectMany(Flatten).ToList();
        tags.Count(t => t.Kind == EmailTemplateTagKind.Collection).ShouldBe(4);
        tags.Where(t => t.Kind == EmailTemplateTagKind.Collection).Select(t => t.Path).ShouldBe(["freight", "invoice.lines", "trips", "proforma.orders"]);
        tags.Count.ShouldBe(119, "11 + 2 + 6 + 3 + 20 + (1 + 17) + 10 + 6 + 11 + (1 + 3) + 7 + 3 + (1 + 5) + 3 + 4 + (1 + 4)");
    }

    [Theory]
    [MemberData(nameof(Purposes))]
    public void GroupsFor_and_TagsFor_give_exactly_the_rows_of_the_purpose_in_the_table_order(EmailTemplatePurpose purpose)
    {
        var expectedRows = Design.Where(r => r.Purposes.Contains(purpose)).ToList();
        expectedRows.Count.ShouldBeGreaterThanOrEqualTo(4, "the four common groups apply to every purpose");

        EmailTemplateTagCatalogue.GroupsFor(purpose).Select(g => g.Key).ShouldBe(expectedRows.Select(r => r.Group));
        EmailTemplateTagCatalogue.TagsFor(purpose).Select(t => t.Path).ShouldBe(expectedRows.SelectMany(PathsOf), "collections' children included");
    }

    [Fact]
    public void Every_purpose_has_the_four_common_groups_first()
    {
        foreach (var purpose in All)
        {
            EmailTemplateTagCatalogue.GroupsFor(purpose).Take(4).Select(g => g.Key).ShouldBe(["company", "user", "recipient", "general"], purpose.ToString());
        }
    }

    // ── paths ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Group_keys_and_tag_paths_are_unique_across_the_whole_catalogue()
    {
        var keys = EmailTemplateTagCatalogue.Groups.Select(g => g.Key).ToList();
        keys.ShouldBeUnique();

        var paths = EmailTemplateTagCatalogue.Groups.SelectMany(g => g.Tags).SelectMany(Flatten).Select(t => t.Path).ToList();
        paths.ShouldBeUnique();
    }

    [Fact]
    public void Every_group_key_tag_path_and_loop_variable_is_a_snake_case_Scriban_member_path()
    {
        foreach (var group in EmailTemplateTagCatalogue.Groups)
        {
            group.Key.ShouldMatch(SnakeCasePath.ToString());
            foreach (var tag in group.Tags.SelectMany(Flatten))
            {
                tag.Path.ShouldMatch(SnakeCasePath.ToString());
                tag.Path.ShouldStartWith(group.Key, Case.Sensitive, $"{tag.Path} belongs to group {group.Key}");
            }
        }

        foreach (var (item, _) in LoopVariables())
        {
            item.ShouldMatch("^[a-z][a-z0-9_]*$");
        }
    }

    [Fact]
    public void A_collection_tag_has_the_group_key_as_path_and_every_child_path_is_the_key_plus_a_field()
    {
        foreach (var group in EmailTemplateTagCatalogue.Groups.Where(g => g.Tags.Any(t => t.Kind == EmailTemplateTagKind.Collection)))
        {
            var collection = group.Tags.ShouldHaveSingleItem();
            collection.Path.ShouldBe(group.Key);
            collection.Children.ShouldNotBeEmpty();
            collection.Children.ShouldAllBe(c => c.Path.StartsWith(group.Key + ".") && !c.Path.Substring(group.Key.Length + 1).Contains('.'));
        }
    }

    // ── snippets ──────────────────────────────────────────────────────────────

    [Fact]
    public void A_value_tag_inserts_its_path_between_double_braces()
    {
        var values = EmailTemplateTagCatalogue.Groups.SelectMany(g => g.Tags).Where(t => t.Kind == EmailTemplateTagKind.Value).ToList();
        values.ShouldNotBeEmpty();
        foreach (var tag in values)
        {
            tag.Snippet.ShouldBe("{{ " + tag.Path + " }}");
        }
    }

    [Fact]
    public void A_collection_tag_inserts_the_loop_block_with_one_line_per_child_and_the_children_insert_the_loop_variable_field()
    {
        foreach (var row in Design.Where(r => r.IsCollection))
        {
            var collection = EmailTemplateTagCatalogue.Groups.Single(g => g.Key == row.Group).Tags.Single();
            var expected = "{{ for " + row.Item + " in " + row.Group + " }}\n"
                           + string.Join("\n", row.Tags.Select(t => "{{ " + row.Item + "." + t + " }}"))
                           + "\n{{ end }}";
            collection.Snippet.ShouldBe(expected, row.Group);

            foreach (var child in collection.Children)
            {
                var field = child.Path[(row.Group.Length + 1)..];
                child.Snippet.ShouldBe("{{ " + row.Item + "." + field + " }}");
            }
        }
    }

    [Fact]
    public void The_loop_variables_are_line_line_planned_trip_and_order()
    {
        LoopVariables().ShouldBe([("line", "freight"), ("line", "invoice.lines"), ("planned_trip", "trips"), ("order", "proforma.orders")]);
    }

    /// <summary>
    /// Design: "The loop variable is never a group key of the same purpose: inside the block it would hide that group."
    /// With the old variable "trip" of "trips" (DriverInstructions also has the "trip" group) this test fails.
    /// </summary>
    [Fact]
    public void No_collection_loop_variable_equals_a_group_key_of_the_same_purpose()
    {
        var offenders = new List<string>();
        foreach (var group in EmailTemplateTagCatalogue.Groups)
        {
            foreach (var collection in group.Tags.Where(t => t.Kind == EmailTemplateTagKind.Collection))
            {
                var item = LoopHeader.Match(collection.Snippet).Groups[1].Value;
                item.ShouldNotBeNullOrEmpty($"{collection.Path} starts with a for block");
                foreach (var purpose in group.Purposes)
                {
                    var hidden = EmailTemplateTagCatalogue.GroupsFor(purpose).Select(g => g.Key).Where(key => key == item || key.StartsWith(item + ".", StringComparison.Ordinal));
                    offenders.AddRange(hidden.Select(key => $"{collection.Path} loops as '{item}', which hides group '{key}' of {purpose}"));
                }
            }
        }

        offenders.ShouldBeEmpty();
    }

    [Fact]
    public void A_loop_variable_is_never_a_Scriban_keyword()
    {
        string[] keywords = ["for", "in", "end", "if", "else", "while", "func", "with", "capture", "case", "when", "break", "continue", "ret", "do", "import", "readonly", "wrap", "include", "tablerow", "this", "true", "false", "null", "empty", "blank"];
        foreach (var (item, path) in LoopVariables())
        {
            keywords.ShouldNotContain(item, path);
        }
    }

    // ── localization keys ─────────────────────────────────────────────────────

    [Fact]
    public void AllLocalizationKeys_is_the_group_label_plus_label_and_description_of_every_tag()
    {
        var expected = new List<string>();
        foreach (var row in Design)
        {
            expected.Add("Platform:TagGroup." + row.Group);
            foreach (var path in PathsOf(row))
            {
                expected.Add("Platform:Tag." + path);
                expected.Add("Platform:TagDescription." + path);
            }
        }

        var keys = EmailTemplateTagCatalogue.AllLocalizationKeys().ToList();
        keys.ShouldBe(expected);
        keys.ShouldBeUnique();
        keys.Count.ShouldBe(16 + 2 * 119);
    }

    [Fact]
    public void The_key_helpers_match_the_properties_of_the_groups_and_tags()
    {
        EmailTemplateTagCatalogue.GroupLabelKeyOf("company").ShouldBe("Platform:TagGroup.company");
        EmailTemplateTagCatalogue.LabelKeyOf("company.name").ShouldBe("Platform:Tag.company.name");
        EmailTemplateTagCatalogue.DescriptionKeyOf("company.name").ShouldBe("Platform:TagDescription.company.name");

        foreach (var group in EmailTemplateTagCatalogue.Groups)
        {
            group.LabelKey.ShouldBe(EmailTemplateTagCatalogue.GroupLabelKeyOf(group.Key));
            foreach (var tag in group.Tags.SelectMany(Flatten))
            {
                tag.LabelKey.ShouldBe(EmailTemplateTagCatalogue.LabelKeyOf(tag.Path));
                tag.DescriptionKey.ShouldBe(EmailTemplateTagCatalogue.DescriptionKeyOf(tag.Path));
            }
        }
    }

    [Fact]
    public void The_tag_kinds_start_at_one_so_a_missing_value_fails_validation()
    {
        Enum.IsDefined((EmailTemplateTagKind)0).ShouldBeFalse();
        ((int)EmailTemplateTagKind.Value).ShouldBe(1);
        ((int)EmailTemplateTagKind.Collection).ShouldBe(2);
    }

    private static List<(string Item, string Path)> LoopVariables()
        => EmailTemplateTagCatalogue.Groups
            .SelectMany(g => g.Tags)
            .Where(t => t.Kind == EmailTemplateTagKind.Collection)
            .Select(t => (LoopHeader.Match(t.Snippet).Groups[1].Value, t.Path))
            .ToList();
}
