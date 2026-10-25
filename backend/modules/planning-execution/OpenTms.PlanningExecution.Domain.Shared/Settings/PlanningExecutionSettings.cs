namespace OpenTms.PlanningExecution.Settings;

/// <summary>The tenant settings of Planning &amp; Execution (design 59 "Calculators").</summary>
public static class PlanningExecutionSettings
{
    /// <summary>Routed durations are multiplied by this percentage (OpenTMS's speed correction; default 120).</summary>
    public const string SpeedCorrectionPercent = "Planning:SpeedCorrectionPercent";
}
