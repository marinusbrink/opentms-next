using OpenTms.MasterData.Localization;
using Volo.Abp.Features;
using Volo.Abp.Localization;
using Volo.Abp.Validation.StringValues;

namespace OpenTms.MasterData.Features;

public class MasterDataFeatureDefinitionProvider : FeatureDefinitionProvider
{
    public override void Define(IFeatureDefinitionContext context)
    {
        var group = context.AddGroup(MasterDataFeatures.GroupName, L("Feature:MasterData"));

        group.AddFeature(
            MasterDataFeatures.FirstRun,
            defaultValue: "false",
            displayName: L("Feature:FirstRun"),
            description: L("Feature:FirstRun.Description"),
            valueType: new ToggleStringValueType());

        group.AddFeature(
            MasterDataFeatures.Relations,
            defaultValue: "false",
            displayName: L("Feature:Relations"),
            description: L("Feature:Relations.Description"),
            valueType: new ToggleStringValueType());
    }

    private static LocalizableString L(string name) => LocalizableString.Create<MasterDataResource>(name);
}
