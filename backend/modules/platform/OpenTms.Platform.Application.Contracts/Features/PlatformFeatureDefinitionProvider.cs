using OpenTms.Platform.Localization;
using Volo.Abp.Features;
using Volo.Abp.Localization;
using Volo.Abp.Validation.StringValues;

namespace OpenTms.Platform.Features;

public class PlatformFeatureDefinitionProvider : FeatureDefinitionProvider
{
    public override void Define(IFeatureDefinitionContext context)
    {
        var group = context.AddGroup(PlatformFeatures.GroupName, L("Feature:Platform"));

        group.AddFeature(
            PlatformFeatures.Maintenance,
            defaultValue: "false",
            displayName: L("Feature:Maintenance"),
            description: L("Feature:Maintenance.Description"),
            valueType: new ToggleStringValueType());
    }

    private static LocalizableString L(string name) => LocalizableString.Create<PlatformResource>(name);
}
