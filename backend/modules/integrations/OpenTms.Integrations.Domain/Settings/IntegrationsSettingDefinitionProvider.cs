using System.Globalization;
using OpenTms.Integrations.Localization;
using OpenTms.Integrations.OrderIntake;
using Volo.Abp.Localization;
using Volo.Abp.Settings;

namespace OpenTms.Integrations.Settings;

/// <summary>
/// The tenant settings of the Integrations module (design 72): per tenant, no screen in this run. The host's service
/// settings are <c>ServiceSettingDefinitionProvider</c>'s.
/// </summary>
public class IntegrationsSettingDefinitionProvider : SettingDefinitionProvider
{
    public override void Define(ISettingDefinitionContext context)
    {
        context.Add(new SettingDefinition(
            IntegrationsSettings.ProfileActivationThreshold,
            OrderIntakeConsts.DefaultProfileActivationThreshold.ToString(CultureInfo.InvariantCulture),
            LocalizableString.Create<IntegrationsResource>("Integrations:Setting.ProfileActivationThreshold"),
            LocalizableString.Create<IntegrationsResource>("Integrations:Setting.ProfileActivationThresholdDescription"),
            isVisibleToClients: false));
        context.Add(new SettingDefinition(
            IntegrationsSettings.NameSimilarityThreshold,
            OrderIntakeConsts.DefaultNameSimilarityThreshold.ToString(CultureInfo.InvariantCulture),
            LocalizableString.Create<IntegrationsResource>("Integrations:Setting.NameSimilarityThreshold"),
            LocalizableString.Create<IntegrationsResource>("Integrations:Setting.NameSimilarityThresholdDescription"),
            isVisibleToClients: false));
        /* Design 74: the acceptance of the AI connector's data sharing. Only ever a tenant's own value. */
        context.Add(new SettingDefinition(
            IntegrationsSettings.AiConnectorAcceptedAt,
            null,
            LocalizableString.Create<IntegrationsResource>("Integrations:Setting.AiConnectorAcceptedAt"),
            LocalizableString.Create<IntegrationsResource>("Integrations:Setting.AiConnectorAcceptedAtDescription"),
            isVisibleToClients: false,
            isInherited: false).WithProviders(TenantSettingValueProvider.ProviderName));
        context.Add(new SettingDefinition(
            IntegrationsSettings.AiConnectorAcceptedByUserId,
            null,
            LocalizableString.Create<IntegrationsResource>("Integrations:Setting.AiConnectorAcceptedByUserId"),
            LocalizableString.Create<IntegrationsResource>("Integrations:Setting.AiConnectorAcceptedByUserIdDescription"),
            isVisibleToClients: false,
            isInherited: false).WithProviders(TenantSettingValueProvider.ProviderName));
    }
}
