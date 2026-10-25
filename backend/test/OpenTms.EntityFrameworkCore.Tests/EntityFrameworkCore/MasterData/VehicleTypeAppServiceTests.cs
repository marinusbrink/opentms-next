using System;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.MasterData;
using OpenTms.MasterData.Fleet;
using OpenTms.MasterData.Transport;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Validation;
using Xunit;

namespace OpenTms.EntityFrameworkCore.MasterData;

/// <summary>
/// VehicleTypeAppService on SQLite (design 41). Risk class: Critical for tenant isolation and the
/// unique name; High for the characteristics replace semantics, the in-use rule and bulk delete.
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class VehicleTypeAppServiceTests : MasterDataAppServiceTestBase
{
    private readonly IVehicleTypeAppService _service;
    private readonly ITransportCharacteristicsAppService _characteristics;
    private readonly ITransportUnitAppService _units;

    public VehicleTypeAppServiceTests()
    {
        _service = GetRequiredService<IVehicleTypeAppService>();
        _characteristics = GetRequiredService<ITransportCharacteristicsAppService>();
        _units = GetRequiredService<ITransportUnitAppService>();
    }

    private Task<TransportCharacteristicsDto> CharacteristicAsync(string code)
        => _characteristics.CreateAsync(new TransportCharacteristicsCreateDto { Code = code, Name = code.ToLowerInvariant() });

    private Task<VehicleTypeDto> CreateAsync(string name, VehicleTypeKind kind = VehicleTypeKind.Trailer, double? maxEpq = null, params Guid[] characteristics)
        => _service.CreateAsync(new VehicleTypeCreateDto { Name = name, Kind = kind, MaxEpq = maxEpq, TransportCharacteristicsIds = [.. characteristics] });

    private static VehicleTypeUpdateDto UpdateFrom(VehicleTypeDto dto) => new()
    {
        Name = dto.Name,
        Kind = dto.Kind,
        Description = dto.Description,
        MaxEpq = dto.MaxEpq,
        MaxLoadingMeters = dto.MaxLoadingMeters,
        DefaultLengthCm = dto.DefaultLengthCm,
        DefaultWidthCm = dto.DefaultWidthCm,
        DefaultHeightCm = dto.DefaultHeightCm,
        DefaultGrossVehicleWeightKg = dto.DefaultGrossVehicleWeightKg,
        TransportCharacteristicsIds = dto.TransportCharacteristics.Select(c => c.Id).ToList(),
        ConcurrencyStamp = dto.ConcurrencyStamp
    };

    [Fact]
    public async Task Create_stores_the_type_with_its_characteristics_and_round_trips()
    {
        using var _ = InTenantA();
        var cool = await CharacteristicAsync("COOL");
        var tail = await CharacteristicAsync("TAIL");

        var created = await _service.CreateAsync(new VehicleTypeCreateDto
        {
            Name = " Reefer trailer ",
            Kind = VehicleTypeKind.Trailer,
            Description = "cooled",
            MaxEpq = 33,
            MaxLoadingMeters = 13.6,
            DefaultLengthCm = 1360,
            DefaultWidthCm = 248,
            DefaultHeightCm = 270,
            DefaultGrossVehicleWeightKg = 40000,
            CostPricePerKm = 0.31234m,
            TransportCharacteristicsIds = [tail.Id, cool.Id, cool.Id]
        });

        created.Name.ShouldBe("Reefer trailer");
        created.Kind.ShouldBe(VehicleTypeKind.Trailer);
        created.MaxEpq.ShouldBe(33);
        created.DefaultGrossVehicleWeightKg.ShouldBe(40000);
        created.CostPricePerKm.ShouldBe(0.3123m, "QP10: four decimals");
        (await _service.GetAsync(created.Id)).CostPricePerKm.ShouldBe(0.3123m);
        created.TransportCharacteristics.Select(c => c.Code).ShouldBe(["COOL", "TAIL"]);

        (await _service.GetAsync(created.Id)).TransportCharacteristics.Count.ShouldBe(2);
        var row = (await _service.GetListAsync(List())).Rows.ShouldHaveSingleItem();
        row.Kind.ShouldBe(VehicleTypeKind.Trailer);
        row.MaxLoadingMeters.ShouldBe(13.6);
        var lookup = (await _service.GetLookupAsync()).ShouldHaveSingleItem();
        lookup.Name.ShouldBe("Reefer trailer");
        lookup.Code.ShouldBe("Trailer");
    }

    [Fact]
    public async Task Characteristics_are_replaced_on_update()
    {
        using var _ = InTenantA();
        var cool = await CharacteristicAsync("COOL");
        var tail = await CharacteristicAsync("TAIL");
        var adr = await CharacteristicAsync("ADR");
        var reefer = await CreateAsync("Reefer", characteristics: [cool.Id, tail.Id]);

        var update = UpdateFrom(reefer);
        update.TransportCharacteristicsIds = [cool.Id, adr.Id];
        var updated = await _service.UpdateAsync(reefer.Id, update);
        updated.TransportCharacteristics.Select(c => c.Code).ShouldBe(["ADR", "COOL"]);

        var none = UpdateFrom(updated);
        none.TransportCharacteristicsIds = [];
        (await _service.UpdateAsync(reefer.Id, none)).TransportCharacteristics.ShouldBeEmpty();
        (await _service.GetAsync(reefer.Id)).TransportCharacteristics.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_unknown_characteristic_and_an_undefined_kind_are_refused()
    {
        using var _ = InTenantA();
        var cool = await CharacteristicAsync("COOL");

        var ex = await Should.ThrowAsync<BusinessException>(() => CreateAsync("Reefer", characteristics: [cool.Id, Guid.NewGuid()]));
        ex.Code.ShouldBe(MasterDataErrorCodes.ReferenceNotFound);
        ex.Data["field"].ShouldBe("transportCharacteristicsIds");
        (await _service.GetListAsync(List())).TotalCount.ShouldBe(0);

        await Should.ThrowAsync<AbpValidationException>(() => CreateAsync("Reefer", (VehicleTypeKind)0));
        await Should.ThrowAsync<AbpValidationException>(() => _service.CreateAsync(new VehicleTypeCreateDto { Name = "Reefer", Kind = VehicleTypeKind.Van, MaxEpq = -1 }));
    }

    [Fact]
    public async Task Name_is_unique_per_tenant_freed_by_a_soft_delete_and_update_checks_the_stamp()
    {
        using var _ = InTenantA();
        var reefer = await CreateAsync("Reefer");
        await CreateAsync("Tautliner");

        (await Should.ThrowAsync<BusinessException>(() => CreateAsync(" Reefer"))).Code.ShouldBe(MasterDataErrorCodes.DuplicateKey);

        var rename = UpdateFrom(reefer);
        rename.Name = "Reefer 13.6";
        rename.Kind = VehicleTypeKind.Rigid;
        var renamed = await _service.UpdateAsync(reefer.Id, rename);
        renamed.Name.ShouldBe("Reefer 13.6");
        renamed.Kind.ShouldBe(VehicleTypeKind.Rigid);

        var clash = UpdateFrom(renamed);
        clash.Name = "Tautliner";
        (await Should.ThrowAsync<BusinessException>(() => _service.UpdateAsync(reefer.Id, clash))).Code.ShouldBe(MasterDataErrorCodes.DuplicateKey);
        await Should.ThrowAsync<AbpDbConcurrencyException>(() => _service.UpdateAsync(reefer.Id, UpdateFrom(reefer)));

        await _service.DeleteAsync(reefer.Id);
        (await CreateAsync("Reefer 13.6")).Id.ShouldNotBe(reefer.Id);
    }

    [Fact]
    public async Task List_searches_name_and_description_and_sorts_on_the_whitelist()
    {
        using var _ = InTenantA();
        await CreateAsync("Tautliner", VehicleTypeKind.Trailer, 33);
        await CreateAsync("Van", VehicleTypeKind.Van, 8);
        await _service.CreateAsync(new VehicleTypeCreateDto { Name = "Tractor", Kind = VehicleTypeKind.Tractor, Description = "pulls trailers" });

        (await _service.GetListAsync(List())).Rows.Select(r => r.Name).ShouldBe(["Tautliner", "Tractor", "Van"]);
        (await _service.GetListAsync(List(sortBy: "kind"))).Rows.Select(r => r.Kind).ShouldBe([VehicleTypeKind.Tractor, VehicleTypeKind.Trailer, VehicleTypeKind.Van]);
        (await _service.GetListAsync(List(sortBy: "maxEpq", sortDir: "desc"))).Rows.First().Name.ShouldBe("Tautliner");
        (await _service.GetListAsync(List(search: "trailer"))).Rows.ShouldHaveSingleItem().Name.ShouldBe("Tractor");
        (await _service.GetListAsync(List(search: "ta"))).FilteredCount.ShouldBe(1);
        (await _service.GetLookupAsync("van")).ShouldHaveSingleItem().Code.ShouldBe("Van");
        foreach (var column in new[] { "description", "maxLoadingMeters", "defaultLengthCm", "defaultWidthCm", "defaultHeightCm", "defaultGrossVehicleWeightKg" })
        {
            (await _service.GetListAsync(List(sortBy: column))).Rows.Count.ShouldBe(3, column);
        }

        (await Should.ThrowAsync<BusinessException>(() => _service.GetListAsync(List(sortBy: "transportCharacteristics")))).Code.ShouldBe(MasterDataErrorCodes.UnknownSortColumn);
    }

    [Fact]
    public async Task A_vehicle_type_used_by_a_load_factor_cannot_be_deleted_singly_or_in_bulk()
    {
        using var _ = InTenantA();
        using var culture = UiCulture("en");
        var trailer = await CreateAsync("Trailer");
        var van = await CreateAsync("Van", VehicleTypeKind.Van);
        var unit = await _units.CreateAsync(new TransportUnitCreateDto { Name = "Europallet", LoadFactors = [new TransportUnitLoadFactorDto { VehicleTypeId = trailer.Id, MaxCapacity = 33 }] });

        var ex = await Should.ThrowAsync<BusinessException>(() => _service.DeleteAsync(trailer.Id));
        ex.Code.ShouldBe(MasterDataErrorCodes.InUse);
        ex.Data["reason"].ShouldBe("used by a load factor of a transport unit");

        var bulk = await _service.BulkDeleteAsync(new MasterDataBulkDeleteRequestDto { Selection = Explicit(trailer.Id, van.Id) });
        bulk.DeletedCount.ShouldBe(1);
        bulk.SkippedRows.ShouldHaveSingleItem().Id.ShouldBe(trailer.Id);
        bulk.SkippedRows.Single().Name.ShouldBe("Trailer");
        bulk.SkippedRows.Single().Reason.ShouldBe("used by a load factor of a transport unit");

        var release = new TransportUnitUpdateDto { Name = "Europallet", LoadFactors = [], ConcurrencyStamp = unit.ConcurrencyStamp };
        await _units.UpdateAsync(unit.Id, release);
        await _service.DeleteAsync(trailer.Id);
        (await _service.GetListAsync(List())).TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task Deleting_a_vehicle_type_drops_its_characteristic_join_rows_and_bulk_delete_twice_deletes_nothing()
    {
        using var _ = InTenantA();
        var cool = await CharacteristicAsync("COOL");
        var reefer = await CreateAsync("Reefer", characteristics: cool.Id);
        var van = await CreateAsync("Van", VehicleTypeKind.Van, characteristics: cool.Id);

        await _service.DeleteAsync(reefer.Id);
        /* The characteristic is still deletable afterwards (no dangling join row blocks it) and the other type keeps it. */
        (await _service.GetAsync(van.Id)).TransportCharacteristics.ShouldHaveSingleItem().Id.ShouldBe(cool.Id);

        var request = new MasterDataBulkDeleteRequestDto { Selection = FilterBased("van") };
        (await _service.BulkDeleteAsync(request)).DeletedCount.ShouldBe(1);
        (await _service.BulkDeleteAsync(request)).DeletedCount.ShouldBe(0);
        await _characteristics.DeleteAsync(cool.Id);
    }

    // ── Tenant isolation (Critical) ───────────────────────────────────────────

    [Fact]
    public async Task Rows_of_tenant_A_are_invisible_and_untouchable_from_tenant_B()
    {
        VehicleTypeDto reefer;
        using (InTenantA())
        {
            var cool = await CharacteristicAsync("COOL");
            reefer = await CreateAsync("Reefer", characteristics: cool.Id);
        }

        using (InTenantB())
        {
            (await _service.GetListAsync(List())).TotalCount.ShouldBe(0);
            (await _service.GetLookupAsync()).ShouldBeEmpty();
            await Should.ThrowAsync<EntityNotFoundException>(() => _service.GetAsync(reefer.Id));
            await Should.ThrowAsync<EntityNotFoundException>(() => _service.UpdateAsync(reefer.Id, UpdateFrom(reefer)));
            await Should.ThrowAsync<EntityNotFoundException>(() => _service.DeleteAsync(reefer.Id));
            (await _service.BulkDeleteAsync(new MasterDataBulkDeleteRequestDto { Selection = Explicit(reefer.Id) })).DeletedCount.ShouldBe(0);
            (await _service.BulkDeleteAsync(new MasterDataBulkDeleteRequestDto { Selection = FilterBased() })).DeletedCount.ShouldBe(0);

            (await Should.ThrowAsync<BusinessException>(() => _units.CreateAsync(new TransportUnitCreateDto { Name = "Crate", LoadFactors = [new TransportUnitLoadFactorDto { VehicleTypeId = reefer.Id }] })))
                .Code.ShouldBe(MasterDataErrorCodes.ReferenceNotFound);

            (await CreateAsync("Reefer")).Id.ShouldNotBe(reefer.Id);
        }

        using (InTenantA())
        {
            (await _service.GetListAsync(List())).Rows.ShouldHaveSingleItem().Id.ShouldBe(reefer.Id);
            (await _service.GetAsync(reefer.Id)).TransportCharacteristics.Count.ShouldBe(1);
        }
    }
}
