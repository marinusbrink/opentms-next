using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTms.Platform.DerivedValues;
using Volo.Abp.DependencyInjection;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;

namespace OpenTms.Platform.RecordUpdates;

/// <summary>
/// Tells the screens that show a record that it changed (design 49, flow 2 D). Inside a unit of work the notice is sent
/// after it commits (a rolled-back change tells nobody); outside one, at once. A lost notice only delays a screen until
/// its next fetch, so delivery is best effort and never fails the caller.
/// </summary>
public interface IRecordUpdateNotifier
{
    Task NotifyAsync(string targetType, Guid targetId, long? version = null, DerivedValueStatus? status = null);
}

public class RecordUpdateNotifier : IRecordUpdateNotifier, ITransientDependency
{
    public ILogger<RecordUpdateNotifier> Logger { get; set; } = NullLogger<RecordUpdateNotifier>.Instance;

    private readonly IRecordUpdateTransport _transport;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly ICurrentTenant _currentTenant;

    public RecordUpdateNotifier(IRecordUpdateTransport transport, IUnitOfWorkManager unitOfWorkManager, ICurrentTenant currentTenant)
    {
        _transport = transport;
        _unitOfWorkManager = unitOfWorkManager;
        _currentTenant = currentTenant;
    }

    public virtual Task NotifyAsync(string targetType, Guid targetId, long? version = null, DerivedValueStatus? status = null)
    {
        var update = new RecordUpdate(_currentTenant.Id, targetType, targetId, version, status);
        var unitOfWork = _unitOfWorkManager.Current;
        if (unitOfWork != null)
        {
            unitOfWork.OnCompleted(() => SendAsync(update));
            return Task.CompletedTask;
        }

        return SendAsync(update);
    }

    private async Task SendAsync(RecordUpdate update)
    {
        try
        {
            await _transport.SendAsync(update);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Record update for {TargetType} {TargetId} of tenant {TenantId} was not delivered.", update.TargetType, update.TargetId, update.TenantId);
        }
    }
}
