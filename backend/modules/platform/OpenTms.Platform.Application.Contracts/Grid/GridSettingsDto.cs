using System.Collections.Generic;

namespace OpenTms.Platform.Grid;

public record GridSettingsDto
{
    public List<ColumnStateDto> ColumnStates { get; init; } = new();
    public List<SortModel> SortModels { get; init; } = new();
}
