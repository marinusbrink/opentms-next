namespace OpenTms.MasterData.Relations;

/// <summary>
/// Why a relation is blocked (PO 2026-10-12: the old "Is geblokkeerd" with a reason). A fixed list for now;
/// what a block stops, and whether the reasons become a tenant-maintained list, is decided by the Financial run.
/// </summary>
public enum RelationBlockReason
{
    None = 0,
    PaymentArrears = 1,
    CreditLimitExceeded = 2,
    Insolvency = 3,
    Dispute = 4,
    Other = 5
}
