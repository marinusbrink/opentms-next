using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using OpenTms.Platform.Grid;
using Shouldly;
using Xunit;

namespace OpenTms.Platform.Grid;

/// <summary>
/// Pure unit tests for GridRequest DTO validation.
/// No DI or database required — tests platform contract constraints directly.
/// Risk class: Critical (Platform contract; a breaking field change breaks every future consumer).
/// </summary>
public class GridRequestValidationTests
{
    private static IList<ValidationResult> Validate(object obj)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(obj, new ValidationContext(obj), results, validateAllProperties: true);
        return results;
    }

    // ── StartRow / EndRow ────────────────────────────────────────────────────

    [Fact]
    public void Valid_block_request_passes_all_validation()
    {
        var req = new GridRequest { StartRow = 0, EndRow = 100 };
        Validate(req).ShouldBeEmpty();
    }

    [Fact]
    public void StartRow_negative_fails_Range_validation()
    {
        var req = new GridRequest { StartRow = -1, EndRow = 50 };
        var results = Validate(req);
        results.ShouldContain(r => r.MemberNames.Contains(nameof(GridRequest.StartRow)));
    }

    [Fact]
    public void EndRow_equal_to_StartRow_fails_IValidatableObject()
    {
        var req = new GridRequest { StartRow = 10, EndRow = 10 };
        var results = Validate(req);
        results.ShouldContain(r => r.MemberNames.Contains(nameof(GridRequest.EndRow)));
    }

    [Fact]
    public void EndRow_less_than_StartRow_fails_IValidatableObject()
    {
        var req = new GridRequest { StartRow = 50, EndRow = 10 };
        var results = Validate(req);
        results.ShouldContain(r => r.MemberNames.Contains(nameof(GridRequest.EndRow)));
    }

    [Fact]
    public void EndRow_minus_StartRow_exactly_200_is_valid()
    {
        var req = new GridRequest { StartRow = 0, EndRow = 200 };
        Validate(req).ShouldBeEmpty();
    }

    [Fact]
    public void EndRow_minus_StartRow_of_201_fails_IValidatableObject()
    {
        var req = new GridRequest { StartRow = 0, EndRow = 201 };
        var results = Validate(req);
        results.ShouldContain(r => r.MemberNames.Contains(nameof(GridRequest.EndRow)));
    }

    [Fact]
    public void EndRow_minus_StartRow_200_at_nonzero_StartRow_is_valid()
    {
        var req = new GridRequest { StartRow = 100, EndRow = 300 };
        Validate(req).ShouldBeEmpty();
    }

    // ── WildcardSearch ────────────────────────────────────────────────────────

    [Fact]
    public void WildcardSearch_null_is_valid()
    {
        var req = new GridRequest { StartRow = 0, EndRow = 10, WildcardSearch = null };
        Validate(req).ShouldBeEmpty();
    }

    [Fact]
    public void WildcardSearch_200_chars_is_valid()
    {
        var req = new GridRequest { StartRow = 0, EndRow = 10, WildcardSearch = new string('x', 200) };
        Validate(req).ShouldBeEmpty();
    }

    [Fact]
    public void WildcardSearch_201_chars_fails_MaxLength()
    {
        var req = new GridRequest { StartRow = 0, EndRow = 10, WildcardSearch = new string('x', 201) };
        var results = Validate(req);
        results.ShouldContain(r => r.MemberNames.Contains(nameof(GridRequest.WildcardSearch)));
    }

    // ── RowGroupCols / GroupKeys ──────────────────────────────────────────────

    [Fact]
    public void RowGroupCols_10_elements_is_valid()
    {
        var req = new GridRequest
        {
            StartRow = 0, EndRow = 10,
            RowGroupCols = Enumerable.Repeat("col", 10).ToList()
        };
        Validate(req).ShouldBeEmpty();
    }

    [Fact]
    public void RowGroupCols_11_elements_fails_IValidatableObject()
    {
        var req = new GridRequest
        {
            StartRow = 0, EndRow = 10,
            RowGroupCols = Enumerable.Repeat("col", 11).ToList()
        };
        var results = Validate(req);
        results.ShouldContain(r => r.MemberNames.Contains(nameof(GridRequest.RowGroupCols)));
    }

    [Fact]
    public void GroupKeys_10_elements_is_valid()
    {
        var req = new GridRequest
        {
            StartRow = 0, EndRow = 10,
            GroupKeys = Enumerable.Repeat("key", 10).ToList()
        };
        Validate(req).ShouldBeEmpty();
    }

    [Fact]
    public void GroupKeys_11_elements_fails_IValidatableObject()
    {
        var req = new GridRequest
        {
            StartRow = 0, EndRow = 10,
            GroupKeys = Enumerable.Repeat("key", 11).ToList()
        };
        var results = Validate(req);
        results.ShouldContain(r => r.MemberNames.Contains(nameof(GridRequest.GroupKeys)));
    }

    // ── ColumnFilterModel ─────────────────────────────────────────────────────

    [Fact]
    public void ColumnFilterModel_filter_500_chars_is_valid()
    {
        var model = new ColumnFilterModel { FilterType = "text", Type = "contains", Filter = new string('a', 500) };
        Validate(model).ShouldBeEmpty();
    }

    [Fact]
    public void ColumnFilterModel_filter_501_chars_fails_MaxLength()
    {
        var model = new ColumnFilterModel { FilterType = "text", Type = "contains", Filter = new string('a', 501) };
        var results = Validate(model);
        results.ShouldContain(r => r.MemberNames.Contains(nameof(ColumnFilterModel.Filter)));
    }

    [Fact]
    public void ColumnFilterModel_filterTo_501_chars_fails_MaxLength()
    {
        var model = new ColumnFilterModel { FilterType = "text", Type = "between", FilterTo = new string('a', 501) };
        var results = Validate(model);
        results.ShouldContain(r => r.MemberNames.Contains(nameof(ColumnFilterModel.FilterTo)));
    }
}
