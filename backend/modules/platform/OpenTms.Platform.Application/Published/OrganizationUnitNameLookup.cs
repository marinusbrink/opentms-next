using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using OpenTms.Platform.ReferenceData;
using Volo.Abp;
using Volo.Abp.Identity;
using Volo.Abp.Uow;

namespace OpenTms.Platform.Published;

/// <summary><see cref="IOrganizationUnitNameLookup"/>: one query under the tenant filter.</summary>
// Published in-process reader: its caller authorizes (design 75, IMetricQuery), so it is anonymous here and has no HTTP route.
[RemoteService(IsEnabled = false)]
[AllowAnonymous]
public class OrganizationUnitNameLookup : PlatformAppServiceBase, IOrganizationUnitNameLookup
{
    private readonly IOrganizationUnitRepository _units;

    public OrganizationUnitNameLookup(IOrganizationUnitRepository units)
    {
        _units = units;
    }

    [UnitOfWork(isTransactional: false)]
    public virtual async Task<IReadOnlyDictionary<Guid, string>> GetAsync(IReadOnlyCollection<Guid> ids)
    {
        var keys = (ids ?? []).Where(id => id != Guid.Empty).Distinct().ToList();
        if (keys.Count > IOrganizationUnitNameLookup.MaxKeys)
        {
            throw new BusinessException(ReferenceDataErrorCodes.TooManyRows).WithData("max", IOrganizationUnitNameLookup.MaxKeys);
        }

        if (keys.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var units = await _units.GetListAsync(keys);
        return units.ToDictionary(u => u.Id, u => u.DisplayName);
    }
}
