using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ClosedXML.Excel;
using OpenTms.Integrations.OrderIntake.Extraction;
using OpenTms.Integrations.OrderIntake.Profiles;
using OpenTms.Orders.Intake;
using Shouldly;
using Xunit;

namespace OpenTms.Integrations.OrderIntake;

/// <summary>
/// The deterministic import-profile executor (design 72 §4 "Rules") across the formats it reads, on small fixture documents
/// built in code and read by the production extractors: spreadsheet row groups and cell references (ClosedXML), CSV with a
/// quoted delimiter, XML with namespaces, normalised text with labels, regexes and text blocks; the transforms date, number,
/// map, split, join, upper; the provenance of every value (cell, path, line); validation and the refusals of a broken rule.
/// Risk class: High — the order chain (a profile that reads a wrong value creates a wrong order without anyone looking).
/// </summary>
public class ImportProfileExecutorTests
{
    private readonly ImportProfileExecutor _executor = new();

    private static FieldRule Field(string target, FieldSourceKind kind, string value, params FieldTransform[] transforms)
        => new() { Target = target, Source = new FieldSource { Kind = kind, Value = value }, Transforms = transforms.ToList() };

    private static FieldTransform Date(string format) => new() { Kind = FieldTransformKind.Date, Format = format };

    private static FieldTransform Number(string? culture = null) => new() { Kind = FieldTransformKind.Number, Culture = culture };

    private static ExtractedDocument Xlsx(Action<IXLWorksheet> fill, string sheet = "Orders")
    {
        using var workbook = new XLWorkbook();
        fill(workbook.AddWorksheet(sheet));
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return new XlsxDocumentExtractor().Extract(stream.ToArray(), DocumentFormat.Xlsx);
    }

    private static void Row(IXLWorksheet sheet, int row, params string[] values)
    {
        for (var i = 0; i < values.Length; i++)
        {
            sheet.Cell(row, i + 1).SetValue(values[i]);
        }
    }

    private static ExtractedDocument OrderSheet() => Xlsx(sheet =>
    {
        Row(sheet, 1, "Ordernr", "Datum", "Postcode laden", "Postcode lossen", "Gewicht", "Eenheid");
        Row(sheet, 2, "A-1", "04-11-2026", "1234AB", "5678CD", "1.250,5", "EUR-pallet");
        Row(sheet, 3, "A-1", "04-11-2026", "1234AB", "9999ZZ", "300", "Colli");
        Row(sheet, 4, "A-2", "05-11-2026", "4321BA", "8765DC", "75", "EUR-pallet");
    });

    private static ImportProfileRules RowGroupRules() => new()
    {
        Sheet = "Orders",
        HeaderRow = 1,
        Orders = new OrderSplitRule { Scope = new OrderScope { Kind = OrderScopeKind.RowGroup, GroupBy = ["Ordernr"] } },
        Lines = new LineSplitRule { Scope = new LineScope { Kind = LineScopeKind.Row } },
        Fields =
        [
            Field("order.customerReference", FieldSourceKind.Column, "Ordernr"),
            Field("stop[Pickup].date", FieldSourceKind.Column, "Datum", Date("dd-MM-yyyy")),
            Field("stop[Delivery].date", FieldSourceKind.Column, "Datum", Date("dd-MM-yyyy")),
            Field("stop[Pickup].address.postalCode", FieldSourceKind.Column, "Postcode laden"),
            Field("stop[Delivery].address.postalCode", FieldSourceKind.Column, "postcode LOSSEN"),
            Field("line.weightKg", FieldSourceKind.Column, "Gewicht", Number("nl-NL")),
            Field("line.transportUnitName", FieldSourceKind.Column, "Eenheid",
                new FieldTransform { Kind = FieldTransformKind.Map, Map = new Dictionary<string, string> { ["eur-pallet"] = "Europallet" } })
        ]
    };

