using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using OpenTms.MasterData.Reference;
using OpenTms.MasterData.Regions;
using OpenTms.MasterData.Transport;
using Shouldly;
using Xunit;

namespace OpenTms.MasterData;

/// <summary>
/// Pure unit tests for the Master Data list contract (design 41, "List request"): paging bounds,
/// search length and the sort direction. Risk class: High — the request is bound from the query
/// string on every list; an unbounded page is the classic way to pull a whole tenant table.
/// </summary>
public class MasterDataListRequestValidationTests
{
    private static IList<ValidationResult> Validate(object dto)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(dto, new ValidationContext(dto), results, validateAllProperties: true);
        return results;
    }

    [Fact]
    public void Defaults_are_a_valid_first_page_of_one_hundred_rows_ascending()
    {
        var request = new MasterDataListRequestDto();

        request.StartRow.ShouldBe(0);
        request.EndRow.ShouldBe(100);
        request.Descending.ShouldBeFalse();
        Validate(request).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(10, 10)]
    [InlineData(50, 10)]
    [InlineData(0, 0)]
    public void EndRow_at_or_below_StartRow_fails(int start, int end)
    {
        Validate(new MasterDataListRequestDto { StartRow = start, EndRow = end })
            .ShouldContain(r => r.MemberNames.Contains(nameof(MasterDataListRequestDto.EndRow)));
    }

    [Fact]
    public void A_page_of_exactly_two_hundred_rows_is_valid_and_two_hundred_and_one_is_not()
    {
        Validate(new MasterDataListRequestDto { StartRow = 100, EndRow = 300 }).ShouldBeEmpty();
        Validate(new MasterDataListRequestDto { StartRow = 0, EndRow = 201 })
            .ShouldContain(r => r.MemberNames.Contains(nameof(MasterDataListRequestDto.EndRow)));
    }

    [Fact]
    public void A_negative_StartRow_fails()
    {
        Validate(new MasterDataListRequestDto { StartRow = -1, EndRow = 10 })
            .ShouldContain(r => r.MemberNames.Contains(nameof(MasterDataListRequestDto.StartRow)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("asc")]
    [InlineData("ASC")]
    [InlineData("desc")]
    [InlineData("Desc")]
    public void SortDir_accepts_asc_and_desc_in_any_case_or_nothing(string? sortDir)
    {
        Validate(new MasterDataListRequestDto { SortDir = sortDir }).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("up")]
    [InlineData("")]
    [InlineData("down")]
    public void SortDir_rejects_anything_else(string sortDir)
    {
        Validate(new MasterDataListRequestDto { SortDir = sortDir })
            .ShouldContain(r => r.MemberNames.Contains(nameof(MasterDataListRequestDto.SortDir)));
    }

    [Fact]
    public void Descending_is_true_only_for_desc()
    {
        new MasterDataListRequestDto { SortDir = "desc" }.Descending.ShouldBeTrue();
        new MasterDataListRequestDto { SortDir = "DESC" }.Descending.ShouldBeTrue();
        new MasterDataListRequestDto { SortDir = "asc" }.Descending.ShouldBeFalse();
        new MasterDataListRequestDto().Descending.ShouldBeFalse();
    }

    [Fact]
    public void WildcardSearch_is_bounded_to_two_hundred_characters()
    {
        Validate(new MasterDataListRequestDto { WildcardSearch = new string('x', MasterDataConsts.MaxSearchLength) }).ShouldBeEmpty();
        Validate(new MasterDataListRequestDto { WildcardSearch = new string('x', MasterDataConsts.MaxSearchLength + 1) })
            .ShouldContain(r => r.MemberNames.Contains(nameof(MasterDataListRequestDto.WildcardSearch)));
    }

    [Fact]
    public void SortBy_is_bounded_to_sixty_four_characters()
    {
        Validate(new MasterDataListRequestDto { SortBy = new string('s', 65) })
            .ShouldContain(r => r.MemberNames.Contains(nameof(MasterDataListRequestDto.SortBy)));
    }

    [Fact]
    public void Entity_requests_inherit_the_same_rules()
    {
        Validate(new CurrencyListRequestDto { StartRow = 5, EndRow = 5, IsActive = true }).ShouldNotBeEmpty();
        Validate(new ArchivableListRequestDto { StartRow = 0, EndRow = 201, IncludeArchived = true }).ShouldNotBeEmpty();
        Validate(new GeographicalRegionListRequestDto { SortDir = "sideways" }).ShouldNotBeEmpty();
        Validate(new CurrencyListRequestDto { IsActive = false }).ShouldBeEmpty();
    }

    [Fact]
    public void Create_dtos_validate_iso_codes_and_bounds_by_annotation()
    {
        Validate(new CurrencyCreateDto { IsoCode = "EURO", Names = new LocalizedNameDto { ["en"] = "Euro" } })
            .ShouldContain(r => r.MemberNames.Contains(nameof(CurrencyCreateDto.IsoCode)));
        Validate(new CurrencyCreateDto { IsoCode = "EUR", MinorUnits = 5, Names = new LocalizedNameDto { ["en"] = "Euro" } })
            .ShouldContain(r => r.MemberNames.Contains(nameof(CurrencyCreateDto.MinorUnits)));
        Validate(new CurrencyCreateDto { IsoCode = "eur", Names = new LocalizedNameDto { ["en"] = "Euro" } }).ShouldBeEmpty();

        Validate(new CountryCreateDto { IsoAlpha2 = "NLD", IsoAlpha3 = "NLD", Names = new LocalizedNameDto { ["en"] = "NL" } })
            .ShouldContain(r => r.MemberNames.Contains(nameof(CountryCreateDto.IsoAlpha2)));
        Validate(new CountryCreateDto { IsoAlpha2 = "NL", IsoAlpha3 = "NL", IsoNumeric = 1000, Names = new LocalizedNameDto { ["en"] = "NL" } })
            .ShouldContain(r => r.MemberNames.Contains(nameof(CountryCreateDto.IsoAlpha3)));

        Validate(new ProductCreateDto { Name = "", DefaultWeightKg = -1 })
            .Select(r => r.MemberNames.First())
            .ShouldBe([nameof(ProductCreateDto.Name), nameof(ProductCreateDto.DefaultWeightKg)], ignoreOrder: true);
        Validate(new ProductCreateDto { Name = "Pallet", AdrUnNumber = "12345" })
            .ShouldContain(r => r.MemberNames.Contains(nameof(ProductCreateDto.AdrUnNumber)));

        Validate(new GeographicalRegionPostalCodeRangeDto { FromNumeric = -1, Prefix = new string('p', 17) })
            .Select(r => r.MemberNames.First())
            .ShouldBe([nameof(GeographicalRegionPostalCodeRangeDto.FromNumeric), nameof(GeographicalRegionPostalCodeRangeDto.Prefix)], ignoreOrder: true);
    }
}
