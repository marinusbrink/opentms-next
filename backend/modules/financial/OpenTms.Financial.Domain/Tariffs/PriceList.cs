using System;
using System.Collections.Generic;
using System.Linq;
using OpenTms.Platform.ReferenceData;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.MultiTenancy;

namespace OpenTms.Financial.Tariffs;

/// <summary>
/// A price list (design 57, Q3/QP5): the header knows its side, currency, the basis its unit prices are in, how a price
/// is multiplied, which quantity selects the column bracket and which stops a rule's lane must match. Its horizontal
/// brackets are the <see cref="Columns"/>; its rules (lanes with their vertical brackets and cells) are
/// <see cref="PriceListRule"/> rows loaded separately, since a list may hold 10,000 of them.
/// </summary>
public class PriceList : ReferenceDataAggregateRoot
{
    public string Code { get; private set; } = default!;

    public string Name { get; private set; } = default!;

    public RateSide Side { get; private set; } = RateSide.Revenue;

    /// <summary>ISO 4217; active in Master Data, checked by the service (QP2).</summary>
    public string CurrencyCode { get; private set; } = "EUR";

    /// <summary>The unit the unit prices are in (per line quantity, kg, km, hour, …).</summary>
    public QuantitySource Basis { get; private set; } = QuantitySource.LineQuantity;

    public PriceMultiplier Multiplier { get; private set; } = PriceMultiplier.PerUnit;

    public ColumnQuantitySource ColumnQuantity { get; private set; } = ColumnQuantitySource.LineBasis;

    public StopMatching StopMatching { get; private set; } = StopMatching.Both;

    /// <summary><c>numeric(18,6)</c>; the quantity is divided by it (a kg list applied to tonnes: 1000).</summary>
    public decimal UnitConversionFactor { get; private set; } = 1m;

    public DateOnly ValidFrom { get; private set; }

    public DateOnly? ValidTill { get; private set; }

    public bool IsArchived { get; set; }

    public virtual ICollection<PriceListColumn> Columns { get; private set; } = new List<PriceListColumn>();

    protected PriceList()
    {
    }

    public PriceList(Guid id, Guid? tenantId, string code, string name, RateSide side, string currencyCode, QuantitySource basis, DateOnly validFrom)
        : base(id, tenantId)
    {
        SetCode(code);
        SetName(name);
        SetSide(side);
        SetCurrencyCode(currencyCode);
        SetBasis(basis);
        SetValidity(validFrom, null);
        SetColumns([null]);
    }

    public void SetCode(string code) => Code = NormalizeCode(code);

    public void SetName(string name) => Name = NormalizeName(name);

    public void SetSide(RateSide side) => Side = FinancialValues.Defined(side, "side");

    public void SetCurrencyCode(string currencyCode) => CurrencyCode = FinancialValues.IsoCode(currencyCode, ReferenceDataConsts.IsoCurrencyCodeLength, "currencyCode");

    /// <summary>The basis is a fact of the line; <see cref="QuantitySource.Manual"/> is for typed quantities only.</summary>
    public void SetBasis(QuantitySource basis)
    {
        FinancialValues.Defined(basis, "basis");
        if (basis == QuantitySource.Manual)
        {
            throw FinancialValues.Invalid("basis", ((int)basis).ToString());
        }

        Basis = basis;
    }

    public void SetMultiplier(PriceMultiplier multiplier) => Multiplier = FinancialValues.Defined(multiplier, "multiplier");

    public void SetColumnQuantity(ColumnQuantitySource columnQuantity) => ColumnQuantity = FinancialValues.Defined(columnQuantity, "columnQuantity");

    public void SetStopMatching(StopMatching stopMatching) => StopMatching = FinancialValues.Defined(stopMatching, "stopMatching");

