using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Platform;
using OpenTms.Platform.ReportDefinitions;
using Shouldly;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Validation;
using Xunit;

namespace OpenTms.EntityFrameworkCore.Platform.MasterData;

/// <summary>
/// ReportDefinitionAppService on SQLite (docs/designs/49-platform-master-data.md, test risk analysis "Scriban/regex
/// validation, guards when in use, default switching", High): the round trip (language normalized, ".pdf" added to the
/// Scriban file-name pattern), configured languages only, the file-name pattern parsed on save, the name unique regardless
/// of case, one default per purpose and language (a new default clears the previous one of that purpose and language
/// only), archive and unarchive (hidden from list and lookup), the list (search, sort whitelist, purpose and archived
/// filters), bulk delete under the list's filters, and the concurrency stamp.
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class ReportDefinitionAppServiceTests : PlatformMasterDataAppServiceTestBase
{
    private readonly IReportDefinitionAppService _service;

    public ReportDefinitionAppServiceTests()
    {
        _service = GetRequiredService<IReportDefinitionAppService>();
    }

    private static ReportDefinitionCreateDto Dto(string name, ReportPurpose purpose = ReportPurpose.Invoice, string culture = "en", bool isDefault = false, Action<ReportDefinitionCreateDto>? configure = null)
    {
        var dto = new ReportDefinitionCreateDto
        {
            Name = name, Purpose = purpose, Culture = culture, IsDefault = isDefault, Provider = ReportRenderingProvider.PdfGeneratorApi,
            ProviderTemplateId = "tpl-" + name.Trim().ToLowerInvariant().Replace(' ', '-'), FileNamePattern = "invoice-{{ number }}"
        };
        configure?.Invoke(dto);
        return dto;
    }

    private static ReportDefinitionUpdateDto UpdateFrom(ReportDefinitionDto dto, Action<ReportDefinitionUpdateDto>? configure = null)
    {
        var update = new ReportDefinitionUpdateDto
        {
            Name = dto.Name, Purpose = dto.Purpose, Culture = dto.Culture, Provider = dto.Provider, ProviderTemplateId = dto.ProviderTemplateId,
            FileNamePattern = dto.FileNamePattern, IsDefault = dto.IsDefault, IsArchived = dto.IsArchived,
            AvailableInCustomerPortal = dto.AvailableInCustomerPortal, Description = dto.Description, ConcurrencyStamp = dto.ConcurrencyStamp
        };
        configure?.Invoke(update);
        return update;
    }

    private static ReportDefinitionListRequestDto Request(string? search = null, ReportPurpose? purpose = null, bool includeArchived = false, string? sortBy = null, string? sortDir = null)
    {
        var request = List<ReportDefinitionListRequestDto>(search: search, sortBy: sortBy, sortDir: sortDir);
        request.Purpose = purpose;
        request.IncludeArchived = includeArchived;
        return request;
    }

    private async Task<List<string>> DefaultsAsync(ReportPurpose purpose, string culture)
        => (await _service.GetListAsync(Request(purpose: purpose, includeArchived: true))).Rows.Where(r => r.IsDefault && r.Culture == culture).Select(r => r.Name).ToList();

    // ── Round trip, languages, file name pattern ─────────────────────────────

    [Fact]
    public async Task A_definition_round_trips_with_its_language_normalized_and_pdf_added_to_the_pattern()
    {
        using var _ = InTenantA();
        var created = await _service.CreateAsync(Dto(" Invoice NL ", culture: " NL ", configure: d =>
        {
            d.ProviderTemplateId = "  123456  ";
            d.FileNamePattern = " factuur-{{ invoice.number }} ";
            d.AvailableInCustomerPortal = true;
            d.Description = " For Dutch customers ";
        }));

        created.Name.ShouldBe("Invoice NL");
        created.Culture.ShouldBe("nl");
        created.Provider.ShouldBe(ReportRenderingProvider.PdfGeneratorApi);
        created.ProviderTemplateId.ShouldBe("123456");
        created.FileNamePattern.ShouldBe("factuur-{{ invoice.number }}.pdf");
        created.AvailableInCustomerPortal.ShouldBeTrue();
        created.Description.ShouldBe("For Dutch customers");
        created.IsDefault.ShouldBeFalse();
        created.IsArchived.ShouldBeFalse();

        (await _service.CreateAsync(Dto("Upper", configure: d => d.FileNamePattern = "INVOICE.PDF"))).FileNamePattern.ShouldBe("INVOICE.PDF", "an existing extension is kept in any case");
        (await _service.CreateAsync(Dto("Pdf in middle", configure: d => d.FileNamePattern = "a.pdf-{{ n }}"))).FileNamePattern.ShouldBe("a.pdf-{{ n }}.pdf");

        var row = (await _service.GetListAsync(Request(search: "invoice nl"))).Rows.ShouldHaveSingleItem();
        (row.Name, row.Purpose, row.Culture, row.Provider, row.IsDefault, row.IsArchived).ShouldBe(("Invoice NL", ReportPurpose.Invoice, "nl", ReportRenderingProvider.PdfGeneratorApi, false, false));
    }

    [Theory]
    [InlineData("de")]
    [InlineData("en-GB")]
    [InlineData("xx")]
    public async Task A_language_that_is_not_configured_is_refused_on_create_and_on_update(string culture)
    {
        using var _ = InTenantA();
        (await ShouldFailAsync(() => _service.CreateAsync(Dto("Other", culture: culture)), PlatformErrorCodes.UnsupportedCulture)).Data["culture"].ShouldBe(culture.ToLowerInvariant());

        var created = await _service.CreateAsync(Dto("English"));
        await ShouldFailAsync(() => _service.UpdateAsync(created.Id, UpdateFrom(created, u => u.Culture = culture)), PlatformErrorCodes.UnsupportedCulture);
        (await _service.GetAsync(created.Id)).Culture.ShouldBe("en");
        await Should.ThrowAsync<AbpValidationException>(() => _service.CreateAsync(Dto("Empty", culture: "")));
    }

    [Theory]
    /* Not "invoice-{{ number": Scriban's parser closes an unterminated block at the end of the text, no error. */
    [InlineData("invoice-{{ 1 + }}")]
    [InlineData("{{ if x }}invoice")]
    public async Task A_file_name_pattern_with_a_Scriban_syntax_error_is_refused(string pattern)
    {
        using var _ = InTenantA();
        var ex = await ShouldFailAsync(() => _service.CreateAsync(Dto("Broken", configure: d => d.FileNamePattern = pattern)), PlatformErrorCodes.TemplateSyntaxError);
        ex.Data["field"].ShouldBe("fileNamePattern");
        ex.Data["language"].ShouldBe("-");
        ex.Data["line"].ShouldBe(1);
        (await _service.GetListAsync(Request(includeArchived: true))).TotalCount.ShouldBe(0);

        var created = await _service.CreateAsync(Dto("Valid"));
        await ShouldFailAsync(() => _service.UpdateAsync(created.Id, UpdateFrom(created, u => u.FileNamePattern = pattern)), PlatformErrorCodes.TemplateSyntaxError);
        (await _service.GetAsync(created.Id)).FileNamePattern.ShouldBe("invoice-{{ number }}.pdf");
    }

    [Fact]
    public async Task The_pattern_bound_counts_the_added_extension_and_the_other_inputs_are_validated()
    {
        using var _ = InTenantA();
        var longest = new string('p', PlatformMasterDataConsts.MaxFileNamePatternLength - 4);
        (await _service.CreateAsync(Dto("Longest", configure: d => d.FileNamePattern = longest))).FileNamePattern.Length.ShouldBe(255);

        await Should.ThrowAsync<AbpValidationException>(() => _service.CreateAsync(Dto("Too long", configure: d => d.FileNamePattern = longest + "p")));
        await Should.ThrowAsync<AbpValidationException>(() => _service.CreateAsync(Dto("No template", configure: d => d.ProviderTemplateId = "")));
        await Should.ThrowAsync<AbpValidationException>(() => _service.CreateAsync(Dto("No pattern", configure: d => d.FileNamePattern = "")));
        await Should.ThrowAsync<AbpValidationException>(() => _service.CreateAsync(Dto("Unknown purpose", (ReportPurpose)0)));
        await Should.ThrowAsync<AbpValidationException>(() => _service.CreateAsync(Dto("Unknown provider", configure: d => d.Provider = (ReportRenderingProvider)2)));
        (await _service.GetListAsync(Request(includeArchived: true))).TotalCount.ShouldBe(1);
    }

    // ── Name unique regardless of case ───────────────────────────────────────

    [Fact]
    public async Task A_name_differing_only_in_case_is_a_duplicate_while_the_own_name_may_change_case()
    {
        using var _ = InTenantA();
        var created = await _service.CreateAsync(Dto("Invoice layout"));

        (await ShouldFailAsync(() => _service.CreateAsync(Dto("INVOICE LAYOUT", ReportPurpose.PurchaseOrder, "nl")), PlatformErrorCodes.DuplicateKey)).Data["field"].ShouldBe("name");
        var other = await _service.CreateAsync(Dto("Order layout"));
        await ShouldFailAsync(() => _service.UpdateAsync(other.Id, UpdateFrom(other, u => u.Name = "invoice layout")), PlatformErrorCodes.DuplicateKey);

        (await _service.UpdateAsync(created.Id, UpdateFrom(created, u => u.Name = "Invoice LAYOUT"))).Name.ShouldBe("Invoice LAYOUT");

        /* An archived definition still holds its name; a deleted one frees it. */
        await _service.ArchiveAsync(created.Id);
        await ShouldFailAsync(() => _service.CreateAsync(Dto("invoice layout")), PlatformErrorCodes.DuplicateKey);
        await _service.DeleteAsync(created.Id);
        (await _service.CreateAsync(Dto("invoice layout"))).Id.ShouldNotBe(created.Id);
    }

    [Fact]
    public async Task A_stale_concurrency_stamp_is_refused()
    {
        using var _ = InTenantA();
        var created = await _service.CreateAsync(Dto("Layout"));
        await _service.UpdateAsync(created.Id, UpdateFrom(created, u => u.Description = "first"));
        await Should.ThrowAsync<AbpDbConcurrencyException>(() => _service.UpdateAsync(created.Id, UpdateFrom(created, u => u.Description = "second")));
        (await _service.GetAsync(created.Id)).Description.ShouldBe("first");
    }

    // ── One default per purpose and language ─────────────────────────────────

    [Fact]
    public async Task A_new_default_clears_the_previous_one_of_the_same_purpose_and_language_only()
    {
        using var _ = InTenantA();
        var enA = await _service.CreateAsync(Dto("Invoice EN A", isDefault: true));
        var nl = await _service.CreateAsync(Dto("Invoice NL", culture: "nl", isDefault: true));
        var order = await _service.CreateAsync(Dto("Order EN", ReportPurpose.PurchaseOrder, isDefault: true));
        (await DefaultsAsync(ReportPurpose.Invoice, "en")).ShouldBe(["Invoice EN A"]);
        (await DefaultsAsync(ReportPurpose.Invoice, "nl")).ShouldBe(["Invoice NL"], "one default per language");

        var enB = await _service.CreateAsync(Dto("Invoice EN B", isDefault: true));
        (await DefaultsAsync(ReportPurpose.Invoice, "en")).ShouldBe(["Invoice EN B"]);
        (await DefaultsAsync(ReportPurpose.Invoice, "nl")).ShouldBe(["Invoice NL"]);
        (await DefaultsAsync(ReportPurpose.PurchaseOrder, "en")).ShouldBe(["Order EN"]);
        (await _service.GetAsync(enA.Id)).IsDefault.ShouldBeFalse();

        /* Moving the Dutch default to English (keeping the flag) takes over the English default. */
        var nlDto = await _service.GetAsync(nl.Id);
        await _service.UpdateAsync(nl.Id, UpdateFrom(nlDto, u => u.Culture = "en"));
        (await DefaultsAsync(ReportPurpose.Invoice, "en")).ShouldBe(["Invoice NL"]);
        (await DefaultsAsync(ReportPurpose.Invoice, "nl")).ShouldBeEmpty();
        (await _service.GetAsync(enB.Id)).IsDefault.ShouldBeFalse();

        /* Updating a non-default to default on update clears the current one. */
        var enADto = await _service.GetAsync(enA.Id);
        (await _service.UpdateAsync(enA.Id, UpdateFrom(enADto, u => u.IsDefault = true))).IsDefault.ShouldBeTrue();
        (await DefaultsAsync(ReportPurpose.Invoice, "en")).ShouldBe(["Invoice EN A"]);
        (await _service.GetAsync(order.Id)).IsDefault.ShouldBeTrue();
    }

    [Fact]
    public async Task An_archived_definition_may_stay_the_default_and_a_default_may_be_deleted()
    {
        /* Design 49, assumption 7 (open for the PO). */
        using var _ = InTenantA();
        var a = await _service.CreateAsync(Dto("A", isDefault: true));
        await _service.ArchiveAsync(a.Id);
        (await _service.GetAsync(a.Id)).ShouldSatisfyAllConditions(d => d.IsDefault.ShouldBeTrue(), d => d.IsArchived.ShouldBeTrue());

        await _service.DeleteAsync(a.Id);
        (await DefaultsAsync(ReportPurpose.Invoice, "en")).ShouldBeEmpty();
        (await _service.CreateAsync(Dto("B", isDefault: true))).IsDefault.ShouldBeTrue();
    }

    // ── Archive, list, lookup, delete ────────────────────────────────────────

    [Fact]
    public async Task Archive_and_unarchive_are_idempotent_and_archived_definitions_leave_the_list_and_the_lookup()
    {
        using var _ = InTenantA();
        var a = await _service.CreateAsync(Dto("Alpha"));
        var b = await _service.CreateAsync(Dto("Bravo"));

        await _service.ArchiveAsync(a.Id);
        await _service.ArchiveAsync(a.Id);
        (await _service.GetAsync(a.Id)).IsArchived.ShouldBeTrue();
        (await _service.GetListAsync(Request())).Rows.ShouldHaveSingleItem().Id.ShouldBe(b.Id);
        (await _service.GetListAsync(Request(includeArchived: true))).TotalCount.ShouldBe(2);
        (await _service.GetLookupAsync()).ShouldHaveSingleItem().Id.ShouldBe(b.Id);
        (await _service.GetLookupAsync("Alpha")).ShouldBeEmpty();

        await _service.UnarchiveAsync(a.Id);
        await _service.UnarchiveAsync(a.Id);
        (await _service.GetLookupAsync()).Select(l => l.Name).ShouldBe(["Alpha", "Bravo"]);

        /* Archived through the editor as well. */
        var bDto = await _service.GetAsync(b.Id);
        (await _service.UpdateAsync(b.Id, UpdateFrom(bDto, u => u.IsArchived = true))).IsArchived.ShouldBeTrue();
        (await _service.CreateAsync(Dto("Charlie", configure: d => d.IsArchived = true))).IsArchived.ShouldBeTrue();
        (await _service.GetLookupAsync()).ShouldHaveSingleItem().Name.ShouldBe("Alpha");

        await Should.ThrowAsync<EntityNotFoundException>(() => _service.ArchiveAsync(Guid.NewGuid()));
        await Should.ThrowAsync<EntityNotFoundException>(() => _service.UnarchiveAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task The_list_searches_filters_and_sorts_on_the_whitelist_only()
    {
        using var _ = InTenantA();
        await _service.CreateAsync(Dto("Charlie", ReportPurpose.Invoice, "nl", isDefault: true, configure: d => d.Description = "monthly"));
        await _service.CreateAsync(Dto("Alpha", ReportPurpose.PurchaseOrder, "en"));
        var bravo = await _service.CreateAsync(Dto("Bravo", ReportPurpose.TransportFile, "en"));
        await _service.ArchiveAsync(bravo.Id);

        (await _service.GetListAsync(Request())).Rows.Select(r => r.Name).ShouldBe(["Alpha", "Charlie"]);
        (await _service.GetListAsync(Request(includeArchived: true))).Rows.Select(r => r.Name).ShouldBe(["Alpha", "Bravo", "Charlie"]);
        (await _service.GetListAsync(Request(search: "MONTH"))).Rows.ShouldHaveSingleItem().Name.ShouldBe("Charlie");
        (await _service.GetListAsync(Request(purpose: ReportPurpose.PurchaseOrder))).Rows.ShouldHaveSingleItem().Name.ShouldBe("Alpha");
        (await _service.GetListAsync(Request(purpose: ReportPurpose.TransportFile))).TotalCount.ShouldBe(0);
        (await _service.GetListAsync(Request(purpose: ReportPurpose.TransportFile, includeArchived: true))).TotalCount.ShouldBe(1);

        (await _service.GetListAsync(Request(includeArchived: true, sortBy: "name", sortDir: "DESC"))).Rows.Select(r => r.Name).ShouldBe(["Charlie", "Bravo", "Alpha"]);
        /* TransportFile (2) < Invoice (3) < PurchaseOrder (4). */
        (await _service.GetListAsync(Request(includeArchived: true, sortBy: "purpose"))).Rows.Select(r => r.Name).ShouldBe(["Bravo", "Charlie", "Alpha"]);
        (await _service.GetListAsync(Request(includeArchived: true, sortBy: "culture", sortDir: "desc"))).Rows.First().Name.ShouldBe("Charlie");
        (await _service.GetListAsync(Request(includeArchived: true, sortBy: "isDefault", sortDir: "desc"))).Rows.First().Name.ShouldBe("Charlie");
        (await _service.GetListAsync(Request(includeArchived: true, sortBy: "isArchived", sortDir: "desc"))).Rows.First().Name.ShouldBe("Bravo");
        (await _service.GetListAsync(Request(includeArchived: true, sortBy: "provider"))).Rows.Count.ShouldBe(3);
        (await ShouldFailAsync(() => _service.GetListAsync(Request(sortBy: "providerTemplateId")), PlatformErrorCodes.UnknownSortColumn)).Data["sortBy"].ShouldBe("providerTemplateId");
        await ShouldFailAsync(() => _service.GetListAsync(Request(sortBy: "fileNamePattern")), PlatformErrorCodes.UnknownSortColumn);
    }

    [Fact]
    public async Task Bulk_delete_follows_the_lists_filters_and_a_second_run_removes_nothing()
    {
        using var _ = InTenantA();
        var a = await _service.CreateAsync(Dto("A invoice"));
        var b = await _service.CreateAsync(Dto("B invoice"));
        var c = await _service.CreateAsync(Dto("C order", ReportPurpose.PurchaseOrder));
        await _service.ArchiveAsync(b.Id);

        /* FilterBased without IncludeArchived leaves the archived row, the purpose filter leaves the order. */
        (await _service.BulkDeleteAsync(new ReportDefinitionBulkDeleteRequestDto { Selection = FilterBased(), Purpose = ReportPurpose.Invoice })).DeletedCount.ShouldBe(1);
        await Should.ThrowAsync<EntityNotFoundException>(() => _service.GetAsync(a.Id));
        (await _service.GetAsync(b.Id)).IsArchived.ShouldBeTrue();
        (await _service.BulkDeleteAsync(new ReportDefinitionBulkDeleteRequestDto { Selection = FilterBased("invoice"), Purpose = ReportPurpose.Invoice, IncludeArchived = true })).DeletedCount.ShouldBe(1);
        (await _service.BulkDeleteAsync(new ReportDefinitionBulkDeleteRequestDto { Selection = Explicit(a.Id, b.Id) })).DeletedCount.ShouldBe(0);
        (await _service.GetListAsync(Request(includeArchived: true))).Rows.ShouldHaveSingleItem().Id.ShouldBe(c.Id);
    }
}
