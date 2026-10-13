using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Features;
using OpenTms.MasterData;
using OpenTms.MasterData.Fleet;
using Shouldly;
using Volo.Abp.Authorization;
using Xunit;

namespace OpenTms.EntityFrameworkCore.MasterData;

/// <summary>
/// TransportCharacteristicsAppService.GetLookupPageAsync on SQLite (design 43, "Relation administration":
/// the server-searched picker above the relation's characteristics grid, UI-LOOK-002). Risk class: Critical for
/// tenant isolation (a list endpoint, the classic leak: items and count) and the feature gate; High for the
/// paging bounds, the order and the search. The permission attribute is asserted by reflection in
/// OpenTms.Application.Tests (TransportCharacteristicsLookupPageAuthorizationTests): these tests run with
/// authorization always allowed.
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class TransportCharacteristicsLookupPageTests : MasterDataAppServiceTestBase
{
    private readonly ITransportCharacteristicsAppService _service;

    public TransportCharacteristicsLookupPageTests()
    {
        _service = GetRequiredService<ITransportCharacteristicsAppService>();
    }

    private Task<TransportCharacteristicsDto> CreateAsync(string code, string name, string? description = null)
        => _service.CreateAsync(new TransportCharacteristicsCreateDto { Code = code, Name = name, Description = description });

    /// <summary>Thirty rows "C00".."C29" named "Characteristic 00".."Characteristic 29", created in reverse order.</summary>
    private async Task<List<Guid>> CreateThirtyAsync()
    {
        var ids = new Guid[30];
        for (var i = 29; i >= 0; i--)
        {
            ids[i] = (await CreateAsync($"C{i:D2}", $"Characteristic {i:D2}")).Id;
        }

        return [.. ids];
    }

    [Fact]
    public async Task Items_are_ordered_by_name_then_code_and_carry_id_code_name_and_description()
    {
        using var _ = InTenantA();
        var tail = await CreateAsync("TAIL", "Tail lift", "for pallets");
        var cool2 = await CreateAsync("COOL2", "Cooled");
        var adr = await CreateAsync("ADR", "Dangerous goods");
        var cool1 = await CreateAsync("COOL1", "Cooled", "reefer");

        var page = await _service.GetLookupPageAsync();

        page.TotalCount.ShouldBe(4);
        page.Items.Select(i => i.Code).ShouldBe(["COOL1", "COOL2", "ADR", "TAIL"], "by name; the two 'Cooled' rows by code");
        page.Items.Select(i => i.Id).ShouldBe([cool1.Id, cool2.Id, adr.Id, tail.Id]);
        var first = page.Items[0];
        first.Name.ShouldBe("Cooled");
        first.Description.ShouldBe("reefer");
        page.Items[2].Description.ShouldBeNull();
        page.Items[3].Name.ShouldBe("Tail lift");
        page.Items[3].Description.ShouldBe("for pallets");
    }

    [Theory]
    [InlineData("cool", new[] { "COOL1", "COOL2" })]
    [InlineData("COOL", new[] { "COOL1", "COOL2" })]
    [InlineData("  Cool  ", new[] { "COOL1", "COOL2" })]
    [InlineData("LIFT", new[] { "TAIL" })]
    [InlineData("pallet", new[] { "TAIL" })]
    [InlineData("REEFER", new[] { "COOL1" })]
    [InlineData("goods", new[] { "ADR" })]
    [InlineData("nothing-like-this", new string[0])]
    public async Task The_term_searches_code_name_and_description_case_insensitively_and_counts_the_filtered_set(string term, string[] expected)
    {
        using var _ = InTenantA();
        await CreateAsync("TAIL", "Tail lift", "For Pallets");
        await CreateAsync("COOL2", "Cooled");
        await CreateAsync("ADR", "Dangerous goods");
        await CreateAsync("COOL1", "Cooled", "Reefer");

        var page = await _service.GetLookupPageAsync(term);

        page.Items.Select(i => i.Code).ShouldBe(expected);
        page.TotalCount.ShouldBe(expected.Length, "TotalCount is the count of the searched set, not of all rows");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task An_empty_or_blank_term_is_no_filter(string? term)
    {
        using var _ = InTenantA();
        await CreateAsync("TAIL", "Tail lift");
        await CreateAsync("ADR", "Dangerous goods");

        var page = await _service.GetLookupPageAsync(term);

        page.TotalCount.ShouldBe(2);
        page.Items.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Pages_hold_25_by_default_and_the_pages_together_are_the_whole_ordered_set_without_overlap()
    {
        using var _ = InTenantA();
        var ids = await CreateThirtyAsync();

        var first = await _service.GetLookupPageAsync();
        var second = await _service.GetLookupPageAsync(skip: MasterDataConsts.LookupPageSize);

        MasterDataConsts.LookupPageSize.ShouldBe(25);
        first.Items.Count.ShouldBe(25);
        first.TotalCount.ShouldBe(30);
        second.Items.Count.ShouldBe(5);
        second.TotalCount.ShouldBe(30, "the count is the whole set on every page");
        first.Items.Concat(second.Items).Select(i => i.Id).ShouldBe(ids);

        var beyond = await _service.GetLookupPageAsync(skip: 30);
        beyond.Items.ShouldBeEmpty();
        beyond.TotalCount.ShouldBe(30);

        /* Paging applies to the searched set too: "characteristic 1" matches C10..C19. */
        var searched = await _service.GetLookupPageAsync("characteristic 1", skip: 5, take: 3);
        searched.TotalCount.ShouldBe(10);
        searched.Items.Select(i => i.Code).ShouldBe(["C15", "C16", "C17"]);
    }

    [Theory]
    [InlineData(26, 25)]
    [InlineData(100, 25)]
    [InlineData(int.MaxValue, 25)]
    [InlineData(25, 25)]
    [InlineData(10, 10)]
    [InlineData(1, 1)]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(int.MinValue, 1)]
    public async Task Take_is_clamped_to_one_through_the_page_size(int take, int expected)
    {
        using var _ = InTenantA();
        await CreateThirtyAsync();

        var page = await _service.GetLookupPageAsync(take: take);

        page.Items.Count.ShouldBe(expected);
        page.Items[0].Code.ShouldBe("C00");
        page.TotalCount.ShouldBe(30);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-100)]
    [InlineData(int.MinValue)]
    public async Task A_negative_skip_is_treated_as_zero(int skip)
    {
        using var _ = InTenantA();
        var ids = await CreateThirtyAsync();

        var page = await _service.GetLookupPageAsync(skip: skip, take: 5);

        page.Items.Select(i => i.Id).ShouldBe(ids.Take(5));
        page.TotalCount.ShouldBe(30);
    }

    [Fact]
    public async Task A_deleted_characteristic_is_neither_listed_nor_counted()
    {
        using var _ = InTenantA();
        var cool = await CreateAsync("COOL", "Cooled");
        var adr = await CreateAsync("ADR", "Dangerous goods");

        await _service.DeleteAsync(cool.Id);

        var page = await _service.GetLookupPageAsync();
        page.TotalCount.ShouldBe(1);
        page.Items.ShouldHaveSingleItem().Id.ShouldBe(adr.Id);
        (await _service.GetLookupPageAsync("cool")).TotalCount.ShouldBe(0);
    }

    // ── Feature flag (Critical) ───────────────────────────────────────────────

    [Fact]
    public async Task With_the_FirstRun_flag_off_the_lookup_page_is_refused()
    {
        using var _ = InTenantA();
        await CreateAsync("COOL", "Cooled");

        using (TestFeatureValueProvider.Override("false"))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.GetLookupPageAsync());
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.GetLookupPageAsync("cool", 0, 1));
        }

        (await _service.GetLookupPageAsync()).TotalCount.ShouldBe(1);
    }

    // ── Tenant isolation (Critical) ───────────────────────────────────────────

    [Fact]
    public async Task Another_tenants_characteristics_never_appear_and_are_not_counted()
    {
        Guid coolA;
        using (InTenantA())
        {
            coolA = (await CreateAsync("COOL", "Cooled", "reefer")).Id;
            await CreateAsync("ADR", "Dangerous goods");
            await CreateAsync("TAIL", "Tail lift");
        }

        using (InTenantB())
        {
            var empty = await _service.GetLookupPageAsync();
            empty.TotalCount.ShouldBe(0);
            empty.Items.ShouldBeEmpty();
            (await _service.GetLookupPageAsync("cool")).TotalCount.ShouldBe(0);
            (await _service.GetLookupPageAsync("reefer")).Items.ShouldBeEmpty("the description of tenant A's row");

            var coolB = await CreateAsync("COOL", "Cooled in B");
            var own = await _service.GetLookupPageAsync();
            own.TotalCount.ShouldBe(1, "tenant A's three rows are not in tenant B's count");
            own.Items.ShouldHaveSingleItem().Id.ShouldBe(coolB.Id);
            own.Items[0].Id.ShouldNotBe(coolA);
            (await _service.GetLookupPageAsync("cool")).Items.ShouldHaveSingleItem().Name.ShouldBe("Cooled in B");
        }

        using (InTenantA())
        {
            var page = await _service.GetLookupPageAsync();
            page.TotalCount.ShouldBe(3);
            page.Items.ShouldNotContain(i => i.Name == "Cooled in B");
            (await _service.GetLookupPageAsync("cool")).Items.ShouldHaveSingleItem().Id.ShouldBe(coolA);
        }
    }
}