    [Fact]
    public void A_spreadsheet_splits_into_orders_by_row_group_with_a_line_per_row_and_cell_provenance()
    {
        var run = _executor.Run(RowGroupRules(), OrderSheet());

        run.Errors.ShouldBeEmpty();
        run.Intakes.Count.ShouldBe(2);

        var first = run.Intakes[0];
        first.Payload.CustomerReference.ShouldBe("A-1");
        first.Payload.Lines.Count.ShouldBe(2);
        first.Payload.Lines[0].WeightKg.ShouldBe(1250.5);
        first.Payload.Lines[0].TransportUnitName.ShouldBe("Europallet", "map is case-insensitive on the source text");
        first.Payload.Lines[1].TransportUnitName.ShouldBe("Colli", "an unmapped value passes unchanged");
        first.Payload.Lines[1].WeightKg.ShouldBe(300);
        first.Payload.Lines[0].Stops.Select(s => s.Role).ShouldBe([IntakeStopRole.Pickup, IntakeStopRole.Delivery], "no stop rule: one Pickup and one Delivery (assumption 3)");
        first.Payload.Lines[0].Stops[0].Date.ShouldBe("2026-11-04");
        first.Payload.Lines[0].Stops[0].Address.PostalCode.ShouldBe("1234AB");
        first.Payload.Lines[1].Stops[1].Address.PostalCode.ShouldBe("9999ZZ");

        first.Provenance["order.customerReference"].Kind.ShouldBe(SourceLocationKind.Cell);
        first.Provenance["order.customerReference"].Ref.ShouldBe("A2");
        first.Provenance["lines[1].weightKg"].Ref.ShouldBe("E3");
        first.Provenance["lines[1].weightKg"].Snippet.ShouldBe("300");
        first.Provenance["lines[0].stops[1].address.postalCode"].Ref.ShouldBe("D2");

        var second = run.Intakes[1];
        second.Payload.CustomerReference.ShouldBe("A-2");
        second.Payload.Lines.ShouldHaveSingleItem().Stops[1].Address.PostalCode.ShouldBe("8765DC");
        second.Provenance["lines[0].weightKg"].Ref.ShouldBe("E4");
    }

    [Fact]
    public void A_continuation_row_without_the_group_key_belongs_to_the_order_above_it()
    {
        var sheet = Xlsx(s =>
        {
            Row(s, 1, "Ordernr", "Datum", "Postcode laden", "Postcode lossen", "Gewicht", "Eenheid");
            Row(s, 2, "A-1", "04-11-2026", "1234AB", "5678CD", "10", "EUR-pallet");
            Row(s, 3, "", "04-11-2026", "1234AB", "5678CD", "20", "EUR-pallet");
        });

        var run = _executor.Run(RowGroupRules(), sheet);

        run.Errors.ShouldBeEmpty();
        run.Intakes.ShouldHaveSingleItem().Payload.Lines.Select(l => l.WeightKg).ShouldBe([10d, 20d]);
    }

    [Fact]
    public void A_form_like_spreadsheet_is_read_by_cell_reference()
    {
        var sheet = Xlsx(s =>
        {
            Row(s, 1, "Transportopdracht");
            Row(s, 2, "Referentie", "PO-55");
            Row(s, 3, "Laadpostcode", "1234 AB");
            Row(s, 4, "Lospostcode", "5678 CD");
            Row(s, 5, "Datum", "2026-11-04");
        }, sheet: "Form");
        var rules = new ImportProfileRules
        {
            Fields =
            [
                Field("order.customerReference", FieldSourceKind.Cell, "B2"),
                Field("order.orderDate", FieldSourceKind.Cell, "b5", Date("yyyy-MM-dd")),
                Field("stop[Pickup].address.postalCode", FieldSourceKind.Cell, "B3", new FieldTransform { Kind = FieldTransformKind.Upper }),
                Field("stop[Delivery].address.postalCode", FieldSourceKind.Cell, "B4")
            ]
        };

        var run = _executor.Run(rules, sheet);

        run.Errors.ShouldBeEmpty();
        var intake = run.Intakes.ShouldHaveSingleItem();
        intake.Payload.CustomerReference.ShouldBe("PO-55");
        intake.Payload.OrderDate.ShouldBe("2026-11-04");
        intake.Payload.Lines.ShouldHaveSingleItem().Stops[0].Address.PostalCode.ShouldBe("1234 AB");
        intake.Provenance["order.customerReference"].Ref.ShouldBe("B2");
        intake.Provenance["order.orderDate"].Ref.ShouldBe("B5");
    }

