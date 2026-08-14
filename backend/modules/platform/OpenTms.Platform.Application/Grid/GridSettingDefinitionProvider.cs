using Volo.Abp.Settings;

namespace OpenTms.Platform.Grid;

public class GridSettingDefinitionProvider : SettingDefinitionProvider
{
    public override void Define(ISettingDefinitionContext context)
    {
        context.Add(
            new SettingDefinition(PlatformSettings.GridSettings, isVisibleToClients: false)
        );
    }
}
