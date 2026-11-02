using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using OpenTms.Platform.RecordUpdates;
using Volo.Abp.DependencyInjection;

namespace OpenTms.RecordUpdates;

/// <summary>
/// Delivers record updates to the hub's groups (design 49): the record's own group and, since design 67, the record
/// type's group. With <c>SignalR:Redis:Configuration</c> set, SignalR's Redis backplane carries the message to the
/// instance the browser is connected to (AP-13); otherwise in process. In process, a connection in both groups gets the
/// message once; over the backplane it may get it twice, which a client's refetch absorbs.
/// </summary>
[Dependency(ReplaceServices = true)]
[ExposeServices(typeof(IRecordUpdateTransport))]
public class SignalRRecordUpdateTransport : IRecordUpdateTransport, ISingletonDependency
{
    private readonly IHubContext<RecordUpdatesHub> _hub;

    public SignalRRecordUpdateTransport(IHubContext<RecordUpdatesHub> hub)
    {
        _hub = hub;
    }

    public Task SendAsync(RecordUpdate update)
        => _hub.Clients.Groups(update.Group, update.TypeGroup).SendAsync(RecordUpdatesHub.UpdatedMethod, ToMessage(update));

    /// <summary>The <c>recordUpdated</c> payload: ids, version, status and the routing hints (null when none); never data.</summary>
    public static object ToMessage(RecordUpdate update) => new
    {
        targetType = update.TargetType,
        targetId = update.TargetId,
        version = update.Version,
        status = update.Status,
        hints = update.Hints == null
            ? null
            : new
            {
                from = update.Hints.From,
                till = update.Hints.Till,
                organizationUnitId = update.Hints.OrganizationUnitId,
                resourceIds = update.Hints.ResourceIds,
                removed = update.Hints.Removed
            }
    };
}
