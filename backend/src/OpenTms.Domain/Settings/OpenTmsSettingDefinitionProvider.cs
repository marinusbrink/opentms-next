using Volo.Abp.Settings;

namespace OpenTms.Settings;

public class OpenTmsSettingDefinitionProvider : SettingDefinitionProvider
{
    public override void Define(ISettingDefinitionContext context)
    {
        //Define your own settings here. Example:
        //context.Add(new SettingDefinition(OpenTmsSettings.MySetting1));
    }
}
