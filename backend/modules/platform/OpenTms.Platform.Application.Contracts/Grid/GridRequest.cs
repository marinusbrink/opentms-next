using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace OpenTms.Platform.Grid;

public record GridRequest : IValidatableObject
{
    [Range(0, int.MaxValue)]
    public int StartRow { get; init; }

    public int EndRow { get; init; }

    public List<SortModel> SortModels { get; init; } = new();

    public Dictionary<string, ColumnFilterModel> ColumnFilters { get; init; } = new();

    [MaxLength(200)]
    public string? WildcardSearch { get; init; }

    public List<string> RowGroupCols { get; init; } = new();

    public List<string> GroupKeys { get; init; } = new();

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (EndRow <= StartRow)
            yield return new ValidationResult("EndRow must be greater than StartRow.", [nameof(EndRow)]);
        else if (EndRow - StartRow > 200)
            yield return new ValidationResult("EndRow − StartRow must not exceed 200.", [nameof(EndRow)]);

        if (RowGroupCols.Count > 10)
            yield return new ValidationResult("RowGroupCols may contain at most 10 elements.", [nameof(RowGroupCols)]);

        if (GroupKeys.Count > 10)
            yield return new ValidationResult("GroupKeys may contain at most 10 elements.", [nameof(GroupKeys)]);
    }
}
