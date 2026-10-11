using System;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.MasterData;
using OpenTms.MasterData.Transport;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Validation;
using Xunit;

namespace OpenTms.EntityFrameworkCore.MasterData;

/// <summary>
/// ProductAppService on SQLite (design 41). Risk class: Critical for tenant isolation (mandatory
/// negative tests on every operation) and the unique name; High for the ADR rule, archiving, the
/// incompatibility sync, concurrency and bulk delete.
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class ProductAppServiceTests : MasterDataAppServiceTestBase
{
    private readonly IProductAppService _service;

    public ProductAppServiceTests()
    {
        _service = GetRequiredService<IProductAppService>();
    }

    private Task<ProductDto> CreateAsync(string name, bool archived = false, double? weight = null, params Guid[] incompatible)
        => _service.CreateAsync(new ProductCreateDto { Name = name, IsArchived = archived, DefaultWeightKg = weight, IncompatibleProductIds = [.. incompatible] });

    private static ProductUpdateDto UpdateFrom(ProductDto dto) => new()
    {
        Name = dto.Name,
        Description = dto.Description,
        IsArchived = dto.IsArchived,
        IsAdr = dto.IsAdr,
        AdrUnNumber = dto.AdrUnNumber,
        AdrClass = dto.AdrClass,
        AdrPackingGroup = dto.AdrPackingGroup,
        AdrDescription = dto.AdrDescription,
        DefaultQuantity = dto.DefaultQuantity,
        DefaultWeightKg = dto.DefaultWeightKg,
        DefaultColli = dto.DefaultColli,
        DefaultVolumeM3 = dto.DefaultVolumeM3,
        DefaultLoadingMeters = dto.DefaultLoadingMeters,
        DefaultEpq = dto.DefaultEpq,
        DefaultLengthCm = dto.DefaultLengthCm,
        DefaultWidthCm = dto.DefaultWidthCm,
        DefaultHeightCm = dto.DefaultHeightCm,
        IncompatibleProductIds = dto.IncompatibleProducts.Select(p => p.Id).ToList(),
        ConcurrencyStamp = dto.ConcurrencyStamp
    };

    [Fact]
    public async Task Create_stores_every_field_and_get_list_and_lookup_return_it()
    {
        using var _ = InTenantA();

        var created = await _service.CreateAsync(new ProductCreateDto
        {
            Name = " Europallet goods ",
            Description = "standard",
            IsAdr = true,
            AdrUnNumber = "1203",
            AdrClass = " 3 ",
            AdrPackingGroup = "ii",
            AdrDescription = "Petrol",
            DefaultQuantity = 1,
            DefaultWeightKg = 800,
            DefaultColli = 2,
            DefaultVolumeM3 = 1.5,
            DefaultLoadingMeters = 0.4,
            DefaultEpq = 1,
            DefaultLengthCm = 120,
            DefaultWidthCm = 80,
            DefaultHeightCm = 144
        });

        created.Name.ShouldBe("Europallet goods");
        created.IsAdr.ShouldBeTrue();
        created.AdrUnNumber.ShouldBe("1203");
        created.AdrClass.ShouldBe("3");
        created.AdrPackingGroup.ShouldBe("II");
        created.AdrDescription.ShouldBe("Petrol");
        created.DefaultWeightKg.ShouldBe(800);
        created.DefaultHeightCm.ShouldBe(144);
        created.IsArchived.ShouldBeFalse();

        var fetched = await _service.GetAsync(created.Id);
        fetched.DefaultVolumeM3.ShouldBe(1.5);
        fetched.DefaultLoadingMeters.ShouldBe(0.4);

        var row = (await _service.GetListAsync(List<ArchivableListRequestDto>())).Rows.ShouldHaveSingleItem();
        row.IsAdr.ShouldBeTrue();
        row.AdrUnNumber.ShouldBe("1203");
        row.AdrClass.ShouldBe("3");
        row.DefaultWeightKg.ShouldBe(800);
        row.DefaultEpq.ShouldBe(1);

        var lookup = (await _service.GetLookupAsync()).ShouldHaveSingleItem();
        lookup.Name.ShouldBe("Europallet goods");
        lookup.Code.ShouldBe("1203");
    }

    [Fact]
    public async Task Adr_requires_un_number_and_class_and_clearing_adr_clears_the_fields()
    {
        using var _ = InTenantA();

        var ex = await Should.ThrowAsync<BusinessException>(() => _service.CreateAsync(new ProductCreateDto { Name = "Petrol", IsAdr = true, AdrUnNumber = "12", AdrClass = "3" }));
        ex.Code.ShouldBe(MasterDataErrorCodes.AdrFieldsRequired);
        (await Should.ThrowAsync<BusinessException>(() => _service.CreateAsync(new ProductCreateDto { Name = "Petrol", IsAdr = true, AdrUnNumber = "1203" }))).Code.ShouldBe(MasterDataErrorCodes.AdrFieldsRequired);
        await Should.ThrowAsync<AbpValidationException>(() => _service.CreateAsync(new ProductCreateDto { Name = "Petrol", IsAdr = true, AdrUnNumber = "12034", AdrClass = "3" }));
        (await _service.GetListAsync(List<ArchivableListRequestDto>())).TotalCount.ShouldBe(0);

        var petrol = await _service.CreateAsync(new ProductCreateDto { Name = "Petrol", IsAdr = true, AdrUnNumber = "1203", AdrClass = "3", AdrPackingGroup = "II", AdrDescription = "flammable" });
        var cleared = UpdateFrom(petrol);
        cleared.IsAdr = false;
        var updated = await _service.UpdateAsync(petrol.Id, cleared);
        updated.IsAdr.ShouldBeFalse();
        updated.AdrUnNumber.ShouldBeNull();
        updated.AdrClass.ShouldBeNull();
        updated.AdrPackingGroup.ShouldBeNull();
        updated.AdrDescription.ShouldBeNull();
        (await _service.GetLookupAsync()).ShouldHaveSingleItem().Code.ShouldBeNull();
    }

    [Fact]
    public async Task Name_is_unique_per_tenant_and_freed_by_a_soft_delete()
    {
        using var _ = InTenantA();
        var pallet = await CreateAsync("Pallet");

        var ex = await Should.ThrowAsync<BusinessException>(() => CreateAsync(" Pallet "));
        ex.Code.ShouldBe(MasterDataErrorCodes.DuplicateKey);
        ex.Data["field"].ShouldBe("name");
        ex.Data["value"].ShouldBe("Pallet");

        await _service.DeleteAsync(pallet.Id);
        await Should.ThrowAsync<EntityNotFoundException>(() => _service.GetAsync(pallet.Id));
        (await CreateAsync("Pallet")).Id.ShouldNotBe(pallet.Id);
    }

    [Fact]
    public async Task Update_with_the_returned_stamp_succeeds_and_a_stale_stamp_conflicts()
    {
        using var _ = InTenantA();
        var pallet = await CreateAsync("Pallet", weight: 10);
        await CreateAsync("Box");

        var update = UpdateFrom(pallet);
        update.Name = "Euro pallet";
        update.DefaultWeightKg = 25;
        var updated = await _service.UpdateAsync(pallet.Id, update);
        updated.Name.ShouldBe("Euro pallet");
        updated.DefaultWeightKg.ShouldBe(25);
        updated.ConcurrencyStamp.ShouldNotBe(pallet.ConcurrencyStamp);

        var clash = UpdateFrom(updated);
        clash.Name = "box";
        /* Uniqueness is on the stored value; "box" and "Box" differ. */
        (await _service.UpdateAsync(pallet.Id, clash)).Name.ShouldBe("box");
        var exactClash = UpdateFrom(await _service.GetAsync(pallet.Id));
        exactClash.Name = "Box";
        (await Should.ThrowAsync<BusinessException>(() => _service.UpdateAsync(pallet.Id, exactClash))).Code.ShouldBe(MasterDataErrorCodes.DuplicateKey);

        await Should.ThrowAsync<AbpDbConcurrencyException>(() => _service.UpdateAsync(pallet.Id, UpdateFrom(pallet)));
        await Should.ThrowAsync<EntityNotFoundException>(() => _service.UpdateAsync(Guid.NewGuid(), UpdateFrom(pallet)));
    }

    [Fact]
    public async Task Archived_products_are_hidden_from_list_and_lookup_unless_asked_for()
    {
        using var _ = InTenantA();
        await CreateAsync("Pallet");
        var legacy = await CreateAsync("Legacy", archived: true);

        var visible = await _service.GetListAsync(List<ArchivableListRequestDto>());
        visible.TotalCount.ShouldBe(1);
        visible.Rows.ShouldHaveSingleItem().Name.ShouldBe("Pallet");
        (await _service.GetListAsync(new ArchivableListRequestDto { IncludeArchived = true })).TotalCount.ShouldBe(2);
        (await _service.GetLookupAsync()).ShouldHaveSingleItem().Name.ShouldBe("Pallet");
        (await _service.GetAsync(legacy.Id)).IsArchived.ShouldBeTrue();
    }

    [Fact]
    public async Task List_searches_name_description_and_un_number_and_sorts_on_the_whitelist()
    {
        using var _ = InTenantA();
        await _service.CreateAsync(new ProductCreateDto { Name = "Petrol", IsAdr = true, AdrUnNumber = "1203", AdrClass = "3", DefaultWeightKg = 700 });
        await _service.CreateAsync(new ProductCreateDto { Name = "Pallet", Description = "wooden", DefaultWeightKg = 25 });
        await _service.CreateAsync(new ProductCreateDto { Name = "Box", DefaultWeightKg = 2 });

        (await _service.GetListAsync(List<ArchivableListRequestDto>())).Rows.Select(r => r.Name).ShouldBe(["Box", "Pallet", "Petrol"]);
        (await _service.GetListAsync(List<ArchivableListRequestDto>(search: "1203"))).Rows.ShouldHaveSingleItem().Name.ShouldBe("Petrol");
        (await _service.GetListAsync(List<ArchivableListRequestDto>(search: "WOOD"))).Rows.ShouldHaveSingleItem().Name.ShouldBe("Pallet");
        (await _service.GetListAsync(List<ArchivableListRequestDto>(search: "p"))).FilteredCount.ShouldBe(2);
        (await _service.GetListAsync(List<ArchivableListRequestDto>(sortBy: "defaultWeightKg", sortDir: "desc"))).Rows.Select(r => r.Name).ShouldBe(["Petrol", "Pallet", "Box"]);
        (await _service.GetListAsync(List<ArchivableListRequestDto>(sortBy: "isAdr", sortDir: "desc"))).Rows.First().Name.ShouldBe("Petrol");
        (await _service.GetListAsync(List<ArchivableListRequestDto>(start: 1, end: 2))).Rows.ShouldHaveSingleItem().Name.ShouldBe("Pallet");

        foreach (var column in new[] { "description", "isArchived", "adrUnNumber", "adrClass", "adrPackingGroup", "defaultQuantity", "defaultColli", "defaultVolumeM3", "defaultLoadingMeters", "defaultEpq", "defaultLengthCm", "defaultWidthCm", "defaultHeightCm" })
        {
            (await _service.GetListAsync(List<ArchivableListRequestDto>(sortBy: column))).Rows.Count.ShouldBe(3, column);
        }

        (await Should.ThrowAsync<BusinessException>(() => _service.GetListAsync(List<ArchivableListRequestDto>(sortBy: "adrDescription")))).Code.ShouldBe(MasterDataErrorCodes.UnknownSortColumn);
    }

    [Fact]
    public async Task Incompatibilities_are_symmetric_self_is_dropped_and_unknown_ids_are_refused()
    {
        using var _ = InTenantA();
        var acid = await CreateAsync("Acid");
        var food = await CreateAsync("Food");
        var paper = await CreateAsync("Paper");

        await _service.UpdateAsync(acid.Id, UpdateWith(acid, food.Id, acid.Id));
        (await _service.GetAsync(acid.Id)).IncompatibleProducts.ShouldHaveSingleItem().Id.ShouldBe(food.Id);
        (await _service.GetAsync(food.Id)).IncompatibleProducts.ShouldHaveSingleItem().Id.ShouldBe(acid.Id);

        var foodDto = await _service.GetAsync(food.Id);
        await _service.UpdateAsync(food.Id, UpdateWith(foodDto, acid.Id, paper.Id));
        (await _service.GetAsync(food.Id)).IncompatibleProducts.Select(p => p.Name).ShouldBe(["Acid", "Paper"]);
        (await _service.GetAsync(acid.Id)).IncompatibleProducts.ShouldHaveSingleItem().Id.ShouldBe(food.Id);

        await _service.UpdateAsync(food.Id, UpdateWith(await _service.GetAsync(food.Id)));
        (await _service.GetAsync(food.Id)).IncompatibleProducts.ShouldBeEmpty();
        (await _service.GetAsync(acid.Id)).IncompatibleProducts.ShouldBeEmpty();
        (await _service.GetAsync(paper.Id)).IncompatibleProducts.ShouldBeEmpty();

        var acidNow = await _service.GetAsync(acid.Id);
        var ex = await Should.ThrowAsync<BusinessException>(() => _service.UpdateAsync(acid.Id, UpdateWith(acidNow, Guid.NewGuid())));
        ex.Code.ShouldBe(MasterDataErrorCodes.ReferenceNotFound);
    }

    /// <summary>
    /// API contract (design 41): CreateAsync and UpdateAsync return the detail DTO, which carries
    /// incompatibleProducts. The response must therefore already list the pairs the same call wrote.
    /// </summary>
    [Fact]
    public async Task The_create_and_update_responses_carry_the_incompatibilities_written_in_the_same_call()
    {
        using var _ = InTenantA();
        var acid = await CreateAsync("Acid");

        var food = await CreateAsync("Food", incompatible: acid.Id);
        food.IncompatibleProducts.ShouldHaveSingleItem().Id.ShouldBe(acid.Id);

        var paper = await CreateAsync("Paper");
        var updated = await _service.UpdateAsync(paper.Id, UpdateWith(paper, acid.Id));
        updated.IncompatibleProducts.ShouldHaveSingleItem().Id.ShouldBe(acid.Id);
    }

    [Fact]
    public async Task Deleting_a_product_removes_its_pairs()
    {
        using var _ = InTenantA();
        var acid = await CreateAsync("Acid");
        var food = await CreateAsync("Food", incompatible: acid.Id);
        (await _service.GetAsync(acid.Id)).IncompatibleProducts.ShouldHaveSingleItem().Id.ShouldBe(food.Id);
        (await _service.GetAsync(food.Id)).IncompatibleProducts.ShouldHaveSingleItem().Id.ShouldBe(acid.Id);

        await _service.DeleteAsync(food.Id);

        (await _service.GetAsync(acid.Id)).IncompatibleProducts.ShouldBeEmpty();
    }

    [Fact]
    public async Task BulkDelete_explicit_filter_based_and_twice()
    {
        using var _ = InTenantA();
        var a = await CreateAsync("Pallet A");
        var b = await CreateAsync("Pallet B");
        var c = await CreateAsync("Pallet C", archived: true);
        var d = await CreateAsync("Box");

        var explicitRequest = new ArchivableBulkDeleteRequestDto { Selection = Explicit(a.Id, Guid.NewGuid()) };
        (await _service.BulkDeleteAsync(explicitRequest)).DeletedCount.ShouldBe(1);
        (await _service.BulkDeleteAsync(explicitRequest)).DeletedCount.ShouldBe(0);

        var filterBased = await _service.BulkDeleteAsync(new ArchivableBulkDeleteRequestDto { Selection = FilterBased("pallet") });
        filterBased.DeletedCount.ShouldBe(1);
        (await _service.GetListAsync(new ArchivableListRequestDto { IncludeArchived = true })).Rows.Select(r => r.Id).ShouldBe([c.Id, d.Id], ignoreOrder: true);
        b.Id.ShouldNotBe(Guid.Empty);

        (await _service.BulkDeleteAsync(new ArchivableBulkDeleteRequestDto { Selection = FilterBased("pallet"), IncludeArchived = true })).DeletedCount.ShouldBe(1);
        (await _service.GetListAsync(new ArchivableListRequestDto { IncludeArchived = true })).Rows.ShouldHaveSingleItem().Id.ShouldBe(d.Id);
    }

    // ── Tenant isolation (Critical, mandatory) ────────────────────────────────

    [Fact]
    public async Task Rows_of_tenant_A_are_invisible_and_untouchable_from_tenant_B_through_every_operation()
    {
        ProductDto pallet;
        ProductDto box;
        using (InTenantA())
        {
            pallet = await CreateAsync("Pallet");
            box = await CreateAsync("Box", incompatible: pallet.Id);
        }

        using (InTenantB())
        {
            var page = await _service.GetListAsync(new ArchivableListRequestDto { IncludeArchived = true });
            page.TotalCount.ShouldBe(0);
            page.Rows.ShouldBeEmpty();
            (await _service.GetListAsync(List<ArchivableListRequestDto>(search: "pallet"))).FilteredCount.ShouldBe(0);
            (await _service.GetLookupAsync()).ShouldBeEmpty();
            (await _service.GetLookupAsync("pallet")).ShouldBeEmpty();

            await Should.ThrowAsync<EntityNotFoundException>(() => _service.GetAsync(pallet.Id));
            await Should.ThrowAsync<EntityNotFoundException>(() => _service.UpdateAsync(pallet.Id, UpdateFrom(pallet)));
            await Should.ThrowAsync<EntityNotFoundException>(() => _service.DeleteAsync(pallet.Id));

            (await _service.BulkDeleteAsync(new ArchivableBulkDeleteRequestDto { Selection = Explicit(pallet.Id, box.Id) })).DeletedCount.ShouldBe(0);
            (await _service.BulkDeleteAsync(new ArchivableBulkDeleteRequestDto { Selection = FilterBased(), IncludeArchived = true })).DeletedCount.ShouldBe(0);

            var ex = await Should.ThrowAsync<BusinessException>(() => CreateAsync("Crate", incompatible: pallet.Id));
            ex.Code.ShouldBe(MasterDataErrorCodes.ReferenceNotFound);

            (await CreateAsync("Pallet")).Id.ShouldNotBe(pallet.Id);
        }

        using (InTenantA())
        {
            var rows = (await _service.GetListAsync(List<ArchivableListRequestDto>())).Rows;
            rows.Select(r => r.Id).ShouldBe([box.Id, pallet.Id]);
            (await _service.GetAsync(pallet.Id)).IncompatibleProducts.ShouldHaveSingleItem().Id.ShouldBe(box.Id);
            (await _service.GetAsync(box.Id)).IncompatibleProducts.ShouldHaveSingleItem().Id.ShouldBe(pallet.Id);
        }
    }

    [Fact]
    public async Task Host_rows_are_invisible_to_a_tenant_and_tenant_rows_to_the_host()
    {
        var hostRow = await CreateAsync("Host pallet");

        using (InTenantA())
        {
            (await _service.GetListAsync(List<ArchivableListRequestDto>())).TotalCount.ShouldBe(0);
            await Should.ThrowAsync<EntityNotFoundException>(() => _service.GetAsync(hostRow.Id));
            await CreateAsync("Tenant pallet");
        }

        (await _service.GetListAsync(List<ArchivableListRequestDto>())).Rows.ShouldHaveSingleItem().Name.ShouldBe("Host pallet");
    }

    private static ProductUpdateDto UpdateWith(ProductDto dto, params Guid[] incompatible)
    {
        var update = UpdateFrom(dto);
        update.IncompatibleProductIds = [.. incompatible];
        return update;
    }
}
