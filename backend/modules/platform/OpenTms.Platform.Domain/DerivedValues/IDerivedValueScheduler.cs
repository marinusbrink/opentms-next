using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OpenTms.Platform.DerivedValues;

/// <summary>
/// The one call a module makes when a save changes what a record's derived values depend on (design 49, flow 2 A):
/// it bumps the record's version and writes the request event to the outbox, both in the save's own transaction, so a
/// rolled-back save requests nothing. Refused outside a transactional unit of work and for an unregistered type.
/// </summary>
public interface IDerivedValueScheduler
{
    /// <summary>Returns the new version the stored values will be calculated for.</summary>
    Task<long> RequestAsync(string targetType, Guid targetId, string? reason = null);

    /// <summary>The bulk form (a retroactive rate change); at most <see cref="DerivedValueConsts.MaxTargetsPerRequest"/> ids.</summary>
    Task<int> RequestManyAsync(string targetType, IReadOnlyCollection<Guid> targetIds, string? reason = null);
}
