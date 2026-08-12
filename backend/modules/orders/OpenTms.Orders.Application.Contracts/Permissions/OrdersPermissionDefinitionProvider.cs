using OpenTms.Orders.Localization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Localization;

namespace OpenTms.Orders.Permissions;

public class OrdersPermissionDefinitionProvider : PermissionDefinitionProvider
{
    public override void Define(IPermissionDefinitionContext context)
    {
        context.AddGroup(OrdersPermissions.GroupName, L("Permission:Orders"));
    }

    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<OrdersResource>(name);
    }
}
