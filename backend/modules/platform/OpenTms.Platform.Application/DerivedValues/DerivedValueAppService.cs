using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using OpenTms.Platform.Permissions;
using Volo.Abp;

namespace OpenTms.Platform.DerivedValues;

/// <summary>
/// The two endpoints of design 49: the states a grid or form shows its "calculating" indicator from, and the bulk
/// re-run. The states need the target type's own watch permission, checked per call (the type is a parameter).
/// </summary>
public class DerivedValueAppService : PlatformAppServiceBase, IDerivedValueAppService
{
    private readonly IDerivedValueStateStore _store;
    private readonly IDerivedValueScheduler _scheduler;
    private readonly DerivedValueOptions _options;

    public DerivedValueAppService(IDerivedValueStateStore store, IDerivedValueScheduler scheduler, IOptions<DerivedValueOptions> options)
    {
        _store = store;
        _scheduler = scheduler;
        _options = options.Value;
    }

    [Authorize]
    public virtual async Task<List<DerivedValueStateDto>> GetStatesAsync(DerivedValueStatesRequestDto input)
    {
        var definition = _options.Find(input.TargetType)
            ?? throw new BusinessException(DerivedValueErrorCodes.UnknownTargetType).WithData("targetType", input.TargetType);
        await AuthorizationService.CheckAsync(definition.WatchPermission);

        var ids = input.TargetIds.Distinct().ToList();
        if (ids.Count > DerivedValueConsts.MaxStatesPerRead)
        {
            throw new BusinessException(DerivedValueErrorCodes.TooManyTargets).WithData("max", DerivedValueConsts.MaxStatesPerRead);
        }

        var states = await _store.GetListAsync(input.TargetType, ids);
        return states.Select(s => new DerivedValueStateDto
        {
            TargetId = s.TargetId,
            RequestedVersion = s.RequestedVersion,
            CalculatedVersion = s.CalculatedVersion,
            Status = s.Status,
            LastErrorCode = s.LastErrorCode,
            CalculatedAt = s.CalculatedAt
        }).ToList();
    }

    [Authorize(PlatformPermissions.DerivedValues.Recalculate)]
    public virtual Task<int> RecalculateAsync(RecalculateDerivedValuesDto input)
        => _scheduler.RequestManyAsync(input.TargetType, input.TargetIds, "Recalculate");
}
