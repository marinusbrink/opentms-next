using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using OpenTms.MasterData.Permissions;
using OpenTms.Platform.Features;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Features;
using Volo.Abp.Linq;
using Volo.Abp.Validation;

namespace OpenTms.MasterData.Transport;

/// <summary>
/// The published resolver of design 48 (§3.15 CHANGE: no find-or-create). One <c>IN</c> over the unique index on the
/// normalised code; archived codes never match; it never creates anything. Like a picker it needs only the Master
/// data app feature (D12), and the ProductRelationCodes view permission.
/// </summary>
[RequiresFeature(AppFeatures.MasterData.Default)]
[Authorize(MasterDataPermissions.ProductRelationCodes.Default)]
public class ProductRelationCodeResolver : IProductRelationCodeResolver, ITransientDependency
{
    private readonly IRepository<ProductRelationCode, Guid> _codes;
    private readonly IAsyncQueryableExecuter _executer;

    public ProductRelationCodeResolver(IRepository<ProductRelationCode, Guid> codes, IAsyncQueryableExecuter executer)
    {
        _codes = codes;
        _executer = executer;
    }

    [Authorize(MasterDataPermissions.ProductRelationCodes.Default)]
    public virtual async Task<List<ProductRelationCodeMatchDto>> ResolveAsync(IReadOnlyCollection<string> codes)
    {
        var normalized = codes.Select(ProductRelationCode.Normalize).Where(c => c != null).Select(c => c!).Distinct().ToList();
        if (normalized.Count == 0)
        {
            return [];
        }

        if (normalized.Count > MasterDataConsts.MaxResolverCodes)
        {
            throw new AbpValidationException($"At most {MasterDataConsts.MaxResolverCodes} codes per call.");
        }

        var query = await _codes.GetQueryableAsync();
        var matches = await _executer.ToListAsync(query.Where(c => !c.IsArchived && normalized.Contains(c.NormalizedCode)));
        return matches.Select(c => new ProductRelationCodeMatchDto
        {
            Id = c.Id,
            Code = c.Code,
            NormalizedCode = c.NormalizedCode,
            RelationId = c.RelationId,
            ProductId = c.ProductId,
            TransportUnitId = c.TransportUnitId
        }).ToList();
    }
}
