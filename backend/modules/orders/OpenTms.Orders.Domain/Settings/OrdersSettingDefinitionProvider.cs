using OpenTms.Orders.Localization;
using OpenTms.Orders.TransportOrders;
using Volo.Abp.Localization;
using Volo.Abp.Settings;

namespace OpenTms.Orders.Settings;

/// <summary>The ABP settings of the Orders module (design 58 "Approval", Assumption 3): per tenant, visible to the client.</summary>
public class OrdersSettingDefinitionProvider : SettingDefinitionProvider
{
    public override void Define(ISettingDefinitionContext context)
    {
        context.Add(new SettingDefinition(
            OrdersSettings.ActiveApprovalRules,
            OrdersSettings.DefaultActiveApprovalRules,
            LocalizableString.Create<OrdersResource>("Orders:Setting.ActiveApprovalRules"),
            LocalizableString.Create<OrdersResource>("Orders:Setting.ActiveApprovalRulesDescription"),
            isVisibleToClients: true));
    }
}
