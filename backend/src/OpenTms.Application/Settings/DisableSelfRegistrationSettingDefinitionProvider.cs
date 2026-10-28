using Volo.Abp.Account.Settings;
using Volo.Abp.Settings;

namespace OpenTms.Settings;

/// <summary>
/// Self-registration is off and stays off (docs/designs/62-sign-in-tenant-detection.md, "No self-registration"): users
/// are created by an administrator or the tenant's provisioning only. The register page and endpoint are gone as well;
/// the setting is off so ABP's account service refuses a registration that would reach it any other way. Lives here,
/// not in the contracts: ABP defines the setting in its account application module, which loads before this module.
/// </summary>
public class DisableSelfRegistrationSettingDefinitionProvider : SettingDefinitionProvider
{
    public override void Define(ISettingDefinitionContext context)
    {
        var selfRegistration = context.GetOrNull(AccountSettingNames.IsSelfRegistrationEnabled);
        if (selfRegistration != null)
        {
            selfRegistration.DefaultValue = false.ToString();
            selfRegistration.IsVisibleToClients = false;
        }
    }
}
