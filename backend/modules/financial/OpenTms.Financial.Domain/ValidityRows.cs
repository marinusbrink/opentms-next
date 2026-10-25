using System;
using System.Collections.Generic;
using System.Linq;
using OpenTms.Platform.ReferenceData;
using Volo.Abp;

namespace OpenTms.Financial;

/// <summary>A row of a value that changes over time (assessment §3.4): valid from its date until the next row's date.</summary>
public interface IValidityRow
{
    DateOnly ValidFrom { get; }
}

/// <summary>
/// The §3.4 versioning rule shared by the VAT rates and the discount or surcharge values (design 52, Domain impact):
/// a change is a new row with its own <c>ValidFrom</c>; rows dated on or before today are fixed (neither changed, removed
/// nor added afterwards), only future rows can be edited. Shared Financial plumbing used by Invoicing and Tariffs.
/// </summary>
public static class ValidityRows
{
    /// <summary>The row valid on <paramref name="date"/>: the latest row starting on or before it; null before the first.</summary>
    public static TRow? On<TRow>(IEnumerable<TRow> rows, DateOnly date)
        where TRow : class, IValidityRow
        => rows.Where(r => r.ValidFrom <= date).OrderByDescending(r => r.ValidFrom).FirstOrDefault();

    /// <summary>
    /// Replaces <paramref name="rows"/> with <paramref name="incoming"/> (rows matched by date are updated in place, new
    /// dates added, missing dates removed). With <paramref name="fixedOn"/> set (an update), every existing row dated on
    /// or before it must come back unchanged and no new row may be dated on or before it; null on create, when nothing
    /// has taken effect yet.
    /// </summary>
    public static void Replace<TRow, TInput>(
        ICollection<TRow> rows,
        IReadOnlyList<TInput> incoming,
        Func<TInput, DateOnly> validFromOf,
        Func<TRow, TInput, bool> sameValue,
        Func<TInput, TRow> create,
        Action<TRow, TInput> update,
        DateOnly? fixedOn,
        int max,
        string field)
        where TRow : class, IValidityRow
    {
        if (incoming.Count == 0)
        {
            throw new BusinessException(FinancialErrorCodes.ValidityRowRequired).WithData("field", field);
        }

        if (incoming.Count > max)
        {
            throw new BusinessException(ReferenceDataErrorCodes.TooManyChildren).WithData("field", field).WithData("max", max);
        }

        var duplicate = incoming.GroupBy(validFromOf).FirstOrDefault(g => g.Count() > 1);
        if (duplicate != null)
        {
            throw new BusinessException(FinancialErrorCodes.DuplicateValidFrom).WithData("validFrom", duplicate.Key.ToString("yyyy-MM-dd"));
        }

        if (fixedOn != null)
        {
            foreach (var row in rows.Where(r => r.ValidFrom <= fixedOn.Value))
            {
                var match = incoming.FirstOrDefault(i => validFromOf(i) == row.ValidFrom);
                if (match == null || !sameValue(row, match))
                {
                    throw FixedRowChanged(row.ValidFrom);
                }
            }

            foreach (var input in incoming.Where(i => validFromOf(i) <= fixedOn.Value))
            {
                if (rows.All(r => r.ValidFrom != validFromOf(input)))
                {
                    throw FixedRowChanged(validFromOf(input));
                }
            }
        }

        foreach (var input in incoming)
        {
            var existing = rows.FirstOrDefault(r => r.ValidFrom == validFromOf(input));
            if (existing == null)
            {
                rows.Add(create(input));
            }
            else if (fixedOn == null || existing.ValidFrom > fixedOn.Value)
            {
                update(existing, input);
            }
        }

        var keep = incoming.Select(validFromOf).ToHashSet();
        foreach (var stale in rows.Where(r => !keep.Contains(r.ValidFrom)).ToList())
        {
            rows.Remove(stale);
        }
    }

    private static BusinessException FixedRowChanged(DateOnly validFrom)
        => new BusinessException(FinancialErrorCodes.FixedValidityRowChanged).WithData("validFrom", validFrom.ToString("yyyy-MM-dd"));
}