    public void SetUnitConversionFactor(decimal factor)
    {
        if (factor <= 0 || !FinancialValues.HasScale(factor, FinancialConsts.FactorScale))
        {
            throw FinancialValues.Invalid("unitConversionFactor", factor.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        UnitConversionFactor = factor;
    }

    public void SetValidity(DateOnly validFrom, DateOnly? validTill)
    {
        if (validTill != null && validTill.Value < validFrom)
        {
            throw new BusinessException(FinancialErrorCodes.InvalidValidity)
                .WithData("validFrom", validFrom.ToString("yyyy-MM-dd"))
                .WithData("validTill", validTill.Value.ToString("yyyy-MM-dd"));
        }

        ValidFrom = validFrom;
        ValidTill = validTill;
    }

    /// <summary>True when the list applies on <paramref name="date"/> (QP1: validity is enforced in the calculation).</summary>
    public bool IsValidOn(DateOnly date) => ValidFrom <= date && (ValidTill == null || date <= ValidTill.Value);

    /// <summary>
    /// Replaces the columns: at least one and at most 50 upper bounds, strictly ascending, the last one null (open) and
    /// only the last. Positions are 1..n in the given order.
    /// </summary>
    public void SetColumns(IReadOnlyList<decimal?> upperBounds)
    {
        if (upperBounds.Count == 0)
        {
            throw new BusinessException(FinancialErrorCodes.ColumnsNotAscending).WithData("position", 0);
        }

        if (upperBounds.Count > FinancialConsts.MaxPriceListColumns)
        {
            throw new BusinessException(ReferenceDataErrorCodes.TooManyChildren).WithData("field", "columns").WithData("max", FinancialConsts.MaxPriceListColumns);
        }

        CheckAscendingWithOpenLast(upperBounds, FinancialErrorCodes.ColumnsNotAscending, FinancialErrorCodes.OpenRowRequired, "columns");

        foreach (var stale in Columns.Where(c => c.Position > upperBounds.Count).ToList())
        {
            Columns.Remove(stale);
        }

        for (var i = 0; i < upperBounds.Count; i++)
        {
            var position = i + 1;
            var existing = Columns.FirstOrDefault(c => c.Position == position);
            if (existing == null)
            {
                Columns.Add(new PriceListColumn(TenantId, Id, position, upperBounds[i]));
            }
            else
            {
                existing.UpperBound = upperBounds[i];
            }
        }
    }

    public int ColumnCount => Columns.Count;

    /// <summary>The column position the value falls in: the first column whose upper bound it does not exceed, else the open last one.</summary>
    public int ColumnPositionOf(decimal value)
    {
        foreach (var column in Columns.OrderBy(c => c.Position))
        {
            if (column.UpperBound == null || value <= column.UpperBound.Value)
            {
                return column.Position;
            }
        }

        return Columns.Count;
    }

    /// <summary>Shared by the columns and the row brackets: strictly ascending bounds with exactly one open (null) bound, last.</summary>
    internal static void CheckAscendingWithOpenLast(IReadOnlyList<decimal?> upperBounds, string notAscendingCode, string openRequiredCode, string field)
    {
        for (var i = 0; i < upperBounds.Count; i++)
        {
            var bound = upperBounds[i];
            var last = i == upperBounds.Count - 1;
            if (bound == null)
            {
                if (!last)
                {
                    throw new BusinessException(openRequiredCode).WithData("field", field).WithData("position", i + 1);
                }

                continue;
            }

            if (!FinancialValues.HasScale(bound.Value, FinancialConsts.ValueScale) || bound.Value < 0)
            {
                throw FinancialValues.Invalid(field, bound.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            if (i > 0 && upperBounds[i - 1] is { } previous && bound.Value <= previous)
            {
                throw new BusinessException(notAscendingCode).WithData("field", field).WithData("position", i + 1);
            }
        }

        if (upperBounds[^1] != null)
        {
            throw new BusinessException(openRequiredCode).WithData("field", field).WithData("position", upperBounds.Count);
        }
    }
}

/// <summary>A horizontal bracket of a price list: everything up to and including <see cref="UpperBound"/>; null = open-ended, last.</summary>
public class PriceListColumn : Entity, IMultiTenant
{
    public Guid? TenantId { get; private set; }

    public Guid PriceListId { get; private set; }

    public int Position { get; private set; }

    /// <summary><c>numeric(18,4)</c>; inclusive.</summary>
    public decimal? UpperBound { get; internal set; }

    protected PriceListColumn()
    {
    }

    internal PriceListColumn(Guid? tenantId, Guid priceListId, int position, decimal? upperBound)
    {
        TenantId = tenantId;
        PriceListId = priceListId;
        Position = position;
        UpperBound = upperBound;
    }

    public override object[] GetKeys() => [PriceListId, Position];
}
