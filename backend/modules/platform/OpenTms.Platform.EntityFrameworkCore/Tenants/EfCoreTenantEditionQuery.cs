using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using OpenTms.Platform.Tenants;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Volo.Abp.TenantManagement;

namespace OpenTms.Platform.EntityFrameworkCore.Tenants;

/// <summary>
/// Groups tenants by their <c>EditionId</c> column. The column is an ABP entity extension of
/// <c>Tenant</c> (mapped by the application's context), so it is reached through <see cref="EF.Property{TProperty}"/>;
/// one GROUP BY query, whatever the number of tenants.
/// </summary>
public class EfCoreTenantEditionQuery : ITenantEditionQuery, ITransientDependency
{
    private readonly IRepository<Tenant, Guid> _tenantRepository;
    private readonly ICurrentTenant _currentTenant;

    public EfCoreTenantEditionQuery(IRepository<Tenant, Guid> tenantRepository, ICurrentTenant currentTenant)
    {
        _tenantRepository = tenantRepository;
        _currentTenant = currentTenant;
    }

    public async Task<IReadOnlyDictionary<Guid, int>> CountTenantsByEditionAsync(IReadOnlyCollection<Guid> editionIds)
    {
        if (editionIds.Count == 0)
        {
            return new Dictionary<Guid, int>();
        }

        using (_currentTenant.Change(null))
        {
            var ids = editionIds.Select(id => (Guid?)id).ToList();
            var query = await _tenantRepository.GetQueryableAsync();
            var counts = await query
                .Select(t => EF.Property<Guid?>(t, TenantProperties.EditionId))
                .Where(editionId => ids.Contains(editionId))
                .GroupBy(editionId => editionId)
                .Select(g => new { EditionId = g.Key, Count = g.Count() })
                .ToListAsync();

            return counts.ToDictionary(c => c.EditionId!.Value, c => c.Count);
        }
    }
}
