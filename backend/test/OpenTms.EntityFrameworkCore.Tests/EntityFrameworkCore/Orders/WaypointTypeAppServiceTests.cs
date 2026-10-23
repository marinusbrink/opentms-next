using System;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.MasterData.Relations;
using OpenTms.Orders;
using OpenTms.Orders.TransportOrders;
using OpenTms.Platform.ReferenceData;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace OpenTms.EntityFrameworkCore.Orders;

/// <summary>
/// WaypointTypeAppService on SQLite (design 51): CRUD, the successor (not itself; a cycle allowed), the Master Data
/// relation category and default relation validated and named through IMasterDataReferenceLookup ("(not found)" after
/// a delete there, refused on the next save), the fleet-management codes and the in-use refusals.
/// Risk class: High; Critical for tenant isolation.
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class WaypointTypeAppServiceTests : ReferenceDataAppServiceTestBase
{
    private readonly IWaypointTypeAppService _service;
    private readonly ITransportOrderTypeAppService _orderTypes;
    private readonly IRelationAppService _relations;
    private readonly IRelationCategoryAppService _categories;

    public WaypointTypeAppServiceTests()
    {
        _service = GetRequiredService<IWaypointTypeAppService>();
        _orderTypes = GetRequiredService<ITransportOrderTypeAppService>();
        _relations = GetRequiredService<IRelationAppService>();
        _categories = GetRequiredService<IRelationCategoryAppService>();
    }

    private static WaypointTypeCreateDto Dto(string code, string en, WaypointActivity activity = WaypointActivity.Load) => new()
    {
        Code = code,
        Names = Names(en),
        Activity = activity
    };

    private Task<WaypointTypeDto> CreateAsync(string code, string en, WaypointActivity activity = WaypointActivity.Load, Action<WaypointTypeCreateDto>? configure = null)
    {
        var dto = Dto(code, en, activity);
        configure?.Invoke(dto);
        return _service.CreateAsync(dto);
    }

    private static WaypointTypeUpdateDto UpdateFrom(WaypointTypeDto dto) => new()
    {
        Code = dto.Code,
        Names = Copy(dto.Names),
        Description = dto.Description,
        Activity = dto.Activity,
        SuccessiveWaypointTypeId = dto.SuccessiveWaypointTypeId,
        RelationCategoryId = dto.RelationCategoryId,
        DefaultRelationId = dto.DefaultRelationId,
        DefaultDurationMinutes = dto.DefaultDurationMinutes,
        FleetManagementCodes = dto.FleetManagementCodes.Select(c => new WaypointTypeFleetManagementCodeDto { Vendor = c.Vendor, Code = c.Code }).ToList(),
        IsArchived = dto.IsArchived,
        ConcurrencyStamp = dto.ConcurrencyStamp
    };

    [Fact]
    public async Task Create_stores_every_field_and_names_the_successor_and_the_master_data_rows()
    {
        using var _ = InTenantA();
        var depot = await _relations.CreateAsync(new RelationCreateDto { Code = "DEPOT", Name = "Own depot", Roles = RelationRoles.Depot });
        var category = await _categories.CreateAsync(new RelationCategoryCreateDto { Name = "Warehouses" });
        var unload = await CreateAsync("UNLOAD", "Unload", WaypointActivity.Unload);

        var load = await CreateAsync(" load ", "Load", configure: d =>
        {
            d.SuccessiveWaypointTypeId = unload.Id;
            d.RelationCategoryId = category.Id;
            d.DefaultRelationId = depot.Id;
            d.DefaultDurationMinutes = 30;
            d.FleetManagementCodes = [new() { Vendor = "Transics", Code = "L" }, new() { Vendor = "Trimble", Code = "100" }];
        });

        load.Code.ShouldBe("LOAD");
        load.Activity.ShouldBe(WaypointActivity.Load);
        load.SuccessiveWaypointTypeName.ShouldBe("Unload");
        load.RelationCategoryName.ShouldBe("Warehouses");
        load.DefaultRelationName.ShouldBe("Own depot");
        load.DefaultDurationMinutes.ShouldBe(30);
        load.FleetManagementCodes.Select(c => (c.Vendor, c.Code)).ShouldBe([("Transics", "L"), ("Trimble", "100")]);

        var row = (await _service.GetListAsync(List())).Rows.Single(r => r.Code == "LOAD");
        row.SuccessiveWaypointTypeName.ShouldBe("Unload");
        row.RelationCategoryName.ShouldBe("Warehouses");
        row.DefaultRelationName.ShouldBe("Own depot");
    }

    [Fact]
    public async Task List_searches_sorts_on_the_next_type_and_hides_archived_rows()
    {
        using var _ = InTenantA();
        var via = await CreateAsync("VIA", "Via", WaypointActivity.Via);
        var unload = await CreateAsync("UNLOAD", "Unload", WaypointActivity.Unload, d => d.SuccessiveWaypointTypeId = via.Id);
        await CreateAsync("LOAD", "Load", configure: d => d.SuccessiveWaypointTypeId = unload.Id);
        await CreateAsync("OLD", "Old", WaypointActivity.Other, d => d.IsArchived = true);

        (await _service.GetListAsync(List())).Rows.Select(r => r.Code).ShouldBe(["LOAD", "UNLOAD", "VIA"]);
        (await _service.GetListAsync(List(includeArchived: true))).TotalCount.ShouldBe(4);
        (await _service.GetListAsync(List(search: "unl"))).Rows.Single().Code.ShouldBe("UNLOAD");
        (await _service.GetListAsync(List(sortBy: "successiveWaypointTypeName"))).Rows.Select(r => r.Code).ShouldBe(["VIA", "LOAD", "UNLOAD"], "no successor sorts first, then Unload, then Via");
        (await _service.GetListAsync(List(sortBy: "activity", sortDir: "desc"))).Rows.First().Code.ShouldBe("VIA");
        (await _service.GetLookupAsync()).Select(l => l.Code).ShouldBe(["LOAD", "UNLOAD", "VIA"]);
        (await Should.ThrowAsync<BusinessException>(() => _service.GetListAsync(List(sortBy: "defaultRelationName")))).Code.ShouldBe(ReferenceDataErrorCodes.UnknownSortColumn);
    }

    [Fact]
    public async Task A_successor_that_is_itself_or_unknown_is_refused_and_a_cycle_is_allowed()
    {
        using var _ = InTenantA();
        var load = await CreateAsync("LOAD", "Load");
        var unload = await CreateAsync("UNLOAD", "Unload", WaypointActivity.Unload, d => d.SuccessiveWaypointTypeId = load.Id);

        var self = UpdateFrom(load);
        self.SuccessiveWaypointTypeId = load.Id;
        (await Should.ThrowAsync<BusinessException>(() => _service.UpdateAsync(load.Id, self))).Code.ShouldBe(OrdersErrorCodes.SuccessorIsSelf);

        (await Should.ThrowAsync<BusinessException>(() => CreateAsync("X", "X", configure: d => d.SuccessiveWaypointTypeId = Guid.NewGuid())))
            .Code.ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound);

        var cycle = UpdateFrom(await _service.GetAsync(load.Id));
        cycle.SuccessiveWaypointTypeId = unload.Id;
        (await _service.UpdateAsync(load.Id, cycle)).SuccessiveWaypointTypeName.ShouldBe("Unload");
    }

    [Fact]
    public async Task Unknown_master_data_ids_are_refused_and_a_row_deleted_there_shows_as_not_found()
    {
        using var _ = InTenantA();
        using var culture = UiCulture("en");
        (await Should.ThrowAsync<BusinessException>(() => CreateAsync("A", "A", configure: d => d.RelationCategoryId = Guid.NewGuid())))
            .Data["field"].ShouldBe("relationCategoryId");
        (await Should.ThrowAsync<BusinessException>(() => CreateAsync("A", "A", configure: d => d.DefaultRelationId = Guid.NewGuid())))
            .Code.ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound);

        var category = await _categories.CreateAsync(new RelationCategoryCreateDto { Name = "Warehouses" });
        var type = await CreateAsync("A", "A", configure: d => d.RelationCategoryId = category.Id);
        await _categories.DeleteAsync(category.Id);

        (await _service.GetAsync(type.Id)).RelationCategoryName.ShouldBe("(not found)");
        (await Should.ThrowAsync<BusinessException>(() => _service.UpdateAsync(type.Id, UpdateFrom(type))))
            .Code.ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound, "the next save refuses it (Assumption 6)");
    }

    [Fact]
    public async Task Another_tenants_relation_is_unknown()
    {
        Guid foreign;
        using (InTenantB())
        {
            foreign = (await _relations.CreateAsync(new RelationCreateDto { Code = "B", Name = "B's depot", Roles = RelationRoles.Depot })).Id;
        }

        using (InTenantA())
        {
            (await Should.ThrowAsync<BusinessException>(() => CreateAsync("A", "A", configure: d => d.DefaultRelationId = foreign)))
                .Code.ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound);
        }
    }

    [Fact]
    public async Task Fleet_management_codes_are_replaced_one_per_vendor_and_at_most_twenty()
    {
        using var _ = InTenantA();
        var type = await CreateAsync("LOAD", "Load", configure: d => d.FleetManagementCodes = [new() { Vendor = "Transics", Code = "L" }]);

        var change = UpdateFrom(type);
        change.FleetManagementCodes = [new() { Vendor = "Trimble", Code = "100" }];
        (await _service.UpdateAsync(type.Id, change)).FleetManagementCodes.ShouldHaveSingleItem().Vendor.ShouldBe("Trimble");

        (await Should.ThrowAsync<BusinessException>(() => CreateAsync("B", "B", configure: d =>
            d.FleetManagementCodes = [new() { Vendor = "Transics", Code = "1" }, new() { Vendor = "transics", Code = "2" }])))
            .Code.ShouldBe(ReferenceDataErrorCodes.DuplicateKey);
        (await Should.ThrowAsync<BusinessException>(() => CreateAsync("C", "C", configure: d =>
            d.FleetManagementCodes = Enumerable.Range(1, 21).Select(i => new WaypointTypeFleetManagementCodeDto { Vendor = $"V{i}", Code = "X" }).ToList())))
            .Code.ShouldBe(ReferenceDataErrorCodes.TooManyChildren);
    }

    [Fact]
    public async Task A_type_used_as_a_default_stop_or_as_a_successor_cannot_be_deleted()
    {
        using var _ = InTenantA();
        using var culture = UiCulture("en");
        var unload = await CreateAsync("UNLOAD", "Unload", WaypointActivity.Unload);
        var load = await CreateAsync("LOAD", "Load", configure: d => d.SuccessiveWaypointTypeId = unload.Id);
        await _orderTypes.CreateAsync(new TransportOrderTypeCreateDto
        {
            Code = "STD",
            Names = Names("Standard"),
            Numbering = Numbering("TO"),
            DefaultWaypoints = [new() { WaypointTypeId = load.Id, Position = 1 }]
        });

        var stop = await Should.ThrowAsync<BusinessException>(() => _service.DeleteAsync(load.Id));
        stop.Code.ShouldBe(ReferenceDataErrorCodes.InUse);
        ((string)stop.Data["reason"]!).ShouldContain("Standard");

        var successor = await Should.ThrowAsync<BusinessException>(() => _service.DeleteAsync(unload.Id));
        successor.Code.ShouldBe(ReferenceDataErrorCodes.InUse);
        ((string)successor.Data["reason"]!).ShouldContain("Load");

        var bulk = await _service.BulkDeleteAsync(new ArchivableBulkDeleteRequestDto { Selection = Explicit(load.Id, unload.Id) });
        bulk.DeletedCount.ShouldBe(0);
        bulk.SkippedRows.Count.ShouldBe(2);

        /* An unused type can go. */
        var via = await CreateAsync("VIA", "Via", WaypointActivity.Via);
        await _service.DeleteAsync(via.Id);
    }

    [Fact]
    public async Task Another_tenant_sees_nothing()
    {
        Guid id;
        using (InTenantA())
        {
            id = (await CreateAsync("LOAD", "Load")).Id;
        }

        using (InTenantB())
        {
            (await _service.GetListAsync(List())).TotalCount.ShouldBe(0);
            (await _service.GetLookupAsync()).ShouldBeEmpty();
            await Should.ThrowAsync<Volo.Abp.Domain.Entities.EntityNotFoundException>(() => _service.GetAsync(id));
            (await Should.ThrowAsync<BusinessException>(() => CreateAsync("UNLOAD", "Unload", configure: d => d.SuccessiveWaypointTypeId = id)))
                .Code.ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound);
        }
    }
}
