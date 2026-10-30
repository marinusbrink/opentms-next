using System;
using System.Collections.Generic;
using System.Linq;
using OpenTms.Platform.ReferenceData;
using Volo.Abp;

namespace OpenTms.Financial.Tariffs;

/// <summary>
/// The bases an order is charged by (design 61 "Order page review" point 3, QP5 kept): the rows of the legacy block "Price
/// calculation" that are switched on. A basis that is on without a price list of that basis makes the order calculation
/// write one open line per order line for it. Stored as a bit mask of <see cref="QuantitySource"/> values (bit n = value n),
/// on the order's terms, the template's terms and a relation's revenue defaults; the API carries the list.
/// </summary>
public static class ChargedBases
{
    /// <summary>The revenue bases in the legacy order of the block (design 61 point 3); the order every read returns them in.</summary>
    public static readonly IReadOnlyList<QuantitySource> RevenueBases =
    [
        QuantitySource.LineQuantity,
        QuantitySource.Colli,
        QuantitySource.Epq,
        QuantitySource.WeightKg,
        QuantitySource.DistanceKm,
        QuantitySource.Positions,
        QuantitySource.PerLine,
        QuantitySource.VolumeM3,
        QuantitySource.LoadingMeters,
        QuantitySource.LoadFactor,
        QuantitySource.DurationH,
        QuantitySource.VehicleType,
        QuantitySource.TollDistanceKm
    ];

    /// <summary>
    /// Whether a basis can be charged to a customer: every fact basis except <see cref="QuantitySource.Manual"/> (a typed
    /// quantity, not a basis) and the cost-only <see cref="QuantitySource.TripDistanceKm"/> (the own-fleet line of a trip).
    /// </summary>
    public static bool IsRevenueBasis(QuantitySource basis) => RevenueBases.Contains(basis);

    /// <summary>
    /// Validates the switched-on bases (each once, revenue bases only) and returns their mask. <paramref name="field"/> names the
    /// field in the refusal: <c>DuplicateKey</c> for a repeated basis, <c>InvalidCode</c> for one that cannot be charged.
    /// </summary>
    public static int ToMask(IReadOnlyCollection<QuantitySource>? bases, string field)
    {
        if (bases == null || bases.Count == 0)
        {
            return 0;
        }

        var duplicate = bases.GroupBy(b => b).FirstOrDefault(g => g.Count() > 1);
        if (duplicate != null)
        {
            throw new BusinessException(ReferenceDataErrorCodes.DuplicateKey).WithData("field", field).WithData("value", ((int)duplicate.Key).ToString());
        }

        var mask = 0;
        foreach (var basis in bases)
        {
            if (!IsRevenueBasis(basis))
            {
                throw new BusinessException(ReferenceDataErrorCodes.InvalidCode).WithData("field", field).WithData("value", ((int)basis).ToString());
            }

            mask |= 1 << (int)basis;
        }

        return mask;
    }

    /// <summary>The bases of a stored mask in the legacy order; unknown bits are ignored.</summary>
    public static IReadOnlyList<QuantitySource> FromMask(int mask)
        => mask == 0 ? Array.Empty<QuantitySource>() : RevenueBases.Where(b => (mask & (1 << (int)b)) != 0).ToList();
}
