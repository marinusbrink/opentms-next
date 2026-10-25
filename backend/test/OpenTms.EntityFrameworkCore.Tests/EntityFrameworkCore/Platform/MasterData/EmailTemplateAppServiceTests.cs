using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Platform;
using OpenTms.Platform.EmailTemplates;
using Shouldly;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Validation;
using Xunit;

namespace OpenTms.EntityFrameworkCore.Platform.MasterData;

/// <summary>
/// EmailTemplateAppService on SQLite (docs/designs/54-platform-master-data.md, "EmailTemplate", PO 2026-10-18: one
/// template per language; test risk analysis "Scriban/regex validation, guards when in use, default switching", High,
/// table-driven): the CRUD round trip with the language, the mail fields, subject and body on the template; culture null
/// or blank = all languages, nl/en accepted (trimmed, any case), anything else UnsupportedCulture; subject and body
/// required and bounded (body 200,000 accepted, 200,001 refused); every Scriban field parsed on save, the refusal naming
/// the field as the screen labels it (From, HTML body; no language suffix) and the line; the name unique regardless of
/// case and of language; one default per purpose across languages; the list row (id, name, purpose, culture, isDefault),
/// the sort whitelist incl. culture, the purpose filter; the lookup; the list and the lookup never read the body (the
/// captured SQL); description kept on update; delete, bulk delete, concurrency stamp and tenant isolation. The partial
/// unique index itself is tested on PostgreSQL (PlatformMasterDataUniqueIndexTests).
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class EmailTemplateAppServiceTests : PlatformMasterDataAppServiceTestBase
{
    private const string Subject = "Invoice {{ invoice.number }}";
    private const string Body = "<p>Dear {{ customer.name }},</p>";

    private readonly IEmailTemplateAppService _service;

    public EmailTemplateAppServiceTests()
    {
        _service = GetRequiredService<IEmailTemplateAppService>();
    }

    private static EmailTemplateCreateDto Dto(string name, EmailTemplatePurpose purpose = EmailTemplatePurpose.Invoice, string? culture = null, bool isDefault = false, Action<EmailTemplateCreateDto>? configure = null)
    {
        var dto = new EmailTemplateCreateDto { Name = name, Purpose = purpose, Culture = culture, IsDefault = isDefault, Subject = Subject, HtmlBody = Body };
        configure?.Invoke(dto);
        return dto;
    }

    private static EmailTemplateUpdateDto UpdateFrom(EmailTemplateDto dto, Action<EmailTemplateUpdateDto>? configure = null)
    {
        var update = new EmailTemplateUpdateDto
        {
            Name = dto.Name, Purpose = dto.Purpose, Culture = dto.Culture, IsDefault = dto.IsDefault,
            SenderAddress = dto.SenderAddress, ReplyTo = dto.ReplyTo, To = dto.To, Cc = dto.Cc, Bcc = dto.Bcc, SendCopyToSender = dto.SendCopyToSender,
            Subject = dto.Subject, HtmlBody = dto.HtmlBody, Description = dto.Description, ConcurrencyStamp = dto.ConcurrencyStamp
        };
        configure?.Invoke(update);
        return update;
    }

    private static EmailTemplateListRequestDto Request(string? search = null, EmailTemplatePurpose? purpose = null, string? sortBy = null, string? sortDir = null, int start = 0, int end = 100)
    {
        var request = List<EmailTemplateListRequestDto>(start, end, search, sortBy, sortDir);
        request.Purpose = purpose;
        return request;
    }

    private async Task<List<string>> DefaultsAsync(EmailTemplatePurpose purpose)
        => (await _service.GetListAsync(Request(purpose: purpose))).Rows.Where(r => r.IsDefault).Select(r => r.Name).ToList();

    // ── CRUD round trip ──────────────────────────────────────────────────────

    [Fact]
    public async Task A_template_round_trips_with_its_language_its_mail_fields_its_subject_and_its_body()
    {
        using var _ = InTenantA();
        var body = "  <p>Beste {{ customer.name }},</p>\n";
        var created = await _service.CreateAsync(Dto("  Invoice mail NL  ", culture: " NL ", configure: d =>
        {
            d.SenderAddress = " {{ company.email }} ";
            d.ReplyTo = "invoices@example.test";
            d.To = "{{ customer.invoice_email }}";
            d.Cc = "";
            d.Bcc = "archive@example.test";
            d.SendCopyToSender = true;
            d.Subject = "  Factuur {{ invoice.number }}  ";
            d.HtmlBody = body;
            d.Description = "  Sent with every invoice  ";
        }));

        created.Name.ShouldBe("Invoice mail NL");
        created.Purpose.ShouldBe(EmailTemplatePurpose.Invoice);
        created.Culture.ShouldBe("nl", "trimmed and lower-cased");
        created.IsDefault.ShouldBeFalse();
        created.SenderAddress.ShouldBe("{{ company.email }}");
        created.ReplyTo.ShouldBe("invoices@example.test");
        created.To.ShouldBe("{{ customer.invoice_email }}");
        created.Cc.ShouldBeNull("an empty address field is no address");
        created.Bcc.ShouldBe("archive@example.test");
        created.SendCopyToSender.ShouldBeTrue();
        created.Subject.ShouldBe("Factuur {{ invoice.number }}", "the subject is trimmed");
        created.HtmlBody.ShouldBe(body, "the body is kept as typed");
        created.Description.ShouldBe("Sent with every invoice");
        created.ConcurrencyStamp.ShouldNotBeNullOrEmpty();

        var read = await _service.GetAsync(created.Id);
        (read.Culture, read.Subject, read.HtmlBody, read.SenderAddress, read.SendCopyToSender)
            .ShouldBe(("nl", "Factuur {{ invoice.number }}", body, "{{ company.email }}", true));

        var row = (await _service.GetListAsync(Request())).Rows.ShouldHaveSingleItem();
        (row.Id, row.Name, row.Purpose, row.Culture, row.IsDefault).ShouldBe((created.Id, "Invoice mail NL", EmailTemplatePurpose.Invoice, "nl", false));
    }

    [Fact]
    public void The_list_row_carries_exactly_id_name_purpose_culture_and_isDefault()
    {
        /* API contract (design 54, "Rows"): no subject, no body, no addresses on a row. */
        typeof(EmailTemplateRowDto).GetProperties().Select(p => p.Name)
            .ShouldBe(["Id", "Name", "Purpose", "Culture", "IsDefault"], ignoreOrder: true);
    }

    [Fact]
    public async Task Update_changes_every_field_and_keeps_the_description_the_client_sends_back()
    {
        using var _ = InTenantA();
        var created = await _service.CreateAsync(Dto("Invoice mail", culture: "en", configure: d => d.Description = "Kept from OpenTMS"));

        var updated = await _service.UpdateAsync(created.Id, UpdateFrom(created, u =>
        {
            u.Name = "Reminder mail";
            u.Purpose = EmailTemplatePurpose.InvoiceReminder;
            u.Culture = null;
            u.SenderAddress = "{{ company.email }}";
            u.ReplyTo = "reply@example.test";
            u.To = "to@example.test";
            u.Cc = "cc@example.test";
            u.Bcc = "bcc@example.test";
            u.SendCopyToSender = true;
            u.Subject = "Reminder {{ invoice.number }}";
            u.HtmlBody = "<p>Reminder</p>";
        }));

        updated.Name.ShouldBe("Reminder mail");
        updated.Purpose.ShouldBe(EmailTemplatePurpose.InvoiceReminder);
        updated.Culture.ShouldBeNull("all languages");
        (updated.SenderAddress, updated.ReplyTo, updated.To, updated.Cc, updated.Bcc, updated.SendCopyToSender)
            .ShouldBe(("{{ company.email }}", "reply@example.test", "to@example.test", "cc@example.test", "bcc@example.test", true));
        (updated.Subject, updated.HtmlBody).ShouldBe(("Reminder {{ invoice.number }}", "<p>Reminder</p>"));
        updated.Description.ShouldBe("Kept from OpenTMS", "the editor has no input for it but sends it back");
        updated.ConcurrencyStamp.ShouldNotBe(created.ConcurrencyStamp);

        /* A second save that touches only the mail fields keeps the description too. */
        var again = await _service.UpdateAsync(created.Id, UpdateFrom(updated, u => { u.Culture = "nl"; u.SendCopyToSender = false; }));
        (again.Culture, again.SendCopyToSender, again.Description).ShouldBe(("nl", false, "Kept from OpenTMS"));
        (await _service.GetAsync(created.Id)).Description.ShouldBe("Kept from OpenTMS");
        (await _service.GetListAsync(Request())).Rows.ShouldHaveSingleItem().Culture.ShouldBe("nl");
    }

    [Fact]
    public async Task A_stale_concurrency_stamp_is_refused()
    {
        using var _ = InTenantA();
        var created = await _service.CreateAsync(Dto("Invoice mail"));
        await _service.UpdateAsync(created.Id, UpdateFrom(created, u => u.Subject = "first"));

        await Should.ThrowAsync<AbpDbConcurrencyException>(() => _service.UpdateAsync(created.Id, UpdateFrom(created, u => u.Subject = "second")));
        (await _service.GetAsync(created.Id)).Subject.ShouldBe("first");
    }

    // ── Name unique regardless of case and of language ───────────────────────

    [Fact]
    public async Task A_name_differing_only_in_case_is_a_duplicate_on_create_and_on_update()
    {
        using var _ = InTenantA();
        await _service.CreateAsync(Dto("Invoice mail"));

        var onCreate = await ShouldFailAsync(() => _service.CreateAsync(Dto("INVOICE MAIL")), PlatformErrorCodes.DuplicateKey);
        onCreate.Data["field"].ShouldBe("name");
        onCreate.Data["value"].ShouldBe("INVOICE MAIL");
        await ShouldFailAsync(() => _service.CreateAsync(Dto(" invoice mail ", EmailTemplatePurpose.TransportOrder)), PlatformErrorCodes.DuplicateKey);

        var other = await _service.CreateAsync(Dto("Reminder mail"));
        (await ShouldFailAsync(() => _service.UpdateAsync(other.Id, UpdateFrom(other, u => u.Name = "invoice MAIL")), PlatformErrorCodes.DuplicateKey))
            .Data["field"].ShouldBe("name");
        (await _service.GetAsync(other.Id)).Name.ShouldBe("Reminder mail");
        (await _service.GetListAsync(Request())).TotalCount.ShouldBe(2);
    }

    [Fact]
    public async Task The_name_is_unique_across_languages_so_each_language_variant_has_its_own_name()
    {
        using var _ = InTenantA();
        await _service.CreateAsync(Dto("Invoice mail", culture: "en"));

        /* Design 54: Name unique per tenant regardless of case; the language is not part of the key. */
        await ShouldFailAsync(() => _service.CreateAsync(Dto("Invoice mail", culture: "nl")), PlatformErrorCodes.DuplicateKey);
        await ShouldFailAsync(() => _service.CreateAsync(Dto("INVOICE MAIL", culture: null)), PlatformErrorCodes.DuplicateKey);
        (await _service.CreateAsync(Dto("Factuurmail", culture: "nl"))).Culture.ShouldBe("nl");
        (await _service.GetListAsync(Request())).TotalCount.ShouldBe(2);
    }

    [Fact]
    public async Task A_template_may_change_the_case_of_its_own_name_and_a_deleted_one_frees_its_name()
    {
        using var _ = InTenantA();
        var created = await _service.CreateAsync(Dto("Invoice mail"));

        (await _service.UpdateAsync(created.Id, UpdateFrom(created, u => u.Name = "INVOICE Mail"))).Name.ShouldBe("INVOICE Mail");

        await _service.DeleteAsync(created.Id);
        (await _service.CreateAsync(Dto("invoice mail"))).Id.ShouldNotBe(created.Id);
        await Should.ThrowAsync<EntityNotFoundException>(() => _service.GetAsync(created.Id));
        await Should.ThrowAsync<EntityNotFoundException>(() => _service.DeleteAsync(created.Id));
    }

    // ── Languages ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("nl", "nl")]
    [InlineData("en", "en")]
    [InlineData("EN", "en")]
    [InlineData(" Nl ", "nl")]
    public async Task Culture_null_or_blank_is_all_languages_and_a_configured_language_is_stored_trimmed_in_lower_case(string? input, string? stored)
    {
        using var _ = InTenantA();
        var created = await _service.CreateAsync(Dto("Mail", culture: input));
        created.Culture.ShouldBe(stored);
        (await _service.GetAsync(created.Id)).Culture.ShouldBe(stored);
        (await _service.GetListAsync(Request())).Rows.ShouldHaveSingleItem().Culture.ShouldBe(stored);

        /* The same on update, from another language. */
        var other = await _service.CreateAsync(Dto("Other", culture: stored == "en" ? "nl" : "en"));
        (await _service.UpdateAsync(other.Id, UpdateFrom(other, u => u.Culture = input))).Culture.ShouldBe(stored);
    }

    [Theory]
    [InlineData("de", "de")]
    [InlineData("FR", "fr")]
    [InlineData("en-GB", "en-gb")]
    [InlineData(" nl-NL ", "nl-nl")]
    [InlineData("-", "-")]
    public async Task A_language_that_is_not_configured_is_refused_on_create_and_on_update(string input, string reported)
    {
        using var _ = InTenantA();
        (await ShouldFailAsync(() => _service.CreateAsync(Dto("Mail", culture: input)), PlatformErrorCodes.UnsupportedCulture))
            .Data["culture"].ShouldBe(reported);
        (await _service.GetListAsync(Request())).TotalCount.ShouldBe(0);

        var valid = await _service.CreateAsync(Dto("Valid", culture: "en"));
        (await ShouldFailAsync(() => _service.UpdateAsync(valid.Id, UpdateFrom(valid, u => u.Culture = input)), PlatformErrorCodes.UnsupportedCulture))
            .Data["culture"].ShouldBe(reported);
        /* The language stays. That the whole refused update rolls back (fields set before the refusal included) needs a
         * transaction, which this SQLite harness disables: PlatformMasterDataUniqueIndexTests proves it on PostgreSQL. */
        (await _service.GetAsync(valid.Id)).Culture.ShouldBe("en");
    }

    [Fact]
    public async Task A_culture_longer_than_ten_characters_is_refused_by_the_request_validation()
    {
        using var _ = InTenantA();
        await Should.ThrowAsync<AbpValidationException>(() => _service.CreateAsync(Dto("Mail", culture: new string('n', PlatformMasterDataConsts.MaxCultureLength + 1))));
        (await _service.GetListAsync(Request())).TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task One_purpose_may_have_a_template_for_every_language_and_one_for_all_languages()
    {
        using var _ = InTenantA();
        await _service.CreateAsync(Dto("Invoice EN", culture: "en"));
        await _service.CreateAsync(Dto("Invoice NL", culture: "nl"));
        await _service.CreateAsync(Dto("Invoice all", culture: null));

        var rows = (await _service.GetListAsync(Request(purpose: EmailTemplatePurpose.Invoice))).Rows;
        rows.Select(r => (r.Name, r.Culture)).ShouldBe([("Invoice all", null), ("Invoice EN", "en"), ("Invoice NL", "nl")], ignoreOrder: true);
    }

    // ── Subject and body: required and bounded ───────────────────────────────

    [Fact]
    public async Task A_body_of_200000_characters_is_accepted_and_one_more_is_refused()
    {
        using var _ = InTenantA();
        var max = "<p>" + new string('x', PlatformMasterDataConsts.MaxHtmlBodyLength - 7) + "</p>";
        max.Length.ShouldBe(200_000);

        var created = await _service.CreateAsync(Dto("Big", configure: d => d.HtmlBody = max));
        (await _service.GetAsync(created.Id)).HtmlBody.ShouldBe(max);

        await Should.ThrowAsync<AbpValidationException>(() => _service.CreateAsync(Dto("Too big", configure: d => d.HtmlBody = max + "x")));
        await Should.ThrowAsync<AbpValidationException>(() => _service.UpdateAsync(created.Id, UpdateFrom(created, u => u.HtmlBody = max + "x")));
        (await _service.GetAsync(created.Id)).HtmlBody.Length.ShouldBe(200_000);
        (await _service.GetListAsync(Request())).TotalCount.ShouldBe(1);
    }

    [Fact]
    public void The_aggregate_itself_refuses_a_body_over_200000_characters_and_an_empty_subject_or_body()
    {
        /* Defence in depth: the bound holds below the DTO validation too. */
        string[] cultures = ["nl", "en"];
        var max = new string('x', PlatformMasterDataConsts.MaxHtmlBodyLength);
        new EmailTemplate(Guid.NewGuid(), TenantA, "Max", EmailTemplatePurpose.Invoice, null, cultures, "s", max).HtmlBody.Length.ShouldBe(200_000);
        Should.Throw<ArgumentException>(() => new EmailTemplate(Guid.NewGuid(), TenantA, "Over", EmailTemplatePurpose.Invoice, null, cultures, "s", max + "x"));
        Should.Throw<ArgumentException>(() => new EmailTemplate(Guid.NewGuid(), TenantA, "No subject", EmailTemplatePurpose.Invoice, null, cultures, " ", "b"));
        Should.Throw<ArgumentException>(() => new EmailTemplate(Guid.NewGuid(), TenantA, "No body", EmailTemplatePurpose.Invoice, null, cultures, "s", " "));
        Should.Throw<ArgumentException>(() => new EmailTemplate(Guid.NewGuid(), TenantA, "Long subject", EmailTemplatePurpose.Invoice, null, cultures, new string('s', PlatformMasterDataConsts.MaxSubjectLength + 1), "b"));
    }

    public static IEnumerable<object[]> RefusedRequests() =>
    [
        ["empty subject", (Action<EmailTemplateCreateDto>)(d => d.Subject = "")],
        ["blank subject", (Action<EmailTemplateCreateDto>)(d => d.Subject = "   ")],
        ["subject of 513", (Action<EmailTemplateCreateDto>)(d => d.Subject = new string('s', PlatformMasterDataConsts.MaxSubjectLength + 1))],
        ["empty body", (Action<EmailTemplateCreateDto>)(d => d.HtmlBody = "")],
        ["blank body", (Action<EmailTemplateCreateDto>)(d => d.HtmlBody = "  \n ")],
        ["name of 129", (Action<EmailTemplateCreateDto>)(d => d.Name = new string('n', PlatformMasterDataConsts.MaxNameLength + 1))],
        ["unknown purpose", (Action<EmailTemplateCreateDto>)(d => d.Purpose = 0)],
        ["From of 513", (Action<EmailTemplateCreateDto>)(d => d.SenderAddress = new string('a', PlatformMasterDataConsts.MaxEmailAddressFieldLength + 1))],
        ["To of 513", (Action<EmailTemplateCreateDto>)(d => d.To = new string('a', PlatformMasterDataConsts.MaxEmailAddressFieldLength + 1))],
        ["description of 2501", (Action<EmailTemplateCreateDto>)(d => d.Description = new string('d', PlatformMasterDataConsts.MaxDescriptionLength + 1))],
    ];

    [Theory]
    [MemberData(nameof(RefusedRequests))]
    public async Task A_request_outside_the_bounds_is_refused_and_stores_nothing(string _, Action<EmailTemplateCreateDto> configure)
    {
        using var tenant = InTenantA();
        await Should.ThrowAsync<AbpValidationException>(() => _service.CreateAsync(Dto("Refused", configure: configure)));
        (await _service.GetListAsync(Request())).TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task A_subject_of_512_characters_is_accepted()
    {
        using var _ = InTenantA();
        var subject = new string('s', PlatformMasterDataConsts.MaxSubjectLength);
        (await _service.CreateAsync(Dto("Long subject", configure: d => d.Subject = subject))).Subject.ShouldBe(subject);
    }

    // ── Scriban validation (table) ───────────────────────────────────────────

    /// <summary>Field id, its label on screen in English and in Dutch (Platform resource).</summary>
    public static IEnumerable<object[]> ScribanFields() =>
    [
        ["senderAddress", "From", "Van"],
        ["replyTo", "Reply-to", "Antwoordadres"],
        ["to", "To", "Aan"],
        ["cc", "Cc", "Cc"],
        ["bcc", "Bcc", "Bcc"],
        ["subject", "Subject", "Onderwerp"],
        ["htmlBody", "HTML body", "HTML-inhoud"],
    ];

    private static void Put(EmailTemplateCreateDto dto, string field, string text)
    {
        switch (field)
        {
            case "senderAddress": dto.SenderAddress = text; break;
            case "replyTo": dto.ReplyTo = text; break;
            case "to": dto.To = text; break;
            case "cc": dto.Cc = text; break;
            case "bcc": dto.Bcc = text; break;
            case "subject": dto.Subject = text; break;
            case "htmlBody": dto.HtmlBody = text; break;
            default: throw new ArgumentOutOfRangeException(nameof(field));
        }
    }

    [Theory]
    [MemberData(nameof(ScribanFields))]
    public async Task A_Scriban_syntax_error_in_any_field_is_refused_naming_the_field_as_the_screen_labels_it_and_the_line(string field, string englishLabel, string dutchLabel)
    {
        using var _ = InTenantA();

        using (UiCulture("en"))
        {
            var create = Dto("Broken", culture: "nl");
            Put(create, field, "Hello {{ 1 + }}");
            var ex = await ShouldFailAsync(() => _service.CreateAsync(create), PlatformErrorCodes.TemplateSyntaxError);
            ex.Data["field"].ShouldBe(field);
            ex.Data["fieldLabel"].ShouldBe(englishLabel, "one template is one language: no language suffix");
            ex.Data["line"].ShouldBe(1);
            ex.Message.ShouldNotContain("Hello", Case.Insensitive, "the template text never goes into the exception");
        }

        using (UiCulture("nl"))
        {
            var create = Dto("Broken", culture: "en");
            Put(create, field, "{{ end }}");
            (await ShouldFailAsync(() => _service.CreateAsync(create), PlatformErrorCodes.TemplateSyntaxError)).Data["fieldLabel"].ShouldBe(dutchLabel);
        }

        (await _service.GetListAsync(Request())).TotalCount.ShouldBe(0);

        /* On update too, and the stored template is unchanged. */
        var valid = await _service.CreateAsync(Dto("Valid"));
        var update = UpdateFrom(valid, u => u.Name = "Changed");
        Put(update, field, "{{ if customer }}no end");
        var onUpdate = await ShouldFailAsync(() => _service.UpdateAsync(valid.Id, update), PlatformErrorCodes.TemplateSyntaxError);
        onUpdate.Data["field"].ShouldBe(field);
        (await _service.GetAsync(valid.Id)).ShouldSatisfyAllConditions(
            t => t.Name.ShouldBe("Valid"),
            t => t.SenderAddress.ShouldBeNull(),
            t => t.Subject.ShouldBe(Subject),
            t => t.HtmlBody.ShouldBe(Body));
    }

    [Fact]
    public async Task The_refusal_names_the_line_of_the_error_in_a_multi_line_body()
    {
        using var _ = InTenantA();
        using var english = UiCulture("en");
        var body = "<html>\n<body>\n<p>{{ customer.name }}</p>\n<p>{{ 1 + }}</p>\n</body>";
        var ex = await ShouldFailAsync(() => _service.CreateAsync(Dto("Broken", culture: "en", configure: d => d.HtmlBody = body)), PlatformErrorCodes.TemplateSyntaxError);
        ex.Data["field"].ShouldBe("htmlBody");
        ex.Data["line"].ShouldBe(4, "1-based, as an editor shows lines");
        ex.Data["fieldLabel"].ShouldBe("HTML body", "the language is the template's, not the field's");
    }

    [Theory]
    [InlineData("Plain text without any expression")]
    [InlineData("{{ company.email }}")]
    [InlineData("<p>{{ for line in invoice.lines }}<span>{{ line.description | html.escape }}</span>{{ end }}</p>")]
    [InlineData("{{ if customer.is_company }}Dear sir/madam{{ else }}Dear {{ customer.first_name }}{{ end }}")]
    [InlineData("{{ invoice.date | date.to_string '%d-%m-%Y' }} {{ invoice.total | math.format 'N2' }}")]
    [InlineData("<style>p { color: red; }</style><p>{ single braces are text }</p>")]
    public async Task Valid_Scriban_is_accepted_in_every_field(string text)
    {
        using var _ = InTenantA();
        var created = await _service.CreateAsync(Dto("Valid", configure: d =>
        {
            d.SenderAddress = d.ReplyTo = d.To = d.Cc = d.Bcc = text;
            d.Subject = text;
            d.HtmlBody = text;
        }));
        (created.SenderAddress, created.Bcc, created.Subject, created.HtmlBody).ShouldBe((text, text, text, text));
    }

    // ── One default per purpose ──────────────────────────────────────────────

    [Fact]
    public async Task Making_a_template_the_default_clears_the_previous_default_of_its_purpose_only()
    {
        using var _ = InTenantA();
        var a = await _service.CreateAsync(Dto("A", isDefault: true));
        var order = await _service.CreateAsync(Dto("Order", EmailTemplatePurpose.TransportOrder, isDefault: true));
        (await DefaultsAsync(EmailTemplatePurpose.Invoice)).ShouldBe(["A"]);

        /* A second default on create clears A. */
        var b = await _service.CreateAsync(Dto("B", isDefault: true));
        b.IsDefault.ShouldBeTrue();
        (await DefaultsAsync(EmailTemplatePurpose.Invoice)).ShouldBe(["B"]);
        (await _service.GetAsync(a.Id)).IsDefault.ShouldBeFalse();

        /* On update: A back as the default clears B. */
        a = await _service.GetAsync(a.Id);
        (await _service.UpdateAsync(a.Id, UpdateFrom(a, u => u.IsDefault = true))).IsDefault.ShouldBeTrue();
        (await DefaultsAsync(EmailTemplatePurpose.Invoice)).ShouldBe(["A"]);

        /* The default of another purpose is untouched throughout. */
        (await DefaultsAsync(EmailTemplatePurpose.TransportOrder)).ShouldBe(["Order"]);
        (await _service.GetAsync(order.Id)).IsDefault.ShouldBeTrue();

        /* Moving a default to another purpose clears that purpose's default and leaves this purpose without one. */
        a = await _service.GetAsync(a.Id);
        await _service.UpdateAsync(a.Id, UpdateFrom(a, u => u.Purpose = EmailTemplatePurpose.TransportOrder));
        (await DefaultsAsync(EmailTemplatePurpose.TransportOrder)).ShouldBe(["A"]);
        (await DefaultsAsync(EmailTemplatePurpose.Invoice)).ShouldBeEmpty();

        /* Saving the default again (no change) keeps it the default. */
        a = await _service.GetAsync(a.Id);
        (await _service.UpdateAsync(a.Id, UpdateFrom(a, u => u.Subject = "kept"))).IsDefault.ShouldBeTrue();
        (await DefaultsAsync(EmailTemplatePurpose.TransportOrder)).ShouldBe(["A"]);

        /* Clearing the flag leaves the purpose without a default (assumption 7). */
        a = await _service.GetAsync(a.Id);
        await _service.UpdateAsync(a.Id, UpdateFrom(a, u => u.IsDefault = false));
        (await DefaultsAsync(EmailTemplatePurpose.TransportOrder)).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_default_is_one_per_purpose_across_languages()
    {
        using var _ = InTenantA();
        var nl = await _service.CreateAsync(Dto("Invoice NL", culture: "nl", isDefault: true));

        /* An English template made the default replaces the Dutch one: the default is per purpose, not per language. */
        var en = await _service.CreateAsync(Dto("Invoice EN", culture: "en", isDefault: true));
        (await DefaultsAsync(EmailTemplatePurpose.Invoice)).ShouldBe(["Invoice EN"]);
        (await _service.GetAsync(nl.Id)).IsDefault.ShouldBeFalse();

        /* The all-languages template too. */
        await _service.CreateAsync(Dto("Invoice all", culture: null, isDefault: true));
        (await DefaultsAsync(EmailTemplatePurpose.Invoice)).ShouldBe(["Invoice all"]);
        (await _service.GetAsync(en.Id)).IsDefault.ShouldBeFalse();

        /* Changing the language of the default keeps it the default. */
        var all = (await _service.GetListAsync(Request(search: "Invoice all"))).Rows.ShouldHaveSingleItem();
        var detail = await _service.GetAsync(all.Id);
        (await _service.UpdateAsync(all.Id, UpdateFrom(detail, u => u.Culture = "nl"))).IsDefault.ShouldBeTrue();
        (await DefaultsAsync(EmailTemplatePurpose.Invoice)).ShouldBe(["Invoice all"]);
    }

    [Fact]
    public async Task A_default_template_may_be_deleted_and_then_the_purpose_has_no_default()
    {
        using var _ = InTenantA();
        var a = await _service.CreateAsync(Dto("A", isDefault: true));
        await _service.DeleteAsync(a.Id);
        (await DefaultsAsync(EmailTemplatePurpose.Invoice)).ShouldBeEmpty();
        (await _service.CreateAsync(Dto("B", isDefault: true))).IsDefault.ShouldBeTrue();
    }

    // ── List and lookup ──────────────────────────────────────────────────────

    [Fact]
    public async Task The_list_searches_name_and_description_filters_on_purpose_sorts_on_the_whitelist_and_pages()
    {
        using var _ = InTenantA();
        await _service.CreateAsync(Dto("Charlie invoice", culture: "nl", configure: d => d.Description = "monthly run"));
        await _service.CreateAsync(Dto("Alpha order", EmailTemplatePurpose.TransportOrder, culture: "en", isDefault: true));
        await _service.CreateAsync(Dto("Bravo reminder", EmailTemplatePurpose.InvoiceReminder, configure: d => d.Subject = "Charlie"));

        var all = await _service.GetListAsync(Request());
        all.TotalCount.ShouldBe(3);
        all.Rows.Select(r => r.Name).ShouldBe(["Alpha order", "Bravo reminder", "Charlie invoice"], "default order: name");

        (await _service.GetListAsync(Request(search: "INVOICE"))).Rows.ShouldHaveSingleItem().Name.ShouldBe("Charlie invoice");
        var byDescription = await _service.GetListAsync(Request(search: " Monthly "));
        byDescription.Rows.ShouldHaveSingleItem().Name.ShouldBe("Charlie invoice");
        (byDescription.TotalCount, byDescription.FilteredCount).ShouldBe((3, 1));
        (await _service.GetListAsync(Request(search: "charlie"))).Rows.ShouldHaveSingleItem().Name.ShouldBe("Charlie invoice", "the subject is not searched");
        (await _service.GetListAsync(Request(search: "dear"))).Rows.ShouldBeEmpty("the body is not searched");

        var invoices = await _service.GetListAsync(Request(purpose: EmailTemplatePurpose.Invoice));
        invoices.Rows.ShouldHaveSingleItem().Name.ShouldBe("Charlie invoice");
        invoices.TotalCount.ShouldBe(1, "the filter is part of the total, the search is not");

        (await _service.GetListAsync(Request(sortBy: "name", sortDir: "desc"))).Rows.Select(r => r.Name).ShouldBe(["Charlie invoice", "Bravo reminder", "Alpha order"]);
        (await _service.GetListAsync(Request(sortBy: "Purpose"))).Rows.Select(r => r.Purpose).ShouldBe(
            [EmailTemplatePurpose.TransportOrder, EmailTemplatePurpose.Invoice, EmailTemplatePurpose.InvoiceReminder]);
        (await _service.GetListAsync(Request(sortBy: "isDefault", sortDir: "desc"))).Rows.First().Name.ShouldBe("Alpha order");

        /* Culture: en before nl ascending, reversed descending; the all-languages row (null) sorts at one end. */
        var byCulture = (await _service.GetListAsync(Request(sortBy: "culture"))).Rows.Select(r => r.Culture).ToList();
        byCulture.Where(c => c != null).ShouldBe(["en", "nl"]);
        (byCulture[0] == null || byCulture[^1] == null).ShouldBeTrue(string.Join(",", byCulture));
        var byCultureDesc = (await _service.GetListAsync(Request(sortBy: "CULTURE", sortDir: "desc"))).Rows.Select(r => r.Culture).ToList();
        byCultureDesc.Where(c => c != null).ShouldBe(["nl", "en"]);

        foreach (var column in new[] { "subject", "htmlBody", "senderAddress", "description", "NormalizedName", "cultures" })
        {
            (await ShouldFailAsync(() => _service.GetListAsync(Request(sortBy: column)), PlatformErrorCodes.UnknownSortColumn)).Data["sortBy"].ShouldBe(column);
        }

        var page2 = await _service.GetListAsync(Request(start: 1, end: 2));
        page2.Rows.ShouldHaveSingleItem().Name.ShouldBe("Bravo reminder");
        page2.TotalCount.ShouldBe(3);

        await Should.ThrowAsync<AbpValidationException>(() => _service.GetListAsync(Request(start: 0, end: 201)));
        await Should.ThrowAsync<AbpValidationException>(() => _service.GetListAsync(Request(start: 5, end: 5)));
        await Should.ThrowAsync<AbpValidationException>(() => _service.GetListAsync(Request(sortDir: "up")));
    }

    [Fact]
    public async Task The_lookup_searches_by_name_in_name_order_and_bounds_the_take()
    {
        using var _ = InTenantA();
        foreach (var name in new[] { "Delta", "Alpha", "Charlie", "Bravo" })
        {
            await _service.CreateAsync(Dto(name));
        }

        var lookup = await _service.GetLookupAsync();
        lookup.Select(l => l.Name).ShouldBe(["Alpha", "Bravo", "Charlie", "Delta"]);
        lookup.ShouldAllBe(l => l.Id != Guid.Empty && l.Code == null);
        (await _service.GetLookupAsync("AR")).Select(l => l.Name).ShouldBe(["Charlie"]);
        (await _service.GetLookupAsync(take: 2)).Count.ShouldBe(2);
        (await _service.GetLookupAsync(take: 0)).Count.ShouldBe(1, "take is clamped to at least one");
        (await _service.GetLookupAsync(take: 100_000)).Count.ShouldBe(4);
    }

    [Fact]
    public async Task The_list_and_the_lookup_never_read_the_body_but_the_detail_does()
    {
        using var _ = InTenantA();
        var big = "<p>" + new string('b', PlatformMasterDataConsts.MaxHtmlBodyLength - 7) + "</p>";
        var created = await _service.CreateAsync(Dto("Big body", configure: d => d.HtmlBody = big));
        await _service.CreateAsync(Dto("Other", EmailTemplatePurpose.TransportOrder));

        using var capture = EfCommandCapture.Begin();
        await Task.Delay(EfCommandCapture.LoggingCacheTime);

        /* Control: the detail reads the body, so the capture sees the template commands. */
        (await _service.GetAsync(created.Id)).HtmlBody.Length.ShouldBe(200_000);
        capture.On("EmailTemplates").ShouldContain(c => c.Contains("\"HtmlBody\"", StringComparison.Ordinal), "control: the detail reads the body");
        capture.Clear();

        (await _service.GetListAsync(Request())).Rows.Count.ShouldBe(2);
        (await _service.GetListAsync(Request(search: "big", purpose: EmailTemplatePurpose.Invoice, sortBy: "culture", sortDir: "desc"))).Rows.ShouldHaveSingleItem();
        (await _service.GetLookupAsync()).Count.ShouldBe(2);
        (await _service.GetLookupAsync("big")).ShouldHaveSingleItem().Id.ShouldBe(created.Id);

        var commands = capture.On("EmailTemplates");
        commands.Count.ShouldBeGreaterThanOrEqualTo(4, string.Join("\n---\n", capture.Commands));
        commands.ShouldAllBe(c => !c.Contains("\"HtmlBody\"", StringComparison.Ordinal), "the list and the lookup project their columns (ReadRowsAsync / ReadLookupsAsync):\n" + string.Join("\n---\n", commands));
        commands.ShouldAllBe(c => !c.Contains("\"Subject\"", StringComparison.Ordinal), "nor the subject");
    }

    // ── Delete ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_and_bulk_delete_remove_templates_and_a_second_run_removes_nothing()
    {
        using var _ = InTenantA();
        var a = await _service.CreateAsync(Dto("A invoice", culture: "nl"));
        var b = await _service.CreateAsync(Dto("B invoice", culture: "en"));
        var c = await _service.CreateAsync(Dto("C order", EmailTemplatePurpose.TransportOrder));
        var d = await _service.CreateAsync(Dto("D order", EmailTemplatePurpose.TransportOrder));

        await _service.DeleteAsync(a.Id);
        await Should.ThrowAsync<EntityNotFoundException>(() => _service.GetAsync(a.Id));

        (await _service.BulkDeleteAsync(new EmailTemplateBulkDeleteRequestDto { Selection = Explicit(b.Id, b.Id, Guid.NewGuid()) })).DeletedCount.ShouldBe(1);

        /* Filter based: the purpose filter and the search apply, the excluded ids stay. */
        (await _service.BulkDeleteAsync(new EmailTemplateBulkDeleteRequestDto { Selection = FilterBased(null, d.Id), Purpose = EmailTemplatePurpose.TransportOrder }))
            .DeletedCount.ShouldBe(1);
        (await _service.GetListAsync(Request())).Rows.ShouldHaveSingleItem().Id.ShouldBe(d.Id);
        (await _service.BulkDeleteAsync(new EmailTemplateBulkDeleteRequestDto { Selection = FilterBased("no such"), Purpose = EmailTemplatePurpose.TransportOrder })).DeletedCount.ShouldBe(0);
        (await _service.BulkDeleteAsync(new EmailTemplateBulkDeleteRequestDto { Selection = FilterBased(), Purpose = EmailTemplatePurpose.Invoice })).DeletedCount.ShouldBe(0);
        (await _service.BulkDeleteAsync(new EmailTemplateBulkDeleteRequestDto { Selection = Explicit(a.Id, b.Id, c.Id) })).DeletedCount.ShouldBe(0, "deleted rows are not deleted twice");
        (await _service.GetAsync(d.Id)).Name.ShouldBe("D order");
    }

    [Fact]
    public async Task A_bulk_delete_of_more_than_1000_explicit_ids_is_refused()
    {
        using var _ = InTenantA();
        var ids = Enumerable.Range(0, 1001).Select(_ => Guid.NewGuid()).ToArray();
        (await ShouldFailAsync(() => _service.BulkDeleteAsync(new EmailTemplateBulkDeleteRequestDto { Selection = Explicit(ids) }), PlatformErrorCodes.TooManyRows))
            .Data["max"].ShouldBe(1000);
    }

    // ── Tenant isolation (Critical; PostgreSQL in PlatformMasterDataTenantIsolationTests) ──

    [Fact]
    public async Task Tenant_B_neither_sees_nor_touches_tenant_As_template_and_has_its_own_name_and_default_space()
    {
        EmailTemplateDto mine;
        using (InTenantA())
        {
            mine = await _service.CreateAsync(Dto("Invoice mail", culture: "nl", isDefault: true, configure: d => d.HtmlBody = "<p>tenant A only</p>"));
        }

        using (InTenantB())
        {
            (await _service.GetListAsync(Request())).TotalCount.ShouldBe(0);
            (await _service.GetListAsync(Request(search: "invoice", purpose: EmailTemplatePurpose.Invoice))).Rows.ShouldBeEmpty();
            (await _service.GetLookupAsync()).ShouldBeEmpty();
            (await _service.GetLookupAsync("invoice")).ShouldBeEmpty();
            await Should.ThrowAsync<EntityNotFoundException>(() => _service.GetAsync(mine.Id));
            await Should.ThrowAsync<EntityNotFoundException>(() => _service.UpdateAsync(mine.Id, UpdateFrom(mine, u => { u.Name = "Stolen"; u.HtmlBody = "<p>stolen</p>"; })));
            await Should.ThrowAsync<EntityNotFoundException>(() => _service.DeleteAsync(mine.Id));
            (await _service.BulkDeleteAsync(new EmailTemplateBulkDeleteRequestDto { Selection = Explicit(mine.Id) })).DeletedCount.ShouldBe(0);
            (await _service.BulkDeleteAsync(new EmailTemplateBulkDeleteRequestDto { Selection = FilterBased() })).DeletedCount.ShouldBe(0);
            (await _service.CreateAsync(Dto("INVOICE MAIL", culture: "nl", isDefault: true))).IsDefault.ShouldBeTrue("tenant B's default does not clear tenant A's");
        }

        using (InTenantA())
        {
            var read = await _service.GetAsync(mine.Id);
            (read.Name, read.IsDefault, read.Culture, read.HtmlBody).ShouldBe(("Invoice mail", true, "nl", "<p>tenant A only</p>"));
            (await _service.GetListAsync(Request())).Rows.ShouldHaveSingleItem().Id.ShouldBe(mine.Id);
            (await _service.GetLookupAsync()).ShouldHaveSingleItem().Id.ShouldBe(mine.Id);
        }
    }
}
