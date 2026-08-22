using Volo.Abp.Features;
using Volo.Abp.Validation.StringValues;

namespace OpenTms.Platform.Features;

public class PlatformFeatureDefinitionProvider : FeatureDefinitionProvider
{
    public override void Define(IFeatureDefinitionContext context)
    {
        var group = context.AddGroup(PlatformFeatures.GroupName, displayName: null);

        group.AddFeature(
            PlatformFeatures.CommonToolbar,
            defaultValue: "false",
            valueType: new ToggleStringValueType());
    }
}
