using System;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.Domain.Repositories;

namespace OpenTms.Integrations.OrderIntake.Profiles;

/// <summary>The import profile repository with the throttled use stamp (design 72 build note 19, review minor 2).</summary>
public interface IImportProfileRepository : IRepository<ImportProfile, Guid>
{
    /// <summary>
    /// Sets <see cref="ImportProfile.LastUsedAt"/> to <paramref name="at"/> in one set-based update, only when it is null or
    /// older than <paramref name="staleBefore"/>; outside the aggregate's concurrency check. True when a row was written.
    /// </summary>
    Task<bool> RecordUseAsync(Guid id, DateTime at, DateTime staleBefore, CancellationToken cancellationToken = default);
}
