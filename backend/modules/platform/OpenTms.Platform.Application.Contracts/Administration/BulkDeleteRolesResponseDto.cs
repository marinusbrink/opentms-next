using System.Collections.Generic;

namespace OpenTms.Platform.Administration;

public record BulkDeleteRolesResponseDto
{
    public int DeletedCount { get; init; }
    public List<SkippedRoleDto> SkippedRows { get; init; } = new();
}
