using OpenTms.Platform.Grid;

namespace OpenTms.Platform.Administration;

public record BulkDeleteRolesRequestDto
{
    public GridSelectionDto Selection { get; init; } = new();
}
