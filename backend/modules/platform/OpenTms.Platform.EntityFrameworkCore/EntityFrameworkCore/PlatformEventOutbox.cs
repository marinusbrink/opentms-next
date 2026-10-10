using System.Threading.Tasks;
using OpenTms.Platform.Events.Outbox;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.DistributedEvents;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;

namespace OpenTms.Platform.EntityFrameworkCore;

/// <summary>
/// ABP's EF Core outbox store on the Platform context, with two additions: every stored event is
/// stamped with the tenant it was raised for, and the commit of the unit of work signals the
/// dispatcher. The DbContext is resolved per call, so the row lands in the current tenant's own
/// database (or in the host database for a shared-tier tenant).
/// </summary>
public class PlatformEventOutbox : DbContextEventOutbox<PlatformDbContext>, ITransientDependency
{
    private readonly ICurrentTenant _currentTenant;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly IOutboxSignal _signal;

    public PlatformEventOutbox(
        IDbContextProvider<PlatformDbContext> dbContextProvider,
        ICurrentTenant currentTenant,
        IUnitOfWorkManager unitOfWorkManager,
        IOutboxSignal signal)
        : base(dbContextProvider)
    {
        _currentTenant = currentTenant;
        _unitOfWorkManager = unitOfWorkManager;
        _signal = signal;
    }

    [UnitOfWork]
    public override async Task EnqueueAsync(OutgoingEventInfo outgoingEvent)
    {
        var tenantId = _currentTenant.Id;
        outgoingEvent.SetTenantId(tenantId);

        await base.EnqueueAsync(outgoingEvent);

        /* OnCompleted runs after the commit; a child unit of work forwards it to the outer one. */
        _unitOfWorkManager.Current?.OnCompleted(() =>
        {
            _signal.Notify(tenantId);
            return Task.CompletedTask;
        });
    }
}
