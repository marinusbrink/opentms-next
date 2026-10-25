using System;
using System.Collections.Generic;
using System.Linq;
using OpenTms.Platform.ReferenceData;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.MultiTenancy;

namespace OpenTms.Financial.Tariffs;

/// <summary>
/// A rule of a price list (design 57): a lane (relation or region per side, at least one side), up to three dimensions
/// (transport unit, product, vehicle type: the specificity <see cref="Tier"/>), the quantity its row brackets grade on,
/// an optional min/max per line (QP8) and the bracket ladder whose cells hold the unit prices per column of the list.
/// A child of the list stored as its own row: a list may hold 10,000 rules, so they are never loaded with the header.
/// </summary>
public class PriceListRule : Entity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }

    public Guid PriceListId { get; private set; }

    public int Position { get; set; }

    /* The lane: Master Data ids, no cross-schema foreign keys (design 56 "Ownership"). */
    public Guid? FromRelationId { get; private set; }

    public Guid? FromRegionId { get; private set; }

    public Guid? ToRelationId { get; private set; }

    public Guid? ToRegionId { get; private set; }

    /* The dimensions (any = null). */
    public Guid? TransportUnitId { get; private set; }

    public Guid? ProductId { get; private set; }

    public Guid? VehicleTypeId { get; private set; }

    /// <summary>The quantity the brackets grade on; <see cref="QuantitySource.Manual"/> = no grading (one open row).</summary>
    public QuantitySource RowQuantity { get; private set; } = QuantitySource.Manual;

    /// <summary><c>numeric(18,2)</c>: the line amount is clamped to these (QP8).</summary>
    public decimal? MinAmount { get; private set; }

    public decimal? MaxAmount { get; private set; }

    public virtual ICollection<PriceListBracket> Brackets { get; private set; } = new List<PriceListBracket>();

    protected PriceListRule()
    {
    }

    public PriceListRule(Guid id, Guid? tenantId, Guid priceListId, int position)
        : base(id)
    {
        TenantId = tenantId;
        PriceListId = priceListId;
        Position = position;
    }

    /// <summary>The number of dimensions set (0–3): the specificity of the lookup cascade.</summary>
    public int Tier => (TransportUnitId == null ? 0 : 1) + (ProductId == null ? 0 : 1) + (VehicleTypeId == null ? 0 : 1);

    public bool HasFromSide => FromRelationId != null || FromRegionId != null;

    public bool HasToSide => ToRelationId != null || ToRegionId != null;

    /// <summary>At least one side set; a side carries a relation or a region, never both.</summary>
    public void SetLane(Guid? fromRelationId, Guid? fromRegionId, Guid? toRelationId, Guid? toRegionId)
    {
        if (fromRelationId != null && fromRegionId != null)
        {
            throw new BusinessException(FinancialErrorCodes.RelationAndRegionOnOneSide).WithData("side", "from");
        }

        if (toRelationId != null && toRegionId != null)
        {
            throw new BusinessException(FinancialErrorCodes.RelationAndRegionOnOneSide).WithData("side", "to");
        }

        if (fromRelationId == null && fromRegionId == null && toRelationId == null && toRegionId == null)
        {
            throw new BusinessException(FinancialErrorCodes.LaneRequired);
        }

        FromRelationId = NonEmpty(fromRelationId, "fromRelationId");
        FromRegionId = NonEmpty(fromRegionId, "fromRegionId");
        ToRelationId = NonEmpty(toRelationId, "toRelationId");
        ToRegionId = NonEmpty(toRegionId, "toRegionId");
    }

    public void SetDimensions(Guid? transportUnitId, Guid? productId, Guid? vehicleTypeId)
    {
        TransportUnitId = NonEmpty(transportUnitId, "transportUnitId");
        ProductId = NonEmpty(productId, "productId");
        VehicleTypeId = NonEmpty(vehicleTypeId, "vehicleTypeId");
    }

    public void SetRowQuantity(QuantitySource rowQuantity) => RowQuantity = FinancialValues.Defined(rowQuantity, "rowQuantity");

    public void SetMinMax(decimal? minAmount, decimal? maxAmount)
    {
        foreach (var (value, field) in new[] { (minAmount, "minAmount"), (maxAmount, "maxAmount") })
        {
            if (value != null && (value.Value < 0 || !FinancialValues.HasScale(value.Value, FinancialConsts.AmountScale)))
            {
                throw FinancialValues.Invalid(field, value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        if (minAmount != null && maxAmount != null && minAmount.Value > maxAmount.Value)
        {
            throw FinancialValues.Invalid("maxAmount", maxAmount.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        MinAmount = minAmount;
        MaxAmount = maxAmount;
    }

    /// <summary>
    /// Replaces the bracket ladder: at most 30 rows, upper bounds strictly ascending with exactly one open (null) row,
    /// last; each row carries one cell per column of the list (a null cell = no price). Existing rows are updated by
    /// position so provenance ids stay stable across edits.
    /// </summary>
    public void SetBrackets(IReadOnlyList<(decimal? UpperBound, IReadOnlyList<decimal?> UnitPrices)> rows, int columnCount, Func<Guid> newId)
    {
        if (rows.Count == 0)
        {
            throw new BusinessException(FinancialErrorCodes.OpenRowRequired).WithData("field", "brackets").WithData("position", 0);
        }

        if (rows.Count > FinancialConsts.MaxPriceListBrackets)
        {
            throw new BusinessException(ReferenceDataErrorCodes.TooManyChildren).WithData("field", "brackets").WithData("max", FinancialConsts.MaxPriceListBrackets);
        }

        PriceList.CheckAscendingWithOpenLast(rows.Select(r => r.UpperBound).ToList(), FinancialErrorCodes.BracketsNotAscending, FinancialErrorCodes.OpenRowRequired, "brackets");

        foreach (var row in rows)
        {
            if (row.UnitPrices.Count != columnCount)
            {
                throw FinancialValues.Invalid("cells", row.UnitPrices.Count.ToString());
            }

            foreach (var price in row.UnitPrices)
            {
                if (price != null && (price.Value < 0 || !FinancialValues.HasScale(price.Value, FinancialConsts.ValueScale)))
                {
                    throw FinancialValues.Invalid("unitPrice", price.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
            }
        }

        foreach (var stale in Brackets.Where(b => b.Position > rows.Count).ToList())
        {
            Brackets.Remove(stale);
        }

        for (var i = 0; i < rows.Count; i++)
        {
            var position = i + 1;
            var bracket = Brackets.FirstOrDefault(b => b.Position == position);
            if (bracket == null)
            {
                bracket = new PriceListBracket(newId(), TenantId, Id, position, rows[i].UpperBound);
                Brackets.Add(bracket);
            }
            else
            {
                bracket.UpperBound = rows[i].UpperBound;
            }

            bracket.SetCells(rows[i].UnitPrices);
        }
    }

    /// <summary>The bracket the value falls in: the first whose bound it does not exceed, else the open row; null when the ladder is empty.</summary>
    public PriceListBracket? BracketOf(decimal value)
    {
        PriceListBracket? open = null;
        foreach (var bracket in Brackets.OrderBy(b => b.Position))
        {
            if (bracket.UpperBound == null)
            {
                open = bracket;
            }
            else if (value <= bracket.UpperBound.Value)
            {
                return bracket;
            }
        }

        return open;
    }

    /// <summary>The single open row, for a rule without grading (RowQuantity Manual).</summary>
    public PriceListBracket? OpenBracket => Brackets.FirstOrDefault(b => b.UpperBound == null);

    private static Guid? NonEmpty(Guid? id, string field)
        => id == Guid.Empty ? throw new BusinessException(ReferenceDataErrorCodes.ReferenceNotFound).WithData("field", field) : id;
}

/// <summary>A vertical bracket of a rule: everything up to and including <see cref="UpperBound"/>; null = the open row, last.</summary>
public class PriceListBracket : Entity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }

    public Guid PriceListRuleId { get; private set; }

    public int Position { get; private set; }

    /// <summary><c>numeric(18,4)</c>; inclusive.</summary>
    public decimal? UpperBound { get; internal set; }

    public virtual ICollection<PriceListCell> Cells { get; private set; } = new List<PriceListCell>();

    protected PriceListBracket()
    {
    }

    internal PriceListBracket(Guid id, Guid? tenantId, Guid ruleId, int position, decimal? upperBound)
        : base(id)
    {
        TenantId = tenantId;
        PriceListRuleId = ruleId;
        Position = position;
        UpperBound = upperBound;
    }

    internal void SetCells(IReadOnlyList<decimal?> unitPrices)
    {
        foreach (var stale in Cells.Where(c => c.ColumnPosition > unitPrices.Count).ToList())
        {
            Cells.Remove(stale);
        }

        for (var i = 0; i < unitPrices.Count; i++)
        {
            var column = i + 1;
            var cell = Cells.FirstOrDefault(c => c.ColumnPosition == column);
            if (cell == null)
            {
                Cells.Add(new PriceListCell(TenantId, Id, column, unitPrices[i]));
            }
            else
            {
                cell.UnitPrice = unitPrices[i];
            }
        }
    }

    /// <summary>The unit price of the given column; null when the cell is empty or absent.</summary>
    public decimal? UnitPriceAt(int columnPosition) => Cells.FirstOrDefault(c => c.ColumnPosition == columnPosition)?.UnitPrice;
}

/// <summary>One cell of the matrix: the unit price of a bracket in a column; null = no price in that cell.</summary>
public class PriceListCell : Entity, IMultiTenant
{
    public Guid? TenantId { get; private set; }

    public Guid PriceListBracketId { get; private set; }

    public int ColumnPosition { get; private set; }

    /// <summary><c>numeric(18,4)</c>.</summary>
    public decimal? UnitPrice { get; internal set; }

    /// <summary>An adjusted unit price (design 57, adjust): never negative, at most four decimals.</summary>
    public void SetUnitPrice(decimal? unitPrice)
    {
        if (unitPrice != null && (unitPrice.Value < 0 || !FinancialValues.HasScale(unitPrice.Value, FinancialConsts.ValueScale)))
        {
            throw FinancialValues.Invalid("unitPrice", unitPrice.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        UnitPrice = unitPrice;
    }

    protected PriceListCell()
    {
    }

    internal PriceListCell(Guid? tenantId, Guid bracketId, int columnPosition, decimal? unitPrice)
    {
        TenantId = tenantId;
        PriceListBracketId = bracketId;
        ColumnPosition = columnPosition;
        UnitPrice = unitPrice;
    }

    public override object[] GetKeys() => [PriceListBracketId, ColumnPosition];
}
