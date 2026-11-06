using OpenTms.Reporting.Localization;
using Volo.Abp.Localization;
using Volo.Abp.Settings;

namespace OpenTms.Reporting.Settings;

/// <summary>The fact workers' state per tenant (design 75 "Migration strategy"): only ever a tenant's own value, no screen.</summary>
public class ReportingSettingDefinitionProvider : SettingDefinitionProvider
{
    public override void Define(ISettingDefinitionContext context)
    {
        Add(context, ReportingSettings.BackfillCompletedAt, "Reporting:Setting.BackfillCompletedAt");
        Add(context, ReportingSettings.BackfillPosition, "Reporting:Setting.BackfillPosition");
        Add(context, ReportingSettings.RepairLastRunAt, "Reporting:Setting.RepairLastRunAt");
    }

    private static void Add(ISettingDefinitionContext context, string name, string text)
    {
        context.Add(new SettingDefinition(
            name,
            null,
            LocalizableString.Create<ReportingResource>(text),
            LocalizableString.Create<ReportingResource>(text + "Description"),
            isVisibleToClients: false,
            isInherited: false).WithProviders(TenantSettingValueProvider.ProviderName));
    }
}
