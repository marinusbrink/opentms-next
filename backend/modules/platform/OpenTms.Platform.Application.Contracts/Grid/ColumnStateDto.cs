using System.ComponentModel.DataAnnotations;

namespace OpenTms.Platform.Grid;

public record ColumnStateDto
{
    [Required]
    public string ColId { get; init; } = default!;

    public bool Visible { get; init; }

    public int Order { get; init; }

    public int? Width { get; init; }
}