    [Fact]
    public void A_csv_with_a_quoted_delimiter_is_read_by_column()
    {
        var csv = "Ref;Van;Naar;Datum;Gewicht;Opmerking\r\nR-1;1234AB;5678CD;04-11-2026;100;\"achterom; bellen\"\r\n";
        var document = new CsvDocumentExtractor().Extract(Encoding.UTF8.GetBytes(csv), DocumentFormat.Csv);
        var rules = new ImportProfileRules
        {
            Orders = new OrderSplitRule { Scope = new OrderScope { Kind = OrderScopeKind.RowGroup, GroupBy = ["Ref"] } },
            Fields =
            [
                Field("order.customerReference", FieldSourceKind.Column, "Ref"),
                Field("order.remarks", FieldSourceKind.Column, "Opmerking"),
                Field("stop[Pickup].address.postalCode", FieldSourceKind.Column, "Van"),
                Field("stop[Delivery].address.postalCode", FieldSourceKind.Column, "Naar"),
                Field("stop[Pickup].date", FieldSourceKind.Column, "Datum", Date("dd-MM-yyyy")),
                Field("stop[Delivery].date", FieldSourceKind.Column, "Datum", Date("dd-MM-yyyy")),
                Field("line.weightKg", FieldSourceKind.Column, "Gewicht", Number())
            ]
        };

        var run = _executor.Run(rules, document);

        run.Errors.ShouldBeEmpty();
        var intake = run.Intakes.ShouldHaveSingleItem();
        intake.Payload.Remarks.ShouldBe("achterom; bellen");
        intake.Payload.Lines.ShouldHaveSingleItem().WeightKg.ShouldBe(100);
        intake.Provenance["order.remarks"].Ref.ShouldBe("F2");
    }

    private const string NamespacedXml = """
        <?xml version="1.0" encoding="utf-8"?>
        <o:Orders xmlns:o="urn:test:orders">
          <o:Order>
            <o:Reference>X-1</o:Reference>
            <o:Pickup><o:Postcode>1234AB</o:Postcode><o:Date>2026-11-04</o:Date></o:Pickup>
            <o:Delivery><o:Postcode>5678CD</o:Postcode></o:Delivery>
            <o:Lines>
              <o:Line><o:Weight>100</o:Weight><o:Unit>EUR-pallet</o:Unit></o:Line>
              <o:Line><o:Weight>250.5</o:Weight><o:Unit>Colli</o:Unit></o:Line>
            </o:Lines>
          </o:Order>
          <o:Order>
            <o:Reference>X-2</o:Reference>
            <o:Pickup><o:Postcode>1111AA</o:Postcode><o:Date>2026-11-05</o:Date></o:Pickup>
            <o:Delivery><o:Postcode>2222BB</o:Postcode></o:Delivery>
            <o:Lines><o:Line><o:Weight>7</o:Weight><o:Unit>Colli</o:Unit></o:Line></o:Lines>
          </o:Order>
        </o:Orders>
        """;

    private static ImportProfileRules XmlRules(string prefix = "o") => new()
    {
        Namespaces = new Dictionary<string, string> { [prefix] = "urn:test:orders" },
        Orders = new OrderSplitRule { Scope = new OrderScope { Kind = OrderScopeKind.Xpath, Value = $"/{prefix}:Orders/{prefix}:Order" } },
        Lines = new LineSplitRule { Scope = new LineScope { Kind = LineScopeKind.Xpath, Value = $"{prefix}:Lines/{prefix}:Line" } },
        Fields =
        [
            Field("order.customerReference", FieldSourceKind.Xpath, $"{prefix}:Reference"),
            Field("line.weightKg", FieldSourceKind.Xpath, $"{prefix}:Weight", Number()),
            Field("line.transportUnitName", FieldSourceKind.Xpath, $"{prefix}:Unit"),
            Field("stop[Pickup].address.postalCode", FieldSourceKind.Xpath, $"../../{prefix}:Pickup/{prefix}:Postcode"),
            Field("stop[Pickup].date", FieldSourceKind.Xpath, $"../../{prefix}:Pickup/{prefix}:Date"),
            Field("stop[Delivery].date", FieldSourceKind.Xpath, $"../../{prefix}:Pickup/{prefix}:Date"),
            Field("stop[Delivery].address.postalCode", FieldSourceKind.Xpath, $"../../{prefix}:Delivery/{prefix}:Postcode")
        ]
    };

