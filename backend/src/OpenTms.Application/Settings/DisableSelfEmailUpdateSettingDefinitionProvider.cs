using Volo.Abp.Identity.Settings;
using Volo.Abp.Settings;

namespace OpenTms.Settings;

/// <summary>
/// Users cannot change their own e-mail address (docs/designs/63-user-menu.md): it is the sign-in key and names the
/// user's accounts in other tenants, so a self-chosen address would list another person's organisations. Only an
/// administrator changes it (Administration app); ABP's own <c>PUT api/account/my-profile</c> then keeps the address it
/// has. Lives here for the same reason as <see cref="DisableSelfRegistrationSettingDefinitionProvider"/>.
/// </summary>
public class DisableSelfEmailUpdateSettingDefinitionProvider : SettingDefinitionProvider
{
    public override void Define(ISettingDefinitionContext context)
    {
        var emailUpdate = context.GetOrNull(IdentitySettingNames.User.IsEmailUpdateEnabled);
        if (emailUpdate != null)
        {
            emailUpdate.DefaultValue = false.ToString();
        }
    }
}
