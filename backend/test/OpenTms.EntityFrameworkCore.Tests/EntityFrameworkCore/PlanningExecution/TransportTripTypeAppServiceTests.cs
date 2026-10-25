using System;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.PlanningExecution.TransportTrips;
using OpenTms.Platform.ReferenceData;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Data;
using Xunit;

namespace OpenTms.EntityFrameworkCore.PlanningExecution;

/// <summary>
/// TransportTripTypeAppService on SQLite (design 53), the first service on the Platform reference-data base (design 50).
/// Risk class: Critical for isolation and the single default; High for CRUD, numbering, archive and delete rules.
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class TransportTripTypeAppServiceTests : ReferenceDataAppServiceTestBase
{
    private readonly ITransportTripTypeAppService _service;

    public TransportTripTypeAppServiceTests()
    {
        _service = GetRequiredService<ITransportTripTypeAppService>();
    }

    private Task<TransportTripTypeDto> CreateAsync(string code, string en, string? nl = null, bool archived = false, TripExecution execution = TripExecution.OwnFleet)
        => _service.CreateAsync(new TransportTripTypeCreateDto
        {
            Code = code,
            Names = Names(en, nl),
            Numbering = Numbering("TR", year: true),
            DefaultExecution = execution,
            IsArchived = archived
        });

    private static TransportTripTypeUpdateDto UpdateFrom(TransportTripTypeDto dto) => new()
    {
        Code = dto.Code,
        Names = Copy(dto.Names),
        Description = dto.Description,
        DefaultExecution = dto.DefaultExecution,
        SendToFleetManagementSystem = dto.SendToFleetManagementSystem,
        Numbering = dto.Numbering,
        IsArchived = dto.IsArchived,
        ConcurrencyStamp = dto.ConcurrencyStamp
    };

    [Fact]
    public async Task Create_normalises_the_code_resolves_the_name_and_makes_the_first_type_the_default()
    {
        using var _ = InTenantA();
        using var culture = UiCulture("nl-NL");

        var created = await CreateAsync(" std ", "Standard", "Standaard", execution: TripExecution.Subcontracted);

        created.Code.ShouldBe("STD");
        created.Name.ShouldBe("Standaard");
        created.Names.ShouldBe(new LocalizedNameDto { ["en"] = "Standard", ["nl"] = "Standaard" });
        created.IsDefault.ShouldBeTrue("a tenant's first type becomes the default");
        created.DefaultExecution.ShouldBe(TripExecution.Subcontracted);
        created.SendToFleetManagementSystem.ShouldBeTrue();
        created.Numbering.Prefix.ShouldBe("TR");
        created.NextNumber.ShouldBe(1);
        created.NextNumberPreview.ShouldBe($"TR-{DateTime.Now.Year}-00001");

        var second = await CreateAsync("CHT", "Charter");
        second.IsDefault.ShouldBeFalse();
    }

    [Fact]
    public async Task List_pages_searches_sorts_and_hides_archived_rows()
    {
        using var _ = InTenantA();
        await CreateAsync("STD", "Standard");
        await CreateAsync("CHT", "Charter");
        await CreateAsync("OLD", "Old", archived: true);

        var list = await _service.GetListAsync(List());
        list.TotalCount.ShouldBe(2);
        list.Rows.Select(r => r.Code).ShouldBe(["CHT", "STD"]);
        list.Rows.Single(r => r.Code == "STD").NumberingExample.ShouldBe($"TR-{DateTime.Now.Year}-00001");

        (await _service.GetListAsync(List(includeArchived: true))).TotalCount.ShouldBe(3);
        (await _service.GetListAsync(List(search: "chart"))).Rows.Single().Code.ShouldBe("CHT");
        (await _service.GetListAsync(List(sortBy: "name", sortDir: "desc"))).Rows.First().Code.ShouldBe("STD");
        (await _service.GetLookupAsync()).Select(l => l.Code).ShouldBe(["CHT", "STD"]);

        var error = await Should.ThrowAsync<BusinessException>(() => _service.GetListAsync(List(sortBy: "nope")));
        error.Code.ShouldBe(ReferenceDataErrorCodes.UnknownSortColumn);
    }

    [Fact]
    public async Task A_duplicate_code_is_refused()
    {
        using var _ = InTenantA();
        await CreateAsync("STD", "Standard");

        var error = await Should.ThrowAsync<BusinessException>(() => CreateAsync("std", "Other"));
        error.Code.ShouldBe(ReferenceDataErrorCodes.DuplicateKey);
    }

    [Fact]
    public async Task An_invalid_code_or_numbering_is_refused()
    {
        using var _ = InTenantA();
        (await Should.ThrowAsync<BusinessException>(() => CreateAsync("S T D", "Standard"))).Code.ShouldBe(ReferenceDataErrorCodes.InvalidCode);
        (await Should.ThrowAsync<BusinessException>(() => _service.CreateAsync(new TransportTripTypeCreateDto
        {
            Code = "X", Names = Names("X"), Numbering = Numbering("TR", year: false, month: true)
        }))).Code.ShouldBe(ReferenceDataErrorCodes.InvalidNumberSequence);
        (await Should.ThrowAsync<BusinessException>(() => _service.CreateAsync(new TransportTripTypeCreateDto
        {
            Code = "X", Names = Names(""), Numbering = Numbering()
        }))).Code.ShouldBe(ReferenceDataErrorCodes.NameRequired);
    }

    [Fact]
    public async Task The_default_moves_with_set_default_and_cannot_be_archived_or_deleted()
    {
        using var _ = InTenantA();
        var standard = await CreateAsync("STD", "Standard");
        var charter = await CreateAsync("CHT", "Charter");

        var archive = UpdateFrom(standard);
        archive.IsArchived = true;
        (await Should.ThrowAsync<BusinessException>(() => _service.UpdateAsync(standard.Id, archive))).Code.ShouldBe(ReferenceDataErrorCodes.DefaultRequired);
        (await Should.ThrowAsync<BusinessException>(() => _service.DeleteAsync(standard.Id))).Code.ShouldBe(ReferenceDataErrorCodes.DefaultRequired);

        await _service.SetDefaultAsync(charter.Id);
        (await _service.GetAsync(charter.Id)).IsDefault.ShouldBeTrue();
        (await _service.GetAsync(standard.Id)).IsDefault.ShouldBeFalse();

        await _service.DeleteAsync(standard.Id);
        (await _service.GetListAsync(List())).Rows.Select(r => r.Code).ShouldBe(["CHT"]);
    }

    [Fact]
    public async Task An_archived_type_cannot_become_the_default()
    {
        using var _ = InTenantA();
        await CreateAsync("STD", "Standard");
        var old = await CreateAsync("OLD", "Old", archived: true);

        (await Should.ThrowAsync<BusinessException>(() => _service.SetDefaultAsync(old.Id))).Code.ShouldBe(ReferenceDataErrorCodes.DefaultRequired);
    }

    [Fact]
    public async Task Bulk_delete_skips_the_default_and_deletes_the_rest()
    {
        using var _ = InTenantA();
        var standard = await CreateAsync("STD", "Standard");
        var charter = await CreateAsync("CHT", "Charter");

        var result = await _service.BulkDeleteAsync(new ArchivableBulkDeleteRequestDto { Selection = Explicit(standard.Id, charter.Id) });

        result.DeletedCount.ShouldBe(1);
        result.SkippedRows.Single().Id.ShouldBe(standard.Id);
    }

    [Fact]
    public async Task Update_applies_the_changes_and_a_stale_stamp_is_a_conflict()
    {
        using var _ = InTenantA();
        var created = await CreateAsync("STD", "Standard");

        var change = UpdateFrom(created);
        change.Names = Names("Standard trip", "Standaard rit");
        change.DefaultExecution = TripExecution.Subcontracted;
        change.SendToFleetManagementSystem = false;
        change.Numbering = Numbering("RIT", year: true, month: true, separator: "/", digits: 4);
        var updated = await _service.UpdateAsync(created.Id, change);

        updated.Name.ShouldBe("Standard trip");
        updated.SendToFleetManagementSystem.ShouldBeFalse();
        updated.NextNumberPreview.ShouldBe($"RIT/{DateTime.Now:yyyy}/{DateTime.Now:MM}/0001");

        change.ConcurrencyStamp = created.ConcurrencyStamp;
        await Should.ThrowAsync<AbpDbConcurrencyException>(() => _service.UpdateAsync(created.Id, change));
    }

    [Fact]
    public async Task Next_number_continues_only_upwards()
    {
        using var _ = InTenantA();
        var created = await CreateAsync("STD", "Standard");

        var change = UpdateFrom(created);
        change.NextNumber = 4711;
        var updated = await _service.UpdateAsync(created.Id, change);
        updated.NextNumber.ShouldBe(4711);
        updated.NextNumberPreview.ShouldBe($"TR-{DateTime.Now.Year}-04711");

        var lower = UpdateFrom(updated);
        lower.NextNumber = 12;
        (await Should.ThrowAsync<BusinessException>(() => _service.UpdateAsync(created.Id, lower))).Code.ShouldBe(ReferenceDataErrorCodes.NextNumberTooLow);
    }

    [Fact]
    public async Task Another_tenant_sees_nothing()
    {
        Guid id;
        using (InTenantA())
        {
            id = (await CreateAsync("STD", "Standard")).Id;
        }

        using (InTenantB())
        {
            (await _service.GetListAsync(List())).TotalCount.ShouldBe(0);
            (await _service.GetLookupAsync()).ShouldBeEmpty();
            await Should.ThrowAsync<Volo.Abp.Domain.Entities.EntityNotFoundException>(() => _service.GetAsync(id));
            (await CreateAsync("STD", "Standard")).IsDefault.ShouldBeTrue("tenant B's first type is its own default");
        }
    }
}
