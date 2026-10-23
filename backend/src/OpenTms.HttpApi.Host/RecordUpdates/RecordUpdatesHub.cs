using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using OpenTms.Platform.RecordUpdates;
using Volo.Abp.AspNetCore.SignalR;

namespace OpenTms.RecordUpdates;

/// <summary>
/// The live record-update hub of design 49, at <c>/signalr-hubs/record-updates</c> (ABP's convention for an
/// <see cref="AbpHub"/>). A screen watches the records it shows; the server pushes <c>recordUpdated</c> with ids, a
/// version and a status, never data. Who may join which group is <see cref="RecordUpdateWatchPolicy"/>'s decision.
/// </summary>
[Authorize]
public class RecordUpdatesHub : AbpHub
{
    public const string UpdatedMethod = "recordUpdated";
    public const string NotAllowedCode = "RecordUpdates:NotAllowed";

    private readonly RecordUpdateWatchPolicy _policy;

    public RecordUpdatesHub(RecordUpdateWatchPolicy policy)
    {
        _policy = policy;
    }

    public virtual async Task Watch(string targetType, List<Guid> targetIds)
    {
        foreach (var group in await GroupsAsync(targetType, targetIds))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, group);
        }
    }

    public virtual async Task Unwatch(string targetType, List<Guid> targetIds)
    {
        foreach (var group in await GroupsAsync(targetType, targetIds))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, group);
        }
    }

    private async Task<IReadOnlyList<string>> GroupsAsync(string targetType, List<Guid> targetIds)
        => await _policy.ResolveGroupsAsync(targetType, targetIds) ?? throw new HubException(NotAllowedCode);
}
