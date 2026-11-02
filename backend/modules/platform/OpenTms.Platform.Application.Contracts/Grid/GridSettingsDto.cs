using System.Collections.Generic;
using System.Text.Json;

namespace OpenTms.Platform.Grid;

public record GridSettingsDto
{
    public List<ColumnStateDto> ColumnStates { get; init; } = new();
    public List<SortModel> SortModels { get; init; } = new();

    /// <summary>
    /// Free-form view state of a bespoke surface that is not a grid (design 66: the Planboard's dockview layout under
    /// <c>planboard.layout</c> and each pane's settings under <c>planboard.pane.&lt;instanceId&gt;</c>). Stored as given,
    /// at most <see cref="GridUserSettingConsts.MaxViewStateLength"/> characters of JSON. Null for plain grids.
    /// </summary>
    public JsonElement? ViewState { get; init; }
}
