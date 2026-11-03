using OpenTms.Integrations.Localization;
using Volo.Abp.Features;
using Volo.Abp.Localization;
using Volo.Abp.Validation.StringValues;

namespace OpenTms.Integrations.Features;

public class IntegrationsFeatureDefinitionProvider : FeatureDefinitionProvider
{
    public override void Define(IFeatureDefinitionContext context)
    {
        var group = context.AddGroup(IntegrationsFeatures.GroupName, L("Feature:Integrations"));
        group.AddFeature(
            IntegrationsFeatures.AddressLookup,
            defaultValue: "false",
            displayName: L("Feature:AddressLookup"),
            description: L("Feature:AddressLookup.Description"),
            valueType: new ToggleStringValueType());
        /* Design 72: order intake and, beneath it, AI mapping; both off until a tenant switches them on (rule 4). */
        var orderIntake = group.AddFeature(
            IntegrationsFeatures.OrderIntake,
            defaultValue: "false",
            displayName: L("Feature:OrderIntake"),
            description: L("Feature:OrderIntake.Description"),
            valueType: new ToggleStringValueType());
        orderIntake.CreateChild(
            IntegrationsFeatures.OrderIntakeAiMapping,
            defaultValue: "false",
            displayName: L("Feature:OrderIntake.AiMapping"),
            description: L("Feature:OrderIntake.AiMapping.Description"),
            valueType: new ToggleStringValueType());
    }

    private static LocalizableString L(string name) => LocalizableString.Create<IntegrationsResource>(name);
}
