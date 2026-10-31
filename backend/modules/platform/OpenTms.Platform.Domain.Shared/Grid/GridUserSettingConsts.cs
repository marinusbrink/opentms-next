namespace OpenTms.Platform.Grid;

public static class GridUserSettingConsts
{
    /// <summary>Matches the gridId validation pattern of design #6 (^[a-zA-Z0-9_.-]{1,100}$).</summary>
    public const int MaxGridIdLength = 100;

    /// <summary>The largest free-form view state (design 66, the Planboard layout and pane settings), in characters of JSON.</summary>
    public const int MaxViewStateLength = 65_536;
}