    [Fact]
    public void An_xml_with_namespaces_is_read_by_xpath_with_the_rules_prefixes_and_path_provenance()
    {
        var document = new XmlDocumentExtractor().Extract(Encoding.UTF8.GetBytes(NamespacedXml), DocumentFormat.Xml);

        /* The prefix is the rules' own: a different prefix bound to the same URI reads the same document. */
        foreach (var prefix in new[] { "o", "ord" })
        {
            var run = _executor.Run(XmlRules(prefix), document);

            run.Errors.ShouldBeEmpty(prefix);
            run.Intakes.Select(i => i.Payload.CustomerReference).ShouldBe(["X-1", "X-2"], prefix);
            var first = run.Intakes[0];
            first.Payload.Lines.Select(l => l.WeightKg).ShouldBe([100d, 250.5d]);
            first.Payload.Lines[1].Stops[0].Address.PostalCode.ShouldBe("1234AB");
            first.Payload.Lines[1].Stops[0].Date.ShouldBe("2026-11-04");
            first.Payload.Lines[1].Stops[1].Address.PostalCode.ShouldBe("5678CD");
            first.Provenance["lines[1].weightKg"].Kind.ShouldBe(SourceLocationKind.Path);
            first.Provenance["lines[1].weightKg"].Ref.ShouldBe("/Orders/Order[1]/Lines/Line[2]/Weight");
            run.Intakes[1].Provenance["order.customerReference"].Ref.ShouldBe("/Orders/Order[2]/Reference");
        }
    }

    [Fact]
    public void An_xml_rule_without_the_namespace_reads_nothing_and_an_invalid_xpath_is_refused()
    {
        var document = new XmlDocumentExtractor().Extract(Encoding.UTF8.GetBytes(NamespacedXml), DocumentFormat.Xml);

        var unbound = XmlRules();
        unbound.Namespaces = null;
        _executor.Run(unbound, document, validate: false).Errors.ShouldNotBeEmpty("the prefix is unbound without the rules' namespaces");

        var broken = XmlRules();
        broken.Fields[0].Source.Value = "o:Reference[";
        _executor.Run(broken, document, validate: false).Errors.ShouldContain(e => e.Code == "invalidXpath");
    }

    private static ExtractedDocument Text(string text) => TextDocumentExtractor.FromText(text, DocumentFormat.Text);

