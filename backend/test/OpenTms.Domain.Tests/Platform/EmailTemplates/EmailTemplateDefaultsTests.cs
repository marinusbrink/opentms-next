using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Scriban;
using Scriban.Runtime;
using Shouldly;
using Xunit;

namespace OpenTms.Platform.EmailTemplates;

/// <summary>
/// EmailTemplateDefaults (docs/designs/49-platform-master-data.md, "Tags and default content", PO 2026-10-20): the
/// eighteen default bodies are embedded in the Domain.Shared assembly itself (WithCulture=false, no satellite
/// assembly), non-empty, parse with Scriban, use only catalogued tags of their purpose (loop variables mapped to their
/// collection) and render with Scriban against a stub model built from the catalogue; the subjects (localization texts)
/// likewise; the sender address per purpose; the culture resolution table. Risk class: Low (texts) — unit tests.
/// </summary>
public class EmailTemplateDefaultsTests
{
    private const string ResourcePrefix = "OpenTms.Platform.EmailTemplates.Defaults.";

    private static readonly Regex TagExpression = new("\\{\\{(.*?)\\}\\}", RegexOptions.Singleline | RegexOptions.CultureInvariant);
    private static readonly Regex ForHeader = new("^for\\s+([a-z][a-z0-9_]*)\\s+in\\s+([a-z][a-z0-9_]*(\\.[a-z][a-z0-9_]*)*)$", RegexOptions.CultureInvariant);
    private static readonly Regex MemberPath = new("^[a-z][a-z0-9_]*(\\.[a-z][a-z0-9_]*)*$", RegexOptions.CultureInvariant);

    private static readonly EmailTemplatePurpose[] All = Enum.GetValues<EmailTemplatePurpose>();

    public static IEnumerable<object[]> PurposesAndCultures()
        => All.SelectMany(p => EmailTemplateDefaults.Cultures.Select(c => new object[] { p, c }));

    public static IEnumerable<object[]> Purposes() => All.Select(p => new object[] { p });

    // ── the embedded resources ────────────────────────────────────────────────

