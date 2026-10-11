using System;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.MasterData;
using OpenTms.MasterData.Fleet;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Validation;
using Xunit;

namespace OpenTms.EntityFrameworkCore.MasterData;

/// <summary>
/// TransportCharacteristicsAppService on SQLite (design 41). Risk class: Critical for tenant
/// isolation and the unique code; High for the CRUD rules and the join rows left behind on delete.
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class TransportCharacteristicsAppServiceTests : MasterDataAppServiceTestBase
{
    private readonly ITransportCharacteristicsAppService _service;
    private readonly IVehicleTypeAppService _vehicleTypes;

    public TransportCharacteristicsAppServiceTests()
    {
        _service = GetRequiredService<ITransportCharacteristicsAppService>();
        _vehicleTypes = GetRequiredService<IVehicleTypeAppService>();
    }

    private Task<TransportCharacteristicsDto> CreateAsync(string code, string name, string? description = null)
        => _service.CreateAsync(new TransportCharacteristicsCreateDto { Code = code, Name = name, Description = description });

    private static TransportCharacteristicsUpdateDto UpdateFrom(TransportCharacteristicsDto dto) => new()
    {
        Code = dto.Code,
        Name = dto.Name,
        Description = dto.Description,
        ConcurrencyStamp = dto.ConcurrencyStamp
    };

    [Fact]
    public async Task Create_normalises_the_code_and_round_trips_through_get_list_and_lookup()
    {
        using var _ = InTenantA();

        var created = await CreateAsync(" cool ", " Cooled ", "reefer");

        created.Code.ShouldBe("COOL");
        created.Name.ShouldBe("Cooled");
        (await _service.GetAsync(created.Id)).Code.ShouldBe("COOL");
        var row = (await _service.GetListAsync(List())).Rows.ShouldHaveSingleItem();
        row.Code.ShouldBe("COOL");
        row.Description.ShouldBe("reefer");
        var lookup = (await _service.GetLookupAsync()).ShouldHaveSingleItem();
        lookup.Code.ShouldBe("COOL");
        lookup.Name.ShouldBe("Cooled");
    }

    [Fact]
    public async Task Code_is_unique_per_tenant_case_insensitively_and_freed_by_a_soft_delete()
    {
        using var _ = InTenantA();
        var cool = await CreateAsync("COOL", "Cooled");

        var ex = await Should.ThrowAsync<BusinessException>(() => CreateAsync("cool", "Another"));
        ex.Code.ShouldBe(MasterDataErrorCodes.DuplicateKey);
        ex.Data["field"].ShouldBe("code");
        ex.Data["value"].ShouldBe("COOL");

        /* Names are free: two codes may share a name. */
        await CreateAsync("COLD", "Cooled");

        await _service.DeleteAsync(cool.Id);
        (await CreateAsync("COOL", "Cooled again")).Id.ShouldNotBe(cool.Id);
    }

    [Fact]
    public async Task Create_rejects_a_blank_code_or_name_by_validation()
    {
        using var _ = InTenantA();

        await Should.ThrowAsync<AbpValidationException>(() => CreateAsync("", "Cooled"));
        await Should.ThrowAsync<AbpValidationException>(() => CreateAsync("COOL", ""));
        await Should.ThrowAsync<AbpValidationException>(() => CreateAsync("   ", "Cooled"));
    }

    [Fact]
    public async Task Update_changes_code_and_name_within_the_unique_rule_and_checks_the_stamp()
    {
        using var _ = InTenantA();
        var cool = await CreateAsync("COOL", "Cooled");
        await CreateAsync("ADR", "Dangerous goods");

        var update = UpdateFrom(cool);
        update.Code = "reefer";
        update.Name = "Reefer";
        var updated = await _service.UpdateAsync(cool.Id, update);
        updated.Code.ShouldBe("REEFER");
        updated.Name.ShouldBe("Reefer");

        var clash = UpdateFrom(updated);
        clash.Code = "adr";
        (await Should.ThrowAsync<BusinessException>(() => _service.UpdateAsync(cool.Id, clash))).Code.ShouldBe(MasterDataErrorCodes.DuplicateKey);

        await Should.ThrowAsync<AbpDbConcurrencyException>(() => _service.UpdateAsync(cool.Id, UpdateFrom(cool)));
    }

    [Fact]
    public async Task List_searches_code_name_and_description_and_sorts_on_the_whitelist()
    {
        using var _ = InTenantA();
        await CreateAsync("COOL", "Cooled", "reefer");
        await CreateAsync("ADR", "Dangerous goods");
        await CreateAsync("TAIL", "Tail lift", "for pallets");

        (await _service.GetListAsync(List())).Rows.Select(r => r.Code).ShouldBe(["COOL", "ADR", "TAIL"]);
        (await _service.GetListAsync(List(sortBy: "code"))).Rows.Select(r => r.Code).ShouldBe(["ADR", "COOL", "TAIL"]);
        (await _service.GetListAsync(List(sortBy: "description", sortDir: "desc"))).Rows.First().Code.ShouldBe("COOL");
        (await _service.GetListAsync(List(search: "adr"))).Rows.ShouldHaveSingleItem().Code.ShouldBe("ADR");
        (await _service.GetListAsync(List(search: "pallet"))).Rows.ShouldHaveSingleItem().Code.ShouldBe("TAIL");
        (await _service.GetListAsync(List(search: "goods"))).Rows.ShouldHaveSingleItem().Code.ShouldBe("ADR");
        (await _service.GetLookupAsync("tail")).ShouldHaveSingleItem().Code.ShouldBe("TAIL");

        (await Should.ThrowAsync<BusinessException>(() => _service.GetListAsync(List(sortBy: "kind")))).Code.ShouldBe(MasterDataErrorCodes.UnknownSortColumn);
    }

    [Fact]
    public async Task Deleting_a_characteristic_removes_it_from_the_vehicle_types_that_carry_it()
    {
        using var _ = InTenantA();
        var cool = await CreateAsync("COOL", "Cooled");
        var tail = await CreateAsync("TAIL", "Tail lift");
        var reefer = await _vehicleTypes.CreateAsync(new VehicleTypeCreateDto { Name = "Reefer", Kind = VehicleTypeKind.Trailer, TransportCharacteristicsIds = [cool.Id, tail.Id] });
        (await _vehicleTypes.GetAsync(reefer.Id)).TransportCharacteristics.Count.ShouldBe(2);

        await _service.DeleteAsync(cool.Id);

        (await _vehicleTypes.GetAsync(reefer.Id)).TransportCharacteristics.Select(c => c.Id).ShouldBe([tail.Id]);

        /* The same through the bulk path. */
        (await _service.BulkDeleteAsync(new MasterDataBulkDeleteRequestDto { Selection = Explicit(tail.Id) })).DeletedCount.ShouldBe(1);
        (await _vehicleTypes.GetAsync(reefer.Id)).TransportCharacteristics.ShouldBeEmpty();
    }

    [Fact]
    public async Task BulkDelete_filter_based_and_twice()
    {
        using var _ = InTenantA();
        var cool = await CreateAsync("COOL", "Cooled");
        var cold = await CreateAsync("COLD", "Cold");
        await CreateAsync("ADR", "Dangerous goods");

        (await _service.BulkDeleteAsync(new MasterDataBulkDeleteRequestDto { Selection = FilterBased("co", cold.Id) })).DeletedCount.ShouldBe(1);
        (await _service.GetListAsync(List(sortBy: "code"))).Rows.Select(r => r.Code).ShouldBe(["ADR", "COLD"]);
        (await _service.BulkDeleteAsync(new MasterDataBulkDeleteRequestDto { Selection = Explicit(cool.Id) })).DeletedCount.ShouldBe(0);
    }

    // ── Tenant isolation (Critical) ───────────────────────────────────────────

    [Fact]
    public async Task Rows_of_tenant_A_are_invisible_and_untouchable_from_tenant_B()
    {
        TransportCharacteristicsDto cool;
        using (InTenantA())
        {
            cool = await CreateAsync("COOL", "Cooled");
        }

        using (InTenantB())
        {
            (await _service.GetListAsync(List())).TotalCount.ShouldBe(0);
            (await _service.GetLookupAsync()).ShouldBeEmpty();
            await Should.ThrowAsync<EntityNotFoundException>(() => _service.GetAsync(cool.Id));
            await Should.ThrowAsync<EntityNotFoundException>(() => _service.UpdateAsync(cool.Id, UpdateFrom(cool)));
            await Should.ThrowAsync<EntityNotFoundException>(() => _service.DeleteAsync(cool.Id));
            (await _service.BulkDeleteAsync(new MasterDataBulkDeleteRequestDto { Selection = FilterBased() })).DeletedCount.ShouldBe(0);

            /* A vehicle type of tenant B cannot reference tenant A's characteristic. */
            var ex = await Should.ThrowAsync<BusinessException>(() => _vehicleTypes.CreateAsync(new VehicleTypeCreateDto { Name = "Reefer", Kind = VehicleTypeKind.Trailer, TransportCharacteristicsIds = [cool.Id] }));
            ex.Code.ShouldBe(MasterDataErrorCodes.ReferenceNotFound);
            ex.Data["field"].ShouldBe("transportCharacteristicsIds");

            (await CreateAsync("COOL", "Cooled B")).Id.ShouldNotBe(cool.Id);
        }

        using (InTenantA())
        {
            (await _service.GetListAsync(List())).Rows.ShouldHaveSingleItem().Id.ShouldBe(cool.Id);
        }
    }
}
