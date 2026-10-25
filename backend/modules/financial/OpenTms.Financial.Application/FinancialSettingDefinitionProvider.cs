using Volo.Abp.Settings;

namespace OpenTms.Financial;

/// <summary>The Financial settings (design 57): the own-fleet cost per kilometre, tenant scope, no default (Assumption 5).</summary>
public class FinancialSettingDefinitionProvider : SettingDefinitionProvider
{
    public override void Define(ISettingDefinitionContext context)
    {
        context.Add(new SettingDefinition(FinancialSettings.OwnFleetCostPerKm, isVisibleToClients: false));
    }
}
