using OpenTms.Platform.Grid;

namespace OpenTms.Platform.Administration;

public record BulkDeleteUsersRequestDto
{
    public GridSelectionDto Selection { get; init; } = new();
}
