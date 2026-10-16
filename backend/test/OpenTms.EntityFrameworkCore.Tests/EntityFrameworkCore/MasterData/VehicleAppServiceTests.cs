using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Features;
using OpenTms.MasterData;
using OpenTms.MasterData.Drivers;
using OpenTms.MasterData.Fleet;
using OpenTms.MasterData.Reference;
using OpenTms.MasterData.Relations;
using OpenTms.Platform.Features;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.Validation;
using Xunit;

namespace OpenTms.EntityFrameworkCore.MasterData;

/// <summary>
/// VehicleAppService on SQLite (design 48, test risk analysis). Risk class: High for the plate normalisation and
/// formatting, the VIN shape, the type defaults on create, the characteristic union with origin, the owner and
/// home-depot role checks, the in-use refusals in the VehicleType / Relation / Driver deletes, the list, the lookup
/// page and the bulk delete; Critical for the tenant isolation and the feature gate (both proven against real
/// PostgreSQL as well, see PostgreSql/MasterData/VehicleTenantIsolationTests).
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class VehicleAppServiceTests : MasterDataAppServiceTestBase
{
    /// <summary>
    /// A fresh instance per call, as a request resolves it: the service caches IQueryables in fields, so one instance
    /// cannot serve two units of work (finding, see One_service_instance_serves_two_list_calls_in_separate_units_of_work).
    /// </summary>
    private IVehicleAppService Service => GetRequiredService<IVehicleAppService>();
    private readonly IVehicleTypeAppService _types;
    private readonly IRelationAppService _relations;
    private readonly IDriverAppService _drivers;
    private readonly ICountryAppService _countries;
    private readonly ITransportCharacteristicsAppService _characteristics;
    private readonly OrganizationUnitManager _organizationUnits;
    private readonly IRepository<VehicleTransportCharacteristic> _characteristicRows;
    private readonly IRepository<VehicleDefaultDriver> _driverRows;
    private readonly IRepository<VehicleExternalIdentifier, Guid> _externalRows;

    private Guid _nl;
    private Guid _be;
    private Guid _tractorType;
    private Guid _trailerType;
    private Guid _rigidType;
    private Guid _ownCompany;
    private Guid _carrier;
    private Guid _depot;
    private Guid _customer;
    private Guid _adr;
    private Guid _tailLift;
    private Guid _cooling;
    private Guid _unit;

    public VehicleAppServiceTests()
    {
        _types = GetRequiredService<IVehicleTypeAppService>();
        _relations = GetRequiredService<IRelationAppService>();
        _drivers = GetRequiredService<IDriverAppService>();
        _countries = GetRequiredService<ICountryAppService>();
        _characteristics = GetRequiredService<ITransportCharacteristicsAppService>();
        _organizationUnits = GetRequiredService<OrganizationUnitManager>();
        _characteristicRows = GetRequiredService<IRepository<VehicleTransportCharacteristic>>();
        _driverRows = GetRequiredService<IRepository<VehicleDefaultDriver>>();
        _externalRows = GetRequiredService<IRepository<VehicleExternalIdentifier, Guid>>();
    }

    // ── set-up ───────────────────────────────────────────────────────────────

    /// <summary>The reference rows of the current tenant every vehicle test needs (design 41 and 43 entities).</summary>
    private async Task SeedAsync()
    {
        _nl = (await _countries.CreateAsync(new CountryCreateDto { IsoAlpha2 = "NL", IsoAlpha3 = "NLD", Names = Names("Netherlands", "Nederland") })).Id;
        _be = (await _countries.CreateAsync(new CountryCreateDto { IsoAlpha2 = "BE", IsoAlpha3 = "BEL", Names = Names("Belgium", "België") })).Id;
        _adr = (await _characteristics.CreateAsync(new TransportCharacteristicsCreateDto { Code = "ADR", Name = "Dangerous goods" })).Id;
        _tailLift = (await _characteristics.CreateAsync(new TransportCharacteristicsCreateDto { Code = "TL", Name = "Tail lift" })).Id;
        _cooling = (await _characteristics.CreateAsync(new TransportCharacteristicsCreateDto { Code = "COOL", Name = "Cooling" })).Id;
        _tractorType = (await _types.CreateAsync(new VehicleTypeCreateDto
        {
            Name = "Tractor 4x2", Kind = VehicleTypeKind.Tractor, DefaultLengthCm = 600, DefaultWidthCm = 255, DefaultHeightCm = 400,
            DefaultGrossVehicleWeightKg = 18_000, TransportCharacteristicsIds = [_adr, _tailLift]
        })).Id;
        _trailerType = (await _types.CreateAsync(new VehicleTypeCreateDto
        {
            Name = "Curtainsider", Kind = VehicleTypeKind.Trailer, DefaultLengthCm = 1360, DefaultWidthCm = 248, DefaultHeightCm = 400,
            DefaultGrossVehicleWeightKg = 39_000, MaxEpq = 33, MaxLoadingMeters = 13.6
        })).Id;
        _rigidType = (await _types.CreateAsync(new VehicleTypeCreateDto { Name = "Box truck", Kind = VehicleTypeKind.Rigid })).Id;
        _ownCompany = (await _relations.CreateAsync(new RelationCreateDto { Code = "OWN", Name = "Own Transport B.V.", Roles = RelationRoles.OwnCompany })).Id;
        _carrier = (await _relations.CreateAsync(new RelationCreateDto { Code = "CAR", Name = "Charter Carrier", Roles = RelationRoles.Carrier })).Id;
        _depot = (await _relations.CreateAsync(new RelationCreateDto { Code = "DEP", Name = "Depot Utrecht", Roles = RelationRoles.Depot })).Id;
        _customer = (await _relations.CreateAsync(new RelationCreateDto { Code = "CUS", Name = "Customer", Roles = RelationRoles.Customer })).Id;
        _unit = Guid.NewGuid();
        await WithUnitOfWorkAsync(() => _organizationUnits.CreateAsync(new OrganizationUnit(_unit, "Depot Utrecht", null, CurrentTenant.Id)));
    }

    private VehicleCreateDto Dto(string name, Guid? typeId = null, string? plate = null, Guid? countryId = null, string? vin = null, Guid? ownerId = null, Action<VehicleCreateDto>? configure = null)
    {
        var dto = new VehicleCreateDto
        {
            Name = name, VehicleTypeId = typeId ?? _tractorType, LicencePlate = plate, RegistrationCountryId = countryId ?? (plate != null ? _nl : null),
            Vin = vin, OwnerRelationId = ownerId ?? _ownCompany
        };
        configure?.Invoke(dto);
        return dto;
    }

    private Task<VehicleDto> CreateAsync(string name, Guid? typeId = null, string? plate = null, Guid? countryId = null, string? vin = null, Guid? ownerId = null, Action<VehicleCreateDto>? configure = null)
        => Service.CreateAsync(Dto(name, typeId, plate, countryId, vin, ownerId, configure));

    private static VehicleUpdateDto UpdateFrom(VehicleDto dto, Action<VehicleUpdateDto>? configure = null)
    {
        var update = new VehicleUpdateDto
        {
            Name = dto.Name, VehicleTypeId = dto.VehicleTypeId, LicencePlate = dto.LicencePlate, RegistrationCountryId = dto.RegistrationCountryId, Vin = dto.Vin,
            OwnerRelationId = dto.OwnerRelationId, HomeDepotRelationId = dto.HomeDepotRelationId, LengthCm = dto.LengthCm, WidthCm = dto.WidthCm, HeightCm = dto.HeightCm,
            GrossVehicleWeightKg = dto.GrossVehicleWeightKg, EmptyWeightKg = dto.EmptyWeightKg, MaxEpq = dto.MaxEpq, MaxLoadingMeters = dto.MaxLoadingMeters,
            MaxPayloadKg = dto.MaxPayloadKg, FuelType = dto.FuelType, EuroClass = dto.EuroClass, IsArchived = dto.IsArchived, Description = dto.Description,
            TransportCharacteristicIds = dto.Characteristics.Where(c => c.Origin == VehicleCharacteristicOrigin.Vehicle).Select(c => c.Id).ToList(),
            DefaultDrivers = dto.DefaultDrivers.Select(d => new VehicleDefaultDriverDto { DriverId = d.DriverId, SortOrder = d.SortOrder }).ToList(),
            OrganizationUnitIds = [.. dto.OrganizationUnitIds],
            ExternalIdentifiers = dto.ExternalIdentifiers.Select(e => new VehicleExternalIdentifierDto { Id = e.Id, Source = e.Source, Identifier = e.Identifier }).ToList(),
            ConcurrencyStamp = dto.ConcurrencyStamp
        };
        configure?.Invoke(update);
        return update;
    }

    private static VehicleListRequestDto Request(string? search = null, string? sortBy = null, string? sortDir = null, bool includeArchived = false, List<VehicleTypeKind>? kinds = null, Guid? typeId = null, Guid? ownerId = null)
    {
        var request = List<VehicleListRequestDto>(search: search, sortBy: sortBy, sortDir: sortDir);
        request.IncludeArchived = includeArchived;
        request.Kinds = kinds;
        request.VehicleTypeId = typeId;
        request.OwnerRelationId = ownerId;
        return request;
    }

    private static async Task<BusinessException> ShouldFailAsync(Func<Task> action, string code, string? field = null)
    {
        var ex = await Should.ThrowAsync<BusinessException>(action);
        ex.Code.ShouldBe(code);
        if (field != null)
        {
            ex.Data["field"].ShouldBe(field);
        }

        return ex;
    }

    // ── CRUD round trip ──────────────────────────────────────────────────────

    [Fact]
    public async Task Create_get_list_and_lookup_round_trip_with_registration_ownership_engine_and_children()
    {
        using var _ = InTenantA();
        await SeedAsync();
        var driver = await _drivers.CreateAsync(new DriverCreateDto { Code = "D1", FirstName = "Jan", LastName = "Jansen" });

        var created = await CreateAsync(" Truck 01 ", plate: " 12-abc-3 ", vin: "wdb9634031l123456", configure: dto =>
        {
            dto.HomeDepotRelationId = _depot;
            dto.FuelType = FuelType.Diesel;
            dto.EuroClass = EuroClass.Euro6;
            dto.Description = "night shifts";
            dto.TransportCharacteristicIds = [_cooling];
            dto.DefaultDrivers = [new VehicleDefaultDriverDto { DriverId = driver.Id, SortOrder = 1 }];
            dto.OrganizationUnitIds = [_unit];
            dto.ExternalIdentifiers = [new VehicleExternalIdentifierDto { Source = " FMS ", Identifier = " 4711 " }];
        });

        created.Name.ShouldBe("Truck 01");
        created.LicencePlate.ShouldBe("12ABC3", "stored normalised: upper case, letters and digits only");
        created.RegistrationCountryId.ShouldBe(_nl);
        created.RegistrationCountryCode.ShouldBe("NL");
        created.Vin.ShouldBe("WDB9634031L123456");
        created.Kind.ShouldBe(VehicleTypeKind.Tractor, "the kind is the type's kind");
        created.VehicleTypeName.ShouldBe("Tractor 4x2");
        created.OwnerRelationId.ShouldBe(_ownCompany);
        created.OwnerName.ShouldBe("Own Transport B.V.");
        created.HomeDepotName.ShouldBe("Depot Utrecht");
        created.FuelType.ShouldBe(FuelType.Diesel);
        created.EuroClass.ShouldBe(EuroClass.Euro6);
        created.Description.ShouldBe("night shifts");
        created.DefaultDrivers.ShouldHaveSingleItem().Name.ShouldBe("Jan Jansen");
        created.OrganizationUnitIds.ShouldBe([_unit]);
        var external = created.ExternalIdentifiers.ShouldHaveSingleItem();
        external.Source.ShouldBe("FMS");
        external.Identifier.ShouldBe("4711");
        created.CombinationName.ShouldBeNull();

        var fetched = await Service.GetAsync(created.Id);
        fetched.LicencePlate.ShouldBe("12ABC3");
        fetched.ConcurrencyStamp.ShouldNotBeNullOrEmpty();

        var row = (await Service.GetListAsync(Request())).Rows.ShouldHaveSingleItem();
        row.LicencePlate.ShouldBe("12-ABC-3", "the list shows a Dutch plate by its sidecode (7: 99-XXX-9)");
        row.RegistrationCountryCode.ShouldBe("NL");
        row.Kind.ShouldBe(VehicleTypeKind.Tractor);
        row.VehicleTypeName.ShouldBe("Tractor 4x2");
        row.OwnerName.ShouldBe("Own Transport B.V.");
        row.HomeDepotName.ShouldBe("Depot Utrecht");
        row.Vin.ShouldBe("WDB9634031L123456");

        var lookup = (await Service.GetLookupAsync()).ShouldHaveSingleItem();
        lookup.Name.ShouldBe("Truck 01");
        var page = await Service.GetLookupPageAsync();
        page.TotalCount.ShouldBe(1);
        page.Items.ShouldHaveSingleItem().LicencePlate.ShouldBe("12-ABC-3");
        page.Items[0].Kind.ShouldBe(VehicleTypeKind.Tractor);
        page.Items[0].VehicleTypeName.ShouldBe("Tractor 4x2");
    }

    // ── Licence plate (High) ─────────────────────────────────────────────────

    [Theory]
    [InlineData("12-abc-3", "NL", "12ABC3", "12-ABC-3")]     // sidecode 7
    [InlineData("ab 12 34", "NL", "AB1234", "AB-12-34")]     // sidecode 1
    [InlineData("12.34.ab", "NL", "1234AB", "12-34-AB")]     // sidecode 2
    [InlineData("v-123-bb", "NL", "V123BB", "V-123-BB")]     // sidecode 10
    [InlineData("1-vtt-23", "NL", "1VTT23", "1-VTT-23")]     // sidecode 8
    [InlineData("gbb-12-x", "NL", "GBB12X", "GBB-12-X")]     // sidecode 11
    [InlineData("ABCDEFG", "NL", "ABCDEFG", "ABCDEFG")]       // seven characters: no Dutch sidecode, shown as stored
    [InlineData("1-abc-123", "BE", "1ABC123", "1ABC123")]     // foreign plates are shown as stored
    [InlineData("12-ab-34", "BE", "12AB34", "12AB34")]       // a Dutch-looking plate of another country is not formatted
    public async Task A_plate_is_stored_normalised_and_listed_formatted_per_registration_country(string typed, string country, string stored, string shown)
    {
        using var _ = InTenantA();
        await SeedAsync();

        var created = await CreateAsync("V1", plate: typed, countryId: country == "NL" ? _nl : _be);

        created.LicencePlate.ShouldBe(stored);
        (await Service.GetListAsync(Request())).Rows.ShouldHaveSingleItem().LicencePlate.ShouldBe(shown);
        (await Service.GetLookupPageAsync()).Items.ShouldHaveSingleItem().LicencePlate.ShouldBe(shown);
    }

    [Theory]
    [InlineData("12#AB")]
    [InlineData("ÄB-12-CD")]
    [InlineData("12ABCDEFGHJKLMNPR")] // 17 characters once normalised; the column holds 16
    public async Task A_plate_with_other_characters_or_too_long_is_refused(string typed)
    {
        using var _ = InTenantA();
        await SeedAsync();

        await ShouldFailAsync(() => CreateAsync("V1", plate: typed), MasterDataErrorCodes.InvalidLicencePlate);
        (await Service.GetListAsync(Request(includeArchived: true))).TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task A_plate_needs_a_registration_country_but_no_plate_needs_none()
    {
        using var _ = InTenantA();
        await SeedAsync();

        await ShouldFailAsync(() => Service.CreateAsync(Dto("V1", configure: d => { d.LicencePlate = "12-ABC-3"; d.RegistrationCountryId = null; })), MasterDataErrorCodes.RegistrationCountryRequired);

        /* Only separators is no plate at all. */
        var empty = await Service.CreateAsync(Dto("V2", configure: d => { d.LicencePlate = " - "; d.RegistrationCountryId = null; }));
        empty.LicencePlate.ShouldBeNull();
        empty.RegistrationCountryId.ShouldBeNull();

        /* A country without a plate is allowed (a new vehicle starts with the default country). */
        var countryOnly = await Service.CreateAsync(Dto("V3", configure: d => d.RegistrationCountryId = _nl));
        countryOnly.LicencePlate.ShouldBeNull();
        countryOnly.RegistrationCountryId.ShouldBe(_nl);
    }

    [Fact]
    public async Task A_plate_is_unique_per_registration_country_whatever_way_it_was_typed()
    {
        using var _ = InTenantA();
        await SeedAsync();
        var first = await CreateAsync("V1", plate: "12-ABC-3");

        var ex = await ShouldFailAsync(() => CreateAsync("V2", plate: "12 abc 3"), MasterDataErrorCodes.DuplicateKey, "licencePlate");
        ex.Data["value"].ShouldBe(string.Empty, "no plate in the error data (rule 5)");

        /* The same characters in another country are another plate. */
        (await CreateAsync("V3", plate: "12ABC3", countryId: _be)).LicencePlate.ShouldBe("12ABC3");

        /* Update to a taken plate is refused too; keeping its own plate is fine. */
        var other = await CreateAsync("V4", plate: "AB-12-34");
        await ShouldFailAsync(() => Service.UpdateAsync(other.Id, UpdateFrom(other, u => u.LicencePlate = "12-abc-3")), MasterDataErrorCodes.DuplicateKey, "licencePlate");
        (await Service.UpdateAsync(first.Id, UpdateFrom(first, u => u.Description = "kept"))).Description.ShouldBe("kept");

        /* A soft delete frees the plate. */
        await Service.DeleteAsync(first.Id);
        (await CreateAsync("V5", plate: "12-ABC-3")).LicencePlate.ShouldBe("12ABC3");
    }

    [Fact]
    public async Task The_list_and_the_lookup_page_find_a_plate_typed_with_separators_and_lower_case()
    {
        using var _ = InTenantA();
        await SeedAsync();
        var probe = await CreateAsync("Truck 01", plate: "12-ABC-3");
        await CreateAsync("Truck 02", plate: "AB-12-34");

        (await Service.GetListAsync(Request(search: "abc-3"))).Rows.ShouldHaveSingleItem().Id.ShouldBe(probe.Id);
        (await Service.GetListAsync(Request(search: "12 abc"))).Rows.ShouldHaveSingleItem().Id.ShouldBe(probe.Id);
        (await Service.GetLookupPageAsync("12-abc")).Items.ShouldHaveSingleItem().Id.ShouldBe(probe.Id);
    }

    // ── VIN (High) ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("WDB9634031I123456")] // I
    [InlineData("WDB9634031O123456")] // O
    [InlineData("WDB9634031Q123456")] // Q
    [InlineData("WDB963403")]         // too short
    [InlineData("WDB9634031L1234567")] // 18
    [InlineData("WDB9634031-123456")] // a dash
    public async Task A_VIN_outside_the_ISO_3779_shape_is_refused(string vin)
    {
        using var _ = InTenantA();
        await SeedAsync();

        await ShouldFailAsync(() => CreateAsync("V1", vin: vin), MasterDataErrorCodes.InvalidVin);
    }

    [Fact]
    public async Task A_VIN_is_stored_upper_case_unique_per_tenant_and_found_by_fragment()
    {
        using var _ = InTenantA();
        await SeedAsync();
        var first = await CreateAsync("V1", vin: " wdb9634031l123456 ");
        first.Vin.ShouldBe("WDB9634031L123456");
        (await CreateAsync("V2")).Vin.ShouldBeNull();
        (await CreateAsync("V3")).Vin.ShouldBeNull();

        var ex = await ShouldFailAsync(() => CreateAsync("V4", vin: "WDB9634031L123456"), MasterDataErrorCodes.DuplicateKey, "vin");
        ex.Data["value"].ShouldBe(string.Empty);

        (await Service.GetListAsync(Request(search: "031l12"))).Rows.ShouldHaveSingleItem().Id.ShouldBe(first.Id);
    }

    // ── Type defaults and characteristics (High) ─────────────────────────────

    [Fact]
    public async Task On_create_an_empty_dimension_takes_the_chosen_types_default_and_a_given_one_is_kept()
    {
        using var _ = InTenantA();
        await SeedAsync();

        var tractor = await CreateAsync("T1", configure: d => { d.LengthCm = 610; d.EmptyWeightKg = 7_500; });
        tractor.LengthCm.ShouldBe(610, "a given value is the vehicle's own");
        tractor.WidthCm.ShouldBe(255);
        tractor.HeightCm.ShouldBe(400);
        tractor.GrossVehicleWeightKg.ShouldBe(18_000);
        tractor.EmptyWeightKg.ShouldBe(7_500);
        tractor.MaxPayloadKg.ShouldBeNull("the type has no payload default");
        tractor.MaxEpq.ShouldBeNull();
        tractor.MaxLoadingMeters.ShouldBeNull();

        /* The chosen type, not a default type. */
        var trailer = await CreateAsync("TR1", typeId: _trailerType);
        trailer.LengthCm.ShouldBe(1360);
        trailer.MaxEpq.ShouldBe(33);
        trailer.MaxLoadingMeters.ShouldBe(13.6);
        trailer.Kind.ShouldBe(VehicleTypeKind.Trailer);

        var defaults = await Service.GetTypeDefaultsAsync(_trailerType);
        defaults.Kind.ShouldBe(VehicleTypeKind.Trailer);
        defaults.LengthCm.ShouldBe(1360);
        defaults.WidthCm.ShouldBe(248);
        defaults.GrossVehicleWeightKg.ShouldBe(39_000);
        defaults.MaxEpq.ShouldBe(33);
        defaults.Characteristics.ShouldBeEmpty();
        (await Service.GetTypeDefaultsAsync(_tractorType)).Characteristics.Select(c => (c.Id, c.Origin)).ShouldBe([(_adr, VehicleCharacteristicOrigin.Type), (_tailLift, VehicleCharacteristicOrigin.Type)], ignoreOrder: true);
        await Should.ThrowAsync<EntityNotFoundException>(() => Service.GetTypeDefaultsAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Dimensions_are_never_re_inherited_after_create()
    {
        using var _ = InTenantA();
        await SeedAsync();
        var vehicle = await CreateAsync("T1");
        vehicle.WidthCm.ShouldBe(255);

        /* Clearing a dimension on update leaves it empty: the type is not asked again. */
        var updated = await Service.UpdateAsync(vehicle.Id, UpdateFrom(vehicle, u => u.WidthCm = null));
        updated.WidthCm.ShouldBeNull();

        /* Changing the type's defaults later does not touch the vehicle. */
        var type = await _types.GetAsync(_tractorType);
        await _types.UpdateAsync(_tractorType, new VehicleTypeUpdateDto
        {
            Name = type.Name, Kind = type.Kind, DefaultLengthCm = 700, DefaultWidthCm = 260, DefaultHeightCm = 410, DefaultGrossVehicleWeightKg = 20_000,
            TransportCharacteristicsIds = type.TransportCharacteristics.Select(c => c.Id).ToList(), ConcurrencyStamp = type.ConcurrencyStamp
        });
        var after = await Service.GetAsync(vehicle.Id);
        after.LengthCm.ShouldBe(600);
        after.WidthCm.ShouldBeNull();
        after.GrossVehicleWeightKg.ShouldBe(18_000);

        /* A changed type on update does not refill either. */
        var retyped = await Service.UpdateAsync(vehicle.Id, UpdateFrom(after, u => { u.VehicleTypeId = _trailerType; u.MaxEpq = null; }));
        retyped.Kind.ShouldBe(VehicleTypeKind.Trailer);
        retyped.MaxEpq.ShouldBeNull();
        retyped.LengthCm.ShouldBe(600);
    }

    [Theory]
    [InlineData(-1d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public async Task A_negative_or_non_finite_dimension_is_refused(double value)
    {
        using var _ = InTenantA();
        await SeedAsync();

        await ShouldFailAsync(() => CreateAsync("T1", configure: d => d.MaxPayloadKg = value), MasterDataErrorCodes.InvalidCode, "maxPayloadKg");
    }

    [Fact]
    public async Task The_characteristics_are_the_live_union_of_the_types_and_the_vehicles_own_marked_by_origin()
    {
        using var _ = InTenantA();
        await SeedAsync();

        /* The vehicle adds Cooling and repeats the type's ADR. */
        var vehicle = await CreateAsync("T1", configure: d => d.TransportCharacteristicIds = [_cooling, _adr]);
        vehicle.Characteristics.Select(c => (c.Code, c.Origin)).ShouldBe(
        [
            ("ADR", VehicleCharacteristicOrigin.Type), ("TL", VehicleCharacteristicOrigin.Type), ("COOL", VehicleCharacteristicOrigin.Vehicle)
        ], ignoreOrder: true);
        vehicle.Characteristics.Count(c => c.Id == _adr).ShouldBe(1, "a characteristic appears once, with the type's origin");
        vehicle.Characteristics.Take(2).ShouldAllBe(c => c.Origin == VehicleCharacteristicOrigin.Type, "the type's rows come first");

        /* The vehicle cannot remove a characteristic of its type: dropping every own row leaves the type's. */
        var cleared = await Service.UpdateAsync(vehicle.Id, UpdateFrom(vehicle, u => u.TransportCharacteristicIds = []));
        cleared.Characteristics.Select(c => c.Id).ShouldBe([_adr, _tailLift], ignoreOrder: true);
        cleared.Characteristics.ShouldAllBe(c => c.Origin == VehicleCharacteristicOrigin.Type);
        (await WithUnitOfWorkAsync(() => _characteristicRows.CountAsync(c => c.VehicleId == vehicle.Id))).ShouldBe(0);

        /* Live: changing the type's set changes the union without touching the vehicle. */
        await Service.UpdateAsync(vehicle.Id, UpdateFrom(cleared, u => u.TransportCharacteristicIds = [_cooling]));
        var type = await _types.GetAsync(_tractorType);
        await _types.UpdateAsync(_tractorType, new VehicleTypeUpdateDto
        {
            Name = type.Name, Kind = type.Kind, DefaultLengthCm = type.DefaultLengthCm, TransportCharacteristicsIds = [_tailLift], ConcurrencyStamp = type.ConcurrencyStamp
        });
        (await Service.GetAsync(vehicle.Id)).Characteristics.Select(c => (c.Id, c.Origin)).ShouldBe(
            [(_tailLift, VehicleCharacteristicOrigin.Type), (_cooling, VehicleCharacteristicOrigin.Vehicle)], ignoreOrder: true);
    }

    [Fact]
    public async Task An_unknown_characteristic_driver_country_or_organisation_unit_is_refused()
    {
        using var _ = InTenantA();
        await SeedAsync();

        await ShouldFailAsync(() => CreateAsync("V1", configure: d => d.TransportCharacteristicIds = [Guid.NewGuid()]), MasterDataErrorCodes.ReferenceNotFound, "transportCharacteristicIds");
        await ShouldFailAsync(() => CreateAsync("V1", configure: d => d.DefaultDrivers = [new VehicleDefaultDriverDto { DriverId = Guid.NewGuid(), SortOrder = 1 }]), MasterDataErrorCodes.ReferenceNotFound, "defaultDrivers");
        await ShouldFailAsync(() => CreateAsync("V1", plate: "12-ABC-3", countryId: Guid.NewGuid()), MasterDataErrorCodes.ReferenceNotFound, "registrationCountryId");
        await ShouldFailAsync(() => CreateAsync("V1", configure: d => d.OrganizationUnitIds = [Guid.NewGuid()]), MasterDataErrorCodes.ReferenceNotFound, "organizationUnitIds");
        await ShouldFailAsync(() => CreateAsync("V1", typeId: Guid.NewGuid()), MasterDataErrorCodes.ReferenceNotFound, "vehicleTypeId");
        (await Service.GetListAsync(Request(includeArchived: true))).TotalCount.ShouldBe(0);
    }

    // ── Owner and home depot (High) ──────────────────────────────────────────

    [Fact]
    public async Task The_owner_must_be_a_carrier_or_the_own_company()
    {
        using var _ = InTenantA();
        await SeedAsync();

        (await CreateAsync("V1", ownerId: _ownCompany)).OwnerName.ShouldBe("Own Transport B.V.");
        (await CreateAsync("V2", ownerId: _carrier)).OwnerName.ShouldBe("Charter Carrier");
        await ShouldFailAsync(() => CreateAsync("V3", ownerId: _customer), MasterDataErrorCodes.OwnerRoleRequired);
        await ShouldFailAsync(() => CreateAsync("V3", ownerId: _depot), MasterDataErrorCodes.OwnerRoleRequired);
        await ShouldFailAsync(() => CreateAsync("V3", ownerId: Guid.NewGuid()), MasterDataErrorCodes.ReferenceNotFound, "ownerRelationId");

        /* On update as well. */
        var vehicle = await CreateAsync("V4");
        await ShouldFailAsync(() => Service.UpdateAsync(vehicle.Id, UpdateFrom(vehicle, u => u.OwnerRelationId = _customer)), MasterDataErrorCodes.OwnerRoleRequired);
    }

    [Fact]
    public async Task The_home_depot_must_be_a_depot_or_the_own_company()
    {
        using var _ = InTenantA();
        await SeedAsync();

        (await CreateAsync("V1", configure: d => d.HomeDepotRelationId = _depot)).HomeDepotName.ShouldBe("Depot Utrecht");
        (await CreateAsync("V2", configure: d => d.HomeDepotRelationId = _ownCompany)).HomeDepotName.ShouldBe("Own Transport B.V.");
        await ShouldFailAsync(() => CreateAsync("V3", configure: d => d.HomeDepotRelationId = _carrier), MasterDataErrorCodes.HomeDepotRoleRequired);
        await ShouldFailAsync(() => CreateAsync("V3", configure: d => d.HomeDepotRelationId = _customer), MasterDataErrorCodes.HomeDepotRoleRequired);
        await ShouldFailAsync(() => CreateAsync("V3", configure: d => d.HomeDepotRelationId = Guid.NewGuid()), MasterDataErrorCodes.ReferenceNotFound, "homeDepotRelationId");
    }

    // ── Uniqueness and children (High; the index is proven on PostgreSQL) ──

    [Fact]
    public async Task A_duplicate_name_is_refused_and_freed_by_delete()
    {
        using var _ = InTenantA();
        await SeedAsync();
        var first = await CreateAsync("Truck 01");

        await ShouldFailAsync(() => CreateAsync(" Truck 01 "), MasterDataErrorCodes.DuplicateKey, "name");
        var second = await CreateAsync("Truck 02");
        await ShouldFailAsync(() => Service.UpdateAsync(second.Id, UpdateFrom(second, u => u.Name = "Truck 01")), MasterDataErrorCodes.DuplicateKey, "name");

        await Service.DeleteAsync(first.Id);
        (await CreateAsync("Truck 01")).Id.ShouldNotBe(first.Id);
    }

    [Fact]
    public async Task A_name_differing_only_in_case_is_a_duplicate_but_a_vehicle_may_change_the_case_of_its_own_name()
    {
        using var _ = InTenantA();
        await SeedAsync();
        var first = await CreateAsync("Truck 01");

        await ShouldFailAsync(() => CreateAsync("TRUCK 01"), MasterDataErrorCodes.DuplicateKey, "name");
        await ShouldFailAsync(() => CreateAsync(" truck 01 "), MasterDataErrorCodes.DuplicateKey, "name");
        var second = await CreateAsync("Truck 02");
        await ShouldFailAsync(() => Service.UpdateAsync(second.Id, UpdateFrom(second, u => u.Name = "truck 01")), MasterDataErrorCodes.DuplicateKey, "name");

        var renamed = await Service.UpdateAsync(first.Id, UpdateFrom(await Service.GetAsync(first.Id), u => u.Name = "TRUCK 01"));
        renamed.Name.ShouldBe("TRUCK 01", "the name keeps the case as typed; only the key ignores it");
    }

    [Fact]
    public async Task Default_drivers_are_ordered_at_most_five_each_once_and_replaced_on_update()
    {
        using var _ = InTenantA();
        await SeedAsync();
        var ids = new List<Guid>();
        for (var i = 1; i <= 6; i++)
        {
            ids.Add((await _drivers.CreateAsync(new DriverCreateDto { Code = $"D{i}", FirstName = "Driver", LastName = $"{i}" })).Id);
        }

        var vehicle = await CreateAsync("V1", configure: d => d.DefaultDrivers =
        [
            new VehicleDefaultDriverDto { DriverId = ids[2], SortOrder = 20 },
            new VehicleDefaultDriverDto { DriverId = ids[0], SortOrder = 10 },
        ]);
        vehicle.DefaultDrivers.Select(d => (d.DriverId, d.SortOrder)).ShouldBe([(ids[0], 1), (ids[2], 2)], "sorted by the given order and renumbered 1, 2, …");

        var updated = await Service.UpdateAsync(vehicle.Id, UpdateFrom(vehicle, u => u.DefaultDrivers = [new VehicleDefaultDriverDto { DriverId = ids[1], SortOrder = 1 }]));
        updated.DefaultDrivers.ShouldHaveSingleItem().DriverId.ShouldBe(ids[1]);
        (await WithUnitOfWorkAsync(() => _driverRows.CountAsync(d => d.VehicleId == vehicle.Id))).ShouldBe(1);

        await ShouldFailAsync(() => CreateAsync("V2", configure: d => d.DefaultDrivers = ids.Select((id, i) => new VehicleDefaultDriverDto { DriverId = id, SortOrder = i }).ToList()), MasterDataErrorCodes.TooManyChildren);
        await ShouldFailAsync(() => CreateAsync("V2", configure: d => d.DefaultDrivers =
        [
            new VehicleDefaultDriverDto { DriverId = ids[0], SortOrder = 1 }, new VehicleDefaultDriverDto { DriverId = ids[0], SortOrder = 2 }
        ]), MasterDataErrorCodes.DuplicateKey);
    }

    [Fact]
    public async Task An_external_id_names_one_vehicle_per_tenant_and_the_list_is_bounded()
    {
        using var _ = InTenantA();
        await SeedAsync();
        await CreateAsync("V1", configure: d => d.ExternalIdentifiers = [new VehicleExternalIdentifierDto { Source = "FMS", Identifier = "4711" }]);

        /* Another source or another identifier is another key. */
        await CreateAsync("V2", configure: d => d.ExternalIdentifiers =
        [
            new VehicleExternalIdentifierDto { Source = "EDI", Identifier = "4711" }, new VehicleExternalIdentifierDto { Source = "FMS", Identifier = "4712" }
        ]);
        await ShouldFailAsync(() => CreateAsync("V3", configure: d => d.ExternalIdentifiers = [new VehicleExternalIdentifierDto { Source = "FMS", Identifier = "4711" }]), MasterDataErrorCodes.DuplicateKey, "externalIdentifiers");
        await ShouldFailAsync(() => CreateAsync("V3", configure: d => d.ExternalIdentifiers =
        [
            new VehicleExternalIdentifierDto { Source = "X", Identifier = "1" }, new VehicleExternalIdentifierDto { Source = "X", Identifier = "1" }
        ]), MasterDataErrorCodes.DuplicateKey, "externalIdentifiers");
        /* A blank source is refused before the service by the DTO's [Required] (400). */
        await Should.ThrowAsync<AbpValidationException>(() => CreateAsync("V3", configure: d => d.ExternalIdentifiers = [new VehicleExternalIdentifierDto { Source = " ", Identifier = "1" }]));
        await ShouldFailAsync(() => CreateAsync("V3", configure: d => d.ExternalIdentifiers = Enumerable.Range(0, 51).Select(i => new VehicleExternalIdentifierDto { Source = "X", Identifier = $"{i}" }).ToList()), MasterDataErrorCodes.TooManyChildren);
        (await CreateAsync("V3", configure: d => d.ExternalIdentifiers = Enumerable.Range(0, 50).Select(i => new VehicleExternalIdentifierDto { Source = "X", Identifier = $"{i}" }).ToList())).ExternalIdentifiers.Count.ShouldBe(50);
    }

    // ── In use (High) ────────────────────────────────────────────────────────

    [Fact]
    public async Task A_vehicle_type_owner_home_depot_or_default_driver_in_use_by_a_vehicle_cannot_be_deleted()
    {
        using var _ = InTenantA();
        await SeedAsync();
        var driver = await _drivers.CreateAsync(new DriverCreateDto { Code = "D1", FirstName = "Jan", LastName = "Jansen" });
        var vehicle = await CreateAsync("V1", ownerId: _carrier, configure: d =>
        {
            d.HomeDepotRelationId = _depot;
            d.DefaultDrivers = [new VehicleDefaultDriverDto { DriverId = driver.Id, SortOrder = 1 }];
        });

        await ShouldFailAsync(() => _types.DeleteAsync(_tractorType), MasterDataErrorCodes.InUse);
        await ShouldFailAsync(() => _relations.DeleteAsync(_carrier), MasterDataErrorCodes.InUse);
        await ShouldFailAsync(() => _relations.DeleteAsync(_depot), MasterDataErrorCodes.InUse);
        await ShouldFailAsync(() => _drivers.DeleteAsync(driver.Id), MasterDataErrorCodes.InUse);

        /* The bulk deletes skip them with the reason. */
        var bulk = await _types.BulkDeleteAsync(new MasterDataBulkDeleteRequestDto { Selection = Explicit(_tractorType) });
        bulk.DeletedCount.ShouldBe(0);
        bulk.SkippedRows.ShouldHaveSingleItem().Reason.ShouldNotBeNullOrWhiteSpace();
        (await _drivers.BulkDeleteAsync(new DriverBulkDeleteRequestDto { Selection = Explicit(driver.Id) })).SkippedRows.ShouldHaveSingleItem();

        /* With the vehicle gone they are free; its join rows went with it. */
        await Service.DeleteAsync(vehicle.Id);
        (await WithUnitOfWorkAsync(() => _driverRows.CountAsync(d => d.VehicleId == vehicle.Id))).ShouldBe(0);
        await _drivers.DeleteAsync(driver.Id);
        await _relations.DeleteAsync(_depot);
        await _relations.DeleteAsync(_carrier);
        await _types.DeleteAsync(_tractorType);
    }

    [Fact]
    public async Task A_registration_country_in_use_by_a_vehicle_cannot_be_deleted()
    {
        using var _ = InTenantA();
        await SeedAsync();
        var lu = (await _countries.CreateAsync(new CountryCreateDto { IsoAlpha2 = "LU", IsoAlpha3 = "LUX", Names = Names("Luxembourg", "Luxemburg") })).Id;
        var vehicle = await CreateAsync("V1", configure: d => { d.LicencePlate = "AB1234"; d.RegistrationCountryId = lu; });

        await ShouldFailAsync(() => _countries.DeleteAsync(lu), MasterDataErrorCodes.InUse);

        await Service.DeleteAsync(vehicle.Id);
        await _countries.DeleteAsync(lu);
    }

    [Fact]
    public async Task Deleting_a_characteristic_removes_it_from_the_vehicles_own()
    {
        using var _ = InTenantA();
        await SeedAsync();
        var vehicle = await CreateAsync("V1", configure: d => d.TransportCharacteristicIds = [_cooling]);

        await _characteristics.DeleteAsync(_cooling);

        (await WithUnitOfWorkAsync(() => _characteristicRows.CountAsync(c => c.VehicleId == vehicle.Id))).ShouldBe(0);
        (await Service.GetAsync(vehicle.Id)).Characteristics.ShouldAllBe(c => c.Origin == VehicleCharacteristicOrigin.Type);
    }

    // ── List, lookup page, archive, bulk delete ──────────────────────────────

    [Fact]
    public async Task List_filters_on_kind_type_owner_and_archived_and_sorts_on_the_whitelist()
    {
        using var _ = InTenantA();
        await SeedAsync();
        var tractor = await CreateAsync("B tractor", plate: "BB-11-CC");
        var trailer = await CreateAsync("A trailer", typeId: _trailerType, ownerId: _carrier, plate: "OA-12-BC");
        var rigid = await CreateAsync("C rigid", typeId: _rigidType, plate: "AA-11-BB");
        var archived = await CreateAsync("D archived", configure: d => d.IsArchived = true);

        (await Service.GetListAsync(Request())).TotalCount.ShouldBe(3);
        (await Service.GetListAsync(Request(includeArchived: true))).TotalCount.ShouldBe(4);
        (await Service.GetListAsync(Request(kinds: [VehicleTypeKind.Trailer]))).Rows.ShouldHaveSingleItem().Id.ShouldBe(trailer.Id);
        (await Service.GetListAsync(Request(kinds: [VehicleTypeKind.Tractor, VehicleTypeKind.Rigid]))).Rows.Select(r => r.Id).ShouldBe([tractor.Id, rigid.Id], ignoreOrder: true);
        (await Service.GetListAsync(Request(typeId: _rigidType))).Rows.ShouldHaveSingleItem().Id.ShouldBe(rigid.Id);
        (await Service.GetListAsync(Request(ownerId: _carrier))).Rows.ShouldHaveSingleItem().Id.ShouldBe(trailer.Id);
        (await Service.GetListAsync(Request(includeArchived: true))).Rows.Single(r => r.Id == archived.Id).IsArchived.ShouldBeTrue();

        (await Service.GetListAsync(Request())).Rows.Select(r => r.Name).ShouldBe(["A trailer", "B tractor", "C rigid"]);
        (await Service.GetListAsync(Request(sortBy: "name", sortDir: "desc"))).Rows.Select(r => r.Name).ShouldBe(["C rigid", "B tractor", "A trailer"]);
        (await Service.GetListAsync(Request(sortBy: "licencePlate"))).Rows.Select(r => r.Name).ShouldBe(["C rigid", "B tractor", "A trailer"]);
        (await Service.GetListAsync(Request(sortBy: "vehicleTypeName"))).Rows.Select(r => r.Name).ShouldBe(["C rigid", "A trailer", "B tractor"]);
        (await Service.GetListAsync(Request(sortBy: "kind"))).Rows.Select(r => r.Kind).ShouldBe([VehicleTypeKind.Tractor, VehicleTypeKind.Trailer, VehicleTypeKind.Rigid]);
        (await Service.GetListAsync(Request(sortBy: "ownerName"))).Rows.First().Name.ShouldBe("A trailer");
        await ShouldFailAsync(() => Service.GetListAsync(Request(sortBy: "vin")), MasterDataErrorCodes.UnknownSortColumn);
    }

    [Fact]
    public async Task The_lookup_page_lists_non_archived_vehicles_by_name_in_pages_of_25_with_kind_and_owner_filters()
    {
        using var _ = InTenantA();
        await SeedAsync();
        for (var i = 0; i < 30; i++)
        {
            await CreateAsync($"Tractor {i:D2}");
        }

        var trailer = await CreateAsync("Trailer 01", typeId: _trailerType, ownerId: _carrier);
        await CreateAsync("Tractor archived", configure: d => d.IsArchived = true);

        var first = await Service.GetLookupPageAsync();
        first.TotalCount.ShouldBe(31);
        first.Items.Count.ShouldBe(MasterDataConsts.LookupPageSize);
        first.Items[0].Name.ShouldBe("Tractor 00");
        (await Service.GetLookupPageAsync(skip: 25)).Items.Count.ShouldBe(6);
        (await Service.GetLookupPageAsync(take: 1000)).Items.Count.ShouldBe(MasterDataConsts.LookupPageSize, "take is clamped");
        (await Service.GetLookupPageAsync("archived")).TotalCount.ShouldBe(0);

        (await Service.GetLookupPageAsync(kinds: [VehicleTypeKind.Trailer])).Items.ShouldHaveSingleItem().Id.ShouldBe(trailer.Id);
        (await Service.GetLookupPageAsync(kinds: [VehicleTypeKind.Tractor, VehicleTypeKind.Rigid, VehicleTypeKind.Van])).TotalCount.ShouldBe(30);
        (await Service.GetLookupPageAsync(ownerRelationId: _carrier)).Items.ShouldHaveSingleItem().Kind.ShouldBe(VehicleTypeKind.Trailer);
        (await Service.GetLookupAsync()).Count.ShouldBe(31);
    }

    [Fact]
    public async Task Archive_and_unarchive_twice_are_one_effect_and_an_archived_vehicle_leaves_the_pickers()
    {
        using var _ = InTenantA();
        await SeedAsync();
        var vehicle = await CreateAsync("V1");

        await Service.ArchiveAsync(vehicle.Id);
        await Service.ArchiveAsync(vehicle.Id);
        (await Service.GetAsync(vehicle.Id)).IsArchived.ShouldBeTrue();
        (await Service.GetLookupAsync()).ShouldBeEmpty();
        (await Service.GetLookupPageAsync()).TotalCount.ShouldBe(0);
        (await Service.GetListAsync(Request())).TotalCount.ShouldBe(0);

        await Service.UnarchiveAsync(vehicle.Id);
        await Service.UnarchiveAsync(vehicle.Id);
        (await Service.GetAsync(vehicle.Id)).IsArchived.ShouldBeFalse();
        (await Service.GetLookupAsync()).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Bulk_delete_removes_the_children_and_a_second_bulk_delete_removes_nothing()
    {
        using var _ = InTenantA();
        await SeedAsync();
        var v1 = await CreateAsync("V1", configure: d =>
        {
            d.TransportCharacteristicIds = [_cooling];
            d.ExternalIdentifiers = [new VehicleExternalIdentifierDto { Source = "FMS", Identifier = "1" }];
        });
        var v2 = await CreateAsync("V2");
        await CreateAsync("V3 archived", configure: d => d.IsArchived = true);

        var result = await Service.BulkDeleteAsync(new VehicleBulkDeleteRequestDto { Selection = Explicit(v1.Id, v2.Id) });
        result.DeletedCount.ShouldBe(2);
        (await Service.BulkDeleteAsync(new VehicleBulkDeleteRequestDto { Selection = Explicit(v1.Id, v2.Id) })).DeletedCount.ShouldBe(0);
        (await WithUnitOfWorkAsync(() => _characteristicRows.CountAsync(c => c.VehicleId == v1.Id))).ShouldBe(0);
        (await WithUnitOfWorkAsync(() => _externalRows.CountAsync(e => e.VehicleId == v1.Id))).ShouldBe(0);

        /* Filter-based: the archived one only with IncludeArchived. */
        (await Service.BulkDeleteAsync(new VehicleBulkDeleteRequestDto { Selection = FilterBased() })).DeletedCount.ShouldBe(0);
        (await Service.BulkDeleteAsync(new VehicleBulkDeleteRequestDto { Selection = FilterBased(), IncludeArchived = true })).DeletedCount.ShouldBe(1);
        (await Service.GetListAsync(Request(includeArchived: true))).TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task One_service_instance_serves_two_list_calls_in_separate_units_of_work()
    {
        /* FINDING: VehicleAppService keeps the IQueryable of the first call's DbContext in _typeQuery/_relationQuery
         * (PrepareQueriesAsync, "??="); a second call on the same instance in another unit of work composes it with
         * the new DbContext and EF Core throws "Cannot use multiple context instances within a single query
         * execution". An application service must be stateless between calls (any in-process caller that holds the
         * instance, a job without an outer unit of work, a test). */
        using var _ = InTenantA();
        await SeedAsync();
        await CreateAsync("T1");
        var service = GetRequiredService<IVehicleAppService>();

        (await service.GetListAsync(Request(kinds: [VehicleTypeKind.Tractor]))).TotalCount.ShouldBe(1);
        (await service.GetListAsync(Request(kinds: [VehicleTypeKind.Tractor]))).TotalCount.ShouldBe(1);
        (await service.GetLookupPageAsync(kinds: [VehicleTypeKind.Tractor])).TotalCount.ShouldBe(1);
        (await service.GetListAsync(Request(sortBy: "ownerName"))).TotalCount.ShouldBe(1);
    }

    // ── Tenant isolation (Critical; PostgreSQL proof in VehicleTenantIsolationTests) ──

    [Fact]
    public async Task Rows_of_tenant_A_are_invisible_and_untouchable_from_tenant_B_and_its_references_cannot_be_borrowed()
    {
        VehicleDto vehicle;
        Guid driverId;
        Guid typeA, nlA, ownerA, coolingA, unitA;
        using (InTenantA())
        {
            await SeedAsync();
            driverId = (await _drivers.CreateAsync(new DriverCreateDto { Code = "D1", FirstName = "Jan", LastName = "Jansen" })).Id;
            vehicle = await CreateAsync("Probe truck", plate: "12-ABC-3", vin: "WDB9634031L123456", configure: d =>
            {
                d.TransportCharacteristicIds = [_cooling];
                d.DefaultDrivers = [new VehicleDefaultDriverDto { DriverId = driverId, SortOrder = 1 }];
                d.ExternalIdentifiers = [new VehicleExternalIdentifierDto { Source = "FMS", Identifier = "4711" }];
            });
            (typeA, nlA, ownerA, coolingA, unitA) = (_tractorType, _nl, _ownCompany, _cooling, _unit);
        }

        using (InTenantB())
        {
            (await Service.GetListAsync(Request(includeArchived: true))).TotalCount.ShouldBe(0);
            (await Service.GetListAsync(Request(search: "probe"))).Rows.ShouldBeEmpty();
            (await Service.GetListAsync(Request(search: "12abc3"))).Rows.ShouldBeEmpty();
            (await Service.GetListAsync(Request(ownerId: ownerA))).Rows.ShouldBeEmpty();
            (await Service.GetLookupAsync()).ShouldBeEmpty();
            (await Service.GetLookupPageAsync("probe")).TotalCount.ShouldBe(0);
            (await Service.GetLookupPageAsync(ownerRelationId: ownerA)).Items.ShouldBeEmpty();
            await Should.ThrowAsync<EntityNotFoundException>(() => Service.GetAsync(vehicle.Id));
            await Should.ThrowAsync<EntityNotFoundException>(() => Service.GetTypeDefaultsAsync(typeA));
            await Should.ThrowAsync<EntityNotFoundException>(() => Service.UpdateAsync(vehicle.Id, UpdateFrom(vehicle)));
            await Should.ThrowAsync<EntityNotFoundException>(() => Service.ArchiveAsync(vehicle.Id));
            await Should.ThrowAsync<EntityNotFoundException>(() => Service.UnarchiveAsync(vehicle.Id));
            await Should.ThrowAsync<EntityNotFoundException>(() => Service.DeleteAsync(vehicle.Id));
            (await Service.BulkDeleteAsync(new VehicleBulkDeleteRequestDto { Selection = Explicit(vehicle.Id) })).DeletedCount.ShouldBe(0);
            (await Service.BulkDeleteAsync(new VehicleBulkDeleteRequestDto { Selection = FilterBased(), IncludeArchived = true })).DeletedCount.ShouldBe(0);

            /* Tenant B has its own reference rows; A's ids are refused one by one. */
            await SeedAsync();
            await ShouldFailAsync(() => CreateAsync("X", typeId: typeA), MasterDataErrorCodes.ReferenceNotFound, "vehicleTypeId");
            await ShouldFailAsync(() => CreateAsync("X", ownerId: ownerA), MasterDataErrorCodes.ReferenceNotFound, "ownerRelationId");
            await ShouldFailAsync(() => CreateAsync("X", configure: d => d.HomeDepotRelationId = ownerA), MasterDataErrorCodes.ReferenceNotFound, "homeDepotRelationId");
            await ShouldFailAsync(() => CreateAsync("X", plate: "1", countryId: nlA), MasterDataErrorCodes.ReferenceNotFound, "registrationCountryId");
            await ShouldFailAsync(() => CreateAsync("X", configure: d => d.TransportCharacteristicIds = [coolingA]), MasterDataErrorCodes.ReferenceNotFound, "transportCharacteristicIds");
            await ShouldFailAsync(() => CreateAsync("X", configure: d => d.DefaultDrivers = [new VehicleDefaultDriverDto { DriverId = driverId, SortOrder = 1 }]), MasterDataErrorCodes.ReferenceNotFound, "defaultDrivers");
            await ShouldFailAsync(() => CreateAsync("X", configure: d => d.OrganizationUnitIds = [unitA]), MasterDataErrorCodes.ReferenceNotFound, "organizationUnitIds");

            /* Tenant B has its own key space: name, plate, VIN and external id. */
            var own = await CreateAsync("Probe truck", plate: "12-ABC-3", vin: "WDB9634031L123456", configure: d => d.ExternalIdentifiers = [new VehicleExternalIdentifierDto { Source = "FMS", Identifier = "4711" }]);
            own.Id.ShouldNotBe(vehicle.Id);
            (await Service.GetListAsync(Request())).Rows.ShouldHaveSingleItem().Id.ShouldBe(own.Id);
        }

        using (InTenantA())
        {
            var mine = await Service.GetAsync(vehicle.Id);
            mine.Name.ShouldBe("Probe truck");
            mine.DefaultDrivers.ShouldHaveSingleItem();
            mine.ExternalIdentifiers.ShouldHaveSingleItem();
            (await Service.GetListAsync(Request())).Rows.ShouldHaveSingleItem().Id.ShouldBe(vehicle.Id);
        }
    }

    // ── Feature gate (Critical) ──────────────────────────────────────────────

    [Fact]
    public async Task With_the_Vehicles_node_off_every_screen_method_is_refused_before_touching_data_but_the_pickers_work()
    {
        using var _ = InTenantA();
        await SeedAsync();
        var vehicle = await CreateAsync("V1");

        foreach (var feature in new[] { AppFeatures.MasterData.General.Fleet.Vehicles, AppFeatures.MasterData.General.Fleet.Default })
        {
            using (TestFeatureValueProvider.Override(feature, "false"))
            {
                await Should.ThrowAsync<AbpAuthorizationException>(() => Service.GetListAsync(Request()));
                await Should.ThrowAsync<AbpAuthorizationException>(() => Service.GetAsync(vehicle.Id));
                await Should.ThrowAsync<AbpAuthorizationException>(() => Service.GetTypeDefaultsAsync(_tractorType));
                await Should.ThrowAsync<AbpAuthorizationException>(() => CreateAsync("V2"));
                await Should.ThrowAsync<AbpAuthorizationException>(() => Service.UpdateAsync(vehicle.Id, UpdateFrom(vehicle, u => u.Name = "Changed")));
                await Should.ThrowAsync<AbpAuthorizationException>(() => Service.DeleteAsync(vehicle.Id));
                await Should.ThrowAsync<AbpAuthorizationException>(() => Service.BulkDeleteAsync(new VehicleBulkDeleteRequestDto { Selection = Explicit(vehicle.Id) }));
                await Should.ThrowAsync<AbpAuthorizationException>(() => Service.ArchiveAsync(vehicle.Id));
                await Should.ThrowAsync<AbpAuthorizationException>(() => Service.UnarchiveAsync(vehicle.Id));

                /* The pickers need only the app (D12). */
                (await Service.GetLookupAsync()).ShouldHaveSingleItem();
                (await Service.GetLookupPageAsync()).TotalCount.ShouldBe(1);
            }
        }

        using (TestFeatureValueProvider.Override(AppFeatures.MasterData.Default, "false"))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => Service.GetLookupAsync());
            await Should.ThrowAsync<AbpAuthorizationException>(() => Service.GetLookupPageAsync());
        }

        var intact = await Service.GetAsync(vehicle.Id);
        intact.Name.ShouldBe("V1");
        intact.IsArchived.ShouldBeFalse();
        (await Service.GetListAsync(Request())).TotalCount.ShouldBe(1);
    }
}
