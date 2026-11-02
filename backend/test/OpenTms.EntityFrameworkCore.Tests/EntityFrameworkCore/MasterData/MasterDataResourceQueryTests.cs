using System;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.MasterData;
using OpenTms.MasterData.Drivers;
using OpenTms.MasterData.Fleet;
using OpenTms.MasterData.Published;
using OpenTms.MasterData.Reference;
using OpenTms.MasterData.Relations;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Identity;
using Xunit;

namespace OpenTms.EntityFrameworkCore.MasterData;

/// <summary>
/// Design 69, the published resource query on SQLite: vehicle kinds and organisation-unit filters, explicit ids, search
/// over name and plate (drivers: code and name), paging with the 500-row clamp, archived rows excluded unless asked, the
/// default drivers in their order, more than 500 explicit ids refused, and the negative tenant-isolation test.
/// Risk class: High (tenant data, published contract).
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class MasterDataResourceQueryTests : MasterDataAppServiceTestBase
{
    private IMasterDataResourceQuery Query => GetRequiredService<IMasterDataResourceQuery>();

    private sealed record Fleet(Guid Tractor, Guid Rigid, Guid Van, Guid Trailer, Guid ArchivedTractor, Guid Unit, Guid DriverA, Guid DriverB, Guid ArchivedDriver, Guid Owner);

    private async Task<Fleet> FleetAsync()
    {
        var nl = (await GetRequiredService<ICountryAppService>().CreateAsync(new CountryCreateDto { IsoAlpha2 = "NL", IsoAlpha3 = "NLD", Names = Names("Netherlands", "Nederland") })).Id;
        var types = GetRequiredService<IVehicleTypeAppService>();
        var tractorType = (await types.CreateAsync(new VehicleTypeCreateDto { Name = "Tractor 4x2", Kind = VehicleTypeKind.Tractor })).Id;
        var rigidType = (await types.CreateAsync(new VehicleTypeCreateDto { Name = "Box truck", Kind = VehicleTypeKind.Rigid })).Id;
        var vanType = (await types.CreateAsync(new VehicleTypeCreateDto { Name = "Delivery van", Kind = VehicleTypeKind.Van })).Id;
        var trailerType = (await types.CreateAsync(new VehicleTypeCreateDto { Name = "Curtainsider", Kind = VehicleTypeKind.Trailer })).Id;
        var owner = (await GetRequiredService<IRelationAppService>().CreateAsync(new RelationCreateDto { Code = "OWN", Name = "Own Transport", Roles = RelationRoles.OwnCompany })).Id;
        var drivers = GetRequiredService<IDriverAppService>();
        var driverA = (await drivers.CreateAsync(new DriverCreateDto { Code = "D01", FirstName = "Jan", MiddleName = "van", LastName = "Dijk", Phone = "+31600000000", Email = "jan@example.com" })).Id;
        var driverB = (await drivers.CreateAsync(new DriverCreateDto { Code = "D02", FirstName = "Piet", LastName = "Pieters" })).Id;
        var archivedDriver = (await drivers.CreateAsync(new DriverCreateDto { Code = "D00", FirstName = "Old", LastName = "Hand", IsArchived = true })).Id;

        var unit = Guid.NewGuid();
        await WithUnitOfWorkAsync(() => GetRequiredService<OrganizationUnitManager>().CreateAsync(new OrganizationUnit(unit, "Depot Utrecht", null, CurrentTenant.Id)));

        var vehicles = GetRequiredService<IVehicleAppService>();
        var tractor = (await vehicles.CreateAsync(new VehicleCreateDto
        {
            Name = "Truck 01", VehicleTypeId = tractorType, OwnerRelationId = owner, LicencePlate = "12-ABC-3", RegistrationCountryId = nl,
            DefaultDrivers = [new VehicleDefaultDriverDto { DriverId = driverB, SortOrder = 1 }, new VehicleDefaultDriverDto { DriverId = driverA, SortOrder = 2 }],
            OrganizationUnitIds = [unit]
        })).Id;
        var rigid = (await vehicles.CreateAsync(new VehicleCreateDto { Name = "Box 07", VehicleTypeId = rigidType, OwnerRelationId = owner })).Id;
        var van = (await vehicles.CreateAsync(new VehicleCreateDto { Name = "Van 02", VehicleTypeId = vanType, OwnerRelationId = owner, OrganizationUnitIds = [unit] })).Id;
        var trailer = (await vehicles.CreateAsync(new VehicleCreateDto { Name = "Trailer 03", VehicleTypeId = trailerType, OwnerRelationId = owner })).Id;
        var archived = (await vehicles.CreateAsync(new VehicleCreateDto { Name = "Truck 00", VehicleTypeId = tractorType, OwnerRelationId = owner, IsArchived = true })).Id;
        return new Fleet(tractor, rigid, van, trailer, archived, unit, driverA, driverB, archivedDriver, owner);
    }

    private static MasterDataVehicleResourceQuery Motor() => new() { Kinds = [VehicleTypeKind.Tractor, VehicleTypeKind.Rigid, VehicleTypeKind.Van] };

    [Fact]
    public async Task Vehicles_are_filtered_by_kind_sorted_by_name_and_carry_their_type_owner_drivers_and_units()
    {
        using var _ = InTenantA();
        var fleet = await FleetAsync();

        var motor = await Query.GetVehiclesAsync(Motor());
        motor.Items.Select(v => v.Name).ShouldBe(["Box 07", "Truck 01", "Van 02"], "the archived tractor and the trailer are not motor rows; sorted by normalised name");
        motor.TotalCount.ShouldBe(3);
        var truck = motor.Items.Single(v => v.Id == fleet.Tractor);
        truck.Kind.ShouldBe(VehicleTypeKind.Tractor);
        truck.VehicleTypeName.ShouldBe("Tractor 4x2");
        truck.LicencePlate.ShouldBe("12ABC3");
        truck.OwnerRelationId.ShouldBe(fleet.Owner);
        truck.DefaultDriverIds.ShouldBe([fleet.DriverB, fleet.DriverA], "in their configured order");
        truck.OrganizationUnitIds.ShouldBe([fleet.Unit]);
        truck.IsArchived.ShouldBeFalse();

        var trailers = await Query.GetVehiclesAsync(new MasterDataVehicleResourceQuery { Kinds = [VehicleTypeKind.Trailer] });
        trailers.Items.ShouldHaveSingleItem().Id.ShouldBe(fleet.Trailer);

        await Should.ThrowAsync<ArgumentException>(() => Query.GetVehiclesAsync(new MasterDataVehicleResourceQuery()));
    }

    [Fact]
    public async Task Organisation_units_explicit_ids_search_and_archived_narrow_the_vehicles()
    {
        using var _ = InTenantA();
        var fleet = await FleetAsync();

        (await Query.GetVehiclesAsync(new MasterDataVehicleResourceQuery { Kinds = Motor().Kinds, OrganizationUnitIds = [fleet.Unit] })).Items.Select(v => v.Id).ShouldBe([fleet.Tractor, fleet.Van]);
        (await Query.GetVehiclesAsync(new MasterDataVehicleResourceQuery { Kinds = Motor().Kinds, OrganizationUnitIds = [Guid.NewGuid()] })).Items.ShouldBeEmpty();
        (await Query.GetVehiclesAsync(new MasterDataVehicleResourceQuery { Kinds = Motor().Kinds, Ids = [fleet.Rigid, fleet.Trailer, Guid.Empty] })).Items.ShouldHaveSingleItem().Id.ShouldBe(fleet.Rigid, "the ids narrow within the kinds");

        (await Query.GetVehiclesAsync(new MasterDataVehicleResourceQuery { Kinds = Motor().Kinds, Search = "TRUCK" })).Items.ShouldHaveSingleItem().Id.ShouldBe(fleet.Tractor, "the name, case-insensitive");
        (await Query.GetVehiclesAsync(new MasterDataVehicleResourceQuery { Kinds = Motor().Kinds, Search = "abc-3" })).Items.ShouldHaveSingleItem().Id.ShouldBe(fleet.Tractor, "the plate without separators");
        (await Query.GetVehiclesAsync(new MasterDataVehicleResourceQuery { Kinds = Motor().Kinds, Search = "   " })).TotalCount.ShouldBe(3, "a blank search is no search");

        var withArchived = await Query.GetVehiclesAsync(new MasterDataVehicleResourceQuery { Kinds = [VehicleTypeKind.Tractor], IncludeArchived = true });
        withArchived.Items.Select(v => v.Id).ShouldBe([fleet.ArchivedTractor, fleet.Tractor]);
        withArchived.Items.First().IsArchived.ShouldBeTrue();
        (await Query.GetVehiclesAsync(new MasterDataVehicleResourceQuery { Kinds = [VehicleTypeKind.Tractor], Ids = [fleet.ArchivedTractor] })).Items.ShouldBeEmpty("archived is excluded even by id unless asked");
    }

    [Fact]
    public async Task Paging_skips_and_takes_with_the_take_clamped_to_one_through_five_hundred()
    {
        using var _ = InTenantA();
        await FleetAsync();

        var page = await Query.GetVehiclesAsync(new MasterDataVehicleResourceQuery { Kinds = Motor().Kinds, Skip = 1, Take = 1 });
        page.Items.Select(v => v.Name).ShouldBe(["Truck 01"]);
        page.TotalCount.ShouldBe(3, "the total ignores the page");

        (await Query.GetVehiclesAsync(new MasterDataVehicleResourceQuery { Kinds = Motor().Kinds, Take = 0 })).Items.Count.ShouldBe(1, "a take below one is one");
        (await Query.GetVehiclesAsync(new MasterDataVehicleResourceQuery { Kinds = Motor().Kinds, Take = -5, Skip = -3 })).Items.Select(v => v.Name).ShouldBe(["Box 07"], "a negative skip is zero");
        (await Query.GetVehiclesAsync(new MasterDataVehicleResourceQuery { Kinds = Motor().Kinds, Take = 100_000 })).Items.Count.ShouldBe(3);
        (await Query.GetDriversAsync(new MasterDataDriverResourceQuery { Take = 0 })).Items.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Five_hundred_explicit_ids_are_allowed_and_five_hundred_and_one_are_refused()
    {
        using var _ = InTenantA();
        await FleetAsync();

        var fiveHundred = Enumerable.Range(0, IMasterDataResourceQuery.MaxTake).Select(_ => Guid.NewGuid()).ToList();
        (await Query.GetVehiclesAsync(new MasterDataVehicleResourceQuery { Kinds = Motor().Kinds, Ids = fiveHundred })).Items.ShouldBeEmpty();
        (await Query.GetDriversAsync(new MasterDataDriverResourceQuery { Ids = fiveHundred })).Items.ShouldBeEmpty();

        var tooMany = fiveHundred.Append(Guid.NewGuid()).ToList();
        (await Should.ThrowAsync<BusinessException>(() => Query.GetVehiclesAsync(new MasterDataVehicleResourceQuery { Kinds = Motor().Kinds, Ids = tooMany }))).Code.ShouldBe(MasterDataErrorCodes.TooManyRows);
        (await Should.ThrowAsync<BusinessException>(() => Query.GetDriversAsync(new MasterDataDriverResourceQuery { Ids = tooMany }))).Code.ShouldBe(MasterDataErrorCodes.TooManyRows);

        var duplicates = Enumerable.Repeat(fiveHundred[0], 600).ToList();
        (await Query.GetDriversAsync(new MasterDataDriverResourceQuery { Ids = duplicates })).Items.ShouldBeEmpty("duplicates count once");
    }

    [Fact]
    public async Task Drivers_are_sorted_by_code_searched_over_code_and_name_and_carry_no_contact_data()
    {
        using var _ = InTenantA();
        var fleet = await FleetAsync();

        var all = await Query.GetDriversAsync(new MasterDataDriverResourceQuery());
        all.Items.Select(d => d.Code).ShouldBe(["D01", "D02"], "the archived driver is excluded");
        all.Items[0].Name.ShouldBe("Jan van Dijk");
        all.TotalCount.ShouldBe(2);

        (await Query.GetDriversAsync(new MasterDataDriverResourceQuery { Search = "piet" })).Items.ShouldHaveSingleItem().Id.ShouldBe(fleet.DriverB);
        (await Query.GetDriversAsync(new MasterDataDriverResourceQuery { Search = "d01" })).Items.ShouldHaveSingleItem().Id.ShouldBe(fleet.DriverA);
        (await Query.GetDriversAsync(new MasterDataDriverResourceQuery { Ids = [fleet.DriverB] })).Items.ShouldHaveSingleItem().Id.ShouldBe(fleet.DriverB);
        (await Query.GetDriversAsync(new MasterDataDriverResourceQuery { IncludeArchived = true })).Items.Select(d => d.Code).ShouldBe(["D00", "D01", "D02"]);
        (await Query.GetDriversAsync(new MasterDataDriverResourceQuery { Skip = 1, Take = 1 })).Items.Select(d => d.Code).ShouldBe(["D02"]);
    }

    [Fact]
    public async Task Negative_tenant_isolation_tenant_B_reads_none_of_tenant_A_resources_not_even_by_id()
    {
        Fleet fleet;
        using (InTenantA())
        {
            fleet = await FleetAsync();
        }

        using (InTenantB())
        {
            var vehicles = await Query.GetVehiclesAsync(new MasterDataVehicleResourceQuery { Kinds = Enum.GetValues<VehicleTypeKind>().ToList(), IncludeArchived = true });
            vehicles.Items.ShouldBeEmpty();
            vehicles.TotalCount.ShouldBe(0);
            (await Query.GetVehiclesAsync(new MasterDataVehicleResourceQuery { Kinds = Motor().Kinds, Ids = [fleet.Tractor, fleet.Van], IncludeArchived = true })).Items.ShouldBeEmpty();
            (await Query.GetVehiclesAsync(new MasterDataVehicleResourceQuery { Kinds = Motor().Kinds, OrganizationUnitIds = [fleet.Unit] })).Items.ShouldBeEmpty();
            (await Query.GetDriversAsync(new MasterDataDriverResourceQuery { IncludeArchived = true })).Items.ShouldBeEmpty();
            (await Query.GetDriversAsync(new MasterDataDriverResourceQuery { Ids = [fleet.DriverA], IncludeArchived = true })).TotalCount.ShouldBe(0);
            (await Query.GetDriversAsync(new MasterDataDriverResourceQuery { Search = "jan" })).Items.ShouldBeEmpty();
        }
    }
}
