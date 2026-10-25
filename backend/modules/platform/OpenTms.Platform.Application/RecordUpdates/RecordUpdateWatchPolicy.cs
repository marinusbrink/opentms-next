using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using OpenTms.Platform.DerivedValues;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.DependencyInjection;
using Volo.Abp.MultiTenancy;

namespace OpenTms.Platform.RecordUpdates;

/// <summary>
/// Which hub groups a client may join (design 49): only a registered target type, only with that type's watch
/// permission, at most <see cref="DerivedValueConsts.MaxWatchPerCall"/> records per call, and always in the caller's own
/// tenant (the group name carries <see cref="ICurrentTenant"/>, never a value from the client).
/// </summary>
public class RecordUpdateWatchPolicy : ITransientDependency
{
    private readonly IPermissionChecker _permissionChecker;
    private readonly ICurrentTenant _currentTenant;
    private readonly DerivedValueOptions _options;

    public RecordUpdateWatchPolicy(IPermissionChecker permissionChecker, ICurrentTenant currentTenant, IOptions<DerivedValueOptions> options)
    {
        _permissionChecker = permissionChecker;
        _currentTenant = currentTenant;
        _options = options.Value;
    }

    /// <summary>The group names to join or leave, or null when the call is not allowed.</summary>
    public virtual async Task<IReadOnlyList<string>?> ResolveGroupsAsync(string? targetType, IReadOnlyCollection<Guid>? targetIds)
    {
        var definition = _options.Find(targetType);
        var ids = (targetIds ?? []).Where(id => id != Guid.Empty).Distinct().ToList();
        if (definition == null || ids.Count > DerivedValueConsts.MaxWatchPerCall || !await _permissionChecker.IsGrantedAsync(definition.WatchPermission))
        {
            return null;
        }

        return ids.Select(id => RecordUpdate.GroupName(_currentTenant.Id, definition.TargetType, id)).ToList();
    }
}
