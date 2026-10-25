using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.MasterData.Published;
using OpenTms.MasterData.Relations;
using OpenTms.Orders.Seed;
using OpenTms.Orders.TransportOrders;
using OpenTms.Platform.Localization;
using OpenTms.Platform.NumberSequences;
using Shouldly;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Xunit;

namespace OpenTms.PostgreSql.ReferenceData;

/// <summary>
/// The Orders master data against real PostgreSQL (design 51): the seed the DbMigrator ran for tenant A (own database)
/// and the shared tenant (host database), seeding again changes nothing, tenant isolation of the three entities, their
/// children, lookups and IMasterDataReferenceLookup, the unique codes, the one-default index and one fleet-management
/// code per vendor. Risk class: Critical.
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class OrdersMasterDataPostgreSqlTests : ReferenceDataPostgreSqlTestBase
{
    private readonly IRepository<WaypointType, Guid> _waypointTypes;
    private readonly IRepository<TimeSlot, Guid> _timeSlots;
    private readonly IRepository<TransportOrderType, Guid> _orderTypes;
    private readonly IRepository<WaypointTypeFleetManagementCode, Guid> _fleetCodes;
    private readonly ITransportOrderTypeAppService _orderTypeService;
    private readonly IWaypointTypeAppService _waypointTypeService;
    private readonly ITimeSlotAppService _timeSlotService;
    private readonly IRelationAppService _relations;
    private readonly IMasterDataReferenceLookup _masterData;
    private readonly OrdersDataSeedContributor _seed;
    private readonly IGuidGenerator _guids;

    public OrdersMasterDataPostgreSqlTests()
    {
        _waypointTypes = GetRequiredService<IRepository<WaypointType, Guid>>();
        _timeSlots = GetRequiredService<IRepository<TimeSlot, Guid>>();
        _orderTypes = GetRequiredService<IRepository<TransportOrderType, Guid>>();
        _fleetCodes = GetRequiredService<IRepository<WaypointTypeFleetManagementCode, Guid>>();
        _orderTypeService = GetRequiredService<ITransportOrderTypeAppService>();
        _waypointTypeService = GetRequiredService<IWaypointTypeAppService>();
        _timeSlotService = GetRequiredService<ITimeSlotAppService>();
        _relations = GetRequiredService<IRelationAppService>();
        _masterData = GetRequiredService<IMasterDataReferenceLookup>();
        _seed = GetRequiredService<OrdersDataSeedContributor>();
        _guids = GetRequiredService<IGuidGenerator>();
    }

    private static LocalizedName Name(string en) => LocalizedName.Create(new Dictionary<string, string?> { ["en"] = en });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Every_tenant_has_the_seeded_rows_and_seeding_again_changes_nothing(bool ownDatabase)
    {
        var tenantId = ownDatabase ? TenantA : SharedTenant;
        await InRolledBackTransactionAsync(tenantId, async () =>
        {
            var waypointTypes = await _waypointTypes.GetListAsync();
            waypointTypes.Select(t => t.Code).ShouldBe(["LOAD", "UNLOAD", "VIA", "COUPLE", "UNCOUPLE", "CUSTOMS"], ignoreOrder: true);
            waypointTypes.ShouldAllBe(t => t.TenantId == tenantId);
            var load = waypointTypes.Single(t => t.Code == "LOAD");
            var unload = waypointTypes.Single(t => t.Code == "UNLOAD");
            load.Activity.ShouldBe(WaypointActivity.Load);
            load.DefaultDurationMinutes.ShouldBe(30);
            load.SuccessiveWaypointTypeId.ShouldBe(unload.Id);
            load.Name.Resolve("nl").ShouldBe("Laden");
            waypointTypes.Single(t => t.Code == "COUPLE").Activity.ShouldBe(WaypointActivity.CoupleTrailer);

            var slots = await _timeSlots.GetListAsync();
            slots.Select(s => s.Code).ShouldBe(["MORNING", "AFTERNOON", "DAY", "EVENING", "NIGHT"], ignoreOrder: true);
            var night = slots.Single(s => s.Code == "NIGHT");
            (night.StartTime, night.EndTime, night.IsOvernight).ShouldBe((new TimeOnly(22, 0), new TimeOnly(6, 0), true));

            var standard = (await _orderTypes.GetListAsync(includeDetails: true)).ShouldHaveSingleItem();
            standard.Code.ShouldBe("STD");
            standard.IsDefault.ShouldBeTrue();
            standard.Name.Resolve("nl").ShouldBe("Standaard");
            standard.Numbering.ShouldBe(NumberSequenceFormat.Create("TO", true, false, "-", 5));
            standard.DefaultWaypoints.OrderBy(w => w.Position).Select(w => w.WaypointTypeId).ShouldBe([load.Id, unload.Id]);

            await _seed.SeedAsync(new DataSeedContext(tenantId));
            await FlushAsync();
            (await _waypointTypes.GetCountAsync()).ShouldBe(6);
            (await _timeSlots.GetCountAsync()).ShouldBe(5);
            (await _orderTypes.GetCountAsync()).ShouldBe(1);
        });
    }

    [Fact]
    public async Task A_tenant_never_sees_another_tenants_rows_children_or_lookups()
    {
        Guid relationOfA = Guid.Empty;
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var load = (await _waypointTypes.GetListAsync(t => t.Code == "LOAD")).Single();
            await _orderTypeService.CreateAsync(new TransportOrderTypeCreateDto
            {
                Code = "ONLYA", Names = Names("Only A"), Numbering = Numbering(), DefaultWaypoints = [new() { WaypointTypeId = load.Id, Position = 1 }]
            });
            await _waypointTypeService.CreateAsync(new WaypointTypeCreateDto
            {
                Code = "ONLYA", Names = Names("Only A"), Activity = WaypointActivity.Other, FleetManagementCodes = [new() { Vendor = "Transics", Code = "A" }]
            });
            await _timeSlotService.CreateAsync(new TimeSlotCreateDto { Code = "ONLYA", Names = Names("Only A"), StartTime = new TimeOnly(1, 0), EndTime = new TimeOnly(2, 0) });
            relationOfA = (await _relations.CreateAsync(new RelationCreateDto { Code = "ONLYA", Name = "Only A", Roles = RelationRoles.Depot })).Id;

            (await _orderTypeService.GetListAsync(List())).Rows.Single(r => r.Code == "ONLYA").DefaultWaypoints.ShouldBe("Load");
            (await _waypointTypeService.GetLookupAsync()).ShouldContain(l => l.Code == "ONLYA");
            (await _masterData.GetRelationsAsync([relationOfA])).ShouldHaveSingleItem();
        });

        await InRolledBackTransactionAsync(SharedTenant, async () =>
        {
            (await _orderTypeService.GetListAsync(List())).Rows.Select(r => r.Code).ShouldBe(["STD"]);
            (await _waypointTypeService.GetLookupAsync()).ShouldNotContain(l => l.Code == "ONLYA");
            (await _timeSlotService.GetLookupAsync()).ShouldNotContain(l => l.Code == "ONLYA");
            (await _fleetCodes.GetListAsync()).ShouldAllBe(c => c.TenantId == SharedTenant);
            (await _orderTypes.GetListAsync(includeDetails: true)).SelectMany(t => t.DefaultWaypoints).ShouldAllBe(w => w.TenantId == SharedTenant);
            (await _masterData.GetRelationsAsync([relationOfA])).ShouldBeEmpty("another tenant's relation is not visible through the published lookup");
        });
    }

    [Fact]
    public async Task Duplicate_codes_are_refused_by_the_indexes()
    {
        await InRolledBackTransactionAsync(TenantA, () => ShouldViolateUniqueIndexAsync(
            () => _waypointTypes.InsertAsync(new WaypointType(_guids.Create(), TenantA, "LOAD", Name("Load"), WaypointActivity.Load), autoSave: true),
            "IX_WaypointTypes_TenantId_Code"));
        await InRolledBackTransactionAsync(TenantA, () => ShouldViolateUniqueIndexAsync(
            () => _timeSlots.InsertAsync(new TimeSlot(_guids.Create(), TenantA, "DAY", Name("Day"), new TimeOnly(1, 0), new TimeOnly(2, 0)), autoSave: true),
            "IX_TimeSlots_TenantId_Code"));
        await InRolledBackTransactionAsync(TenantA, () => ShouldViolateUniqueIndexAsync(
            () => _orderTypes.InsertAsync(new TransportOrderType(_guids.Create(), TenantA, "STD", Name("Std"), NumberSequenceFormat.Create("T", false, false, "", 5)), autoSave: true),
            "IX_TransportOrderTypes_TenantId_Code"));
    }

    [Fact]
    public async Task A_second_default_order_type_is_refused_by_the_index()
    {
        await InRolledBackTransactionAsync(TenantA, () => ShouldViolateUniqueIndexAsync(
            async () =>
            {
                var second = new TransportOrderType(_guids.Create(), TenantA, "SECOND", Name("Second"), NumberSequenceFormat.Create("T", false, false, "", 5));
                second.SetDefault(true);
                await _orderTypes.InsertAsync(second, autoSave: true);
            },
            "IX_TransportOrderTypes_TenantId_Default"));
    }

    [Fact]
    public async Task A_second_code_for_one_vendor_is_refused_by_the_index()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var load = (await _waypointTypes.GetListAsync(t => t.Code == "LOAD")).Single();
            await _fleetCodes.InsertAsync(new WaypointTypeFleetManagementCode(_guids.Create(), TenantA, load.Id, "Transics", "L1"), autoSave: true);
            await ShouldViolateUniqueIndexAsync(
                () => _fleetCodes.InsertAsync(new WaypointTypeFleetManagementCode(_guids.Create(), TenantA, load.Id, "Transics", "L2"), autoSave: true),
                "IX_WaypointTypeFleetManagementCodes_WaypointTypeId_Vendor");
        });
    }

    [Fact]
    public async Task Set_default_moves_the_default_and_the_seeded_stops_keep_their_waypoint_types_in_use()
    {
        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var express = await _orderTypeService.CreateAsync(new TransportOrderTypeCreateDto { Code = "EXP", Names = Names("Express"), Numbering = Numbering() });
            await _orderTypeService.SetDefaultAsync(express.Id);
            (await _orderTypes.GetListAsync(t => t.IsDefault)).ShouldHaveSingleItem().Code.ShouldBe("EXP");

            var load = (await _waypointTypes.GetListAsync(t => t.Code == "LOAD")).Single();
            (await Should.ThrowAsync<Volo.Abp.BusinessException>(() => _waypointTypeService.DeleteAsync(load.Id)))
                .Code.ShouldBe(OpenTms.Platform.ReferenceData.ReferenceDataErrorCodes.InUse);
        });
    }
}
