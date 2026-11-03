using System;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.Domain.Repositories;

namespace OpenTms.Integrations.OrderIntake;

/// <summary>The API client repository with the throttled use stamp (design 72 build note 19, review B1).</summary>
public interface IApiClientRepository : IRepository<ApiClient, Guid>
{
    /// <summary>
    /// Sets <see cref="ApiClient.LastUsedAt"/> to <paramref name="at"/> in one set-based update, only when it is null or
    /// older than <paramref name="staleBefore"/>. The aggregate's concurrency stamp is neither checked nor changed, so
    /// parallel calls of one client never conflict. True when a row was written.
    /// </summary>
    Task<bool> RecordUseAsync(Guid id, DateTime at, DateTime staleBefore, CancellationToken cancellationToken = default);
}
