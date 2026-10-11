using System;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.MasterData;
using OpenTms.MasterData.Reference;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Validation;
using Xunit;

namespace OpenTms.EntityFrameworkCore.MasterData;

/// <summary>
/// AddressTypeAppService on SQLite (design 41). Risk class: Critical for tenant isolation and the
/// one-Visiting-one-Postal rule; High for the CRUD and concurrency rules.
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class AddressTypeAppServiceTests : MasterDataAppServiceTestBase
{
    private readonly IAddressTypeAppService _service;

    public AddressTypeAppServiceTests()
    {
        _service = GetRequiredService<IAddressTypeAppService>();
    }

    private Task<AddressTypeDto> CreateAsync(AddressTypeKind kind, string en, string? nl = null, string? description = null)
        => _service.CreateAsync(new AddressTypeCreateDto { Kind = kind, Names = Names(en, nl), Description = description });

    private static AddressTypeUpdateDto UpdateFrom(AddressTypeDto dto) => new()
    {
        Kind = dto.Kind,
        Names = Copy(dto.Names),
        Description = dto.Description,
        ConcurrencyStamp = dto.ConcurrencyStamp
    };

    [Fact]
    public async Task Create_get_list_and_lookup_round_trip_with_the_resolved_name()
    {
        using var _ = InTenantA();
        using var culture = UiCulture("nl");

        var visiting = await CreateAsync(AddressTypeKind.Visiting, "Visiting address", "Bezoekadres", "where trucks go");

        visiting.Kind.ShouldBe(AddressTypeKind.Visiting);
        visiting.Name.ShouldBe("Bezoekadres");
        visiting.Names["en"].ShouldBe("Visiting address");
        visiting.Description.ShouldBe("where trucks go");

        (await _service.GetAsync(visiting.Id)).Names.Count.ShouldBe(2);
        var row = (await _service.GetListAsync(List())).Rows.ShouldHaveSingleItem();
        row.Kind.ShouldBe(AddressTypeKind.Visiting);
        row.Name.ShouldBe("Bezoekadres");
        row.Description.ShouldBe("where trucks go");

        var lookup = (await _service.GetLookupAsync()).ShouldHaveSingleItem();
        lookup.Name.ShouldBe("Bezoekadres");
        lookup.Code.ShouldBe("Visiting");
    }

    [Fact]
    public async Task Two_visiting_or_two_postal_types_are_refused_but_any_number_of_other_types_is_allowed()
    {
        using var _ = InTenantA();
        await CreateAsync(AddressTypeKind.Visiting, "Visiting address");
        await CreateAsync(AddressTypeKind.Postal, "Postal address");

        var visiting = await Should.ThrowAsync<BusinessException>(() => CreateAsync(AddressTypeKind.Visiting, "Second visiting"));
        visiting.Code.ShouldBe(MasterDataErrorCodes.DuplicateKey);
        visiting.Data["field"].ShouldBe("kind");
        visiting.Data["value"].ShouldBe("Visiting");

        (await Should.ThrowAsync<BusinessException>(() => CreateAsync(AddressTypeKind.Postal, "Second postal"))).Code.ShouldBe(MasterDataErrorCodes.DuplicateKey);

        await CreateAsync(AddressTypeKind.Other, "Warehouse");
        await CreateAsync(AddressTypeKind.Other, "Billing");
        await CreateAsync(AddressTypeKind.Other, "Warehouse");

        (await _service.GetListAsync(List())).TotalCount.ShouldBe(5);
        (await _service.GetLookupAsync()).Where(l => l.Code == null).Count().ShouldBe(3);
    }

    [Fact]
    public async Task A_soft_deleted_visiting_type_frees_the_kind()
    {
        using var _ = InTenantA();
        var visiting = await CreateAsync(AddressTypeKind.Visiting, "Visiting address");

        await _service.DeleteAsync(visiting.Id);

        (await CreateAsync(AddressTypeKind.Visiting, "Visiting address again")).Id.ShouldNotBe(visiting.Id);
    }

    [Fact]
    public async Task Update_cannot_change_the_kind_to_an_already_taken_one_but_may_keep_or_change_it_otherwise()
    {
        using var _ = InTenantA();
        var visiting = await CreateAsync(AddressTypeKind.Visiting, "Visiting address");
        var other = await CreateAsync(AddressTypeKind.Other, "Warehouse");

        var clash = UpdateFrom(other);
        clash.Kind = AddressTypeKind.Visiting;
        (await Should.ThrowAsync<BusinessException>(() => _service.UpdateAsync(other.Id, clash))).Code.ShouldBe(MasterDataErrorCodes.DuplicateKey);

        var same = UpdateFrom(visiting);
        same.Names["nl"] = "Bezoekadres";
        var updated = await _service.UpdateAsync(visiting.Id, same);
        updated.Kind.ShouldBe(AddressTypeKind.Visiting);
        updated.Names["nl"].ShouldBe("Bezoekadres");

        var toPostal = UpdateFrom(other);
        toPostal.Kind = AddressTypeKind.Postal;
        (await _service.UpdateAsync(other.Id, toPostal)).Kind.ShouldBe(AddressTypeKind.Postal);

        await Should.ThrowAsync<AbpDbConcurrencyException>(() => _service.UpdateAsync(visiting.Id, UpdateFrom(visiting)));
    }

    [Fact]
    public async Task The_default_culture_name_is_required_and_the_kind_must_be_defined()
    {
        using var _ = InTenantA();

        (await Should.ThrowAsync<BusinessException>(() => _service.CreateAsync(new AddressTypeCreateDto { Kind = AddressTypeKind.Other, Names = new LocalizedNameDto { ["nl"] = "Magazijn" } })))
            .Code.ShouldBe(MasterDataErrorCodes.NameRequired);
        await Should.ThrowAsync<AbpValidationException>(() => _service.CreateAsync(new AddressTypeCreateDto { Kind = (AddressTypeKind)9, Names = Names("X") }));
    }

    [Fact]
    public async Task List_sorts_on_resolved_name_kind_or_description_and_searches_names()
    {
        using var _ = InTenantA();
        await CreateAsync(AddressTypeKind.Visiting, "Visiting address", "Bezoekadres");
        await CreateAsync(AddressTypeKind.Postal, "Postal address", "Postadres");
        await CreateAsync(AddressTypeKind.Other, "Warehouse", "Magazijn", "dock");

        using (UiCulture("nl"))
        {
            (await _service.GetListAsync(List())).Rows.Select(r => r.Name).ShouldBe(["Bezoekadres", "Magazijn", "Postadres"]);
            (await _service.GetListAsync(List(search: "adres"))).Rows.Count.ShouldBe(2);
            (await _service.GetListAsync(List(search: "warehouse"))).Rows.ShouldHaveSingleItem().Name.ShouldBe("Magazijn");
            (await _service.GetListAsync(List(search: "dock"))).Rows.ShouldHaveSingleItem().Name.ShouldBe("Magazijn");
        }

        using (UiCulture("en"))
        {
            (await _service.GetListAsync(List())).Rows.Select(r => r.Name).ShouldBe(["Postal address", "Visiting address", "Warehouse"]);
            (await _service.GetListAsync(List(sortBy: "kind"))).Rows.Select(r => r.Kind).ShouldBe([AddressTypeKind.Other, AddressTypeKind.Visiting, AddressTypeKind.Postal]);
            (await _service.GetListAsync(List(sortBy: "description", sortDir: "desc"))).Rows.First().Name.ShouldBe("Warehouse");
        }

        (await Should.ThrowAsync<BusinessException>(() => _service.GetListAsync(List(sortBy: "id")))).Code.ShouldBe(MasterDataErrorCodes.UnknownSortColumn);
    }

    [Fact]
    public async Task BulkDelete_explicit_and_filter_based_and_twice()
    {
        using var _ = InTenantA();
        var visiting = await CreateAsync(AddressTypeKind.Visiting, "Visiting address");
        var a = await CreateAsync(AddressTypeKind.Other, "Alpha dock");
        var b = await CreateAsync(AddressTypeKind.Other, "Beta dock");

        (await _service.BulkDeleteAsync(new MasterDataBulkDeleteRequestDto { Selection = FilterBased("dock", b.Id) })).DeletedCount.ShouldBe(1);
        (await _service.GetListAsync(List())).Rows.Select(r => r.Id).ShouldBe([b.Id, visiting.Id], ignoreOrder: true);

        var explicitRequest = new MasterDataBulkDeleteRequestDto { Selection = Explicit(a.Id, b.Id, visiting.Id) };
        (await _service.BulkDeleteAsync(explicitRequest)).DeletedCount.ShouldBe(2);
        (await _service.BulkDeleteAsync(explicitRequest)).DeletedCount.ShouldBe(0);
        (await _service.GetListAsync(List())).TotalCount.ShouldBe(0);
    }

    // ── Tenant isolation (Critical) ───────────────────────────────────────────

    [Fact]
    public async Task Rows_of_tenant_A_are_invisible_and_untouchable_from_tenant_B_and_B_has_its_own_visiting_type()
    {
        AddressTypeDto visiting;
        using (InTenantA())
        {
            visiting = await CreateAsync(AddressTypeKind.Visiting, "Visiting address");
        }

        using (InTenantB())
        {
            (await _service.GetListAsync(List())).TotalCount.ShouldBe(0);
            (await _service.GetLookupAsync()).ShouldBeEmpty();
            await Should.ThrowAsync<EntityNotFoundException>(() => _service.GetAsync(visiting.Id));
            await Should.ThrowAsync<EntityNotFoundException>(() => _service.UpdateAsync(visiting.Id, UpdateFrom(visiting)));
            await Should.ThrowAsync<EntityNotFoundException>(() => _service.DeleteAsync(visiting.Id));
            (await _service.BulkDeleteAsync(new MasterDataBulkDeleteRequestDto { Selection = FilterBased() })).DeletedCount.ShouldBe(0);

            /* The partial unique index is per tenant: B gets its own Visiting type. */
            (await CreateAsync(AddressTypeKind.Visiting, "Visiting address B")).Kind.ShouldBe(AddressTypeKind.Visiting);
        }

        using (InTenantA())
        {
            (await _service.GetListAsync(List())).Rows.ShouldHaveSingleItem().Id.ShouldBe(visiting.Id);
        }
    }
}
