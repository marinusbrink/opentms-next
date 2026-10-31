using Volo.Abp.Settings;

namespace OpenTms.Platform.Emailing;

/// <summary>
/// The host's e-mail settings (design 64): global only, so no tenant or user can override them; the token and the webhook
/// password are encrypted at rest. Not visible to clients: the settings page reads them through its own service.
/// </summary>
public class EmailSettingDefinitionProvider : SettingDefinitionProvider
{
    public override void Define(ISettingDefinitionContext context)
    {
        Add(context, EmailSettingNames.ServerToken, encrypted: true);
        Add(context, EmailSettingNames.FromAddress);
        Add(context, EmailSettingNames.FromName);
        Add(context, EmailSettingNames.TrackOpens);
        Add(context, EmailSettingNames.TrackLinks);
        Add(context, EmailSettingNames.WebhookUsername);
        Add(context, EmailSettingNames.WebhookPassword, encrypted: true);
    }

    private static void Add(ISettingDefinitionContext context, string name, bool encrypted = false)
    {
        context.Add(new SettingDefinition(name, isVisibleToClients: false, isEncrypted: encrypted)
            .WithProviders(GlobalSettingValueProvider.ProviderName));
    }
}