    [Fact]
    public void Normalised_text_is_read_by_label_regex_and_transforms_with_line_provenance()
    {
        var document = Text(string.Join('\n',
            "Transportopdracht",
            "Onze referentie: PO-778  Datum: 04-11-2026",
            "Laadpostcode: 1234 AB",
            "Losplaats",
            "ROTTERDAM",
            "Gewicht: 1200 kg / 3 pallets",
            "Opmerking: bellen",
            "Opmerking: achterom"));
        var rules = new ImportProfileRules
        {
            Fields =
            [
                Field("order.customerReference", FieldSourceKind.Label, "Onze referentie:"),
                Field("order.orderDate", FieldSourceKind.Label, "Datum:", Date("dd-MM-yyyy")),
                Field("stop[Pickup].address.postalCode", FieldSourceKind.Regex, @"Laadpostcode:\s*(\d{4}\s?[A-Z]{2})"),
                Field("stop[Delivery].address.city", FieldSourceKind.Label, "Losplaats",
                    new FieldTransform { Kind = FieldTransformKind.Trim }),
                Field("line.weightKg", FieldSourceKind.Label, "Gewicht:",
                    new FieldTransform { Kind = FieldTransformKind.Split, Separator = "/", Index = 0 }, Number()),
                Field("line.quantity", FieldSourceKind.Regex, @"(\d+) pallets"),
                new FieldRule
                {
                    Target = "order.remarks",
                    Source = new FieldSource { Kind = FieldSourceKind.Label, Value = "Opmerking:", Occurrence = 0 },
                    Transforms = [new FieldTransform { Kind = FieldTransformKind.Join, Separator = " | " }]
                }
            ]
        };

        var run = _executor.Run(rules, document);

        run.Errors.ShouldBeEmpty();
        var intake = run.Intakes.ShouldHaveSingleItem();
        intake.Payload.CustomerReference.ShouldBe("PO-778", "a label's value ends at two spaces");
        intake.Payload.OrderDate.ShouldBe("2026-11-04");
        var line = intake.Payload.Lines.ShouldHaveSingleItem();
        line.Stops[0].Address.PostalCode.ShouldBe("1234 AB");
        line.Stops[1].Address.City.ShouldBe("ROTTERDAM", "a label that ends its line takes the next line");
        line.WeightKg.ShouldBe(1200, "split keeps the first part, number drops the unit");
        line.Quantity.ShouldBe(3);
        intake.Payload.Remarks.ShouldBe("bellen | achterom", "occurrence 0 keeps every occurrence for join");

        intake.Provenance["order.customerReference"].Kind.ShouldBe(SourceLocationKind.Line);
        intake.Provenance["order.customerReference"].Ref.ShouldBe("line 2");
        intake.Provenance["lines[0].stops[0].address.postalCode"].Ref.ShouldBe("line 3");
        intake.Provenance["lines[0].stops[1].address.city"].Ref.ShouldBe("line 5");
        intake.Provenance["lines[0].weightKg"].Snippet.ShouldBe("1200 kg / 3 pallets", "the snippet is the raw text that was read");
    }

    [Fact]
    public void A_text_document_splits_into_orders_and_order_fields_fall_back_to_the_preamble()
    {
        var document = Text(string.Join('\n',
            "Klant: ACME Logistics",
            "Order 1001",
            "Laadpostcode: 1234AB",
            "Order 1002",
            "Laadpostcode: 5678CD"));
        var rules = new ImportProfileRules
        {
            Orders = new OrderSplitRule { Scope = new OrderScope { Kind = OrderScopeKind.TextSplit, Value = @"^Order \d+" } },
            Fields =
            [
                Field("order.customerReference", FieldSourceKind.Regex, @"^Order (\d+)"),
                Field("order.remarks", FieldSourceKind.Label, "Klant:"),
                Field("stop[Pickup].address.postalCode", FieldSourceKind.Label, "Laadpostcode:")
            ]
        };

        var run = _executor.Run(rules, document, validate: false);

        run.Errors.ShouldBeEmpty();
        run.Intakes.Select(i => i.Payload.CustomerReference).ShouldBe(["1001", "1002"]);
        run.Intakes.Select(i => i.Payload.Remarks).ShouldBe(["ACME Logistics", "ACME Logistics"]);
        run.Intakes.Select(i => i.Payload.Lines[0].Stops[0].Address.PostalCode).ShouldBe(["1234AB", "5678CD"]);
    }

    [Fact]
    public void A_reference_target_and_a_constant_are_read()
    {
        var rules = new ImportProfileRules
        {
            Fields =
            [
                Field("order.references[CMR]", FieldSourceKind.Regex, @"CMR\s+(\S+)"),
                Field("order.orderTypeName", FieldSourceKind.Constant, "Distributie")
            ]
        };

        var run = _executor.Run(rules, Text("Vrachtbrief CMR 556677"), validate: false);

        run.Errors.ShouldBeEmpty();
        var intake = run.Intakes.ShouldHaveSingleItem();
        intake.Payload.References.ShouldHaveSingleItem().Type.ShouldBe("CMR");
        intake.Payload.References[0].Value.ShouldBe("556677");
        intake.Payload.OrderTypeName.ShouldBe("Distributie");
        intake.Provenance["order.orderTypeName"].Kind.ShouldBe(SourceLocationKind.Constant);
    }

