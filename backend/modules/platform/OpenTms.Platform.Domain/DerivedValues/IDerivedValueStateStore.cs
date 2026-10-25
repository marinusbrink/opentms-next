using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OpenTms.Platform.DerivedValues;

/// <summary>
/// The atomic statements behind design 49, on the Platform's own table, in the current unit of work's transaction.
/// The row locks they take order a request against a completing job (see the design, "Why the result is discarded").
/// </summary>
public interface IDerivedValueStateStore
{
    /// <summary>Upserts the state of (current tenant, type, id): RequestedVersion + 1, Pending. Returns the new version.</summary>
    Task<long> RequestAsync(string targetType, Guid targetId);

    /// <summary>The bulk form of <see cref="RequestAsync"/>: one statement for every id; returns each id's new version.</summary>
    Task<IReadOnlyDictionary<Guid, long>> RequestManyAsync(string targetType, IReadOnlyCollection<Guid> targetIds);

    /// <summary>Marks <paramref name="version"/> calculated, only if it is still the requested one. False = superseded.</summary>
    Task<bool> TryCompleteAsync(Guid stateId, long version);

    /// <summary>Marks the attempt for <paramref name="version"/> failed, only if it is still the requested one.</summary>
    Task<bool> TryFailAsync(Guid stateId, long version, string errorCode);

    Task<DerivedValueState?> FindAsync(string targetType, Guid targetId);

    Task<List<DerivedValueState>> GetListAsync(string targetType, IReadOnlyCollection<Guid> targetIds);
}
