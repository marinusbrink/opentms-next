using OpenTms.Platform;
using OpenTms.Platform.PredefinedRoles;
using P = OpenTms.Financial.Permissions.FinancialPermissions;

namespace OpenTms.Financial.Permissions;

/// <summary>The Financial permissions of the predefined roles (docs/designs/71-predefined-roles.md).</summary>
public static class FinancialPredefinedRoles
{
    public static void Configure(PredefinedRoleOptions options)
    {
        options.Grant(PlatformRoleNames.Finance,
            P.PriceLists.Default, P.PriceLists.Create, P.PriceLists.Update, P.PriceLists.Delete, P.PriceLists.Adjust, P.PriceLists.Simulate,
            P.RateLines.Default, P.RateLines.Update,
            P.RelationFinancialSettings.Default, P.RelationFinancialSettings.Update,
            P.VatPercentages.Default, P.VatPercentages.Create, P.VatPercentages.Update, P.VatPercentages.Delete,
            P.AdditionalServices.Default, P.AdditionalServices.Create, P.AdditionalServices.Update, P.AdditionalServices.Delete,
            P.DiscountsOrSurcharges.Default, P.DiscountsOrSurcharges.Create, P.DiscountsOrSurcharges.Update, P.DiscountsOrSurcharges.Delete,
            P.OutboundInvoiceTypes.Default, P.OutboundInvoiceTypes.Create, P.OutboundInvoiceTypes.Update, P.OutboundInvoiceTypes.Delete,
            P.InboundInvoiceTypes.Default, P.InboundInvoiceTypes.Create, P.InboundInvoiceTypes.Update, P.InboundInvoiceTypes.Delete);

        options.Grant(PlatformRoleNames.MasterDataManager,
            P.VatPercentages.Default, P.VatPercentages.Create, P.VatPercentages.Update, P.VatPercentages.Delete,
            P.AdditionalServices.Default, P.AdditionalServices.Create, P.AdditionalServices.Update, P.AdditionalServices.Delete,
            P.DiscountsOrSurcharges.Default, P.DiscountsOrSurcharges.Create, P.DiscountsOrSurcharges.Update, P.DiscountsOrSurcharges.Delete,
            P.OutboundInvoiceTypes.Default, P.OutboundInvoiceTypes.Create, P.OutboundInvoiceTypes.Update, P.OutboundInvoiceTypes.Delete,
            P.InboundInvoiceTypes.Default, P.InboundInvoiceTypes.Create, P.InboundInvoiceTypes.Update, P.InboundInvoiceTypes.Delete);

        options.Grant(PlatformRoleNames.ReadOnly,
            P.PriceLists.Default, P.RateLines.Default, P.RelationFinancialSettings.Default,
            P.VatPercentages.Default, P.AdditionalServices.Default, P.DiscountsOrSurcharges.Default,
            P.OutboundInvoiceTypes.Default, P.InboundInvoiceTypes.Default);
    }
}
