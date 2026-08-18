using System.Collections.Generic;

namespace OpenTms.Platform.Administration;

public record BulkDeleteUsersResponseDto
{
    public int DeletedCount { get; init; }
    public List<SkippedRowDto> SkippedRows { get; init; } = new();
}
