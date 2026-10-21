using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Platform.EmailTemplates;
using Shouldly;
using Volo.Abp.Validation;
using Xunit;

namespace OpenTms.EntityFrameworkCore.Platform.MasterData;

/// <summary>
/// The editor support of EmailTemplateAppService on SQLite (docs/designs/49-platform-master-data.md, "Tags and default
/// content", PO 2026-10-20): GetTagsAsync serves the catalogue's groups of a purpose with labels and descriptions in
/// the UI language, kinds, snippets and the children of a collection; GetDefaultsAsync serves the sender address per
/// purpose, the subject in the template's language (not the UI's) and the embedded body; an undefined purpose is a
/// validation error (400), never a missing resource (500). Neither reads tenant data (the catalogue and the defaults
/// are assembly data), so there is no tenant-isolation obligation beyond showing the answer is tenant-independent;
/// permissions and the feature chain of both methods are pinned by PlatformMasterDataPermissionTests and
/// PlatformMasterDataFeatureGatingTests (reflection over every interface method). Risk class: Low (texts) for the
/// content, Critical (authorization) for the gates, which the two tests named cover.
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class EmailTemplateEditorSupportAppServiceTests : PlatformMasterDataAppServiceTestBase
{
    private readonly IEmailTemplateAppService _service;

    public EmailTemplateEditorSupportAppServiceTests()
    {
        _service = GetRequiredService<IEmailTemplateAppService>();
    }

    public static IEnumerable<object[]> Purposes() => Enum.GetValues<EmailTemplatePurpose>().Select(p => new object[] { p });

    private static IEnumerable<EmailTemplateTagDto> Flatten(EmailTemplateTagDto tag) => new[] { tag }.Concat(tag.Children);

    // ── GetTagsAsync ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("en", "Company", "Company name", "The name of your own company.", "Invoice lines")]
    [InlineData("nl", "Bedrijf", "Bedrijfsnaam", "De naam van uw eigen bedrijf.", "Factuurregels")]
    public async Task The_tags_of_a_purpose_are_labelled_and_described_in_the_UI_language(string culture, string company, string companyName, string companyNameDescription, string invoiceLines)
    {
        using var _ = InTenantA();
        using var ui = UiCulture(culture);

        var groups = await _service.GetTagsAsync(EmailTemplatePurpose.Invoice);

        var companyGroup = groups.First();
        companyGroup.Group.ShouldBe("company");
        companyGroup.Label.ShouldBe(company);
        var name = companyGroup.Tags.First();
        name.Path.ShouldBe("company.name");
        name.Label.ShouldBe(companyName);
        name.Description.ShouldBe(companyNameDescription);
        name.Kind.ShouldBe(EmailTemplateTagKind.Value);
        name.Snippet.ShouldBe("{{ company.name }}");
        name.Children.ShouldBeEmpty();

        var lines = groups.Single(g => g.Group == "invoice.lines").Tags.ShouldHaveSingleItem();
        lines.Label.ShouldBe(invoiceLines);
    }

    [Theory]
    [MemberData(nameof(Purposes))]
    public async Task The_groups_are_exactly_GroupsFor_the_purpose_with_every_tag_kind_snippet_and_child_of_the_catalogue(EmailTemplatePurpose purpose)
    {
        using var _ = InTenantA();
        using var ui = UiCulture("en");

        var groups = await _service.GetTagsAsync(purpose);
        var expected = EmailTemplateTagCatalogue.GroupsFor(purpose);

        groups.Select(g => g.Group).ShouldBe(expected.Select(g => g.Key));
        foreach (var (dto, group) in groups.Zip(expected))
        {
            dto.Tags.Select(t => t.Path).ShouldBe(group.Tags.Select(t => t.Path), group.Key);
            foreach (var (tagDto, tag) in dto.Tags.Zip(group.Tags))
            {
                tagDto.Kind.ShouldBe(tag.Kind);
                tagDto.Snippet.ShouldBe(tag.Snippet);
                tagDto.Children.Select(c => c.Path).ShouldBe(tag.Children.Select(c => c.Path));
                foreach (var (childDto, child) in tagDto.Children.Zip(tag.Children))
                {
                    childDto.Kind.ShouldBe(EmailTemplateTagKind.Value);
                    childDto.Snippet.ShouldBe(child.Snippet);
                    childDto.Children.ShouldBeEmpty();
                }
            }
        }

        groups.SelectMany(g => g.Tags).SelectMany(Flatten).Select(t => t.Path).ShouldBe(EmailTemplateTagCatalogue.TagsFor(purpose).Select(t => t.Path));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("nl")]
    public async Task Every_label_and_description_of_every_purpose_is_localized_never_a_key_or_a_path(string culture)
    {
        using var _ = InTenantA();
        using var ui = UiCulture(culture);

        foreach (var purpose in Enum.GetValues<EmailTemplatePurpose>())
        {
            foreach (var group in await _service.GetTagsAsync(purpose))
            {
                group.Label.ShouldNotBeNullOrWhiteSpace($"{culture} {purpose} {group.Group}");
                group.Label.ShouldNotStartWith("Platform:");
                group.Label.ShouldNotBe(group.Group);
                foreach (var tag in group.Tags.SelectMany(Flatten))
                {
                    tag.Label.ShouldNotBeNullOrWhiteSpace($"{culture} {purpose} {tag.Path}");
                    tag.Label.ShouldNotStartWith("Platform:");
                    tag.Label.ShouldNotBe(tag.Path);
                    tag.Description.ShouldNotBeNullOrWhiteSpace($"{culture} {purpose} {tag.Path}");
                    tag.Description.ShouldNotStartWith("Platform:");
                }
            }
        }
    }

    [Fact]
    public async Task A_collection_tag_carries_its_children_and_the_loop_block_while_a_child_inserts_the_loop_variable_field()
    {
        using var _ = InTenantA();
        using var ui = UiCulture("en");

        var trips = (await _service.GetTagsAsync(EmailTemplatePurpose.DriverInstructions)).Single(g => g.Group == "trips").Tags.ShouldHaveSingleItem();
        trips.Path.ShouldBe("trips");
        trips.Kind.ShouldBe(EmailTemplateTagKind.Collection);
        trips.Children.Select(c => c.Path).ShouldBe(["trips.number", "trips.date", "trips.vehicle", "trips.first_stop", "trips.last_stop"]);
        trips.Snippet.ShouldBe("{{ for planned_trip in trips }}\n{{ planned_trip.number }}\n{{ planned_trip.date }}\n{{ planned_trip.vehicle }}\n{{ planned_trip.first_stop }}\n{{ planned_trip.last_stop }}\n{{ end }}");
        trips.Children[0].Snippet.ShouldBe("{{ planned_trip.number }}");
        trips.Children.ShouldAllBe(c => c.Kind == EmailTemplateTagKind.Value);
    }

    [Fact]
    public async Task The_tags_do_not_depend_on_the_tenant()
    {
        using var ui = UiCulture("nl");
        List<EmailTemplateTagGroupDto> a;
        List<EmailTemplateTagGroupDto> b;
        using (InTenantA())
        {
            a = await _service.GetTagsAsync(EmailTemplatePurpose.EtaNotification);
        }

        using (InTenantB())
        {
            b = await _service.GetTagsAsync(EmailTemplatePurpose.EtaNotification);
        }

        a.Select(g => g.Group + "=" + g.Label).ShouldBe(b.Select(g => g.Group + "=" + g.Label));
        a.SelectMany(g => g.Tags).SelectMany(Flatten).Select(t => t.Path + "=" + t.Label).ShouldBe(b.SelectMany(g => g.Tags).SelectMany(Flatten).Select(t => t.Path + "=" + t.Label));
    }

    // ── GetDefaultsAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task The_defaults_come_in_the_requested_language_not_the_UI_language()
    {
        using var _ = InTenantA();
        using var ui = UiCulture("en");

        var dutch = await _service.GetDefaultsAsync(EmailTemplatePurpose.Invoice, "nl");
        dutch.Subject.ShouldBe("{{ invoice.type_name }} {{ invoice.number }} van {{ company.name }}");
        dutch.HtmlBody.ShouldBe(EmailTemplateDefaults.HtmlBodyFor(EmailTemplatePurpose.Invoice, "nl"));
        dutch.HtmlBody.ShouldContain("Geachte {{ recipient.salutation }}");
        dutch.SenderAddress.ShouldBe("{{ company.invoice_email }}");
        dutch.ReplyTo.ShouldBe("{{ company.invoice_email }}");

        /* The culture switch for the subject is scoped: the UI culture is back afterwards. */
        CultureInfo.CurrentUICulture.Name.ShouldBe("en");

        var english = await _service.GetDefaultsAsync(EmailTemplatePurpose.Invoice, " EN ");
        english.Subject.ShouldBe("{{ invoice.type_name }} {{ invoice.number }} of {{ company.name }}");
        english.HtmlBody.ShouldBe(EmailTemplateDefaults.HtmlBodyFor(EmailTemplatePurpose.Invoice, "en"));
        english.HtmlBody.ShouldContain("Dear {{ recipient.salutation }}");
    }

    [Theory]
    [InlineData(null, "nl", "nl")]
    [InlineData("", "nl", "nl")]
    [InlineData("fr", "nl", "nl")]
    [InlineData(null, "en", "en")]
    [InlineData("de", "en", "en")]
    [InlineData("nl", "en", "nl")]
    public async Task An_empty_or_unknown_culture_gives_the_UI_language(string? culture, string uiCulture, string expected)
    {
        using var _ = InTenantA();
        using var ui = UiCulture(uiCulture);

        var defaults = await _service.GetDefaultsAsync(EmailTemplatePurpose.TransportOrder, culture);
        defaults.HtmlBody.ShouldBe(EmailTemplateDefaults.HtmlBodyFor(EmailTemplatePurpose.TransportOrder, expected));
        defaults.Subject.ShouldBe(expected == "nl"
            ? "{{ order.type_name }} {{ order.number }} van {{ company.name }}"
            : "{{ order.type_name }} {{ order.number }} of {{ company.name }}");
    }

    [Theory]
    [MemberData(nameof(Purposes))]
    public async Task Every_purpose_has_defaults_in_both_languages_with_its_sender_address_as_From_and_Reply_to(EmailTemplatePurpose purpose)
    {
        using var _ = InTenantA();
        using var ui = UiCulture("en");

        foreach (var culture in EmailTemplateDefaults.Cultures)
        {
            var defaults = await _service.GetDefaultsAsync(purpose, culture);
            defaults.SenderAddress.ShouldBe(EmailTemplateDefaults.SenderAddressFor(purpose));
            defaults.ReplyTo.ShouldBe(defaults.SenderAddress);
            defaults.Subject.ShouldNotBeNullOrWhiteSpace($"{purpose} {culture}");
            defaults.Subject.ShouldNotStartWith("Platform:", Case.Sensitive, "the subject is the text, not its key");
            defaults.HtmlBody.ShouldBe(EmailTemplateDefaults.HtmlBodyFor(purpose, culture));
        }
    }

    [Fact]
    public async Task The_invoice_purposes_send_from_the_invoice_address_the_others_from_the_company_address()
    {
        using var _ = InTenantA();
        foreach (var purpose in new[] { EmailTemplatePurpose.Invoice, EmailTemplatePurpose.InvoiceReminder, EmailTemplatePurpose.ProformaOverview })
        {
            (await _service.GetDefaultsAsync(purpose, "en")).SenderAddress.ShouldBe("{{ company.invoice_email }}", purpose.ToString());
        }

        foreach (var purpose in new[] { EmailTemplatePurpose.TransportOrder, EmailTemplatePurpose.TransportFile, EmailTemplatePurpose.PurchaseOrder, EmailTemplatePurpose.EtaNotification, EmailTemplatePurpose.DriverInstructions, EmailTemplatePurpose.DigitalArchive })
        {
            (await _service.GetDefaultsAsync(purpose, "en")).SenderAddress.ShouldBe("{{ company.email }}", purpose.ToString());
        }
    }

    [Fact]
    public async Task The_defaults_are_accepted_by_the_create_validation_as_they_are()
    {
        /* What the editor fills in on create must save: every default parses as Scriban on the way in. */
        using var _ = InTenantA();
        foreach (var purpose in Enum.GetValues<EmailTemplatePurpose>())
        {
            foreach (var culture in EmailTemplateDefaults.Cultures)
            {
                var defaults = await _service.GetDefaultsAsync(purpose, culture);
                var created = await _service.CreateAsync(new EmailTemplateCreateDto
                {
                    Name = $"{purpose} {culture}",
                    Purpose = purpose,
                    Culture = culture,
                    SenderAddress = defaults.SenderAddress,
                    ReplyTo = defaults.ReplyTo,
                    Subject = defaults.Subject,
                    HtmlBody = defaults.HtmlBody
                });
                created.HtmlBody.ShouldBe(defaults.HtmlBody);
                created.Subject.ShouldBe(defaults.Subject);
            }
        }
    }

    // ── undefined purpose ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(99)]
    [InlineData(-1)]
    public async Task An_undefined_purpose_is_a_validation_error_on_both_methods(int purpose)
    {
        using var _ = InTenantA();

        var tags = await Should.ThrowAsync<AbpValidationException>(() => _service.GetTagsAsync((EmailTemplatePurpose)purpose));
        tags.ValidationErrors.ShouldHaveSingleItem().MemberNames.ShouldContain("purpose");

        var defaults = await Should.ThrowAsync<AbpValidationException>(() => _service.GetDefaultsAsync((EmailTemplatePurpose)purpose, "en"));
        defaults.ValidationErrors.ShouldHaveSingleItem().MemberNames.ShouldContain("purpose");
    }
}
