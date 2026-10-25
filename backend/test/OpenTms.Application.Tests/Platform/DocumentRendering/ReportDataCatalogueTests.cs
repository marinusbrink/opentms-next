using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenTms.Platform.ReportDefinitions;
using Shouldly;
using Xunit;

namespace OpenTms.Platform.DocumentRendering;

/// <summary>
/// The report data catalogue (docs/designs/55-report-rendering.md, "Report data catalogue"): per purpose the OpenTMS
/// payload a template receives. Every sample conforms to its own catalogue; every catalogued path occurs in the sample;
/// the contract check reports unknown fields, wrong JSON kinds and formats and a wrong EntityType while allowing nulls and
/// missing fields; the invoice money follows OpenTMS (NetPrice = TotalPrice + VatPrice) and every Formatted*/
/// CurrencyFormatted* string agrees with its number in Dutch notation; the field counts are pinned to the design's.
/// Risk class: Medium (design: "Catalogue and sample JSON").
/// </summary>
public class ReportDataCatalogueTests
{
    public static TheoryData<ReportPurpose> Purposes => new()
    {
        ReportPurpose.TransportOrderConfirmation, ReportPurpose.TransportFile, ReportPurpose.Invoice,
        ReportPurpose.PurchaseOrder, ReportPurpose.TransportUnitBalance
    };

    private static readonly CultureInfo Dutch = CultureInfo.GetCultureInfo("nl-NL");

    /// <summary>Every catalogue path present in a payload (array elements as "[]"), keys with null values included.</summary>
    private static HashSet<string> PathsOf(JsonNode? node, string prefix = "")
    {
        var paths = new HashSet<string>(StringComparer.Ordinal);
        Collect(node, prefix, paths);
        return paths;

        static void Collect(JsonNode? node, string prefix, HashSet<string> into)
        {
            switch (node)
            {
                case JsonObject obj:
                    foreach (var (name, child) in obj)
                    {
                        var path = prefix.Length == 0 ? name : prefix + "." + name;
                        into.Add(path);
                        Collect(child, path, into);
                    }
                    break;
                case JsonArray array:
                    foreach (var element in array)
                    {
                        if (element is null)
                        {
                            continue;
                        }

                        /* The catalogue lists "X[]" for an array of plain values only; elements of an array of objects
                         * appear through their fields ("X[].Field"). */
                        if (element is not JsonObject)
                        {
                            into.Add(prefix + "[]");
                        }

                        Collect(element, prefix + "[]", into);
                    }
                    break;
            }
        }
    }

