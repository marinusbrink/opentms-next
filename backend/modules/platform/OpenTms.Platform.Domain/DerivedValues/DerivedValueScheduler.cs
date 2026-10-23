using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Uow;

namespace OpenTms.Platform.DerivedValues;

public class DerivedValueScheduler : IDerivedValueScheduler, ITransientDependency
{
    private readonly IDerivedValueStateStore _store;
    private readonly IDistributedEventBus _eventBus;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly DerivedValueOptions _options;

    public DerivedValueScheduler(
        IDerivedValueStateStore store,
        IDistributedEventBus eventBus,
        IUnitOfWorkManager unitOfWorkManager,
        IOptions<DerivedValueOptions> options)
    {
        _store = store;
        _eventBus = eventBus;
        _unitOfWorkManager = unitOfWorkManager;
        _options = options.Value;
    }

    public virtual async Task<long> RequestAsync(string targetType, Guid targetId, string? reason = null)
    {
        Validate(targetType, reason);
        return await RequestOneAsync(targetType, targetId, reason);
    }

    public virtual async Task<int> RequestManyAsync(string targetType, IReadOnlyCollection<Guid> targetIds, string? reason = null)
    {
        Validate(targetType, reason);
        var ids = (targetIds ?? []).Where(id => id != Guid.Empty).Distinct().ToList();
        if (ids.Count > DerivedValueConsts.MaxTargetsPerRequest)
        {
            throw new BusinessException(DerivedValueErrorCodes.TooManyTargets).WithData("max", DerivedValueConsts.MaxTargetsPerRequest);
        }

        /* One upsert for all ids, then one outbox row per thousand records, all in the caller's transaction. */
        var versions = await _store.RequestManyAsync(targetType, ids);
        foreach (var chunk in versions.Chunk(DerivedValueConsts.MaxTargetsPerEvent))
        {
            await _eventBus.PublishAsync(new DerivedValuesRequestedEto
            {
                TargetType = targetType,
                Targets = chunk.Select(v => new DerivedValueTargetVersion { TargetId = v.Key, Version = v.Value }).ToList(),
                Reason = reason
            });
        }

        return versions.Count;
    }

    private async Task<long> RequestOneAsync(string targetType, Guid targetId, string? reason)
    {
        var version = await _store.RequestAsync(targetType, targetId);
        await _eventBus.PublishAsync(new DerivedValuesRequestedEto
        {
            TargetType = targetType,
            Targets = [new DerivedValueTargetVersion { TargetId = targetId, Version = version }],
            Reason = reason
        });
        return version;
    }

    private void Validate(string targetType, string? reason)
    {
        if (_options.Find(targetType)?.CalculatorType == null)
        {
            throw new BusinessException(DerivedValueErrorCodes.UnknownTargetType).WithData("targetType", targetType ?? "");
        }

        if (reason != null)
        {
            Check.Length(reason, nameof(reason), DerivedValueConsts.MaxReasonLength);
        }

        /* The version bump and the event must commit or roll back with the fact (as the outbox itself). */
        if (_unitOfWorkManager.Current?.Options.IsTransactional != true)
        {
            throw new BusinessException(DerivedValueErrorCodes.RequiresTransaction);
        }
    }
}
