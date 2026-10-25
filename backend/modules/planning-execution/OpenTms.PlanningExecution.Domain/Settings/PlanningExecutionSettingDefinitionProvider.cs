using OpenTms.PlanningExecution.Settings;
using Volo.Abp.Settings;

namespace OpenTms.PlanningExecution.Settings;

/// <summary>The tenant settings of Planning &amp; Execution (design 59 "Calculators": the speed correction of routed durations).</summary>
public class PlanningExecutionSettingDefinitionProvider : SettingDefinitionProvider
{
    public override void Define(ISettingDefinitionContext context)
    {
        context.Add(new SettingDefinition(
            PlanningExecutionSettings.SpeedCorrectionPercent,
            PlanningExecutionConsts.DefaultSpeedCorrectionPercent.ToString(),
            isVisibleToClients: false));
    }
}
