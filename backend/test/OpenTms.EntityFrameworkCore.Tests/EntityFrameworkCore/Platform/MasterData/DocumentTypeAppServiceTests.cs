using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Platform;
using OpenTms.Platform.DocumentTypes;
using Shouldly;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Validation;
using Xunit;

namespace OpenTms.EntityFrameworkCore.Platform.MasterData;

/// <summary>
/// DocumentTypeAppService on SQLite (docs/designs/49-platform-master-data.md, test risk analysis "Scriban/regex validation",
/// High, table-driven): the code upper-cased and unique per tenant, the name per language with English required, the
/// typed transformation pipeline (each kind once, at most four rows, the argument required for the two kinds that take
/// one, a regex argument that compiles, positions renumbered 1, 2, …), archive, the list (search over code, name and
/// description, sort whitelist), lookup, bulk delete and the concurrency stamp.
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class DocumentTypeAppServiceTests : PlatformMasterDataAppServiceTestBase
{
    private readonly IDocumentTypeAppService _service;
    private readonly IRepository<DocumentTypeTransformation> _transformationRows;

    public DocumentTypeAppServiceTests()
    {
        _service = GetRequiredService<IDocumentTypeAppService>();
        _transformationRows = GetRequiredService<IRepository<DocumentTypeTransformation>>();
    }

    private static DocumentTypeTransformationInputDto Step(DocumentTransformationKind kind, int position, string? argument = null)
        => new() { Kind = kind, Position = position, Argument = argument };

    private static DocumentTypeCreateDto Dto(string code, string en = "Freight document", string? nl = null, Action<DocumentTypeCreateDto>? configure = null)
    {
        var dto = new DocumentTypeCreateDto { Code = code, Names = Names(en, nl) };
        configure?.Invoke(dto);
        return dto;
    }

    private static DocumentTypeUpdateDto UpdateFrom(DocumentTypeDto dto, Action<DocumentTypeUpdateDto>? configure = null)
    {
        var names = new PlatformLocalizedNameDto();
        foreach (var (culture, text) in dto.Names)
        {
            names[culture] = text;
        }

        var update = new DocumentTypeUpdateDto
        {
            Code = dto.Code, Names = names, IsFreightDocument = dto.IsFreightDocument, IncludeWithInvoice = dto.IncludeWithInvoice,
            ShowInListIndicator = dto.ShowInListIndicator, IsArchived = dto.IsArchived, Description = dto.Description,
            Transformations = dto.Transformations.Select(t => Step(t.Kind, t.Position, t.Argument)).ToList(),
            ConcurrencyStamp = dto.ConcurrencyStamp
        };
        configure?.Invoke(update);
        return update;
    }

    private static DocumentTypeListRequestDto Request(string? search = null, bool includeArchived = false, string? sortBy = null, string? sortDir = null)
    {
        var request = List<DocumentTypeListRequestDto>(search: search, sortBy: sortBy, sortDir: sortDir);
        request.IncludeArchived = includeArchived;
        return request;
    }

    // ── Round trip ───────────────────────────────────────────────────────────

    [Fact]
    public async Task A_type_round_trips_with_its_code_upper_cased_its_names_flags_and_transformations()
    {
        using var _ = InTenantA();
        var created = await _service.CreateAsync(Dto(" cmr-scan ", "CMR scan", "CMR-scan", d =>
        {
            d.IsFreightDocument = true;
            d.IncludeWithInvoice = true;
            d.ShowInListIndicator = true;
            d.Description = " Scanned consignment notes ";
            d.Transformations = [Step(DocumentTransformationKind.ImageToPdf, 1), Step(DocumentTransformationKind.SplitPdfByReferencePattern, 2, @"^CMR(\d{6})$")];
        }));

        created.Code.ShouldBe("CMR-SCAN");
        created.Names["en"].ShouldBe("CMR scan");
        created.Names["nl"].ShouldBe("CMR-scan");
        (created.IsFreightDocument, created.IncludeWithInvoice, created.ShowInListIndicator, created.IsArchived).ShouldBe((true, true, true, false));
        created.Description.ShouldBe("Scanned consignment notes");
        created.Transformations.Select(t => (t.Position, t.Kind, t.Argument)).ShouldBe(
            [(1, DocumentTransformationKind.ImageToPdf, (string?)null), (2, DocumentTransformationKind.SplitPdfByReferencePattern, @"^CMR(\d{6})$")]);

        using (UiCulture("nl"))
        {
            (await _service.GetAsync(created.Id)).Name.ShouldBe("CMR-scan");
            (await _service.GetListAsync(Request())).Rows.ShouldHaveSingleItem().Name.ShouldBe("CMR-scan");
            (await _service.GetLookupAsync()).ShouldHaveSingleItem().ShouldSatisfyAllConditions(l => l.Name.ShouldBe("CMR-scan"), l => l.Code.ShouldBe("CMR-SCAN"));
        }

        using (UiCulture("en"))
        {
            var row = (await _service.GetListAsync(Request())).Rows.ShouldHaveSingleItem();
            (row.Code, row.Name, row.IsFreightDocument, row.IncludeWithInvoice, row.IsArchived).ShouldBe(("CMR-SCAN", "CMR scan", true, true, false));
        }

        /* A name in English only resolves to English in Dutch too. */
        var english = await _service.CreateAsync(Dto("POD", "Proof of delivery"));
        using (UiCulture("nl"))
        {
            (await _service.GetAsync(english.Id)).Name.ShouldBe("Proof of delivery");
        }
    }

    [Theory]
    [InlineData(null, "Naam")]
    [InlineData("", "Naam")]
    [InlineData("   ", "Naam")]
    public async Task The_English_name_is_required(string? en, string nl)
    {
        using var _ = InTenantA();
        var dto = new DocumentTypeCreateDto { Code = "X", Names = Names(en, nl) };
        if (en == null)
        {
            dto.Names.Remove("en");
        }

        (await ShouldFailAsync(() => _service.CreateAsync(dto), PlatformErrorCodes.NameRequired)).Data["culture"].ShouldBe("en");
        await ShouldFailAsync(() => _service.CreateAsync(Dto("Y", new string('n', 129))), PlatformErrorCodes.NameRequired);
        (await _service.GetListAsync(Request(includeArchived: true))).TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task The_code_is_unique_per_tenant_regardless_of_case_and_freed_by_a_delete()
    {
        using var _ = InTenantA();
        var freight = await _service.CreateAsync(Dto("freight"));
        freight.Code.ShouldBe("FREIGHT");

        var ex = await ShouldFailAsync(() => _service.CreateAsync(Dto(" Freight ", "Other")), PlatformErrorCodes.DuplicateKey);
        ex.Data["field"].ShouldBe("code");
        ex.Data["value"].ShouldBe("FREIGHT");

        var general = await _service.CreateAsync(Dto("GENERAL", "General document"));
        await ShouldFailAsync(() => _service.UpdateAsync(general.Id, UpdateFrom(general, u => u.Code = "freight")), PlatformErrorCodes.DuplicateKey);
        (await _service.GetAsync(general.Id)).Code.ShouldBe("GENERAL");

        /* Its own code in other case is the same code; a new code is fine. */
        (await _service.UpdateAsync(general.Id, UpdateFrom(general, u => u.Code = "general"))).Code.ShouldBe("GENERAL");
        general = await _service.GetAsync(general.Id);
        (await _service.UpdateAsync(general.Id, UpdateFrom(general, u => u.Code = "misc"))).Code.ShouldBe("MISC");

        await _service.DeleteAsync(freight.Id);
        (await _service.CreateAsync(Dto("FREIGHT"))).Id.ShouldNotBe(freight.Id);

        await Should.ThrowAsync<AbpValidationException>(() => _service.CreateAsync(Dto(new string('C', PlatformMasterDataConsts.MaxCodeLength + 1))));
        await Should.ThrowAsync<AbpValidationException>(() => _service.CreateAsync(Dto("")));
    }

    // ── The transformation pipeline (table) ──────────────────────────────────

    public static IEnumerable<object?[]> PipelineCases() =>
    [
        /* kinds:arguments as "Kind=argument"; expected error code or null for accepted. */
        ["ImageToPdf", null],
        ["ImageToPdf;LinkByFileName", null],
        ["RenameByParentProperty=OrderNumber", null],
        ["SplitPdfByReferencePattern=^REF-(\\d+)$", null],
        ["SplitPdfByReferencePattern=(a+)+$", null], // catastrophic, but compiles: it is only ever run with the 1 s timeout
        ["ImageToPdf;LinkByFileName;RenameByParentProperty=Reference;SplitPdfByReferencePattern=\\d+", null],
        ["ImageToPdf;ImageToPdf", PlatformErrorCodes.DuplicateTransformation],
        ["SplitPdfByReferencePattern=a;SplitPdfByReferencePattern=b", PlatformErrorCodes.DuplicateTransformation],
        ["RenameByParentProperty", PlatformErrorCodes.TransformationArgumentRequired],
        ["RenameByParentProperty=   ", PlatformErrorCodes.TransformationArgumentRequired],
        ["SplitPdfByReferencePattern", PlatformErrorCodes.TransformationArgumentRequired],
        ["SplitPdfByReferencePattern=([a-z", PlatformErrorCodes.InvalidRegex],
        ["SplitPdfByReferencePattern=*abc", PlatformErrorCodes.InvalidRegex],
        ["SplitPdfByReferencePattern=(?<name", PlatformErrorCodes.InvalidRegex],
    ];

    private static List<DocumentTypeTransformationInputDto> Pipeline(string spec)
        => spec.Split(';').Select((part, index) =>
        {
            var pieces = part.Split('=', 2);
            return Step(Enum.Parse<DocumentTransformationKind>(pieces[0]), index + 1, pieces.Length > 1 ? pieces[1] : null);
        }).ToList();

    [Theory]
    [MemberData(nameof(PipelineCases))]
    public async Task Each_kind_occurs_once_the_argument_is_required_for_two_kinds_and_a_pattern_must_compile(string spec, string? error)
    {
        using var _ = InTenantA();
        var steps = Pipeline(spec);

        if (error == null)
        {
            var created = await _service.CreateAsync(Dto("T", configure: d => d.Transformations = steps));
            created.Transformations.Select(t => t.Kind).ShouldBe(steps.Select(s => s.Kind));
            created.Transformations.Select(t => t.Position).ShouldBe(Enumerable.Range(1, steps.Count));
            return;
        }

        var ex = await ShouldFailAsync(() => _service.CreateAsync(Dto("T", configure: d => d.Transformations = steps)), error);
        ex.Data["kind"].ShouldNotBeNull();
        ex.Message.ShouldNotContain("([a-z", Case.Sensitive, "the argument never goes into the exception");
        (await _service.GetListAsync(Request(includeArchived: true))).TotalCount.ShouldBe(0);
        (await WithUnitOfWorkAsync(() => _transformationRows.CountAsync())).ShouldBe(0);

        /* The same rules on update; the stored pipeline is unchanged. */
        var valid = await _service.CreateAsync(Dto("V", configure: d => d.Transformations = [Step(DocumentTransformationKind.ImageToPdf, 1)]));
        await ShouldFailAsync(() => _service.UpdateAsync(valid.Id, UpdateFrom(valid, u => u.Transformations = Pipeline(spec))), error);
        (await _service.GetAsync(valid.Id)).Transformations.ShouldHaveSingleItem().Kind.ShouldBe(DocumentTransformationKind.ImageToPdf);
    }

    [Fact]
    public async Task At_most_four_rows_positions_one_to_four_and_known_kinds_only()
    {
        using var _ = InTenantA();
        var five = Enum.GetValues<DocumentTransformationKind>().Select((k, i) => Step(k, i + 1, "x")).Append(Step(DocumentTransformationKind.ImageToPdf, 4)).ToList();
        await Should.ThrowAsync<AbpValidationException>(() => _service.CreateAsync(Dto("FIVE", configure: d => d.Transformations = five)));
        await Should.ThrowAsync<AbpValidationException>(() => _service.CreateAsync(Dto("ZERO", configure: d => d.Transformations = [Step(DocumentTransformationKind.ImageToPdf, 0)])));
        await Should.ThrowAsync<AbpValidationException>(() => _service.CreateAsync(Dto("FIFTH", configure: d => d.Transformations = [Step(DocumentTransformationKind.ImageToPdf, 5)])));
        await Should.ThrowAsync<AbpValidationException>(() => _service.CreateAsync(Dto("UNKNOWN", configure: d => d.Transformations = [Step((DocumentTransformationKind)9, 1)])));
        await Should.ThrowAsync<AbpValidationException>(() => _service.CreateAsync(Dto("LONG", configure: d => d.Transformations = [Step(DocumentTransformationKind.RenameByParentProperty, 1, new string('a', 256))])));
        (await _service.GetListAsync(Request(includeArchived: true))).TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task Rows_are_ordered_by_position_then_by_list_order_and_renumbered_from_one()
    {
        using var _ = InTenantA();
        var created = await _service.CreateAsync(Dto("ORDERED", configure: d => d.Transformations =
        [
            Step(DocumentTransformationKind.SplitPdfByReferencePattern, 4, "^A"),
            Step(DocumentTransformationKind.ImageToPdf, 2),
            Step(DocumentTransformationKind.LinkByFileName, 2),
        ]));

        created.Transformations.Select(t => (t.Position, t.Kind)).ShouldBe(
        [
            (1, DocumentTransformationKind.ImageToPdf), (2, DocumentTransformationKind.LinkByFileName), (3, DocumentTransformationKind.SplitPdfByReferencePattern)
        ]);

        /* Update: reorder, drop one, add one, change an argument. */
        var updated = await _service.UpdateAsync(created.Id, UpdateFrom(created, u => u.Transformations =
        [
            Step(DocumentTransformationKind.SplitPdfByReferencePattern, 1, "^B"),
            Step(DocumentTransformationKind.RenameByParentProperty, 3, "Reference"),
            Step(DocumentTransformationKind.ImageToPdf, 2),
        ]));

        updated.Transformations.Select(t => (t.Position, t.Kind, t.Argument)).ShouldBe(
        [
            (1, DocumentTransformationKind.SplitPdfByReferencePattern, (string?)"^B"),
            (2, DocumentTransformationKind.ImageToPdf, null),
            (3, DocumentTransformationKind.RenameByParentProperty, "Reference"),
        ]);
        (await WithUnitOfWorkAsync(() => _transformationRows.CountAsync(t => t.DocumentTypeId == created.Id))).ShouldBe(3);

        /* An empty pipeline removes every row. */
        updated = await _service.GetAsync(created.Id);
        (await _service.UpdateAsync(created.Id, UpdateFrom(updated, u => u.Transformations = []))).Transformations.ShouldBeEmpty();
        (await WithUnitOfWorkAsync(() => _transformationRows.CountAsync(t => t.DocumentTypeId == created.Id))).ShouldBe(0);
    }

    [Fact]
    public async Task Arguments_are_trimmed_and_an_empty_one_is_no_argument()
    {
        using var _ = InTenantA();
        var created = await _service.CreateAsync(Dto("ARG", configure: d => d.Transformations =
        [
            Step(DocumentTransformationKind.RenameByParentProperty, 1, "  OrderNumber  "),
            Step(DocumentTransformationKind.ImageToPdf, 2, "   "),
        ]));
        created.Transformations.Select(t => t.Argument).ShouldBe(["OrderNumber", null]);
    }

    // ── Archive, list, lookup, delete, concurrency ───────────────────────────

    [Fact]
    public async Task Archive_hides_a_type_from_the_list_and_the_lookup_and_twice_is_one_effect()
    {
        using var _ = InTenantA();
        var a = await _service.CreateAsync(Dto("A", "Alpha"));
        var b = await _service.CreateAsync(Dto("B", "Bravo", configure: d => d.Transformations = [Step(DocumentTransformationKind.ImageToPdf, 1)]));

        await _service.ArchiveAsync(b.Id);
        await _service.ArchiveAsync(b.Id);
        (await _service.GetAsync(b.Id)).ShouldSatisfyAllConditions(t => t.IsArchived.ShouldBeTrue(), t => t.Transformations.Count.ShouldBe(1, "archive keeps the pipeline"));
        (await _service.GetListAsync(Request())).Rows.ShouldHaveSingleItem().Id.ShouldBe(a.Id);
        (await _service.GetListAsync(Request(includeArchived: true))).TotalCount.ShouldBe(2);
        (await _service.GetLookupAsync()).ShouldHaveSingleItem().Id.ShouldBe(a.Id);

        await _service.UnarchiveAsync(b.Id);
        await _service.UnarchiveAsync(b.Id);
        (await _service.GetLookupAsync()).Count.ShouldBe(2);
        await Should.ThrowAsync<EntityNotFoundException>(() => _service.ArchiveAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task The_list_searches_code_name_in_both_languages_and_description_and_sorts_on_the_whitelist()
    {
        using var _ = InTenantA();
        await _service.CreateAsync(Dto("CMR", "Consignment note", "Vrachtbrief", d => { d.IsFreightDocument = true; d.Description = "signed at delivery"; }));
        await _service.CreateAsync(Dto("INV", "Invoice copy", "Factuurkopie", d => d.IncludeWithInvoice = true));
        await _service.CreateAsync(Dto("ARCH", "Archive", "Archief", d => d.IsArchived = true));

        using (UiCulture("nl"))
        {
            (await _service.GetListAsync(Request())).Rows.Select(r => r.Name).ShouldBe(["Factuurkopie", "Vrachtbrief"], "the default sort is the name in the UI language");
            (await _service.GetListAsync(Request(search: "vracht"))).Rows.ShouldHaveSingleItem().Code.ShouldBe("CMR");
            (await _service.GetListAsync(Request(search: "consignment"))).Rows.ShouldHaveSingleItem().Code.ShouldBe("CMR", "the English name is searched too");
            (await _service.GetListAsync(Request(search: "inv"))).Rows.ShouldHaveSingleItem().Code.ShouldBe("INV");
            (await _service.GetListAsync(Request(search: "DELIVERY"))).Rows.ShouldHaveSingleItem().Code.ShouldBe("CMR");
            (await _service.GetListAsync(Request(search: "archief"))).Rows.ShouldBeEmpty();
            (await _service.GetListAsync(Request(search: "archief", includeArchived: true))).Rows.ShouldHaveSingleItem().Code.ShouldBe("ARCH");
        }

        using (UiCulture("en"))
        {
            (await _service.GetListAsync(Request(includeArchived: true))).Rows.Select(r => r.Name).ShouldBe(["Archive", "Consignment note", "Invoice copy"]);
            (await _service.GetListAsync(Request(includeArchived: true, sortBy: "code", sortDir: "desc"))).Rows.Select(r => r.Code).ShouldBe(["INV", "CMR", "ARCH"]);
            (await _service.GetListAsync(Request(includeArchived: true, sortBy: "isFreightDocument", sortDir: "desc"))).Rows.First().Code.ShouldBe("CMR");
            (await _service.GetListAsync(Request(includeArchived: true, sortBy: "includeWithInvoice", sortDir: "desc"))).Rows.First().Code.ShouldBe("INV");
            (await _service.GetListAsync(Request(includeArchived: true, sortBy: "isArchived", sortDir: "desc"))).Rows.First().Code.ShouldBe("ARCH");
            (await _service.GetListAsync(Request(includeArchived: true, sortBy: "name", sortDir: "desc"))).Rows.First().Code.ShouldBe("INV");
            (await ShouldFailAsync(() => _service.GetListAsync(Request(sortBy: "showInListIndicator")), PlatformErrorCodes.UnknownSortColumn)).Data["sortBy"].ShouldBe("showInListIndicator");
            (await _service.GetLookupAsync("note")).ShouldHaveSingleItem().Code.ShouldBe("CMR");
        }
    }

    [Fact]
    public async Task Delete_and_bulk_delete_remove_the_type_with_its_pipeline()
    {
        using var _ = InTenantA();
        var a = await _service.CreateAsync(Dto("A", "Alpha", configure: d => d.Transformations = [Step(DocumentTransformationKind.ImageToPdf, 1)]));
        var b = await _service.CreateAsync(Dto("B", "Bravo", configure: d => d.IsArchived = true));
        var c = await _service.CreateAsync(Dto("C", "Charlie"));

        await _service.DeleteAsync(a.Id);
        await Should.ThrowAsync<EntityNotFoundException>(() => _service.GetAsync(a.Id));
        await Should.ThrowAsync<EntityNotFoundException>(() => _service.DeleteAsync(a.Id));

        (await _service.BulkDeleteAsync(new DocumentTypeBulkDeleteRequestDto { Selection = FilterBased() })).DeletedCount.ShouldBe(1, "the archived one stays without IncludeArchived");
        (await _service.BulkDeleteAsync(new DocumentTypeBulkDeleteRequestDto { Selection = FilterBased(), IncludeArchived = true })).DeletedCount.ShouldBe(1);
        (await _service.BulkDeleteAsync(new DocumentTypeBulkDeleteRequestDto { Selection = Explicit(a.Id, b.Id, c.Id) })).DeletedCount.ShouldBe(0);
        (await _service.GetListAsync(Request(includeArchived: true))).TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task A_stale_concurrency_stamp_is_refused()
    {
        using var _ = InTenantA();
        var created = await _service.CreateAsync(Dto("A"));
        await _service.UpdateAsync(created.Id, UpdateFrom(created, u => u.Description = "first"));
        await Should.ThrowAsync<AbpDbConcurrencyException>(() => _service.UpdateAsync(created.Id, UpdateFrom(created, u => u.Description = "second")));
        (await _service.GetAsync(created.Id)).Description.ShouldBe("first");
    }
}
