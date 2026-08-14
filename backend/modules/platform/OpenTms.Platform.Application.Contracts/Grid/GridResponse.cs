using System.Collections.Generic;

namespace OpenTms.Platform.Grid;

public record GridResponse<T>
{
    public List<T> Rows { get; init; } = new();
    public long TotalCount { get; init; }
    public long FilteredCount { get; init; }
}
