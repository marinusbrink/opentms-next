using System;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.MasterData;
using OpenTms.MasterData.Reference;
using OpenTms.MasterData.Regions;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Validation;
using Xunit;

namespace OpenTms.EntityFrameworkCore.MasterData;

/// <summary>
/// GeographicalRegionAppService on SQLite (design 41). Risk class: Critical for tenant isolation
/// (mandatory negative tests on every operation including the postcode match) and the unique name;
/// High for the range replace semantics, the two range forms, the country reference and the matcher.
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class GeographicalRegionAppServiceTests : MasterDataAppServiceTestBase
{
    private readonly IGeographicalRegionAppService _service;
    private readonly ICountryAppService _countries;

    public GeographicalRegionAppServiceTests()
    {
        _service = GetRequiredService<IGeographicalRegionAppService>();
        _countries = GetRequiredService<ICountryAppService>();
    }

    private Task<CountryDto> CountryAsync(string alpha2, string alpha3, string name)
        => _countries.CreateAsync(new CountryCreateDto { IsoAlpha2 = alpha2, IsoAlpha3 = alpha3, Names = Names(name) });

    private static GeographicalRegionPostalCodeRangeDto Numeric(Guid countryId, int from, int till, Guid? id = null)
        => new() { Id = id, CountryId = countryId, FromNumeric = from, TillNumeric = till };

    private static GeographicalRegionPostalCodeRangeDto Prefix(Guid countryId, string prefix, Guid? id = null)
        => new() { Id = id, CountryId = countryId, Prefix = prefix };

    private Task<GeographicalRegionDto> CreateAsync(string name, params GeographicalRegionPostalCodeRangeDto[] ranges)
        => _service.CreateAsync(new GeographicalRegionCreateDto { Name = name, PostalCodeRanges = [.. ranges] });

    private static GeographicalRegionUpdateDto UpdateFrom(GeographicalRegionDto dto) => new()
    {
        Name = dto.Name,
        Description = dto.Description,
        PostalCodeRanges = dto.PostalCodeRanges.Select(r => new GeographicalRegionPostalCodeRangeDto { Id = r.Id, CountryId = r.CountryId, FromNumeric = r.FromNumeric, TillNumeric = r.TillNumeric, Prefix = r.Prefix }).ToList(),
        ConcurrencyStamp = dto.ConcurrencyStamp
    };

    [Fact]
    public async Task Create_stores_the_region_with_its_ranges_and_round_trips()
    {
        using var _ = InTenantA();
        var nl = await CountryAsync("NL", "NLD", "Netherlands");
        var uk = await CountryAsync("GB", "GBR", "United Kingdom");

        var created = await _service.CreateAsync(new GeographicalRegionCreateDto
        {
            Name = " Randstad ",
            Description = "west",
            PostalCodeRanges = [Numeric(nl.Id, 1000, 1999), Numeric(nl.Id, 2000, 2999), Prefix(uk.Id, " sw ")]
        });

        created.Name.ShouldBe("Randstad");
        created.Description.ShouldBe("west");
        created.PostalCodeRanges.Count.ShouldBe(3);
        created.PostalCodeRanges.ShouldAllBe(r => r.Id != null);
        created.PostalCodeRanges.Select(r => r.CountryCode).ShouldBe(["GB", "NL", "NL"]);
        created.PostalCodeRanges.First().Prefix.ShouldBe("SW");
        created.PostalCodeRanges.First().FromNumeric.ShouldBeNull();
        created.PostalCodeRanges.Last().FromNumeric.ShouldBe(2000);

        (await _service.GetAsync(created.Id)).PostalCodeRanges.Count.ShouldBe(3);
        var row = (await _service.GetListAsync(List<GeographicalRegionListRequestDto>())).Rows.ShouldHaveSingleItem();
        row.Name.ShouldBe("Randstad");
        row.RangeCount.ShouldBe(3);
        var lookup = (await _service.GetLookupAsync()).ShouldHaveSingleItem();
        lookup.Name.ShouldBe("Randstad");
        lookup.Code.ShouldBeNull();
    }

    [Fact]
    public async Task An_invalid_range_form_is_refused_and_nothing_is_written()
    {
        using var _ = InTenantA();
        var nl = await CountryAsync("NL", "NLD", "Netherlands");

        var both = await Should.ThrowAsync<BusinessException>(() => CreateAsync("Bad", new GeographicalRegionPostalCodeRangeDto { CountryId = nl.Id, FromNumeric = 1000, TillNumeric = 1999, Prefix = "SW" }));
        both.Code.ShouldBe(MasterDataErrorCodes.InvalidPostalCodeRange);
        (await Should.ThrowAsync<BusinessException>(() => CreateAsync("Bad", Numeric(nl.Id, 2000, 1000)))).Code.ShouldBe(MasterDataErrorCodes.InvalidPostalCodeRange);
        (await Should.ThrowAsync<BusinessException>(() => CreateAsync("Bad", new GeographicalRegionPostalCodeRangeDto { CountryId = nl.Id, FromNumeric = 1000 }))).Code.ShouldBe(MasterDataErrorCodes.InvalidPostalCodeRange);
        (await Should.ThrowAsync<BusinessException>(() => CreateAsync("Bad", new GeographicalRegionPostalCodeRangeDto { CountryId = nl.Id }))).Code.ShouldBe(MasterDataErrorCodes.InvalidPostalCodeRange);
        await Should.ThrowAsync<AbpValidationException>(() => CreateAsync("Bad", Numeric(nl.Id, -1, 1000)));

        (await _service.GetListAsync(List<GeographicalRegionListRequestDto>())).TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task A_range_for_an_unknown_country_is_refused()
    {
        using var _ = InTenantA();

        var ex = await Should.ThrowAsync<BusinessException>(() => CreateAsync("Nowhere", Numeric(Guid.NewGuid(), 1000, 1999)));

        ex.Code.ShouldBe(MasterDataErrorCodes.ReferenceNotFound);
        ex.Data["field"].ShouldBe("postalCodeRanges.countryId");
    }

    [Fact]
    public async Task Ranges_are_replaced_on_update_keeping_the_ids_of_rows_that_stay()
    {
        using var _ = InTenantA();
        var nl = await CountryAsync("NL", "NLD", "Netherlands");
        var be = await CountryAsync("BE", "BEL", "Belgium");
        var region = await CreateAsync("Randstad", Numeric(nl.Id, 1000, 1999), Numeric(nl.Id, 2000, 2999));
        var first = region.PostalCodeRanges.Single(r => r.FromNumeric == 1000);
        var second = region.PostalCodeRanges.Single(r => r.FromNumeric == 2000);

        var update = UpdateFrom(region);
        update.PostalCodeRanges = [Numeric(nl.Id, 1000, 1499, first.Id), Prefix(be.Id, "10")];
        var updated = await _service.UpdateAsync(region.Id, update);

        updated.PostalCodeRanges.Count.ShouldBe(2);
        var kept = updated.PostalCodeRanges.Single(r => r.Id == first.Id);
        kept.TillNumeric.ShouldBe(1499);
        updated.PostalCodeRanges.ShouldNotContain(r => r.Id == second.Id);
        updated.PostalCodeRanges.Single(r => r.CountryId == be.Id).Prefix.ShouldBe("10");
        (await _service.GetListAsync(List<GeographicalRegionListRequestDto>())).Rows.Single().RangeCount.ShouldBe(2);

        var none = UpdateFrom(updated);
        none.PostalCodeRanges = [];
        (await _service.UpdateAsync(region.Id, none)).PostalCodeRanges.ShouldBeEmpty();
        /* With the ranges gone the country is free again. */
        await _countries.DeleteAsync(be.Id);
    }

    [Fact]
    public async Task Name_is_unique_per_tenant_freed_by_a_soft_delete_and_update_checks_the_stamp()
    {
        using var _ = InTenantA();
        var region = await CreateAsync("Randstad");
        await CreateAsync("Noord");

        (await Should.ThrowAsync<BusinessException>(() => CreateAsync(" Randstad "))).Code.ShouldBe(MasterDataErrorCodes.DuplicateKey);

        var rename = UpdateFrom(region);
        rename.Name = "West";
        (await _service.UpdateAsync(region.Id, rename)).Name.ShouldBe("West");
        var clash = UpdateFrom(await _service.GetAsync(region.Id));
        clash.Name = "Noord";
        (await Should.ThrowAsync<BusinessException>(() => _service.UpdateAsync(region.Id, clash))).Code.ShouldBe(MasterDataErrorCodes.DuplicateKey);
        await Should.ThrowAsync<AbpDbConcurrencyException>(() => _service.UpdateAsync(region.Id, UpdateFrom(region)));

        await _service.DeleteAsync(region.Id);
        (await CreateAsync("West")).Id.ShouldNotBe(region.Id);
    }

    [Fact]
    public async Task GetMatches_returns_the_regions_whose_ranges_match_in_the_given_country_only()
    {
        using var _ = InTenantA();
        using var culture = UiCulture("en");
        var nl = await CountryAsync("NL", "NLD", "Netherlands");
        var uk = await CountryAsync("GB", "GBR", "United Kingdom");
        var de = await CountryAsync("DE", "DEU", "Germany");
        var randstad = await CreateAsync("Randstad", Numeric(nl.Id, 1000, 1999), Numeric(nl.Id, 2000, 2999));
        var amsterdam = await CreateAsync("Amsterdam", Numeric(nl.Id, 1000, 1099));
        var london = await CreateAsync("London", Prefix(uk.Id, "SW"), Prefix(uk.Id, "EC"));
        var ruhr = await CreateAsync("Ruhr", Numeric(de.Id, 40000, 49999));

        (await _service.GetMatchesAsync(nl.Id, "1234 AB")).Select(r => r.Name).ShouldBe(["Randstad"]);
        (await _service.GetMatchesAsync(nl.Id, "1050aa")).Select(r => r.Name).ShouldBe(["Amsterdam", "Randstad"]);
        (await _service.GetMatchesAsync(nl.Id, "3000 AA")).ShouldBeEmpty();
        (await _service.GetMatchesAsync(nl.Id, "SW1A 1AA")).ShouldBeEmpty();
        (await _service.GetMatchesAsync(uk.Id, "sw1a 1aa")).Select(r => r.Name).ShouldBe(["London"]);
        (await _service.GetMatchesAsync(uk.Id, "EC2A 1NT")).Select(r => r.Name).ShouldBe(["London"]);
        (await _service.GetMatchesAsync(uk.Id, "W1A 1AA")).ShouldBeEmpty();
        /* A range of another country is ignored: 1234 AB is in no German range and 40001 in no Dutch one. */
        (await _service.GetMatchesAsync(de.Id, "1234 AB")).ShouldBeEmpty();
        (await _service.GetMatchesAsync(de.Id, "40001")).Select(r => r.Name).ShouldBe(["Ruhr"]);
        (await _service.GetMatchesAsync(nl.Id, "40001")).ShouldBeEmpty();
        (await _service.GetMatchesAsync(nl.Id, "")).ShouldBeEmpty();
        (await _service.GetMatchesAsync(nl.Id, "   ")).ShouldBeEmpty();
        (await _service.GetMatchesAsync(nl.Id, new string('1', 17))).ShouldBeEmpty();
        (await _service.GetMatchesAsync(Guid.NewGuid(), "1234 AB")).ShouldBeEmpty();

        var match = (await _service.GetMatchesAsync(nl.Id, "1234AB")).Single();
        match.Id.ShouldBe(randstad.Id);
        match.Code.ShouldBeNull();
        amsterdam.Id.ShouldNotBe(Guid.Empty);
        london.Id.ShouldNotBe(Guid.Empty);
        ruhr.Id.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task List_filters_on_country_searches_name_and_description_and_sorts_on_the_whitelist()
    {
        using var _ = InTenantA();
        var nl = await CountryAsync("NL", "NLD", "Netherlands");
        var uk = await CountryAsync("GB", "GBR", "United Kingdom");
        await CreateAsync("Randstad", Numeric(nl.Id, 1000, 1999), Numeric(nl.Id, 2000, 2999));
        await CreateAsync("London", Prefix(uk.Id, "SW"));
        await _service.CreateAsync(new GeographicalRegionCreateDto { Name = "Empty", Description = "no ranges yet" });

        (await _service.GetListAsync(List<GeographicalRegionListRequestDto>())).Rows.Select(r => r.Name).ShouldBe(["Empty", "London", "Randstad"]);
        (await _service.GetListAsync(List<GeographicalRegionListRequestDto>(sortBy: "rangeCount", sortDir: "desc"))).Rows.Select(r => r.RangeCount).ShouldBe([2, 1, 0]);
        (await _service.GetListAsync(List<GeographicalRegionListRequestDto>(sortBy: "description"))).Rows.Count.ShouldBe(3);

        var dutch = await _service.GetListAsync(new GeographicalRegionListRequestDto { CountryId = nl.Id });
        dutch.TotalCount.ShouldBe(1);
        dutch.Rows.ShouldHaveSingleItem().Name.ShouldBe("Randstad");
        (await _service.GetListAsync(new GeographicalRegionListRequestDto { CountryId = Guid.NewGuid() })).TotalCount.ShouldBe(0);

        (await _service.GetListAsync(List<GeographicalRegionListRequestDto>(search: "ranges"))).Rows.ShouldHaveSingleItem().Name.ShouldBe("Empty");
        (await _service.GetListAsync(List<GeographicalRegionListRequestDto>(search: "LON"))).Rows.ShouldHaveSingleItem().Name.ShouldBe("London");
        (await _service.GetLookupAsync("rand")).ShouldHaveSingleItem().Name.ShouldBe("Randstad");

        (await Should.ThrowAsync<BusinessException>(() => _service.GetListAsync(List<GeographicalRegionListRequestDto>(sortBy: "postalCodeRanges")))).Code.ShouldBe(MasterDataErrorCodes.UnknownSortColumn);
    }

    [Fact]
    public async Task Deleting_a_region_clears_its_ranges_so_the_country_can_go_and_bulk_delete_is_idempotent()
    {
        using var _ = InTenantA();
        var nl = await CountryAsync("NL", "NLD", "Netherlands");
        var randstad = await CreateAsync("Randstad", Numeric(nl.Id, 1000, 1999));
        var noord = await CreateAsync("Noord", Numeric(nl.Id, 7000, 9999));
        var zuid = await CreateAsync("Zuid", Numeric(nl.Id, 4000, 6999));

        await _service.DeleteAsync(randstad.Id);
        (await _service.GetMatchesAsync(nl.Id, "1234 AB")).ShouldBeEmpty();

        var request = new GeographicalRegionBulkDeleteRequestDto { Selection = Explicit(noord.Id, zuid.Id) };
        (await _service.BulkDeleteAsync(request)).DeletedCount.ShouldBe(2);
        (await _service.BulkDeleteAsync(request)).DeletedCount.ShouldBe(0);
        (await _service.GetListAsync(List<GeographicalRegionListRequestDto>())).TotalCount.ShouldBe(0);
        (await _service.GetMatchesAsync(nl.Id, "8000 AA")).ShouldBeEmpty();

        await _countries.DeleteAsync(nl.Id);
    }

    /// <summary>
    /// Design 41, "Delete many": each row goes through the same checks as DeleteAsync, and a region's
    /// ranges are cleared before the soft delete so they stop blocking their country.
    /// </summary>
    [Fact]
    public async Task Filter_based_bulk_delete_clears_the_ranges_like_a_single_delete_does()
    {
        using var _ = InTenantA();
        var nl = await CountryAsync("NL", "NLD", "Netherlands");
        await CreateAsync("Noord", Numeric(nl.Id, 7000, 9999));
        await CreateAsync("Zuid", Numeric(nl.Id, 4000, 6999));

        (await _service.BulkDeleteAsync(new GeographicalRegionBulkDeleteRequestDto { Selection = FilterBased(), CountryId = nl.Id })).DeletedCount.ShouldBe(2);
        (await _service.GetListAsync(List<GeographicalRegionListRequestDto>())).TotalCount.ShouldBe(0);

        await _countries.DeleteAsync(nl.Id);
        await Should.ThrowAsync<EntityNotFoundException>(() => _countries.GetAsync(nl.Id));
    }

    [Fact]
    public async Task BulkDelete_filter_based_honours_the_country_filter_the_search_and_the_exclusions()
    {
        using var _ = InTenantA();
        var nl = await CountryAsync("NL", "NLD", "Netherlands");
        var uk = await CountryAsync("GB", "GBR", "United Kingdom");
        var randstad = await CreateAsync("Randstad", Numeric(nl.Id, 1000, 1999));
        var noord = await CreateAsync("Noord", Numeric(nl.Id, 7000, 9999));
        var london = await CreateAsync("London", Prefix(uk.Id, "SW"));

        var result = await _service.BulkDeleteAsync(new GeographicalRegionBulkDeleteRequestDto { Selection = FilterBased("n", noord.Id), CountryId = nl.Id });

        result.DeletedCount.ShouldBe(1);
        (await _service.GetListAsync(List<GeographicalRegionListRequestDto>())).Rows.Select(r => r.Id).ShouldBe([london.Id, noord.Id]);
        randstad.Id.ShouldNotBe(Guid.Empty);
    }

    // ── Tenant isolation (Critical, mandatory) ────────────────────────────────

    [Fact]
    public async Task Rows_of_tenant_A_are_invisible_and_untouchable_from_tenant_B_including_the_match()
    {
        CountryDto nl;
        GeographicalRegionDto randstad;
        using (InTenantA())
        {
            nl = await CountryAsync("NL", "NLD", "Netherlands");
            randstad = await CreateAsync("Randstad", Numeric(nl.Id, 1000, 1999));
        }

        using (InTenantB())
        {
            (await _service.GetListAsync(List<GeographicalRegionListRequestDto>())).TotalCount.ShouldBe(0);
            (await _service.GetListAsync(new GeographicalRegionListRequestDto { CountryId = nl.Id })).TotalCount.ShouldBe(0);
            (await _service.GetLookupAsync()).ShouldBeEmpty();
            (await _service.GetMatchesAsync(nl.Id, "1234 AB")).ShouldBeEmpty();
            await Should.ThrowAsync<EntityNotFoundException>(() => _service.GetAsync(randstad.Id));
            await Should.ThrowAsync<EntityNotFoundException>(() => _service.UpdateAsync(randstad.Id, UpdateFrom(randstad)));
            await Should.ThrowAsync<EntityNotFoundException>(() => _service.DeleteAsync(randstad.Id));
            (await _service.BulkDeleteAsync(new GeographicalRegionBulkDeleteRequestDto { Selection = Explicit(randstad.Id) })).DeletedCount.ShouldBe(0);
            (await _service.BulkDeleteAsync(new GeographicalRegionBulkDeleteRequestDto { Selection = FilterBased() })).DeletedCount.ShouldBe(0);

            /* A range may not point at a country of tenant A. */
            var ex = await Should.ThrowAsync<BusinessException>(() => CreateAsync("Randstad B", Numeric(nl.Id, 1000, 1999)));
            ex.Code.ShouldBe(MasterDataErrorCodes.ReferenceNotFound);
            ex.Data["field"].ShouldBe("postalCodeRanges.countryId");

            (await CreateAsync("Randstad")).Id.ShouldNotBe(randstad.Id);
        }

        using (InTenantA())
        {
            (await _service.GetListAsync(List<GeographicalRegionListRequestDto>())).Rows.ShouldHaveSingleItem().Id.ShouldBe(randstad.Id);
            (await _service.GetMatchesAsync(nl.Id, "1234 AB")).ShouldHaveSingleItem().Id.ShouldBe(randstad.Id);
        }
    }
}
