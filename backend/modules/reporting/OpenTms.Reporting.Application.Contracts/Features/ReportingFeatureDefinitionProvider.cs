using OpenTms.Reporting.Localization;
using Volo.Abp.Features;
using Volo.Abp.Localization;
using Volo.Abp.Validation.StringValues;

namespace OpenTms.Reporting.Features;

/// <summary>Design 75: off per tenant until the host switches it on (constitution rule 4).</summary>
public class ReportingFeatureDefinitionProvider : FeatureDefinitionProvider
{
    public override void Define(IFeatureDefinitionContext context)
    {
        var group = context.AddGroup(ReportingFeatures.GroupName, L("Feature:Reporting"));
        group.AddFeature(
            ReportingFeatures.Metrics,
            defaultValue: "false",
            displayName: L("Feature:Metrics"),
            description: L("Feature:Metrics.Description"),
            valueType: new ToggleStringValueType());
    }

    private static LocalizableString L(string name) => LocalizableString.Create<ReportingResource>(name);
}
