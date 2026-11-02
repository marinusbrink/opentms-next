using System.Linq;
using Volo.Abp.Settings;

namespace OpenTms.Integrations.ServiceSettings;

/// <summary>
/// The host's service settings: global only, so no tenant or user can override them; keys and secrets are encrypted at
/// rest (ABP's setting encryption). Not visible to clients: the page reads them through its own service.
/// </summary>
public class ServiceSettingDefinitionProvider : SettingDefinitionProvider
{
    public override void Define(ISettingDefinitionContext context)
    {
        foreach (var name in ServiceSettingNames.All)
        {
            context.Add(new SettingDefinition(name, isVisibleToClients: false, isEncrypted: ServiceSettingNames.Encrypted.Contains(name))
                .WithProviders(GlobalSettingValueProvider.ProviderName));
        }
    }
}
