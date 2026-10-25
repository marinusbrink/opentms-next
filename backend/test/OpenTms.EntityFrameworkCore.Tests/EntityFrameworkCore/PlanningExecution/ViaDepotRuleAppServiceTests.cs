using System;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.MasterData.Regions;
using OpenTms.MasterData.Relations;
using OpenTms.PlanningExecution;
using OpenTms.PlanningExecution.Legs;
using OpenTms.PlanningExecution.ViaDepots;
using OpenTms.Platform.ReferenceData;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Xunit;

namespace OpenTms.EntityFrameworkCore.PlanningExecution;

/// <summary>ViaDepotRuleAppService on SQLite (design 59 "ViaDepotRule", design-50 shape): scope rules, references, list, archive, delete, isolation. Risk class: High.</summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class ViaDepotRuleAppServiceTests : PlanningAppServiceTestBase
{
    private IViaDepotRuleAppService Rules => GetRequiredService<IViaDepotRuleAppService>();

    private ViaDepotRuleCreateDto Dto(ViaDepotScope scope, Guid? relationId, Guid? regionId, (Guid RelationId, Guid AddressId) depot, int position = 1) => new()
    {
        Scope = scope,
        RelationId = relationId,
        GeographicalRegionId = regionId,
        DepotRelationId = depot.RelationId,
        DepotAddressId = depot.AddressId,
        ArrivalWaypointTypeId = DepotIn,
        DepartureWaypointTypeId = DepotOut,
        Position = position
    };

    [Fact]
    public async Task Create_validates_the_scope_and_the_master_data_and_the_list_shows_names_sorted_by_scope_and_position()
    {
        using var _ = InTenantA();
        var depot = await PlaceAsync("DEPOT", "Depot", 52.09, 5.12, RelationRoles.Depot, "Utrecht");
        var customer = await PlaceAsync("CUST", "Customer", 52, 5);
        var region = (await GetRequiredService<IGeographicalRegionAppService>().CreateAsync(new GeographicalRegionCreateDto { Name = "North" })).Id;

        var byRegion = await Rules.CreateAsync(Dto(ViaDepotScope.Region, null, region, depot, 2));
        var byCustomer = await Rules.CreateAsync(Dto(ViaDepotScope.Customer, customer.RelationId, null, depot, 1));
        byRegion.GeographicalRegionName.ShouldBe("North");
        byCustomer.RelationName.ShouldBe("Customer");
        byCustomer.DepotRelationName.ShouldBe("Depot");
        byCustomer.DepotAddressCity.ShouldBe("Utrecht");
        byCustomer.DepartureWaypointTypeId.ShouldBe(DepotOut);

        (await Should.ThrowAsync<BusinessException>(() => Rules.CreateAsync(Dto(ViaDepotScope.Region, customer.RelationId, region, depot)))).Code.ShouldBe(PlanningExecutionErrorCodes.InvalidViaDepotScope);
        (await Should.ThrowAsync<BusinessException>(() => Rules.CreateAsync(Dto(ViaDepotScope.Relation, null, null, depot)))).Code.ShouldBe(PlanningExecutionErrorCodes.InvalidViaDepotScope);
        (await Should.ThrowAsync<BusinessException>(() => Rules.CreateAsync(Dto(ViaDepotScope.Relation, Guid.NewGuid(), null, depot)))).Code.ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound);
        (await Should.ThrowAsync<BusinessException>(() => Rules.CreateAsync(Dto(ViaDepotScope.Region, null, Guid.NewGuid(), depot)))).Code.ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound);
        (await Should.ThrowAsync<BusinessException>(() => Rules.CreateAsync(Dto(ViaDepotScope.Customer, customer.RelationId, null, (depot.RelationId, customer.AddressId)))))
            .Code.ShouldBe(ReferenceDataErrorCodes.ReferenceNotFound, "the depot address must belong to the depot relation");

        var rows = await Rules.GetListAsync(List());
        rows.Rows.Select(r => r.Scope).ShouldBe([ViaDepotScope.Customer, ViaDepotScope.Region]);
        (await Rules.GetListAsync(List(sortBy: "position", sortDir: "desc"))).Rows.Select(r => r.Position).ShouldBe([2, 1]);
    }

    [Fact]
    public async Task Update_archive_delete_and_bulk_delete_follow_the_reference_data_shape()
    {
        using var _ = InTenantA();
        var depot = await PlaceAsync("DEPOT", "Depot", 52.09, 5.12, RelationRoles.Depot);
        var customer = await PlaceAsync("CUST", "Customer", 52, 5);
        var rule = await Rules.CreateAsync(Dto(ViaDepotScope.Customer, customer.RelationId, null, depot));

        var update = new ViaDepotRuleUpdateDto
        {
            Scope = ViaDepotScope.Relation, RelationId = customer.RelationId, DepotRelationId = depot.RelationId, DepotAddressId = depot.AddressId,
            ArrivalWaypointTypeId = DepotIn, Overnight = true, Position = 3, IsArchived = true, Description = "Night runs", ConcurrencyStamp = rule.ConcurrencyStamp
        };
        var updated = await Rules.UpdateAsync(rule.Id, update);
        updated.Scope.ShouldBe(ViaDepotScope.Relation);
        updated.Overnight.ShouldBeTrue();
        updated.DepartureWaypointTypeId.ShouldBeNull();
        updated.IsArchived.ShouldBeTrue();
        updated.Description.ShouldBe("Night runs");
        (await Rules.GetListAsync(List())).Rows.ShouldBeEmpty("archived rows are hidden");
        (await Rules.GetListAsync(List(includeArchived: true))).Rows.ShouldHaveSingleItem();

        update.ConcurrencyStamp = rule.ConcurrencyStamp;
        await Should.ThrowAsync<Volo.Abp.Data.AbpDbConcurrencyException>(() => Rules.UpdateAsync(rule.Id, update));

        var second = await Rules.CreateAsync(Dto(ViaDepotScope.Customer, customer.RelationId, null, depot, 2));
        var result = await Rules.BulkDeleteAsync(new ArchivableBulkDeleteRequestDto { Selection = Explicit(second.Id, rule.Id), IncludeArchived = false });
        result.DeletedCount.ShouldBe(2, "an explicit selection names its rows regardless of the archive filter");
        await Should.ThrowAsync<EntityNotFoundException>(() => Rules.GetAsync(rule.Id));
        await Should.ThrowAsync<EntityNotFoundException>(() => Rules.DeleteAsync(second.Id));
    }

    [Fact]
    public async Task Another_tenant_sees_nothing()
    {
        Guid ruleId;
        using (InTenantA())
        {
            var depot = await PlaceAsync("DEPOT", "Depot", 52.09, 5.12, RelationRoles.Depot);
            ruleId = (await Rules.CreateAsync(Dto(ViaDepotScope.Relation, depot.RelationId, null, depot))).Id;
        }

        using var _ = InTenantB();
        (await Rules.GetListAsync(List())).Rows.ShouldBeEmpty();
        await Should.ThrowAsync<EntityNotFoundException>(() => Rules.GetAsync(ruleId));
    }
}
