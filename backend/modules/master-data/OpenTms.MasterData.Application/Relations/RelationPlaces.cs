using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.MasterData.Reference;
using Volo.Abp.Domain.Repositories;

namespace OpenTms.MasterData.Relations;

/// <summary>
/// The place a relation is shown with in other lists (design 48, product relation codes): the city of its visiting
/// address (by address-type kind), else of its default address, else of its first address by city. The same choice
/// as the relation list's city column (design 43).
/// </summary>
internal static class RelationPlaces
{
    public static async Task<Dictionary<Guid, string>> CitiesAsync(
        IRepository<RelationAddress, Guid> addresses,
        IRepository<AddressType, Guid> addressTypes,
        IReadOnlyCollection<Guid> relationIds)
    {
        if (relationIds.Count == 0)
        {
            return [];
        }

        var ids = relationIds.Distinct().ToList();
        var rows = await addresses.GetListAsync(a => ids.Contains(a.RelationId));
        if (rows.Count == 0)
        {
            return [];
        }

        var visiting = (await addressTypes.GetListAsync(t => t.Kind == AddressTypeKind.Visiting)).Select(t => t.Id).ToHashSet();
        return rows
            .GroupBy(a => a.RelationId)
            .ToDictionary(
                g => g.Key,
                g => (g.FirstOrDefault(a => a.AddressTypeId != null && visiting.Contains(a.AddressTypeId.Value))
                      ?? g.FirstOrDefault(a => a.AddressTypeId == null)
                      ?? g.OrderBy(a => a.City).First()).City);
    }
}
