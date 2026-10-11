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
using Xunit;

namespace OpenTms.EntityFrameworkCore.MasterData;

/// <summary>
/// TransportUnitAppService on SQLite (design 41). Risk class: Critical for tenant isolation and the
/// unique name; High for the load-factor replace semantics and uniqueness, the vehicle-type
/// reference, the incompatibility sync, concurrency and bulk delete.
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class TransportUnitAppServiceTests : MasterDataAppServiceTestBase
{
    private readonly ITransportUnitAppService _service;
    private readonly IVehicleTypeAppService _vehicleTypes;

    public TransportUnitAppServiceTests()
    {
        _service = GetRequiredService<ITransportUnitAppService>();
        _vehicleTypes = GetRequiredService<IVehicleTypeAppService>();
    }

    private Task<VehicleTypeDto> VehicleTypeAsync(string name, VehicleTypeKind kind = VehicleTypeKind.Trailer)
        => _vehicleTypes.CreateAsync(new VehicleTypeCreateDto { Name = name, Kind = kind });

    private static TransportUnitLoadFactorDto Factor(Guid vehicleTypeId, double? factor = null, double? max = null, Guid? id = null)
        => new() { Id = id, VehicleTypeId = vehicleTypeId, LoadingCapacityFactor = factor, MaxCapacity = max };

    private Task<TransportUnitDto> CreateAsync(string name, bool archived = false, params TransportUnitLoadFactorDto[] factors)
        => _service.CreateAsync(new TransportUnitCreateDto { Name = name, IsArchived = archived, LoadFactors = [.. factors] });

    private static TransportUnitUpdateDto UpdateFrom(TransportUnitDto dto) => new()
    {
        Name = dto.Name,
        Description = dto.Description,
        IsArchived = dto.IsArchived,
        LengthCm = dto.LengthCm,
        WidthCm = dto.WidthCm,
        HeightCm = dto.HeightCm,
        TareWeightKg = dto.TareWeightKg,
        MaxLoadKg = dto.MaxLoadKg,
        IsStackable = dto.IsStackable,
        MaxStackCount = dto.MaxStackCount,
        LoadingMeters = dto.LoadingMeters,
        EpqFactor = dto.EpqFactor,
        IsExchangeable = dto.IsExchangeable,
        LoadFactors = dto.LoadFactors.Select(f => Factor(f.VehicleTypeId, f.LoadingCapacityFactor, f.MaxCapacity, f.Id)).ToList(),
        IncompatibleTransportUnitIds = dto.IncompatibleTransportUnits.Select(u => u.Id).ToList(),
        ConcurrencyStamp = dto.ConcurrencyStamp
    };

    [Fact]
    public async Task Create_stores_the_unit_with_its_load_factors_and_round_trips()
    {
        using var _ = InTenantA();
        var trailer = await VehicleTypeAsync("Trailer");
        var van = await VehicleTypeAsync("Van", VehicleTypeKind.Van);

        var created = await _service.CreateAsync(new TransportUnitCreateDto
        {
            Name = " Europallet ",
            Description = "EUR 1",
            LengthCm = 120,
            WidthCm = 80,
            HeightCm = 15,
            TareWeightKg = 25,
            MaxLoadKg = 1500,
            IsStackable = true,
            MaxStackCount = 2,
            LoadingMeters = 0.4,
            EpqFactor = 1,
            IsExchangeable = true,
            LoadFactors = [Factor(trailer.Id, 1, 33), Factor(van.Id, 1, 8)]
        });

        created.Name.ShouldBe("Europallet");
        created.IsExchangeable.ShouldBeTrue();
        created.LoadingMeters.ShouldBe(0.4);
        created.LoadFactors.Count.ShouldBe(2);
        created.LoadFactors.ShouldAllBe(f => f.Id != null);
        created.LoadFactors.Select(f => f.VehicleTypeName).ShouldBe(["Trailer", "Van"]);
        created.LoadFactors.Single(f => f.VehicleTypeId == trailer.Id).MaxCapacity.ShouldBe(33);

        (await _service.GetAsync(created.Id)).LoadFactors.Count.ShouldBe(2);
        var row = (await _service.GetListAsync(List<ArchivableListRequestDto>())).Rows.ShouldHaveSingleItem();
        row.EpqFactor.ShouldBe(1);
        row.IsExchangeable.ShouldBeTrue();
        var lookup = (await _service.GetLookupAsync()).ShouldHaveSingleItem();
        lookup.Name.ShouldBe("Europallet");
        lookup.Code.ShouldBeNull();
    }

    [Fact]
    public async Task Load_factors_are_replaced_on_update_keeping_the_ids_of_rows_that_stay()
    {
        using var _ = InTenantA();
        var trailer = await VehicleTypeAsync("Trailer");
        var van = await VehicleTypeAsync("Van", VehicleTypeKind.Van);
        var rigid = await VehicleTypeAsync("Rigid", VehicleTypeKind.Rigid);
        var unit = await CreateAsync("Europallet", factors: [Factor(trailer.Id, 1, 33), Factor(van.Id, 1, 8)]);
        var trailerFactor = unit.LoadFactors.Single(f => f.VehicleTypeId == trailer.Id);
        var vanFactor = unit.LoadFactors.Single(f => f.VehicleTypeId == van.Id);

        var update = UpdateFrom(unit);
        update.LoadFactors = [Factor(trailer.Id, 1.5, 30, trailerFactor.Id), Factor(rigid.Id, 1, 18)];
        var updated = await _service.UpdateAsync(unit.Id, update);

        updated.LoadFactors.Count.ShouldBe(2);
        var kept = updated.LoadFactors.Single(f => f.VehicleTypeId == trailer.Id);
        kept.Id.ShouldBe(trailerFactor.Id);
        kept.LoadingCapacityFactor.ShouldBe(1.5);
        kept.MaxCapacity.ShouldBe(30);
        updated.LoadFactors.ShouldNotContain(f => f.Id == vanFactor.Id);
        updated.LoadFactors.Single(f => f.VehicleTypeId == rigid.Id).VehicleTypeName.ShouldBe("Rigid");

        /* A row sent without id for an existing vehicle type keeps its id too. */
        var again = UpdateFrom(await _service.GetAsync(unit.Id));
        again.LoadFactors = [Factor(trailer.Id, 2, 20)];
        var keptById = await _service.UpdateAsync(unit.Id, again);
        keptById.LoadFactors.ShouldHaveSingleItem().Id.ShouldBe(trailerFactor.Id);
        keptById.LoadFactors.Single().LoadingCapacityFactor.ShouldBe(2);

        var none = UpdateFrom(keptById);
        none.LoadFactors = [];
        (await _service.UpdateAsync(unit.Id, none)).LoadFactors.ShouldBeEmpty();
    }

    [Fact]
    public async Task Two_load_factors_for_one_vehicle_type_are_refused()
    {
        using var _ = InTenantA();
        var trailer = await VehicleTypeAsync("Trailer");

        var ex = await Should.ThrowAsync<BusinessException>(() => CreateAsync("Europallet", factors: [Factor(trailer.Id, 1, 33), Factor(trailer.Id, 2, 20)]));

        ex.Code.ShouldBe(MasterDataErrorCodes.DuplicateLoadFactor);
        (await _service.GetListAsync(List<ArchivableListRequestDto>())).TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task A_load_factor_for_an_unknown_vehicle_type_is_refused()
    {
        using var _ = InTenantA();

        var ex = await Should.ThrowAsync<BusinessException>(() => CreateAsync("Europallet", factors: Factor(Guid.NewGuid(), 1, 33)));

        ex.Code.ShouldBe(MasterDataErrorCodes.ReferenceNotFound);
        ex.Data["field"].ShouldBe("loadFactors.vehicleTypeId");
    }

    [Fact]
    public async Task Name_is_unique_per_tenant_and_freed_by_a_soft_delete_and_update_checks_the_stamp()
    {
        using var _ = InTenantA();
        var pallet = await CreateAsync("Europallet");

        var ex = await Should.ThrowAsync<BusinessException>(() => CreateAsync("Europallet "));
        ex.Code.ShouldBe(MasterDataErrorCodes.DuplicateKey);

        var rename = UpdateFrom(pallet);
        rename.Name = "EUR pallet";
        rename.IsExchangeable = true;
        var renamed = await _service.UpdateAsync(pallet.Id, rename);
        renamed.Name.ShouldBe("EUR pallet");
        renamed.IsExchangeable.ShouldBeTrue();
        await Should.ThrowAsync<AbpDbConcurrencyException>(() => _service.UpdateAsync(pallet.Id, UpdateFrom(pallet)));

        await _service.DeleteAsync(pallet.Id);
        (await CreateAsync("EUR pallet")).Id.ShouldNotBe(pallet.Id);
    }

    [Fact]
    public async Task Archived_units_are_hidden_unless_asked_and_list_searches_name_and_description()
    {
        using var _ = InTenantA();
        await CreateAsync("Europallet");
        await _service.CreateAsync(new TransportUnitCreateDto { Name = "Rolcontainer", Description = "retail cage", TareWeightKg = 30 });
        await CreateAsync("Old crate", archived: true);

        var visible = await _service.GetListAsync(List<ArchivableListRequestDto>());
        visible.TotalCount.ShouldBe(2);
        visible.Rows.Select(r => r.Name).ShouldBe(["Europallet", "Rolcontainer"]);
        (await _service.GetListAsync(new ArchivableListRequestDto { IncludeArchived = true })).TotalCount.ShouldBe(3);
        (await _service.GetLookupAsync()).Count.ShouldBe(2);

        (await _service.GetListAsync(List<ArchivableListRequestDto>(search: "pal"))).Rows.ShouldHaveSingleItem().Name.ShouldBe("Europallet");
        (await _service.GetListAsync(List<ArchivableListRequestDto>(search: "cage"))).Rows.ShouldHaveSingleItem().Name.ShouldBe("Rolcontainer");
        (await _service.GetListAsync(List<ArchivableListRequestDto>(sortBy: "tareWeightKg", sortDir: "desc"))).Rows.First().Name.ShouldBe("Rolcontainer");
        foreach (var column in new[] { "description", "isArchived", "lengthCm", "widthCm", "heightCm", "maxLoadKg", "isStackable", "maxStackCount", "loadingMeters", "epqFactor", "isExchangeable" })
        {
            (await _service.GetListAsync(List<ArchivableListRequestDto>(sortBy: column))).Rows.Count.ShouldBe(2, column);
        }

        (await Should.ThrowAsync<BusinessException>(() => _service.GetListAsync(List<ArchivableListRequestDto>(sortBy: "loadFactors")))).Code.ShouldBe(MasterDataErrorCodes.UnknownSortColumn);
    }

    [Fact]
    public async Task Incompatibilities_are_symmetric_and_removed_on_delete()
    {
        using var _ = InTenantA();
        var pallet = await CreateAsync("Europallet");
        var crate = await _service.CreateAsync(new TransportUnitCreateDto { Name = "Crate", IncompatibleTransportUnitIds = [pallet.Id, pallet.Id] });
        (await _service.GetAsync(crate.Id)).IncompatibleTransportUnits.ShouldHaveSingleItem().Name.ShouldBe("Europallet");
        (await _service.GetAsync(pallet.Id)).IncompatibleTransportUnits.ShouldHaveSingleItem().Id.ShouldBe(crate.Id);

        var self = UpdateFrom(await _service.GetAsync(pallet.Id));
        self.IncompatibleTransportUnitIds = [pallet.Id];
        await _service.UpdateAsync(pallet.Id, self);
        (await _service.GetAsync(pallet.Id)).IncompatibleTransportUnits.ShouldBeEmpty();
        (await _service.GetAsync(crate.Id)).IncompatibleTransportUnits.ShouldBeEmpty();

        var back = UpdateFrom(await _service.GetAsync(crate.Id));
        back.IncompatibleTransportUnitIds = [pallet.Id];
        await _service.UpdateAsync(crate.Id, back);
        (await _service.GetAsync(pallet.Id)).IncompatibleTransportUnits.ShouldHaveSingleItem().Id.ShouldBe(crate.Id);
        await _service.DeleteAsync(crate.Id);
        (await _service.GetAsync(pallet.Id)).IncompatibleTransportUnits.ShouldBeEmpty();
    }

    /// <summary>
    /// API contract (design 41): CreateAsync and UpdateAsync return the detail DTO, which carries
    /// incompatibleTransportUnits. The response must therefore already list the pairs the same call wrote.
    /// </summary>
    [Fact]
    public async Task The_create_and_update_responses_carry_the_incompatibilities_written_in_the_same_call()
    {
        using var _ = InTenantA();
        var pallet = await CreateAsync("Europallet");

        var crate = await _service.CreateAsync(new TransportUnitCreateDto { Name = "Crate", IncompatibleTransportUnitIds = [pallet.Id] });
        crate.IncompatibleTransportUnits.ShouldHaveSingleItem().Id.ShouldBe(pallet.Id);

        var box = await CreateAsync("Box");
        var update = UpdateFrom(box);
        update.IncompatibleTransportUnitIds = [pallet.Id];
        (await _service.UpdateAsync(box.Id, update)).IncompatibleTransportUnits.ShouldHaveSingleItem().Id.ShouldBe(pallet.Id);
    }

    /// <summary>
    /// Design 41, "Delete many": each row goes through the same checks as DeleteAsync, and a unit's
    /// load factors are cleared before the soft delete so they stop blocking their vehicle type.
    /// </summary>
    [Fact]
    public async Task Filter_based_bulk_delete_clears_the_load_factors_like_a_single_delete_does()
    {
        using var _ = InTenantA();
        var trailer = await VehicleTypeAsync("Trailer");
        await CreateAsync("Pallet A", factors: Factor(trailer.Id, 1, 33));
        await CreateAsync("Pallet B", factors: Factor(trailer.Id, 1, 20));

        (await _service.BulkDeleteAsync(new ArchivableBulkDeleteRequestDto { Selection = FilterBased("pallet") })).DeletedCount.ShouldBe(2);
        (await _service.GetListAsync(new ArchivableListRequestDto { IncludeArchived = true })).TotalCount.ShouldBe(0);

        await _vehicleTypes.DeleteAsync(trailer.Id);
        await Should.ThrowAsync<EntityNotFoundException>(() => _vehicleTypes.GetAsync(trailer.Id));
    }

    [Fact]
    public async Task Deleting_a_unit_frees_its_vehicle_types_for_deletion()
    {
        using var _ = InTenantA();
        using var culture = UiCulture("en");
        var trailer = await VehicleTypeAsync("Trailer");
        var unit = await CreateAsync("Europallet", factors: Factor(trailer.Id, 1, 33));

        var ex = await Should.ThrowAsync<BusinessException>(() => _vehicleTypes.DeleteAsync(trailer.Id));
        ex.Code.ShouldBe(MasterDataErrorCodes.InUse);
        ex.Data["reason"].ShouldBe("used by a load factor of a transport unit");

        await _service.DeleteAsync(unit.Id);
        await _vehicleTypes.DeleteAsync(trailer.Id);
        await Should.ThrowAsync<EntityNotFoundException>(() => _vehicleTypes.GetAsync(trailer.Id));
    }

    [Fact]
    public async Task BulkDelete_explicit_filter_based_and_twice()
    {
        using var _ = InTenantA();
        var trailer = await VehicleTypeAsync("Trailer");
        var a = await CreateAsync("Pallet A", factors: Factor(trailer.Id, 1, 33));
        var b = await CreateAsync("Pallet B");
        var c = await CreateAsync("Crate");

        var request = new ArchivableBulkDeleteRequestDto { Selection = Explicit(a.Id, b.Id) };
        (await _service.BulkDeleteAsync(request)).DeletedCount.ShouldBe(2);
        (await _service.BulkDeleteAsync(request)).DeletedCount.ShouldBe(0);
        /* The bulk path clears the load factors too, so the vehicle type is free. */
        await _vehicleTypes.DeleteAsync(trailer.Id);

        (await _service.BulkDeleteAsync(new ArchivableBulkDeleteRequestDto { Selection = FilterBased("crate") })).DeletedCount.ShouldBe(1);
        (await _service.GetListAsync(new ArchivableListRequestDto { IncludeArchived = true })).TotalCount.ShouldBe(0);
        c.Id.ShouldNotBe(Guid.Empty);
    }

    // ── Tenant isolation (Critical) ───────────────────────────────────────────

    [Fact]
    public async Task Rows_of_tenant_A_are_invisible_and_untouchable_from_tenant_B()
    {
        VehicleTypeDto trailer;
        TransportUnitDto pallet;
        using (InTenantA())
        {
            trailer = await VehicleTypeAsync("Trailer");
            pallet = await CreateAsync("Europallet", factors: Factor(trailer.Id, 1, 33));
        }

        using (InTenantB())
        {
            (await _service.GetListAsync(new ArchivableListRequestDto { IncludeArchived = true })).TotalCount.ShouldBe(0);
            (await _service.GetLookupAsync()).ShouldBeEmpty();
            await Should.ThrowAsync<EntityNotFoundException>(() => _service.GetAsync(pallet.Id));
            await Should.ThrowAsync<EntityNotFoundException>(() => _service.UpdateAsync(pallet.Id, UpdateFrom(pallet)));
            await Should.ThrowAsync<EntityNotFoundException>(() => _service.DeleteAsync(pallet.Id));
            (await _service.BulkDeleteAsync(new ArchivableBulkDeleteRequestDto { Selection = FilterBased() })).DeletedCount.ShouldBe(0);

            /* Neither a load factor nor an incompatibility may reach into tenant A. */
            var factor = await Should.ThrowAsync<BusinessException>(() => CreateAsync("Crate", factors: Factor(trailer.Id, 1, 33)));
            factor.Code.ShouldBe(MasterDataErrorCodes.ReferenceNotFound);
            var pair = await Should.ThrowAsync<BusinessException>(() => _service.CreateAsync(new TransportUnitCreateDto { Name = "Crate", IncompatibleTransportUnitIds = [pallet.Id] }));
            pair.Code.ShouldBe(MasterDataErrorCodes.ReferenceNotFound);

            (await CreateAsync("Europallet")).Id.ShouldNotBe(pallet.Id);
        }

        using (InTenantA())
        {
            (await _service.GetListAsync(List<ArchivableListRequestDto>())).Rows.ShouldHaveSingleItem().Id.ShouldBe(pallet.Id);
            (await _service.GetAsync(pallet.Id)).LoadFactors.ShouldHaveSingleItem().VehicleTypeId.ShouldBe(trailer.Id);
        }
    }
}
