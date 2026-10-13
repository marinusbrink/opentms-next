using System;

namespace OpenTms.MasterData.Relations;

/// <summary>
/// The explicit roles of a relation (assessment §3.1: a typed set, not derived from categories). A relation
/// carries at least one; exactly one relation per tenant carries OwnCompany (partial unique index).
/// </summary>
[Flags]
public enum RelationRoles
{
    None = 0,
    Customer = 1,
    Carrier = 2,
    Creditor = 4,
    Depot = 8,
    OwnCompany = 16
}

public enum ContactPersonRole
{
    Other = 0,
    Planning = 1,
    Invoicing = 2,
    Sales = 3,
    Management = 4
}
