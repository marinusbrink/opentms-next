using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using OpenTms.Platform.RecordUpdates;
using Volo.Abp.DependencyInjection;

namespace OpenTms.RecordUpdates;

/// <summary>
/// Delivers record updates to the hub's groups (design 49). With <c>SignalR:Redis:Configuration</c> set, SignalR's Redis
/// backplane carries the message to the instance the browser is connected to (AP-13); otherwise in process.
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
        => _hub.Clients.Group(update.Group).SendAsync(RecordUpdatesHub.UpdatedMethod, new
        {
            targetType = update.TargetType,
            targetId = update.TargetId,
            version = update.Version,
            status = update.Status
        });
}
