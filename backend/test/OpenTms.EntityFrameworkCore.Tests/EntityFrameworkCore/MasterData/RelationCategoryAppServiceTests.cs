using System;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.MasterData;
using OpenTms.MasterData.Relations;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Validation;
using Xunit;

namespace OpenTms.EntityFrameworkCore.MasterData;

/// <summary>
/// RelationCategoryAppService on SQLite (design 41). Risk class: Critical for tenant isolation and the
/// unique name; High for the CRUD, concurrency and bulk-delete rules.
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class RelationCategoryAppServiceTests : MasterDataAppServiceTestBase
{
    private readonly IRelationCategoryAppService _service;

    public RelationCategoryAppServiceTests()
    {
        _service = GetRequiredService<IRelationCategoryAppService>();
    }

    private Task<RelationCategoryDto> CreateAsync(string name, string? description = null)
        => _service.CreateAsync(new RelationCategoryCreateDto { Name = name, Description = description });

    private static RelationCategoryUpdateDto UpdateFrom(RelationCategoryDto dto) => new()
    {
        Name = dto.Name,
        Description = dto.Description,
        ConcurrencyStamp = dto.ConcurrencyStamp
    };

    [Fact]
    public async Task Create_get_list_and_lookup_round_trip()
    {
        using var _ = InTenantA();

        var created = await CreateAsync("  Customers ", " Paying relations ");

        created.Name.ShouldBe("Customers");
        created.Description.ShouldBe("Paying relations");
        (await _service.GetAsync(created.Id)).Name.ShouldBe("Customers");
        var row = (await _service.GetListAsync(List())).Rows.ShouldHaveSingleItem();
        row.Name.ShouldBe("Customers");
        row.Description.ShouldBe("Paying relations");
        var lookup = (await _service.GetLookupAsync()).ShouldHaveSingleItem();
        lookup.Name.ShouldBe("Customers");
        lookup.Code.ShouldBeNull();
    }

    [Fact]
    public async Task Name_is_required_and_unique_per_tenant_and_freed_by_a_soft_delete()
    {
        using var _ = InTenantA();
        var customers = await CreateAsync("Customers");

        await Should.ThrowAsync<AbpValidationException>(() => CreateAsync(""));
        var ex = await Should.ThrowAsync<BusinessException>(() => CreateAsync(" Customers "));
        ex.Code.ShouldBe(MasterDataErrorCodes.DuplicateKey);
        ex.Data["field"].ShouldBe("name");
        ex.Data["value"].ShouldBe("Customers");

        await _service.DeleteAsync(customers.Id);
        (await CreateAsync("Customers")).Id.ShouldNotBe(customers.Id);
    }

    [Fact]
    public async Task Update_renames_within_the_unique_rule_and_checks_the_stamp()
    {
        using var _ = InTenantA();
        var customers = await CreateAsync("Customers");
        await CreateAsync("Carriers");

        var renamed = UpdateFrom(customers);
        renamed.Name = "Clients";
        renamed.Description = "renamed";
        var updated = await _service.UpdateAsync(customers.Id, renamed);
        updated.Name.ShouldBe("Clients");
        updated.Description.ShouldBe("renamed");
        updated.ConcurrencyStamp.ShouldNotBe(customers.ConcurrencyStamp);

        var clash = UpdateFrom(updated);
        clash.Name = "Carriers";
        (await Should.ThrowAsync<BusinessException>(() => _service.UpdateAsync(customers.Id, clash))).Code.ShouldBe(MasterDataErrorCodes.DuplicateKey);

        var sameName = UpdateFrom(updated);
        sameName.Description = "same name again";
        (await _service.UpdateAsync(customers.Id, sameName)).Description.ShouldBe("same name again");

        await Should.ThrowAsync<AbpDbConcurrencyException>(() => _service.UpdateAsync(customers.Id, UpdateFrom(customers)));
        await Should.ThrowAsync<EntityNotFoundException>(() => _service.UpdateAsync(Guid.NewGuid(), UpdateFrom(customers)));
    }

    [Fact]
    public async Task List_pages_searches_name_and_description_and_sorts_on_the_whitelist()
    {
        using var _ = InTenantA();
        await CreateAsync("Carriers", "subcontractors");
        await CreateAsync("Customers", "paying");
        await CreateAsync("Agents", "forwarders");

        (await _service.GetListAsync(List())).Rows.Select(r => r.Name).ShouldBe(["Agents", "Carriers", "Customers"]);
        (await _service.GetListAsync(List(sortBy: "name", sortDir: "desc"))).Rows.Select(r => r.Name).ShouldBe(["Customers", "Carriers", "Agents"]);
        (await _service.GetListAsync(List(sortBy: "description"))).Rows.Select(r => r.Name).ShouldBe(["Agents", "Customers", "Carriers"]);

        var page = await _service.GetListAsync(List(start: 1, end: 2));
        page.Rows.ShouldHaveSingleItem().Name.ShouldBe("Carriers");
        page.TotalCount.ShouldBe(3);

        var searched = await _service.GetListAsync(List(search: "PAY"));
        searched.FilteredCount.ShouldBe(1);
        searched.TotalCount.ShouldBe(3);
        searched.Rows.ShouldHaveSingleItem().Name.ShouldBe("Customers");
        (await _service.GetListAsync(List(search: "c"))).Rows.Count.ShouldBe(2);
        (await _service.GetLookupAsync("agent")).ShouldHaveSingleItem().Name.ShouldBe("Agents");
        (await _service.GetLookupAsync(take: 2)).Count.ShouldBe(2);

        (await Should.ThrowAsync<BusinessException>(() => _service.GetListAsync(List(sortBy: "creationTime")))).Code.ShouldBe(MasterDataErrorCodes.UnknownSortColumn);
    }

    [Fact]
    public async Task Delete_and_bulk_delete_remove_rows_and_a_second_bulk_delete_removes_nothing()
    {
        using var _ = InTenantA();
        var a = await CreateAsync("Alpha");
        var b = await CreateAsync("Beta");
        var c = await CreateAsync("Gamma");

        await _service.DeleteAsync(a.Id);
        await Should.ThrowAsync<EntityNotFoundException>(() => _service.DeleteAsync(a.Id));

        var request = new MasterDataBulkDeleteRequestDto { Selection = Explicit(b.Id, c.Id) };
        var first = await _service.BulkDeleteAsync(request);
        first.DeletedCount.ShouldBe(2);
        first.SkippedRows.ShouldBeEmpty();
        (await _service.BulkDeleteAsync(request)).DeletedCount.ShouldBe(0);
        (await _service.GetListAsync(List())).TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task BulkDelete_filter_based_honours_search_and_exclusions()
    {
        using var _ = InTenantA();
        var a = await CreateAsync("Alpha group");
        var b = await CreateAsync("Beta group");
        await CreateAsync("Gamma");

        (await _service.BulkDeleteAsync(new MasterDataBulkDeleteRequestDto { Selection = FilterBased("group", a.Id) })).DeletedCount.ShouldBe(1);

        (await _service.GetListAsync(List())).Rows.Select(r => r.Name).ShouldBe(["Alpha group", "Gamma"]);
        b.Id.ShouldNotBe(Guid.Empty);
    }

    // ── Tenant isolation (Critical) ───────────────────────────────────────────

    [Fact]
    public async Task Rows_of_tenant_A_are_invisible_and_untouchable_from_tenant_B()
    {
        RelationCategoryDto customers;
        using (InTenantA())
        {
            customers = await CreateAsync("Customers");
        }

        using (InTenantB())
        {
            (await _service.GetListAsync(List())).TotalCount.ShouldBe(0);
            (await _service.GetListAsync(List(search: "cust"))).Rows.ShouldBeEmpty();
            (await _service.GetLookupAsync()).ShouldBeEmpty();
            await Should.ThrowAsync<EntityNotFoundException>(() => _service.GetAsync(customers.Id));
            await Should.ThrowAsync<EntityNotFoundException>(() => _service.UpdateAsync(customers.Id, UpdateFrom(customers)));
            await Should.ThrowAsync<EntityNotFoundException>(() => _service.DeleteAsync(customers.Id));
            (await _service.BulkDeleteAsync(new MasterDataBulkDeleteRequestDto { Selection = Explicit(customers.Id) })).DeletedCount.ShouldBe(0);
            (await _service.BulkDeleteAsync(new MasterDataBulkDeleteRequestDto { Selection = FilterBased() })).DeletedCount.ShouldBe(0);

            (await CreateAsync("Customers")).Id.ShouldNotBe(customers.Id);
        }

        using (InTenantA())
        {
            (await _service.GetListAsync(List())).Rows.ShouldHaveSingleItem().Id.ShouldBe(customers.Id);
        }
    }
}
