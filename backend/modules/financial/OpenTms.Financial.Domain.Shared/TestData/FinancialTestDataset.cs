using System;
using System.Collections.Generic;

namespace OpenTms.Financial.TestData;

/// <summary>
/// What Financial generated in a "Reset to dataset" run (docs/designs/65-tenant-test-datasets.md), published for the
/// contributors after it (Orders, Planning &amp; Execution). Ids only; archived records are left out, so every id here
/// may be linked to a new order.
/// </summary>
public sealed class FinancialTestDataset
{
    /// <summary>The price lists that are not archived: revenue and cost, current, future and expired ones.</summary>
    public List<Guid> PriceListIds { get; } = new();

    public List<Guid> DiscountOrSurchargeIds { get; } = new();

    public List<Guid> AdditionalServiceIds { get; } = new();
}
