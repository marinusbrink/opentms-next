using OpenTms.Financial.Localization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Localization;
using Volo.Abp.MultiTenancy;

namespace OpenTms.Financial.Permissions;

public class FinancialPermissionDefinitionProvider : PermissionDefinitionProvider
{
    public override void Define(IPermissionDefinitionContext context)
    {
        var group = context.AddGroup(FinancialPermissions.GroupName, L("Permission:Financial"));

        /* Both sides (assessment §3.11, §4: host-level reference data is parked but must stay possible). Design 52. */
        Add(group, FinancialPermissions.VatPercentages.Default, "VatPercentages");
        Add(group, FinancialPermissions.AdditionalServices.Default, "AdditionalServices");
        Add(group, FinancialPermissions.DiscountsOrSurcharges.Default, "DiscountsOrSurcharges");
        Add(group, FinancialPermissions.OutboundInvoiceTypes.Default, "OutboundInvoiceTypes");
        Add(group, FinancialPermissions.InboundInvoiceTypes.Default, "InboundInvoiceTypes");

        /* Design 57. */
        var priceLists = Add(group, FinancialPermissions.PriceLists.Default, "PriceLists");
        priceLists.AddChild(FinancialPermissions.PriceLists.Adjust, L("Permission:PriceLists.Adjust"), MultiTenancySides.Both);
        priceLists.AddChild(FinancialPermissions.PriceLists.Simulate, L("Permission:PriceLists.Simulate"), MultiTenancySides.Both);

        var rateLines = group.AddPermission(FinancialPermissions.RateLines.Default, L("Permission:RateLines"), MultiTenancySides.Both);
        rateLines.AddChild(FinancialPermissions.RateLines.Update, L("Permission:RateLines.Update"), MultiTenancySides.Both);

        var settings = group.AddPermission(FinancialPermissions.RelationFinancialSettings.Default, L("Permission:RelationFinancialSettings"), MultiTenancySides.Both);
        settings.AddChild(FinancialPermissions.RelationFinancialSettings.Update, L("Permission:RelationFinancialSettings.Update"), MultiTenancySides.Both);
    }

    /// <summary>The view permission and its .Create, .Update and .Delete children, each with its text.</summary>
    private static PermissionDefinition Add(PermissionGroupDefinition group, string name, string entity)
    {
        var view = group.AddPermission(name, L($"Permission:{entity}"), MultiTenancySides.Both);
        view.AddChild(name + ".Create", L($"Permission:{entity}.Create"), MultiTenancySides.Both);
        view.AddChild(name + ".Update", L($"Permission:{entity}.Update"), MultiTenancySides.Both);
        view.AddChild(name + ".Delete", L($"Permission:{entity}.Delete"), MultiTenancySides.Both);
        return view;
    }

    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<FinancialResource>(name);
    }
}
