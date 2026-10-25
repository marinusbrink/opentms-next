using System;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.MasterData;
using OpenTms.MasterData.Drivers;
using OpenTms.MasterData.Fleet;
using OpenTms.MasterData.Published;
using OpenTms.MasterData.Reference;
using OpenTms.MasterData.Relations;
using OpenTms.MasterData.Transport;
using Shouldly;
using Volo.Abp;
using Volo.Abp.MultiTenancy;
using Xunit;
using MasterDataNames = OpenTms.MasterData.LocalizedNameDto;

namespace OpenTms.EntityFrameworkCore.MasterData;

/// <summary>
/// The reads design 59 added to the Master Data published lookup (addresses, vehicles with their type's figures and the
/// union of characteristics, combinations, drivers, incompatibilities): what Planning's calculators consume. Tenant
/// isolation and the key bound as the existing lookup methods. Risk class: Critical (tenant), High.
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class MasterDataReferenceLookupFleetTests : OpenTmsEntityFrameworkCoreTestBase
{
    private readonly IMasterDataReferenceLookup _lookup;
    private readonly ICurrentTenant _currentTenant;
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();

    public MasterDataReferenceLookupFleetTests()
    {
        _lookup = GetRequiredService<IMasterDataReferenceLookup>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
    }

    private Guid _countryId;

    private async Task<(Guid RelationId, Guid AddressId)> RelationWithAddressAsync(string code, string name, double? latitude)
    {
        var country = await GetRequiredService<ICountryAppService>().CreateAsync(new CountryCreateDto
        {
            IsoAlpha2 = "NL", IsoAlpha3 = "NLD", Names = new MasterDataNames { ["en"] = "Netherlands" }, PrimaryTimeZoneId = "Europe/Amsterdam"
        });
        _countryId = country.Id;
        var relations = GetRequiredService<IRelationAppService>();
        var relation = await relations.CreateAsync(new RelationCreateDto
        {
            Code = code,
            Name = name,
            Roles = RelationRoles.Customer | RelationRoles.Depot | RelationRoles.OwnCompany,
            Addresses = [new RelationAddressDto { CountryId = country.Id, Street = "Dam", PostalCode = "1012JS", City = "Amsterdam", Latitude = latitude, Longitude = latitude == null ? null : 4.89, TimeZoneId = "Europe/Amsterdam" }]
        });
        return (relation.Id, (await relations.GetAddressesAsync(relation.Id)).Single().Id!.Value);
    }

    [Fact]
    public async Task Addresses_vehicles_combinations_drivers_and_incompatibilities_come_back_with_the_calculator_facts()
    {
        using var _ = _currentTenant.Change(_tenantA);
        var (relationId, addressId) = await RelationWithAddressAsync("OWN", "Own company", 52.37);
        var reefer = (await GetRequiredService<ITransportCharacteristicsAppService>().CreateAsync(new TransportCharacteristicsCreateDto { Code = "REEFER", Name = "Reefer" })).Id;
        var tailLift = (await GetRequiredService<ITransportCharacteristicsAppService>().CreateAsync(new TransportCharacteristicsCreateDto { Code = "TAIL", Name = "Tail lift" })).Id;
        var trailerType = (await GetRequiredService<IVehicleTypeAppService>().CreateAsync(new VehicleTypeCreateDto
        {
            Name = "Reefer trailer", Kind = VehicleTypeKind.Trailer, MaxEpq = 33, MaxLoadingMeters = 13.6, DefaultGrossVehicleWeightKg = 36_000, TransportCharacteristicsIds = [reefer]
        })).Id;
        var vehicles = GetRequiredService<IVehicleAppService>();
        var trailer = (await vehicles.CreateAsync(new VehicleCreateDto
        {
            Name = "Trailer 1", VehicleTypeId = trailerType, OwnerRelationId = relationId, MaxPayloadKg = 24_000, MaxEpq = 34, LicencePlate = "OJ-12-AB", RegistrationCountryId = _countryId, TransportCharacteristicIds = [tailLift]
        })).Id;
        var tractorType = (await GetRequiredService<IVehicleTypeAppService>().CreateAsync(new VehicleTypeCreateDto { Name = "Tractor", Kind = VehicleTypeKind.Tractor })).Id;
        var tractor = (await vehicles.CreateAsync(new VehicleCreateDto { Name = "Truck 1", VehicleTypeId = tractorType, OwnerRelationId = relationId, GrossVehicleWeightKg = 18_000, EuroClass = EuroClass.Euro6, FuelType = FuelType.Hvo })).Id;
        var combination = (await GetRequiredService<IVehicleCombinationAppService>().CreateAsync(new VehicleCombinationCreateDto
        {
            Name = "Combi", Members = [new VehicleCombinationMemberInputDto { VehicleId = tractor, Position = 1 }, new VehicleCombinationMemberInputDto { VehicleId = trailer, Position = 2 }]
        })).Id;
        var driver = (await GetRequiredService<IDriverAppService>().CreateAsync(new DriverCreateDto { Code = "D1", FirstName = "Jan", MiddleName = "van", LastName = "Dijk" })).Id;
        var temperatures = GetRequiredService<ITemperatureAppService>();
        var frozen = (await temperatures.CreateAsync(new TemperatureCreateDto { Name = "Frozen" })).Id;
        var ambient = (await temperatures.CreateAsync(new TemperatureCreateDto { Name = "Ambient", IncompatibleTemperatureIds = [frozen] })).Id;
        var chilled = (await temperatures.CreateAsync(new TemperatureCreateDto { Name = "Chilled" })).Id;

        var addresses = await _lookup.GetRelationAddressesAsync([addressId, Guid.NewGuid()]);
        var address = addresses.ShouldHaveSingleItem();
        address.RelationId.ShouldBe(relationId);
        address.RelationName.ShouldBe("Own company");
        address.City.ShouldBe("Amsterdam");
        address.CountryCode.ShouldBe("NL");
        address.Latitude.ShouldBe(52.37);
        address.TimeZoneId.ShouldBe("Europe/Amsterdam");
        (await _lookup.GetAddressesOfRelationsAsync([relationId])).ShouldHaveSingleItem().Id.ShouldBe(addressId);

        var fleet = await _lookup.GetVehiclesAsync([trailer, tractor, Guid.NewGuid()]);
        fleet.Count.ShouldBe(2);
        var trailerRow = fleet.Single(v => v.Id == trailer);
        trailerRow.Kind.ShouldBe(VehicleTypeKind.Trailer);
        trailerRow.VehicleTypeName.ShouldBe("Reefer trailer");
        trailerRow.LicencePlate.ShouldBe("OJ12AB");
        trailerRow.MaxEpq.ShouldBe(34, "the vehicle's own figure wins");
        trailerRow.MaxLoadingMeters.ShouldBe(13.6, "the type's figure when the vehicle has none");
        trailerRow.MaxPayloadKg.ShouldBe(24_000);
        trailerRow.GrossVehicleWeightKg.ShouldBe(36_000, "the type's default when the vehicle has none");
        trailerRow.TransportCharacteristicIds.ShouldBe([reefer, tailLift], ignoreOrder: true);
        var tractorRow = fleet.Single(v => v.Id == tractor);
        tractorRow.EuroClass.ShouldBe(EuroClass.Euro6);
        tractorRow.FuelType.ShouldBe(FuelType.Hvo);
        tractorRow.TransportCharacteristicIds.ShouldBeEmpty();

        var combinations = await _lookup.GetVehicleCombinationsAsync([combination]);
        combinations.ShouldHaveSingleItem().VehicleIds.ShouldBe([tractor, trailer]);

        var drivers = await _lookup.GetDriversAsync([driver]);
        drivers.ShouldHaveSingleItem().Name.ShouldBe("Jan van Dijk");

        var pairs = await _lookup.GetIncompatibilitiesAsync([frozen, ambient, chilled], [], [Guid.NewGuid()]);
        var pair = pairs.Temperatures.ShouldHaveSingleItem();
        new[] { pair.LeftId, pair.RightId }.ShouldBe([frozen, ambient], ignoreOrder: true);
        pairs.Products.ShouldBeEmpty();
        pairs.TransportUnits.ShouldBeEmpty();
        (await _lookup.GetIncompatibilitiesAsync([frozen], [], [])).Temperatures.ShouldBeEmpty("a pair needs both sides in the input");

        /* Another tenant sees none of it. */
        using (_currentTenant.Change(_tenantB))
        {
            (await _lookup.GetRelationAddressesAsync([addressId])).ShouldBeEmpty();
            (await _lookup.GetAddressesOfRelationsAsync([relationId])).ShouldBeEmpty();
            (await _lookup.GetVehiclesAsync([trailer, tractor])).ShouldBeEmpty();
            (await _lookup.GetVehicleCombinationsAsync([combination])).ShouldBeEmpty();
            (await _lookup.GetDriversAsync([driver])).ShouldBeEmpty();
            (await _lookup.GetIncompatibilitiesAsync([frozen, ambient], [], [])).Temperatures.ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task Empty_inputs_return_nothing_and_more_than_the_bound_is_refused()
    {
        using var _ = _currentTenant.Change(_tenantA);
        (await _lookup.GetVehiclesAsync([])).ShouldBeEmpty();
        (await _lookup.GetDriversAsync([Guid.Empty])).ShouldBeEmpty();
        (await _lookup.GetRelationAddressesAsync([])).ShouldBeEmpty();
        var tooMany = Enumerable.Range(0, IMasterDataReferenceLookup.MaxKeys + 1).Select(_ => Guid.NewGuid()).ToList();
        (await Should.ThrowAsync<BusinessException>(() => _lookup.GetVehiclesAsync(tooMany))).Code.ShouldBe(MasterDataErrorCodes.TooManyRows);
        (await Should.ThrowAsync<BusinessException>(() => _lookup.GetIncompatibilitiesAsync(tooMany, [], []))).Code.ShouldBe(MasterDataErrorCodes.TooManyRows);
    }
}