    [Fact]
    public void Validation_requires_a_reference_a_line_and_both_stops_with_a_place_and_a_date()
    {
        /* The same sheet without the delivery postal code: layout drift on a matched profile (design 72 "Validation"). */
        var rules = RowGroupRules();
        rules.Fields.RemoveAll(f => f.Target == "stop[Delivery].address.postalCode");

        var run = _executor.Run(rules, OrderSheet());

        run.IsValid.ShouldBeFalse();
        run.Errors.ShouldContain(e => e.Code == ImportProfileValidator.Required);
        _executor.Run(rules, OrderSheet(), validate: false).IsValid.ShouldBeTrue("without validation the run itself succeeds");
    }

    [Fact]
    public void A_stop_without_its_own_date_passes_validation_when_the_intake_has_an_order_date()
    {
        /* Build note 7. */
        var intake = new OrderIntakeDto
        {
            CustomerReference = "R",
            OrderDate = "2026-11-04",
            Lines =
            [
                new OrderIntakeLineDto
                {
                    Stops =
                    [
                        new OrderIntakeStopDto { Role = IntakeStopRole.Pickup, Address = new OrderIntakeAddressDto { PostalCode = "1234AB" } },
                        new OrderIntakeStopDto { Role = IntakeStopRole.Delivery, Address = new OrderIntakeAddressDto { City = "Utrecht" } }
                    ]
                }
            ]
        };

        ImportProfileValidator.Validate([intake]).ShouldBeEmpty();
        intake.OrderDate = null;
        ImportProfileValidator.Validate([intake]).ShouldNotBeEmpty();
        ImportProfileValidator.Validate([]).ShouldContain(e => e.Code == ImportProfileValidator.NoIntakes);
    }

    [Fact]
    public void Broken_rules_are_refused_with_a_code_never_an_exception()
    {
        var sheet = OrderSheet();

        var missingColumn = RowGroupRules();
        missingColumn.Fields[0].Source.Value = "Bestaat niet";
        _executor.Run(missingColumn, sheet).Errors.ShouldContain(e => e.Code == "columnNotFound");

        var missingSheet = RowGroupRules();
        missingSheet.Sheet = "Andere";
        _executor.Run(missingSheet, sheet).Errors.ShouldContain(e => e.Code == "sheetNotFound");

        var badDate = RowGroupRules();
        badDate.Fields[1].Transforms = [Date("yyyy/MM/dd")];
        _executor.Run(badDate, sheet).Errors.ShouldContain(e => e.Code == IntakeValuePaths.InvalidDate);

        var unknownTarget = RowGroupRules();
        unknownTarget.Fields.Add(Field("order.doesNotExist", FieldSourceKind.Constant, "x"));
        _executor.Run(unknownTarget, sheet, validate: false).Errors.ShouldContain(e => e.Code == IntakeValuePaths.UnknownPath);

        var badRegex = new ImportProfileRules { Fields = [Field("order.customerReference", FieldSourceKind.Regex, "(unclosed")] };
        _executor.Run(badRegex, Text("x"), validate: false).Errors.ShouldContain(e => e.Code == "invalidRegex");

        var cellOnText = new ImportProfileRules { Fields = [Field("order.customerReference", FieldSourceKind.Cell, "A1")] };
        _executor.Run(cellOnText, Text("x"), validate: false).Errors.ShouldContain(e => e.Code == "cellNeedsTable");

        var negative = RowGroupRules();
        negative.Fields.Add(Field("line.colli", FieldSourceKind.Constant, "-3"));
        _executor.Run(negative, sheet, validate: false).Errors.ShouldContain(e => e.Code == IntakeValuePaths.NegativeNumber);
    }

    [Fact]
    public void A_catastrophic_regex_times_out_into_an_error()
    {
        var rules = new ImportProfileRules { Fields = [Field("order.customerReference", FieldSourceKind.Regex, @"^(a+)+$")] };

        var run = _executor.Run(rules, Text(new string('a', 40) + "!"), validate: false);

        run.Errors.ShouldContain(e => e.Code == "regexTimeout");
    }

