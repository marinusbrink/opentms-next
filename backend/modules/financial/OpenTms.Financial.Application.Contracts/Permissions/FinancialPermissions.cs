namespace OpenTms.Financial.Permissions;

public static class FinancialPermissions
{
    public const string GroupName = "Financial";

    /// <summary>Design 52: the VAT percentages list of the Master data app (Invoicing).</summary>
    public static class VatPercentages
    {
        public const string Default = GroupName + ".VatPercentages";
        public const string Create = Default + ".Create";
        public const string Update = Default + ".Update";
        public const string Delete = Default + ".Delete";
    }

    /// <summary>Design 52: the additional services list (Pricing).</summary>
    public static class AdditionalServices
    {
        public const string Default = GroupName + ".AdditionalServices";
        public const string Create = Default + ".Create";
        public const string Update = Default + ".Update";
        public const string Delete = Default + ".Delete";
    }

    /// <summary>Design 52: the discounts and surcharges list (Pricing).</summary>
    public static class DiscountsOrSurcharges
    {
        public const string Default = GroupName + ".DiscountsOrSurcharges";
        public const string Create = Default + ".Create";
        public const string Update = Default + ".Update";
        public const string Delete = Default + ".Delete";
    }

    /// <summary>Design 52: the outbound invoice types list (Invoicing).</summary>
    public static class OutboundInvoiceTypes
    {
        public const string Default = GroupName + ".OutboundInvoiceTypes";
        public const string Create = Default + ".Create";
        public const string Update = Default + ".Update";
        public const string Delete = Default + ".Delete";
    }

    /// <summary>Design 52: the inbound invoice types list (Invoicing).</summary>
    public static class InboundInvoiceTypes
    {
        public const string Default = GroupName + ".InboundInvoiceTypes";
        public const string Create = Default + ".Create";
        public const string Update = Default + ".Update";
        public const string Delete = Default + ".Delete";
    }

    /// <summary>Design 57: the price lists list (Pricing), with the Adjust and Simulate commands.</summary>
    public static class PriceLists
    {
        public const string Default = GroupName + ".PriceLists";
        public const string Create = Default + ".Create";
        public const string Update = Default + ".Update";
        public const string Delete = Default + ".Delete";
        public const string Adjust = Default + ".Adjust";
        public const string Simulate = Default + ".Simulate";
    }

    /// <summary>Design 57: the rate lines of orders and trips; Update covers manual lines, adjustments and the fixed price.</summary>
    public static class RateLines
    {
        public const string Default = GroupName + ".RateLines";
        public const string Update = Default + ".Update";
    }

    /// <summary>Design 57: the relation financial settings list (Pricing).</summary>
    public static class RelationFinancialSettings
    {
        public const string Default = GroupName + ".RelationFinancialSettings";
        public const string Update = Default + ".Update";
    }
}
