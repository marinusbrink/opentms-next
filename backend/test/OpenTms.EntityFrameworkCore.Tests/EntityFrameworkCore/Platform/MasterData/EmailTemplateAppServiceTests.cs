using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Platform;
using OpenTms.Platform.EmailTemplates;
using Shouldly;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Validation;
using Xunit;

namespace OpenTms.EntityFrameworkCore.Platform.MasterData;

/// <summary>
/// EmailTemplateAppService on SQLite (docs/designs/49-platform-master-data.md, test risk analysis "Scriban/regex validation,
/// guards when in use, default switching", High, table-driven): the CRUD round trip with per-language content, the name
/// unique regardless of case, every Scriban field parsed on save (field, language and line in the refusal), English
/// content required, configured languages only, the 200,000-character body bound, one default per purpose (setting a new
/// one clears the previous), the list (search, sort whitelist, purpose filter, paging), lookup, delete and bulk delete,
/// and the concurrency stamp. The partial unique index itself is tested on PostgreSQL (PlatformMasterDataUniqueIndexTests).
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class EmailTemplateAppServiceTests : PlatformMasterDataAppServiceTestBase
{
    private readonly IEmailTemplateAppService _service;
    private readonly IRepository<EmailTemplateContent> _contents;

    public EmailTemplateAppServiceTests()
    {
        _service = GetRequiredService<IEmailTemplateAppService>();
        _contents = GetRequiredService<IRepository<EmailTemplateContent>>();
    }

    private static EmailTemplateContentInputDto Content(string culture, string subject = "Invoice {{ invoice.number }}", string body = "<p>Dear {{ customer.name }},</p>")
        => new() { Culture = culture, Subject = subject, HtmlBody = body };

    private static EmailTemplateCreateDto Dto(string name, EmailTemplatePurpose purpose = EmailTemplatePurpose.Invoice, bool isDefault = false, Action<EmailTemplateCreateDto>? configure = null)
    {
        var dto = new EmailTemplateCreateDto { Name = name, Purpose = purpose, IsDefault = isDefault, Contents = [Content("en")] };
        configure?.Invoke(dto);
        return dto;
    }

    private static EmailTemplateUpdateDto UpdateFrom(EmailTemplateDto dto, Action<EmailTemplateUpdateDto>? configure = null)
    {
        var update = new EmailTemplateUpdateDto
        {
            Name = dto.Name, Purpose = dto.Purpose, IsDefault = dto.IsDefault, SenderAddress = dto.SenderAddress, SenderName = dto.SenderName,
            ReplyTo = dto.ReplyTo, To = dto.To, Cc = dto.Cc, Bcc = dto.Bcc, SendCopyToSender = dto.SendCopyToSender, Description = dto.Description,
            Contents = dto.Contents.Select(c => new EmailTemplateContentInputDto { Culture = c.Culture, Subject = c.Subject, HtmlBody = c.HtmlBody }).ToList(),
            ConcurrencyStamp = dto.ConcurrencyStamp
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
    public async Task A_template_round_trips_with_its_addresses_and_its_content_per_language()
    {
        using var _ = InTenantA();
        var created = await _service.CreateAsync(Dto("  Invoice mail  ", configure: d =>
        {
            d.SenderAddress = " {{ company.email }} ";
            d.SenderName = "{{ company.name }}";
            d.ReplyTo = "invoices@example.test";
            d.To = "{{ customer.invoice_email }}";
            d.Cc = "";
            d.Bcc = "archive@example.test";
            d.SendCopyToSender = true;
            d.Description = "  Sent with every invoice  ";
            d.Contents = [Content("NL", "Factuur {{ invoice.number }}", "<p>Beste {{ customer.name }},</p>"), Content("en")];
        }));

        created.Name.ShouldBe("Invoice mail");
        created.Purpose.ShouldBe(EmailTemplatePurpose.Invoice);
        created.SenderAddress.ShouldBe("{{ company.email }}");
        created.SenderName.ShouldBe("{{ company.name }}");
        created.ReplyTo.ShouldBe("invoices@example.test");
        created.To.ShouldBe("{{ customer.invoice_email }}");
        created.Cc.ShouldBeNull("an empty address field is no address");
        created.Bcc.ShouldBe("archive@example.test");
        created.SendCopyToSender.ShouldBeTrue();
        created.Description.ShouldBe("Sent with every invoice");
        created.ConcurrencyStamp.ShouldNotBeNullOrEmpty();
        created.Contents.Select(c => c.Culture).ShouldBe(["en", "nl"], "ordered by culture, the culture normalized");
        created.Contents.Single(c => c.Culture == "nl").Subject.ShouldBe("Factuur {{ invoice.number }}");

        var read = await _service.GetAsync(created.Id);
        read.Contents.Count.ShouldBe(2);
        read.Contents.Single(c => c.Culture == "nl").HtmlBody.ShouldBe("<p>Beste {{ customer.name }},</p>");

        var row = (await _service.GetListAsync(Request())).Rows.ShouldHaveSingleItem();
        (row.Id, row.Name, row.Purpose, row.IsDefault, row.Cultures).ShouldBe((created.Id, "Invoice mail", EmailTemplatePurpose.Invoice, false, "en, nl"));
    }

    [Fact]
    public async Task Update_replaces_the_content_rows_and_the_fields()
    {
        using var _ = InTenantA();
        var created = await _service.CreateAsync(Dto("Invoice mail", configure: d => d.Contents = [Content("en"), Content("nl")]));

        var updated = await _service.UpdateAsync(created.Id, UpdateFrom(created, u =>
        {
            u.Name = "Invoice mail v2";
            u.Purpose = EmailTemplatePurpose.InvoiceReminder;
            u.SendCopyToSender = true;
            u.Contents = [Content("en", "Reminder {{ invoice.number }}", "<p>Reminder</p>")];
        }));

        updated.Name.ShouldBe("Invoice mail v2");
        updated.Purpose.ShouldBe(EmailTemplatePurpose.InvoiceReminder);
        updated.SendCopyToSender.ShouldBeTrue();
        updated.Contents.ShouldHaveSingleItem().Subject.ShouldBe("Reminder {{ invoice.number }}");
        updated.ConcurrencyStamp.ShouldNotBe(created.ConcurrencyStamp);
        (await WithUnitOfWorkAsync(() => _contents.CountAsync(c => c.EmailTemplateId == created.Id))).ShouldBe(1, "the dropped language's row is removed");
        (await _service.GetListAsync(Request())).Rows.ShouldHaveSingleItem().Cultures.ShouldBe("en");

        /* Adding the language back. */
        var again = await _service.UpdateAsync(created.Id, UpdateFrom(updated, u => u.Contents = [Content("nl", "Herinnering", "<p>Herinnering</p>"), Content("en")]));
        again.Contents.Select(c => c.Culture).ShouldBe(["en", "nl"]);
    }

    [Fact]
    public async Task A_stale_concurrency_stamp_is_refused()
    {
        using var _ = InTenantA();
        var created = await _service.CreateAsync(Dto("Invoice mail"));
        await _service.UpdateAsync(created.Id, UpdateFrom(created, u => u.Description = "first"));

        await Should.ThrowAsync<AbpDbConcurrencyException>(() => _service.UpdateAsync(created.Id, UpdateFrom(created, u => u.Description = "second")));
        (await _service.GetAsync(created.Id)).Description.ShouldBe("first");
    }

    // ── Name unique regardless of case ───────────────────────────────────────

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

    // ── Scriban validation (table) ───────────────────────────────────────────

    public static IEnumerable<object[]> ScribanFields() =>
    [
        ["senderAddress", null!], ["senderName", null!], ["replyTo", null!], ["to", null!], ["cc", null!], ["bcc", null!],
        ["subject", "en"], ["subject", "nl"], ["htmlBody", "en"], ["htmlBody", "nl"],
    ];

    private static void Put(EmailTemplateCreateDto dto, string field, string? culture, string text)
    {
        switch (field)
        {
            case "senderAddress": dto.SenderAddress = text; break;
            case "senderName": dto.SenderName = text; break;
            case "replyTo": dto.ReplyTo = text; break;
            case "to": dto.To = text; break;
            case "cc": dto.Cc = text; break;
            case "bcc": dto.Bcc = text; break;
            case "subject": dto.Contents.Single(c => c.Culture == culture).Subject = text; break;
            case "htmlBody": dto.Contents.Single(c => c.Culture == culture).HtmlBody = text; break;
            default: throw new ArgumentOutOfRangeException(nameof(field));
        }
    }

    [Theory]
    [MemberData(nameof(ScribanFields))]
    public async Task A_Scriban_syntax_error_in_any_field_is_refused_naming_the_field_the_language_and_the_line(string field, string? culture)
    {
        using var _ = InTenantA();

        var create = Dto("Broken", configure: d => d.Contents = [Content("en"), Content("nl")]);
        Put(create, field, culture, "Hello {{ 1 + }}");
        var ex = await ShouldFailAsync(() => _service.CreateAsync(create), PlatformErrorCodes.TemplateSyntaxError);
        ex.Data["field"].ShouldBe(field);
        ex.Data["language"].ShouldBe(culture ?? "-");
        ex.Data["line"].ShouldBe(1);
        ex.Message.ShouldNotContain("Hello", Case.Insensitive, "the template text never goes into the exception");
        (await _service.GetListAsync(Request())).TotalCount.ShouldBe(0);

        /* On update too, and the stored template is unchanged. */
        var valid = await _service.CreateAsync(Dto("Valid", configure: d => d.Contents = [Content("en"), Content("nl")]));
        var update = UpdateFrom(valid);
        Put(update, field, culture, "{{ if customer }}no end");
        (await ShouldFailAsync(() => _service.UpdateAsync(valid.Id, update), PlatformErrorCodes.TemplateSyntaxError)).Data["field"].ShouldBe(field);
        (await _service.GetAsync(valid.Id)).ShouldSatisfyAllConditions(
            t => t.SenderAddress.ShouldBeNull(),
            t => t.Contents.Single(c => c.Culture == "en").Subject.ShouldBe("Invoice {{ invoice.number }}"));
    }

    [Fact]
    public async Task The_refusal_names_the_line_of_the_error_in_a_multi_line_body()
    {
        using var _ = InTenantA();
        var body = "<html>\n<body>\n<p>{{ customer.name }}</p>\n<p>{{ 1 + }}</p>\n</body>";
        var ex = await ShouldFailAsync(() => _service.CreateAsync(Dto("Broken", configure: d => d.Contents = [Content("en", body: body)])), PlatformErrorCodes.TemplateSyntaxError);
        ex.Data["field"].ShouldBe("htmlBody");
        ex.Data["language"].ShouldBe("en");
        ex.Data["line"].ShouldBe(4, "1-based, as an editor shows lines");
        ex.Data["fieldLabel"].ShouldBe("HTML body (English)", "the message names the field as the screen labels it");
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
            d.SenderAddress = d.SenderName = d.ReplyTo = d.To = d.Cc = d.Bcc = text;
            d.Contents = [Content("en", text, text), Content("nl", text, text)];
        }));
        created.Bcc.ShouldBe(text);
        created.Contents.ShouldAllBe(c => c.Subject == text && c.HtmlBody == text);
    }

    // ── Languages and the body bound ─────────────────────────────────────────

    [Fact]
    public async Task English_content_is_required_and_only_the_configured_languages_are_accepted()
    {
        using var _ = InTenantA();

        (await ShouldFailAsync(() => _service.CreateAsync(Dto("Dutch only", configure: d => d.Contents = [Content("nl")])), PlatformErrorCodes.DefaultLanguageContentRequired))
            .Data["culture"].ShouldBe("en");
        await ShouldFailAsync(() => _service.CreateAsync(Dto("Nothing", configure: d => d.Contents = [])), PlatformErrorCodes.DefaultLanguageContentRequired);
        (await ShouldFailAsync(() => _service.CreateAsync(Dto("German", configure: d => d.Contents = [Content("en"), Content("de")])), PlatformErrorCodes.UnsupportedCulture))
            .Data["culture"].ShouldBe("de");
        await ShouldFailAsync(() => _service.CreateAsync(Dto("Regional", configure: d => d.Contents = [Content("en-GB")])), PlatformErrorCodes.UnsupportedCulture);
        (await ShouldFailAsync(() => _service.CreateAsync(Dto("Twice", configure: d => d.Contents = [Content("en"), Content("EN")])), PlatformErrorCodes.DuplicateKey))
            .Data["field"].ShouldBe("culture");
        (await _service.GetListAsync(Request())).TotalCount.ShouldBe(0);

        /* An update that drops English is refused and changes nothing. */
        var created = await _service.CreateAsync(Dto("Both", configure: d => d.Contents = [Content("en"), Content("nl")]));
        await ShouldFailAsync(() => _service.UpdateAsync(created.Id, UpdateFrom(created, u => u.Contents = [Content("nl")])), PlatformErrorCodes.DefaultLanguageContentRequired);
        (await _service.GetAsync(created.Id)).Contents.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_body_of_200000_characters_is_accepted_and_one_more_is_refused()
    {
        using var _ = InTenantA();
        var max = "<p>" + new string('x', PlatformMasterDataConsts.MaxHtmlBodyLength - 7) + "</p>";
        max.Length.ShouldBe(200_000);

        var created = await _service.CreateAsync(Dto("Big", configure: d => d.Contents = [Content("en", body: max)]));
        (await _service.GetAsync(created.Id)).Contents.Single().HtmlBody.Length.ShouldBe(200_000);

        await Should.ThrowAsync<AbpValidationException>(() => _service.CreateAsync(Dto("Too big", configure: d => d.Contents = [Content("en", body: max + "x")])));
        await Should.ThrowAsync<AbpValidationException>(() => _service.CreateAsync(Dto("Three", configure: d => d.Contents = [Content("en"), Content("nl"), Content("de")])));
        await Should.ThrowAsync<AbpValidationException>(() => _service.CreateAsync(Dto("Unknown purpose", (EmailTemplatePurpose)0)));
        await Should.ThrowAsync<AbpValidationException>(() => _service.CreateAsync(Dto(new string('n', PlatformMasterDataConsts.MaxNameLength + 1))));
        await Should.ThrowAsync<AbpValidationException>(() => _service.CreateAsync(Dto("Long address", configure: d => d.To = new string('a', PlatformMasterDataConsts.MaxEmailAddressFieldLength + 1))));
        await Should.ThrowAsync<AbpValidationException>(() => _service.CreateAsync(Dto("Empty subject", configure: d => d.Contents = [Content("en", subject: "")])));
        (await _service.GetListAsync(Request())).TotalCount.ShouldBe(1);
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
        (await _service.UpdateAsync(a.Id, UpdateFrom(a, u => u.Description = "kept"))).IsDefault.ShouldBeTrue();
        (await DefaultsAsync(EmailTemplatePurpose.TransportOrder)).ShouldBe(["A"]);

        /* Clearing the flag leaves the purpose without a default (assumption 7). */
        a = await _service.GetAsync(a.Id);
        await _service.UpdateAsync(a.Id, UpdateFrom(a, u => u.IsDefault = false));
        (await DefaultsAsync(EmailTemplatePurpose.TransportOrder)).ShouldBeEmpty();
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

    // ── List, lookup, delete ─────────────────────────────────────────────────

    [Fact]
    public async Task The_list_searches_name_and_description_filters_on_purpose_sorts_on_the_whitelist_and_pages()
    {
        using var _ = InTenantA();
        await _service.CreateAsync(Dto("Charlie invoice", configure: d => d.Description = "monthly run"));
        await _service.CreateAsync(Dto("Alpha order", EmailTemplatePurpose.TransportOrder, isDefault: true));
        await _service.CreateAsync(Dto("Bravo reminder", EmailTemplatePurpose.InvoiceReminder));

        var all = await _service.GetListAsync(Request());
        all.TotalCount.ShouldBe(3);
        all.Rows.Select(r => r.Name).ShouldBe(["Alpha order", "Bravo reminder", "Charlie invoice"], "default order: name");

        (await _service.GetListAsync(Request(search: "INVOICE"))).Rows.ShouldHaveSingleItem().Name.ShouldBe("Charlie invoice");
        var byDescription = await _service.GetListAsync(Request(search: " Monthly "));
        byDescription.Rows.ShouldHaveSingleItem().Name.ShouldBe("Charlie invoice");
        (byDescription.TotalCount, byDescription.FilteredCount).ShouldBe((3, 1));

        var invoices = await _service.GetListAsync(Request(purpose: EmailTemplatePurpose.Invoice));
        invoices.Rows.ShouldHaveSingleItem().Name.ShouldBe("Charlie invoice");
        invoices.TotalCount.ShouldBe(1, "the filter is part of the total, the search is not");

        (await _service.GetListAsync(Request(sortBy: "name", sortDir: "desc"))).Rows.Select(r => r.Name).ShouldBe(["Charlie invoice", "Bravo reminder", "Alpha order"]);
        (await _service.GetListAsync(Request(sortBy: "Purpose"))).Rows.Select(r => r.Purpose).ShouldBe(
            [EmailTemplatePurpose.TransportOrder, EmailTemplatePurpose.Invoice, EmailTemplatePurpose.InvoiceReminder]);
        (await _service.GetListAsync(Request(sortBy: "isDefault", sortDir: "desc"))).Rows.First().Name.ShouldBe("Alpha order");

        (await ShouldFailAsync(() => _service.GetListAsync(Request(sortBy: "subject")), PlatformErrorCodes.UnknownSortColumn)).Data["sortBy"].ShouldBe("subject");
        await ShouldFailAsync(() => _service.GetListAsync(Request(sortBy: "NormalizedName")), PlatformErrorCodes.UnknownSortColumn);

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

        (await _service.GetLookupAsync()).Select(l => l.Name).ShouldBe(["Alpha", "Bravo", "Charlie", "Delta"]);
        (await _service.GetLookupAsync("AR")).Select(l => l.Name).ShouldBe(["Charlie"]);
        (await _service.GetLookupAsync(take: 2)).Count.ShouldBe(2);
        (await _service.GetLookupAsync(take: 0)).Count.ShouldBe(1, "take is clamped to at least one");
        (await _service.GetLookupAsync(take: 100_000)).Count.ShouldBe(4);
        (await _service.GetLookupAsync()).ShouldAllBe(l => l.Code == null);
    }

    [Fact]
    public async Task Delete_and_bulk_delete_remove_the_template_with_its_content_and_a_second_run_removes_nothing()
    {
        using var _ = InTenantA();
        var a = await _service.CreateAsync(Dto("A invoice", configure: d => d.Contents = [Content("en"), Content("nl")]));
        var b = await _service.CreateAsync(Dto("B invoice"));
        var c = await _service.CreateAsync(Dto("C order", EmailTemplatePurpose.TransportOrder));
        var d = await _service.CreateAsync(Dto("D order", EmailTemplatePurpose.TransportOrder));

        await _service.DeleteAsync(a.Id);
        await Should.ThrowAsync<EntityNotFoundException>(() => _service.GetAsync(a.Id));

        (await _service.BulkDeleteAsync(new EmailTemplateBulkDeleteRequestDto { Selection = Explicit(b.Id, b.Id, Guid.NewGuid()) })).DeletedCount.ShouldBe(1);

        /* Filter based: the purpose filter and the search apply, the excluded ids stay. */
        (await _service.BulkDeleteAsync(new EmailTemplateBulkDeleteRequestDto { Selection = FilterBased(excludedIds: d.Id), Purpose = EmailTemplatePurpose.TransportOrder }))
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
            mine = await _service.CreateAsync(Dto("Invoice mail", isDefault: true));
        }

        using (InTenantB())
        {
            (await _service.GetListAsync(Request())).TotalCount.ShouldBe(0);
            (await _service.GetLookupAsync()).ShouldBeEmpty();
            await Should.ThrowAsync<EntityNotFoundException>(() => _service.GetAsync(mine.Id));
            await Should.ThrowAsync<EntityNotFoundException>(() => _service.UpdateAsync(mine.Id, UpdateFrom(mine, u => u.Name = "Stolen")));
            await Should.ThrowAsync<EntityNotFoundException>(() => _service.DeleteAsync(mine.Id));
            (await _service.BulkDeleteAsync(new EmailTemplateBulkDeleteRequestDto { Selection = Explicit(mine.Id) })).DeletedCount.ShouldBe(0);
            (await _service.BulkDeleteAsync(new EmailTemplateBulkDeleteRequestDto { Selection = FilterBased() })).DeletedCount.ShouldBe(0);
            (await _service.CreateAsync(Dto("INVOICE MAIL", isDefault: true))).IsDefault.ShouldBeTrue("tenant B's default does not clear tenant A's");
        }

        using (InTenantA())
        {
            var read = await _service.GetAsync(mine.Id);
            (read.Name, read.IsDefault).ShouldBe(("Invoice mail", true));
            (await _service.GetListAsync(Request())).Rows.ShouldHaveSingleItem().Id.ShouldBe(mine.Id);
        }
    }
}
