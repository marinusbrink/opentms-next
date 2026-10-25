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
    }

    /// <summary>The view permission and its .Create, .Update and .Delete children, each with its text.</summary>
    private static void Add(PermissionGroupDefinition group, string name, string entity)
    {
        var view = group.AddPermission(name, L($"Permission:{entity}"), MultiTenancySides.Both);
        view.AddChild(name + ".Create", L($"Permission:{entity}.Create"), MultiTenancySides.Both);
        view.AddChild(name + ".Update", L($"Permission:{entity}.Update"), MultiTenancySides.Both);
        view.AddChild(name + ".Delete", L($"Permission:{entity}.Delete"), MultiTenancySides.Both);
    }

    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<FinancialResource>(name);
    }
}