    [Fact]
    public void All_eighteen_default_bodies_are_embedded_in_the_main_assembly_under_their_logical_name()
    {
        var names = typeof(EmailTemplateDefaults).Assembly.GetManifestResourceNames().Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal)).ToList();
        var expected = All.SelectMany(p => EmailTemplateDefaults.Cultures.Select(c => $"{ResourcePrefix}{p}.{c}.html")).ToList();

        expected.Count.ShouldBe(18);
        names.OrderBy(n => n, StringComparer.Ordinal).ShouldBe(expected.OrderBy(n => n, StringComparer.Ordinal), "nine purposes × nl/en, nothing more");
    }

    [Theory]
    [MemberData(nameof(PurposesAndCultures))]
    public void The_body_is_the_trimmed_resource_non_empty_and_HTML(EmailTemplatePurpose purpose, string culture)
    {
        var body = EmailTemplateDefaults.HtmlBodyFor(purpose, culture);

        using var stream = typeof(EmailTemplateDefaults).Assembly.GetManifestResourceStream($"{ResourcePrefix}{purpose}.{culture}.html");
        stream.ShouldNotBeNull();
        using var reader = new StreamReader(stream);
        var resource = reader.ReadToEnd();

        body.ShouldNotBeNullOrWhiteSpace();
        body.ShouldBe(resource.Trim());
        body.ShouldStartWith("<");
        body.ShouldEndWith(">");
        body.ShouldContain("{{ company.name }}", Case.Sensitive, "every default names the own company");
        if (purpose != EmailTemplatePurpose.EtaNotification)
        {
            body.ShouldContain("{{ user.full_name }}", Case.Sensitive, "every default but the automatic ETA notification signs with the sending user");
        }
    }

    [Fact]
    public void A_culture_the_defaults_do_not_exist_in_is_refused_not_silently_empty()
    {
        Should.Throw<InvalidOperationException>(() => EmailTemplateDefaults.HtmlBodyFor(EmailTemplatePurpose.Invoice, "de"));
    }

    [Fact]
    public void The_body_is_read_once_and_cached()
    {
        var first = EmailTemplateDefaults.HtmlBodyFor(EmailTemplatePurpose.Invoice, "en");
        var second = EmailTemplateDefaults.HtmlBodyFor(EmailTemplatePurpose.Invoice, "en");
        ReferenceEquals(first, second).ShouldBeTrue();
    }

    // ── Scriban: parse, catalogued tags only, render ──────────────────────────

    [Theory]
    [MemberData(nameof(PurposesAndCultures))]
    public void The_body_and_the_subject_parse_with_Scriban(EmailTemplatePurpose purpose, string culture)
    {
        foreach (var (field, text) in Texts(purpose, culture))
        {
            var template = Template.Parse(text);
            template.HasErrors.ShouldBeFalse($"{purpose}.{culture} {field}: {string.Join("; ", template.Messages)}");
        }
    }

    [Theory]
    [MemberData(nameof(PurposesAndCultures))]
    public void Every_tag_in_the_body_and_the_subject_is_a_catalogued_path_of_the_purpose(EmailTemplatePurpose purpose, string culture)
    {
        var tags = EmailTemplateTagCatalogue.TagsFor(purpose).ToDictionary(t => t.Path, t => t);

        foreach (var (field, text) in Texts(purpose, culture))
        {
            var used = UsedPaths(text, purpose, $"{purpose}.{culture} {field}");
            if (field == "htmlBody")
            {
                used.ShouldNotBeEmpty($"{purpose}.{culture} {field} uses tags");
            }
            foreach (var path in used)
            {
                tags.ShouldContainKey(path, $"{purpose}.{culture} {field} uses '{path}', which the catalogue does not list for {purpose}");
                tags[path].Kind.ShouldBe(EmailTemplateTagKind.Value, $"'{path}' is used as a value in {purpose}.{culture} {field}");
            }
        }
    }

    [Theory]
    [MemberData(nameof(PurposesAndCultures))]
    public void The_body_and_the_subject_render_every_tag_they_use_against_a_model_shaped_by_the_catalogue(EmailTemplatePurpose purpose, string culture)
    {
        foreach (var (field, text) in Texts(purpose, culture))
        {
            var template = Template.Parse(text);
            template.HasErrors.ShouldBeFalse();

            var context = new TemplateContext { StrictVariables = true };
            context.PushGlobal(StubModelFor(purpose));
            var output = template.Render(context);

            output.ShouldNotContain("{{", Case.Sensitive, $"{purpose}.{culture} {field}");
            output.ShouldNotContain("}}", Case.Sensitive, $"{purpose}.{culture} {field}");
            foreach (var path in UsedPaths(text, purpose, $"{purpose}.{culture} {field}"))
            {
                output.ShouldContain(Marker(path), Case.Sensitive, $"{purpose}.{culture} {field} renders '{path}'");
            }
        }
    }

    [Fact]
    public void The_defaults_that_list_lines_loop_over_the_catalogued_collection_and_render_one_row_per_item()
    {
        /* The OpenTMS freight table (ETA notification) and the pro forma order table became loop blocks; the stub holds one item, so one row. */
        var eta = Render(EmailTemplatePurpose.EtaNotification, "en");
        eta.ShouldContain(Marker("freight.product"));
        Regex.Matches(eta, Regex.Escape(Marker("freight.product"))).Count.ShouldBe(1, "one item in the stub, one row");

        var proforma = Render(EmailTemplatePurpose.ProformaOverview, "nl");
        proforma.ShouldContain(Marker("proforma.orders.number"));
        proforma.ShouldContain(Marker("proforma.orders.amount"));
        Regex.Matches(proforma, Regex.Escape(Marker("proforma.orders.number"))).Count.ShouldBe(1, "one item in the stub, one row");
    }

    // ── sender address, subject keys, localization keys ───────────────────────

    [Theory]
    [InlineData(EmailTemplatePurpose.TransportOrder, "{{ company.email }}")]
    [InlineData(EmailTemplatePurpose.TransportFile, "{{ company.email }}")]
    [InlineData(EmailTemplatePurpose.Invoice, "{{ company.invoice_email }}")]
    [InlineData(EmailTemplatePurpose.InvoiceReminder, "{{ company.invoice_email }}")]
    [InlineData(EmailTemplatePurpose.PurchaseOrder, "{{ company.email }}")]
    [InlineData(EmailTemplatePurpose.EtaNotification, "{{ company.email }}")]
    [InlineData(EmailTemplatePurpose.DriverInstructions, "{{ company.email }}")]
    [InlineData(EmailTemplatePurpose.DigitalArchive, "{{ company.email }}")]
    [InlineData(EmailTemplatePurpose.ProformaOverview, "{{ company.invoice_email }}")]
    public void The_sender_address_is_the_invoice_address_for_the_invoice_purposes_else_the_company_address(EmailTemplatePurpose purpose, string expected)
    {
        EmailTemplateDefaults.SenderAddressFor(purpose).ShouldBe(expected);
        EmailTemplateTagCatalogue.TagsFor(purpose).Select(t => t.Snippet).ShouldContain(expected, "the sender address is a catalogued tag of every purpose");
    }

    [Theory]
    [MemberData(nameof(Purposes))]
    public void The_subject_key_is_the_purpose_name_under_DefaultSubject(EmailTemplatePurpose purpose)
    {
        EmailTemplateDefaults.SubjectKeyOf(purpose).ShouldBe("Platform:DefaultSubject." + purpose);
    }

    [Fact]
    public void AllLocalizationKeys_is_one_subject_key_per_purpose()
    {
        EmailTemplateDefaults.AllLocalizationKeys().ShouldBe(All.Select(p => "Platform:DefaultSubject." + p));
        EmailTemplateDefaults.AllLocalizationKeys().Count().ShouldBe(9);
    }

    [Fact]
    public void The_defaults_exist_in_nl_and_en_and_default_to_en()
    {
        EmailTemplateDefaults.Cultures.ShouldBe(["nl", "en"]);
        EmailTemplateDefaults.DefaultCulture.ShouldBe("en");
    }

    // ── ResolveCulture ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(" NL ", "en-US", "nl")]
    [InlineData("EN", "nl-NL", "en")]
    [InlineData("nl", "nl-NL", "nl")]
    [InlineData(null, "nl-NL", "nl")]
    [InlineData(null, "nl", "nl")]
    [InlineData(null, "en-GB", "en")]
    [InlineData("", "de-DE", "en")]
    [InlineData("   ", "de-DE", "en")]
    [InlineData("fr", "de-DE", "en")]
    [InlineData("fr", "en-US", "en")]
    [InlineData("fr", "nl-NL", "nl")]
    [InlineData("de", "nl-BE", "nl")]
    [InlineData(null, "", "en")]
    public void ResolveCulture_takes_nl_or_en_trimmed_any_case_else_the_UI_language_when_supported_else_en(string? culture, string uiCulture, string expected)
    {
        /* "" is the invariant culture (two-letter language "iv"). */
        EmailTemplateDefaults.ResolveCulture(culture, CultureInfo.GetCultureInfo(uiCulture)).ShouldBe(expected);
    }

    [Theory]
    [InlineData("nl-NL", "nl")]
    [InlineData("en-US", "en")]
    [InlineData("de-DE", "en")]
    public void ResolveCulture_without_a_UI_culture_uses_the_current_UI_culture(string current, string expected)
    {
        using var _ = new CultureScope(current);
        EmailTemplateDefaults.ResolveCulture(null).ShouldBe(expected);
        EmailTemplateDefaults.ResolveCulture("xx").ShouldBe(expected);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static string Marker(string path) => "<" + path + ">";

    private static string Render(EmailTemplatePurpose purpose, string culture)
    {
        var context = new TemplateContext { StrictVariables = true };
        context.PushGlobal(StubModelFor(purpose));
        return Template.Parse(EmailTemplateDefaults.HtmlBodyFor(purpose, culture)).Render(context);
    }

    private static IEnumerable<(string Field, string Text)> Texts(EmailTemplatePurpose purpose, string culture)
    {
        yield return ("htmlBody", EmailTemplateDefaults.HtmlBodyFor(purpose, culture));
        yield return ("subject", Subjects(culture)[EmailTemplateDefaults.SubjectKeyOf(purpose)]);
    }

    /// <summary>
    /// The value paths a text uses, with the loop variable of a for block mapped to its collection path
    /// ("line.product" inside "for line in freight" is "freight.product"). Any other construct fails the test: the
    /// defaults use catalogued tags only.
    /// </summary>
    private static List<string> UsedPaths(string text, EmailTemplatePurpose purpose, string where)
    {
        var collections = EmailTemplateTagCatalogue.TagsFor(purpose).Where(t => t.Kind == EmailTemplateTagKind.Collection).Select(t => t.Path).ToHashSet();
        var loops = new Dictionary<string, string>(StringComparer.Ordinal);
        var used = new List<string>();
        var depth = 0;

        foreach (Match match in TagExpression.Matches(text))
        {
            var expression = match.Groups[1].Value.Trim();
            var header = ForHeader.Match(expression);
            if (header.Success)
            {
                var item = header.Groups[1].Value;
                var path = header.Groups[2].Value;
                collections.ShouldContain(path, $"{where}: 'for {item} in {path}' loops over a catalogued collection of {purpose}");
                loops[item] = path;
                depth++;
                continue;
            }

            if (expression == "end")
            {
                depth--;
                depth.ShouldBeGreaterThanOrEqualTo(0, $"{where}: an end without a for");
                continue;
            }

            expression.ShouldMatch(MemberPath.ToString(), $"{where}: '{expression}' is not a plain member path");
            var first = expression.Split('.')[0];
            used.Add(loops.TryGetValue(first, out var collection) ? collection + expression[first.Length..] : expression);
        }

        depth.ShouldBe(0, $"{where}: every for has its end");
        return used.Distinct().ToList();
    }

    /// <summary>
    /// A model with every catalogued path of the purpose: a value path holds its own marker ("&lt;company.name&gt;"), a
    /// collection holds one item whose fields hold their full-path markers ("&lt;freight.product&gt;").
    /// </summary>
    private static ScriptObject StubModelFor(EmailTemplatePurpose purpose)
    {
        var root = new ScriptObject();
        foreach (var group in EmailTemplateTagCatalogue.GroupsFor(purpose))
        {
            foreach (var tag in group.Tags)
            {
                if (tag.Kind == EmailTemplateTagKind.Collection)
                {
                    var item = new ScriptObject();
                    foreach (var child in tag.Children)
                    {
                        item[child.Path[(tag.Path.Length + 1)..]] = Marker(child.Path);
                    }

                    Set(root, tag.Path, new ScriptArray { item });
                }
                else
                {
                    Set(root, tag.Path, Marker(tag.Path));
                }
            }
        }

        return root;
    }

    private static void Set(ScriptObject root, string path, object value)
    {
        var segments = path.Split('.');
        var current = root;
        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (!current.TryGetValue(segments[i], out var next) || next is not ScriptObject nested)
            {
                nested = new ScriptObject();
                current[segments[i]] = nested;
            }

            current = nested;
        }

        current[segments[^1]] = value;
    }

    private static Dictionary<string, string> Subjects(string culture)
    {
        var path = Path.Combine(FindRepositoryRoot(), "backend", "modules", "platform", "OpenTms.Platform.Domain.Shared", "Localization", "Platform", culture + ".json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.GetProperty("texts").EnumerateObject()
            .Where(p => p.Name.StartsWith("Platform:DefaultSubject.", StringComparison.Ordinal))
            .ToDictionary(p => p.Name, p => p.Value.GetString() ?? string.Empty);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "backend", "OpenTms.slnx")))
        {
            directory = directory.Parent;
        }

        directory.ShouldNotBeNull("the test must run inside the repository");
        return directory.FullName;
    }
}
