namespace OpenTms.Orders.TransportOrders;

/// <summary>The ABP settings of the Orders module (design 58 "Approval", Assumption 3).</summary>
public static class OrdersSettings
{
    /// <summary>A comma-separated list of <c>ApprovalRule</c> names; the default is the four OpenTMS system rules.</summary>
    public const string ActiveApprovalRules = "Orders:ActiveApprovalRules";

    public const string DefaultActiveApprovalRules = "OrderMustBeOpen,ReferencesMustExist,NextInvoicePrice,FreightDocumentsAvailable";

    /// <summary>UI-GRID-013: the most rows one export of the order list may hold (default <see cref="OrdersConsts.DefaultExportMaxRows"/>).</summary>
    public const string ExportMaxRows = "Orders:Export:MaxRows";
}

/// <summary>Whether a discount or surcharge came with the customer's defaults or was picked by the clerk (design 58).</summary>
public enum DiscountOrSurchargeSource
{
    Default = 1,
    Manual = 2
}

/// <summary>The kinds of remark an order keeps apart from its free-text remarks (design 58: a revision's reason).</summary>
public enum OrderRemarkKind
{
    Revision = 1
}

/// <summary>The saved views of the order list (design 58 "API contract").</summary>
public enum TransportOrderView
{
    All = 0,
    Today = 1,
    Tomorrow = 2,
    ThisWeek = 3,
    Open = 4,
    Drafts = 5
}
