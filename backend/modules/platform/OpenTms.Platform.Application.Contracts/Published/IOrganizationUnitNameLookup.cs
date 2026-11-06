using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OpenTms.Platform.Published;

/// <summary>
/// The display names of organisation units of the current tenant (design 75), for answers that name the unit by id; read per
/// request, so no module keeps a copy of a name. In-process only; unknown ids are simply absent, at most <see cref="MaxKeys"/>.
/// </summary>
public interface IOrganizationUnitNameLookup
{
    public const int MaxKeys = 500;

    Task<IReadOnlyDictionary<Guid, string>> GetAsync(IReadOnlyCollection<Guid> ids);
}
