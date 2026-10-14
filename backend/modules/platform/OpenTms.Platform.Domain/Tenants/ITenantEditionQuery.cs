using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OpenTms.Platform.Tenants;

/// <summary>
/// Reads <c>AbpTenants.EditionId</c> across tenants (an extension column, so only the EF Core adapter can
/// query it). Soft-deleted tenants do not count.
/// </summary>
public interface ITenantEditionQuery
{
    Task<IReadOnlyDictionary<Guid, int>> CountTenantsByEditionAsync(IReadOnlyCollection<Guid> editionIds);
}
