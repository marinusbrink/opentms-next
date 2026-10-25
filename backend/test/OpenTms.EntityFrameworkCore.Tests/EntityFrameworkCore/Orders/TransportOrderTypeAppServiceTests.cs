using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Orders.TransportOrders;
using OpenTms.Platform.ReferenceData;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Data;
using Xunit;

namespace OpenTms.EntityFrameworkCore.Orders;

/// <summary>
/// TransportOrderTypeAppService on SQLite (design 51). Risk class: Critical for isolation and the single default; High
/// for CRUD, numbering, the default stops, the required-field flags and the delete rules.
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class TransportOrderTypeAppServiceTests : ReferenceDataAppServiceTestBase
{
    private readonly ITransportOrderTypeAppService _service;
    private readonly IWaypointTypeAppService _waypointTypes;

    public TransportOrderTypeAppServiceTests()
    {
        _service = GetRequiredService<ITransportOrderTypeAppService>();
        _waypointTypes = GetRequiredService<IWaypointTypeAppService>();
    }

    private async Task<Guid> WaypointTypeAsync(string code, string en, WaypointActivity activity = WaypointActivity.Load)
        => (await _waypointTypes.CreateAsync(new WaypointTypeCreateDto { Code = code, Names = Names(en), Activity = activity })).Id;

    private static List<TransportOrderTypeDefaultWaypointDto> Stops(params Guid[] waypointTypeIds)
        => waypointTypeIds.Select((id, i) => new TransportOrderTypeDefaultWaypointDto { WaypointTypeId = id, Position = i + 1 }).ToList();

    private Task<TransportOrderTypeDto> CreateAsync(string code, string en, string? nl = null, bool archived = false, List<TransportOrderTypeDefaultWaypointDto>? stops = null, TransportOrderRequiredFields required = TransportOrderRequiredFields.None)
        => _service.CreateAsync(new TransportOrderTypeCreateDto
        {
            Code = code,
            Names = Names(en, nl),
            Numbering = Numbering("TO", year: true),
            DefaultWaypoints = stops ?? [],
            RequiredFields = required,
            IsArchived = archived
        });

    private static TransportOrderTypeUpdateDto UpdateFrom(TransportOrderTypeDto dto) => new()
    {
        Code = dto.Code,
        Names = Copy(dto.Names),
        Description = dto.Description,
        RequiredFields = dto.RequiredFields,
        DefaultWaypoints = dto.DefaultWaypoints.Select(w => new TransportOrderTypeDefaultWaypointDto { WaypointTypeId = w.WaypointTypeId, Position = w.Position }).ToList(),
        Numbering = dto.Numbering,
        IsArchived = dto.IsArchived,
        ConcurrencyStamp = dto.ConcurrencyStamp
    };

    [Fact]
    public async Task Create_stores_the_stops_the_flags_and_the_numbering_and_makes_the_first_type_the_default()
    {
        using var _ = InTenantA();
        using var culture = UiCulture("nl-NL");
        var load = await WaypointTypeAsync("LOAD", "Load");
        var unload = await WaypointTypeAsync("UNLOAD", "Unload", WaypointActivity.Unload);

        var created = await CreateAsync(" std ", "Standard", "Standaard",
            stops: [new() { WaypointTypeId = unload, Position = 2 }, new() { WaypointTypeId = load, Position = 1 }],
            required: TransportOrderRequiredFields.CustomerReference | TransportOrderRequiredFields.WeightPerLine);

        created.Code.ShouldBe("STD");
        created.Name.ShouldBe("Standaard");
        created.IsDefault.ShouldBeTrue("a tenant's first type becomes the default");
        created.RequiredFields.ShouldBe(TransportOrderRequiredFields.CustomerReference | TransportOrderRequiredFields.WeightPerLine);
        created.DefaultWaypoints.Select(w => (w.Position, w.WaypointTypeId, w.Name)).ShouldBe([(1, load, "Load"), (2, unload, "Unload")]);
        created.NextNumber.ShouldBe(1);
        created.NextNumberPreview.ShouldBe($"TO-{DateTime.Now.Year}-00001");

        var reread = await _service.GetAsync(created.Id);
        reread.RequiredFields.ShouldBe(created.RequiredFields, "the flags round-trip");
        reread.DefaultWaypoints.Count.ShouldBe(2);

        (await CreateAsync("EXP", "Express")).IsDefault.ShouldBeFalse();
    }

    [Fact]
    public async Task List_shows_the_stops_joined_pages_searches_sorts_and_hides_archived_rows()
    {
        using var _ = InTenantA();
        var load = await WaypointTypeAsync("LOAD", "Load");
        var unload = await WaypointTypeAsync("UNLOAD", "Unload", WaypointActivity.Unload);
        await CreateAsync("STD", "Standard", stops: Stops(load, load, unload));
        await CreateAsync("EXP", "Express");
        await CreateAsync("OLD", "Old", archived: true);

        var list = await _service.GetListAsync(List());
        list.TotalCount.ShouldBe(2);
        list.Rows.Select(r => r.Code).ShouldBe(["EXP", "STD"]);
        var standard = list.Rows.Single(r => r.Code == "STD");
        standard.DefaultWaypoints.ShouldBe("Load, Load, Unload");
        standard.NumberingExample.ShouldBe($"TO-{DateTime.Now.Year}-00001");
        list.Rows.Single(r => r.Code == "EXP").DefaultWaypoints.ShouldBe("");

        (await _service.GetListAsync(List(includeArchived: true))).TotalCount.ShouldBe(3);
        (await _service.GetListAsync(List(search: "expr"))).Rows.Single().Code.ShouldBe("EXP");
        (await _service.GetListAsync(List(sortBy: "name", sortDir: "desc"))).Rows.First().Code.ShouldBe("STD");
        (await _service.GetLookupAsync()).Select(l => l.Code).ShouldBe(["EXP", "STD"]);
        (await Should.ThrowAsync<BusinessException>(() => _service.GetListAsync(List(sortBy: "defaultWaypoints")))).Code.ShouldBe(ReferenceDataErrorCodes.UnknownSortColumn);
    }

    [Fact]
    public async Task Stops_must_exist_in_the_tenant_and_are_at_most_ten()
    {
        using var _ = InTenantA();
        var load = await WaypointTypeAsync("LOAD", "Load");

        (await Should.ThrowAsync<BusinessException>(() => CreateAsync("STD", "Standard", stops: Stops(load, Guid.NewGuid()))))
            .Code.ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound);
        (await Should.ThrowAsync<BusinessException>(() => CreateAsync("STD", "Standard", stops: Stops(Enumerable.Repeat(load, 11).ToArray()))))
            .Code.ShouldBe(ReferenceDataErrorCodes.TooManyChildren);

        Guid otherTenants;
        using (InTenantB())
        {
            otherTenants = await WaypointTypeAsync("LOAD", "Load");
        }

        (await Should.ThrowAsync<BusinessException>(() => CreateAsync("STD", "Standard", stops: Stops(otherTenants))))
            .Code.ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound, "another tenant's waypoint type is unknown here");
    }

    [Fact]
    public async Task Undefined_required_field_flags_are_refused()
    {
        using var _ = InTenantA();
        (await Should.ThrowAsync<BusinessException>(() => CreateAsync("STD", "Standard", required: (TransportOrderRequiredFields)8192)))
            .Code.ShouldBe(ReferenceDataErrorCodes.InvalidCode);
    }

    [Fact]
    public async Task Update_replaces_the_stops_and_flags_and_a_stale_stamp_is_a_conflict()
    {
        using var _ = InTenantA();
        var load = await WaypointTypeAsync("LOAD", "Load");
        var unload = await WaypointTypeAsync("UNLOAD", "Unload", WaypointActivity.Unload);
        var via = await WaypointTypeAsync("VIA", "Via", WaypointActivity.Via);
        var created = await CreateAsync("STD", "Standard", stops: Stops(load, unload));

        var change = UpdateFrom(created);
        change.DefaultWaypoints = Stops(load, via, unload);
        change.RequiredFields = TransportOrderRequiredFields.Temperature;
        change.Numbering = Numbering("ORD", year: true, month: true, separator: "/", digits: 4);
        var updated = await _service.UpdateAsync(created.Id, change);

        updated.DefaultWaypoints.Select(w => w.WaypointTypeId).ShouldBe([load, via, unload]);
        updated.RequiredFields.ShouldBe(TransportOrderRequiredFields.Temperature);
        updated.NextNumberPreview.ShouldBe($"ORD/{DateTime.Now:yyyy}/{DateTime.Now:MM}/0001");

        var fewer = UpdateFrom(updated);
        fewer.DefaultWaypoints = Stops(unload);
        (await _service.UpdateAsync(created.Id, fewer)).DefaultWaypoints.ShouldHaveSingleItem().WaypointTypeId.ShouldBe(unload);

        change.ConcurrencyStamp = created.ConcurrencyStamp;
        await Should.ThrowAsync<AbpDbConcurrencyException>(() => _service.UpdateAsync(created.Id, change));
    }

    [Fact]
    public async Task The_default_moves_with_set_default_and_cannot_be_archived_or_deleted()
    {
        using var _ = InTenantA();
        var standard = await CreateAsync("STD", "Standard");
        var express = await CreateAsync("EXP", "Express");

        var archive = UpdateFrom(standard);
        archive.IsArchived = true;
        (await Should.ThrowAsync<BusinessException>(() => _service.UpdateAsync(standard.Id, archive))).Code.ShouldBe(ReferenceDataErrorCodes.DefaultRequired);
        (await Should.ThrowAsync<BusinessException>(() => _service.DeleteAsync(standard.Id))).Code.ShouldBe(ReferenceDataErrorCodes.DefaultRequired);

        await _service.SetDefaultAsync(express.Id);
        (await _service.GetAsync(express.Id)).IsDefault.ShouldBeTrue();
        (await _service.GetAsync(standard.Id)).IsDefault.ShouldBeFalse();

        await _service.DeleteAsync(standard.Id);
        (await _service.GetListAsync(List())).Rows.Select(r => r.Code).ShouldBe(["EXP"]);
    }

    [Fact]
    public async Task Bulk_delete_skips_the_default_and_deleting_a_type_frees_its_stops()
    {
        using var _ = InTenantA();
        var load = await WaypointTypeAsync("LOAD", "Load");
        var standard = await CreateAsync("STD", "Standard");
        var express = await CreateAsync("EXP", "Express", stops: Stops(load));

        var result = await _service.BulkDeleteAsync(new ArchivableBulkDeleteRequestDto { Selection = Explicit(standard.Id, express.Id) });
        result.DeletedCount.ShouldBe(1);
        result.SkippedRows.Single().Id.ShouldBe(standard.Id);

        /* The deleted type's stop no longer holds the waypoint type. */
        await _waypointTypes.DeleteAsync(load);
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
        updated.NextNumberPreview.ShouldBe($"TO-{DateTime.Now.Year}-04711");

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