    /// <summary>The parent object and the property name of a catalogue path in a payload; "[]" steps into the first element.</summary>
    private static (JsonObject Parent, string Name, string Concrete) Locate(JsonObject root, string path)
    {
        var segments = path.Split('.');
        JsonNode current = root;
        var concrete = new List<string>();
        for (var i = 0; i < segments.Length - 1; i++)
        {
            current = Step(current, segments[i], concrete);
        }

        var last = segments[^1];
        if (last.EndsWith("[]", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("a plain array element is not a property: " + path);
        }

        concrete.Add(last);
        return (current.AsObject(), last, string.Join(".", concrete));

        static JsonNode Step(JsonNode node, string segment, List<string> concrete)
        {
            if (segment.EndsWith("[]", StringComparison.Ordinal))
            {
                var name = segment[..^2];
                concrete.Add(name + "[0]");
                return node[name]!.AsArray()[0]!;
            }

            concrete.Add(segment);
            return node[segment]!;
        }
    }

    /// <summary>The first field of a type in any purpose whose sample value exists, with its purpose.</summary>
    private static (ReportPurpose Purpose, ReportDataField Field) First(ReportDataFieldType type)
    {
        foreach (var purpose in Enum.GetValues<ReportPurpose>())
        {
            var sample = ReportDataCatalogue.SampleFor(purpose);
            foreach (var field in ReportDataCatalogue.FieldsFor(purpose).Where(f => f.Type == type && !f.Path.EndsWith("[]", StringComparison.Ordinal)))
            {
                var (parent, name, _) = Locate(sample, field.Path);
                if (parent[name] is not null)
                {
                    return (purpose, field);
                }
            }
        }

        throw new InvalidOperationException("no field of type " + type);
    }

    // ── Samples ────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Purposes))]
    public void Every_sample_conforms_to_its_own_catalogue_and_serializes_to_the_envelope(ReportPurpose purpose)
    {
        var sample = ReportDataCatalogue.SampleFor(purpose);

        ReportDataCatalogue.Validate(purpose, sample).ShouldBeEmpty();
        sample.Select(p => p.Key).ShouldBe(["CompanyLogo", "Me", "Entity", "EntityType"]);
        sample["EntityType"]!.GetValue<string>().ShouldBe(ReportDataCatalogue.EntityTypeOf(purpose));
        sample["CompanyLogo"]!.GetValue<string>().ShouldStartWith("data:image/png;base64,");

        /* What the renderer sends: through System.Text.Json and back, still conforming. */
        var roundTripped = JsonNode.Parse(JsonSerializer.Serialize(JsonSerializer.SerializeToElement(sample)));
        ReportDataCatalogue.Validate(purpose, roundTripped).ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Purposes))]
    public void Every_catalogued_path_occurs_in_the_sample_and_the_sample_has_no_other(ReportPurpose purpose)
    {
        var catalogued = ReportDataCatalogue.FieldsFor(purpose).Select(f => f.Path).ToList();
        var inSample = PathsOf(ReportDataCatalogue.SampleFor(purpose));

        catalogued.Distinct().Count().ShouldBe(catalogued.Count, "each path once");
        catalogued.Where(p => !inSample.Contains(p)).ShouldBeEmpty($"{purpose}: catalogued but missing from the sample");
        inSample.Where(p => !catalogued.Contains(p)).ShouldBeEmpty($"{purpose}: in the sample but not catalogued");
    }

    [Theory]
    [InlineData(ReportPurpose.TransportOrderConfirmation, 1817)]
    [InlineData(ReportPurpose.TransportFile, 3890)]
    [InlineData(ReportPurpose.Invoice, 874)]
    [InlineData(ReportPurpose.PurchaseOrder, 309)]
    [InlineData(ReportPurpose.TransportUnitBalance, 611)]
    public void The_field_counts_are_the_designs(ReportPurpose purpose, int count)
    {
        ReportDataCatalogue.FieldsFor(purpose).Count.ShouldBe(count);
    }

    [Theory]
    [MemberData(nameof(Purposes))]
    public void Every_field_has_a_description_and_a_sample_of_its_kind(ReportPurpose purpose)
    {
        foreach (var field in ReportDataCatalogue.FieldsFor(purpose))
        {
            field.Description.ShouldNotBeNullOrWhiteSpace(field.Path);
            if (field.Type is ReportDataFieldType.Object or ReportDataFieldType.Array)
            {
                field.Sample.ShouldBeNull(field.Path);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Purposes))]
    public void Each_call_returns_a_fresh_sample(ReportPurpose purpose)
    {
        var first = ReportDataCatalogue.SampleFor(purpose);
        first["EntityType"] = "changed";
        first.Remove("Me");

        var second = ReportDataCatalogue.SampleFor(purpose);
        second["EntityType"]!.GetValue<string>().ShouldBe(ReportDataCatalogue.EntityTypeOf(purpose));
        second.ContainsKey("Me").ShouldBeTrue();
    }

    [Fact]
    public void The_entity_types_are_OpenTMSs_class_names()
    {
        ReportDataCatalogue.EntityTypeOf(ReportPurpose.TransportOrderConfirmation).ShouldBe("TransportOrder");
        ReportDataCatalogue.EntityTypeOf(ReportPurpose.TransportFile).ShouldBe("TransportFile");
        ReportDataCatalogue.EntityTypeOf(ReportPurpose.Invoice).ShouldBe("Invoice");
        ReportDataCatalogue.EntityTypeOf(ReportPurpose.PurchaseOrder).ShouldBe("PurchaseOrder");
        ReportDataCatalogue.EntityTypeOf(ReportPurpose.TransportUnitBalance).ShouldBe("GetSingleOutput`1");
    }

    [Fact]
    public void The_transport_unit_balance_keeps_the_extra_Entity_wrapper()
    {
        var paths = ReportDataCatalogue.FieldsFor(ReportPurpose.TransportUnitBalance).Select(f => f.Path).ToList();
        paths.ShouldContain("Entity.Entity");
        paths.ShouldContain("Entity.Entity.Period");
        paths.Where(p => p.StartsWith("Entity.", StringComparison.Ordinal)).ShouldAllBe(p => p == "Entity.Entity" || p.StartsWith("Entity.Entity.", StringComparison.Ordinal));
    }

    // ── Money and formatted numbers ────────────────────────────────────────────

    [Fact]
    public void The_invoice_sample_follows_OpenTMS_NetPrice_is_TotalPrice_plus_VatPrice()
    {
        var invoice = ReportDataCatalogue.SampleFor(ReportPurpose.Invoice)["Entity"]!.AsObject();
        var total = invoice["TotalPrice"]!.GetValue<double>();
        var vat = invoice["VatPrice"]!.GetValue<double>();
        var net = invoice["NetPrice"]!.GetValue<double>();
        var lines = invoice["InvoiceLines"]!.AsArray();
        var percentage = invoice["Vat"]!["Percentage"]!.GetValue<double>();

        lines.Count.ShouldBe(2);
        ((decimal)total).ShouldBe(lines.Sum(l => (decimal)l!["Price"]!.GetValue<double>()), "TotalPrice is the sum of the line prices, excluding VAT");
        ((decimal)vat).ShouldBe(Math.Round((decimal)total * (decimal)percentage / 100m, 2, MidpointRounding.AwayFromZero));
        ((decimal)net).ShouldBe(Math.Round((decimal)total + (decimal)vat, 2), "the OpenTMS quirk: NetPrice includes VAT");
        net.ShouldBeGreaterThan(total);
        percentage.ShouldBeGreaterThan(0, "a sample with VAT shows the quirk");
    }

    [Theory]
    [MemberData(nameof(Purposes))]
    public void Every_formatted_number_agrees_with_its_number_in_Dutch_notation(ReportPurpose purpose)
    {
        var checkedPairs = 0;
        Walk(ReportDataCatalogue.SampleFor(purpose));

        if (purpose is ReportPurpose.Invoice)
        {
            checkedPairs.ShouldBeGreaterThanOrEqualTo(6, "the invoice's net, total and VAT amounts, both forms");
            var invoice = ReportDataCatalogue.SampleFor(purpose)["Entity"]!;
            invoice["CurrencyFormattedNetPrice"]!.GetValue<string>()
                .ShouldBe("€ " + invoice["NetPrice"]!.GetValue<double>().ToString("#,##0.00", Dutch));
        }

        void Walk(JsonNode? node)
        {
            switch (node)
            {
                case JsonObject obj:
                    foreach (var (name, value) in obj)
                    {
                        foreach (var prefix in new[] { "CurrencyFormatted", "Formatted" })
                        {
                            if (!name.StartsWith(prefix, StringComparison.Ordinal) || name.Length == prefix.Length)
                            {
                                continue;
                            }

                            if (obj[name[prefix.Length..]] is JsonValue number && number.GetValueKind() == JsonValueKind.Number && value is not null)
                            {
                                var expected = number.GetValue<double>().ToString("#,##0.00", Dutch);
                                if (prefix == "CurrencyFormatted")
                                {
                                    expected = "€ " + expected;
                                }

                                value.GetValue<string>().ShouldBe(expected, $"{purpose}: {name} against {name[prefix.Length..]} = {number.ToJsonString()}");
                                checkedPairs++;
                            }

                            break;
                        }

                        Walk(value);
                    }
                    break;
                case JsonArray array:
                    foreach (var element in array)
                    {
                        Walk(element);
                    }
                    break;
            }
        }
    }

    // ── The contract check ─────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Purposes))]
    public void Nulls_and_missing_fields_are_allowed(ReportPurpose purpose)
    {
        var payload = ReportDataCatalogue.SampleFor(purpose);
        payload.Remove("CompanyLogo");
        payload["Me"] = null;
        var entity = payload["Entity"]!.AsObject();
        foreach (var name in entity.Select(p => p.Key).Take(5).ToList())
        {
            entity[name] = null;
        }

        entity.Remove(entity.Last().Key);

        ReportDataCatalogue.Validate(purpose, payload).ShouldBeEmpty();
        ReportDataCatalogue.Validate(purpose, new JsonObject()).ShouldBeEmpty("an empty envelope has only missing fields");
    }

    [Fact]
    public void An_unknown_field_is_reported_with_its_concrete_path()
    {
        var payload = ReportDataCatalogue.SampleFor(ReportPurpose.Invoice);
        payload["Entity"]!["Bogus"] = 1;
        payload["Entity"]!["InvoiceLines"]![1]!["Extra"] = "x";
        payload["Surprise"] = true;

        var deviations = ReportDataCatalogue.Validate(ReportPurpose.Invoice, payload);

        deviations.Select(d => d.Path).ShouldBe(["Entity.Bogus", "Entity.InvoiceLines[1].Extra", "Surprise"], ignoreOrder: true);
        deviations.ShouldAllBe(d => d.Message.Contains("unknown field"));
    }

    [Fact]
    public void A_field_of_one_purpose_is_unknown_in_another()
    {
        var invoice = ReportDataCatalogue.SampleFor(ReportPurpose.Invoice);
        invoice["EntityType"] = ReportDataCatalogue.EntityTypeOf(ReportPurpose.PurchaseOrder);

        ReportDataCatalogue.Validate(ReportPurpose.PurchaseOrder, invoice).ShouldNotBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Purposes))]
    public void A_wrong_EntityType_is_reported(ReportPurpose purpose)
    {
        var payload = ReportDataCatalogue.SampleFor(purpose);
        payload["EntityType"] = "Something";

        var deviation = ReportDataCatalogue.Validate(purpose, payload).ShouldHaveSingleItem();
        deviation.Path.ShouldBe("EntityType");
        deviation.Message.ShouldContain(ReportDataCatalogue.EntityTypeOf(purpose));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("[]")]
    [InlineData("\"text\"")]
    [InlineData("42")]
    public void A_payload_that_is_not_an_object_is_reported_at_the_root(string? json)
    {
        var node = json == null ? null : JsonNode.Parse(json);

        ReportDataCatalogue.Validate(ReportPurpose.Invoice, node).ShouldHaveSingleItem().Path.ShouldBe("$");
    }

    public static TheoryData<ReportDataFieldType, string> WrongValues => new()
    {
        { ReportDataFieldType.String, "5" },
        { ReportDataFieldType.String, "true" },
        { ReportDataFieldType.Integer, "1.5" },
        { ReportDataFieldType.Integer, "\"7\"" },
        { ReportDataFieldType.Decimal, "\"1,50\"" },
        { ReportDataFieldType.Boolean, "\"true\"" },
        { ReportDataFieldType.Boolean, "1" },
        { ReportDataFieldType.Date, "\"2026-10-21T00:00:00\"" },
        { ReportDataFieldType.Date, "\"21-10-2026\"" },
        { ReportDataFieldType.Date, "\"2026-02-30\"" },
        { ReportDataFieldType.Date, "20261021" },
        { ReportDataFieldType.DateTime, "\"2026-10-21\"" },
        { ReportDataFieldType.DateTime, "\"21-10-2026 10:00\"" },
        { ReportDataFieldType.DateTime, "\"2026-10-21 10:00:00\"" },
        { ReportDataFieldType.DateTime, "\"2026-13-01T10:00:00\"" },
        { ReportDataFieldType.TimeSpan, "\"2:10\"" },
        { ReportDataFieldType.TimeSpan, "\"two hours\"" },
        { ReportDataFieldType.TimeSpan, "\"01:61:00\"" },
        { ReportDataFieldType.TimeSpan, "7200" },
        { ReportDataFieldType.Guid, "\"not-a-guid\"" },
        { ReportDataFieldType.Guid, "\"0f8fad5bd9cb469fa16570867728950e\"" },
        { ReportDataFieldType.Guid, "\"{0f8fad5b-d9cb-469f-a165-70867728950e}\"" },
        { ReportDataFieldType.Enum, "\"Open\"" },
        { ReportDataFieldType.Enum, "1.5" },
        { ReportDataFieldType.Object, "[]" },
        { ReportDataFieldType.Object, "\"x\"" },
        { ReportDataFieldType.Array, "{}" },
        { ReportDataFieldType.Array, "\"x\"" },
    };

    [Theory]
    [MemberData(nameof(WrongValues))]
    public void A_wrong_kind_or_format_is_reported_at_its_concrete_path(ReportDataFieldType type, string json)
    {
        var (purpose, field) = First(type);
        var payload = ReportDataCatalogue.SampleFor(purpose);
        var (parent, name, concrete) = Locate(payload, field.Path);
        parent[name] = JsonNode.Parse(json);

        var deviation = ReportDataCatalogue.Validate(purpose, payload).ShouldHaveSingleItem($"{purpose} {field.Path} = {json}");

        deviation.Path.ShouldBe(concrete);
        deviation.Message.ShouldStartWith("expected ");
    }

    public static TheoryData<ReportDataFieldType, string> RightValues => new()
    {
        { ReportDataFieldType.Integer, "-12" },
        { ReportDataFieldType.Decimal, "12" },
        { ReportDataFieldType.Decimal, "1250.75" },
        { ReportDataFieldType.Date, "\"2028-02-29\"" },
        { ReportDataFieldType.DateTime, "\"2026-10-21T10:15:00\"" },
        { ReportDataFieldType.DateTime, "\"2026-10-21T10:15:00.1234567\"" },
        { ReportDataFieldType.DateTime, "\"2026-10-21T10:15:00Z\"" },
        { ReportDataFieldType.DateTime, "\"2026-10-21T10:15:00+02:00\"" },
        { ReportDataFieldType.TimeSpan, "\"02:10:00\"" },
        { ReportDataFieldType.TimeSpan, "\"1.02:10:00\"" },
        { ReportDataFieldType.TimeSpan, "\"-00:30:00\"" },
        { ReportDataFieldType.Guid, "\"0f8fad5b-d9cb-469f-a165-70867728950e\"" },
        { ReportDataFieldType.Enum, "0" },
    };

    [Theory]
    [MemberData(nameof(RightValues))]
    public void A_value_in_the_documented_format_passes(ReportDataFieldType type, string json)
    {
        var (purpose, field) = First(type);
        var payload = ReportDataCatalogue.SampleFor(purpose);
        var (parent, name, _) = Locate(payload, field.Path);
        parent[name] = JsonNode.Parse(json);

        ReportDataCatalogue.Validate(purpose, payload).ShouldBeEmpty($"{purpose} {field.Path} = {json}");
    }

    [Fact]
    public void A_wrong_element_in_an_array_of_objects_is_reported_with_its_index()
    {
        var payload = ReportDataCatalogue.SampleFor(ReportPurpose.Invoice);
        payload["Entity"]!["InvoiceLines"]!.AsArray().Add(JsonValue.Create("not a line"));

        ReportDataCatalogue.Validate(ReportPurpose.Invoice, payload).ShouldHaveSingleItem().Path.ShouldBe("Entity.InvoiceLines[2]");
    }

    [Fact]
    public void An_unknown_purpose_has_no_catalogue()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => ReportDataCatalogue.SampleFor((ReportPurpose)99));
    }
}
