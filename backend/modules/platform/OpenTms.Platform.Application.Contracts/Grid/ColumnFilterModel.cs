using System.ComponentModel.DataAnnotations;

namespace OpenTms.Platform.Grid;

public record ColumnFilterModel
{
    [Required]
    public string FilterType { get; init; } = default!;

    [Required]
    public string Type { get; init; } = default!;

    [MaxLength(500)]
    public string? Filter { get; init; }

    [MaxLength(500)]
    public string? FilterTo { get; init; }
}