    [Fact]
    public void Rules_round_trip_through_their_json_and_the_ai_parser_refuses_garbage()
    {
        var rules = RowGroupRules();
        var json = ImportProfileRules.ToJson(rules);

        var back = ImportProfileRules.FromJson(json);
        back.Fields.Count.ShouldBe(rules.Fields.Count);
        _executor.Run(back, OrderSheet()).Intakes.Count.ShouldBe(2);

        OpenTms.Integrations.OrderIntake.Ai.IntakeLearner.ParseRules(json).ShouldNotBeNull();
        OpenTms.Integrations.OrderIntake.Ai.IntakeLearner.ParseRules("").ShouldBeNull();
        OpenTms.Integrations.OrderIntake.Ai.IntakeLearner.ParseRules("{not json").ShouldBeNull();
        OpenTms.Integrations.OrderIntake.Ai.IntakeLearner.ParseRules("{\"fields\":[]}").ShouldBeNull("rules without fields are no profile");
    }

    [Fact]
    public void The_comparison_normalises_numbers_dates_and_whitespace_and_names_every_difference()
    {
        OrderIntakeDto Make(string reference, double weight) => new()
        {
            CustomerReference = reference,
            Lines = [new OrderIntakeLineDto { WeightKg = weight, Stops = [new OrderIntakeStopDto { Role = IntakeStopRole.Pickup, Address = new OrderIntakeAddressDto { City = "Utrecht" } }] }]
        };

        IntakeComparison.Compare([Make("PO  1", 100)], [Make("PO 1", 100.0)], truncated: false).ShouldBeEmpty();
        var differences = IntakeComparison.Compare([Make("PO 1", 100)], [Make("PO 2", 99)], truncated: false);
        differences.Select(d => d.Path).ShouldBe(["intakes[0].lines[0].weightKg", "intakes[0].order.customerReference"], ignoreOrder: true);
        IntakeComparison.Compare([Make("PO 1", 100)], [Make("PO 1", 100), Make("PO 2", 1)], truncated: false).ShouldNotBeEmpty("a different intake count is a difference");
    }
}

/// <summary>
/// Number parsing of the transforms (design 72 §4 "transforms ... number ... culture"). Risk class: High — a weight read as a
/// thousand times too much or too little.
/// </summary>
public class ValueParserTests
{
    [Theory]
    [InlineData("1250.5", null, "1250.5")]
    [InlineData("1,250.5", null, "1250.5")]
    [InlineData("1.250,5", "nl-NL", "1250.5")]
    [InlineData("12,5", "nl-NL", "12.5")]
    [InlineData(" 3 ", null, "3")]
    public void Numbers_read_in_the_given_culture(string text, string? culture, string expected)
    {
        ValueParsers.Number(text, culture).ShouldBe((expected, (string?)null));
    }

    [Fact]
    public void Without_a_culture_a_dutch_number_falls_back_to_nl_NL_as_documented()
    {
        /* ValueParsers.Number's own contract: "culture null means invariant, then nl-NL when that fails". AI-proposed rules
         * often leave the culture out; a Dutch "1.250,5" must not fail the run. */
        ValueParsers.Number("1.250,5", null).ShouldBe(("1250.5", (string?)null));
    }

    [Theory]
    [InlineData("04-11-2026", null, "2026-11-04")]
    [InlineData("2026-11-04", null, "2026-11-04")]
    [InlineData("4/11/2026", null, "2026-11-04")]
    [InlineData("20261104", null, "2026-11-04")]
    [InlineData("04.11.26", "dd.MM.yy", "2026-11-04")]
    public void Dates_become_yyyy_MM_dd(string text, string? format, string expected)
    {
        ValueParsers.Date(text, format, null).ShouldBe((expected, (string?)null));
    }

    [Fact]
    public void Garbage_is_an_error_code_never_a_value()
    {
        ValueParsers.Number("twaalf", null).Error.ShouldBe(IntakeValuePaths.InvalidNumber);
        ValueParsers.Date("morgen", null, null).Error.ShouldBe(IntakeValuePaths.InvalidDate);
        ValueParsers.Time("25:99", null).Error.ShouldBe(IntakeValuePaths.InvalidTime);
        ValueParsers.Time("8.30", null).Value.ShouldBe("08:30");
    }
}
