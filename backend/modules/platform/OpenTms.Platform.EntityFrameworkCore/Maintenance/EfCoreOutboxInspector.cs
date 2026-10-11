using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using OpenTms.Platform.Maintenance.Outbox;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;

namespace OpenTms.Platform.EntityFrameworkCore.Maintenance;

/// <summary>
/// Counts the waiting events in one database's outbox. The context is resolved inside the tenant's
/// scope, so the connection string of that tenant (or the host) is used: the outbox table is per
/// database (AD-16).
/// </summary>
public class EfCoreOutboxInspector : IOutboxInspector, ITransientDependency
{
    private readonly IDbContextProvider<PlatformDbContext> _dbContextProvider;
    private readonly ICurrentTenant _currentTenant;
    private readonly IUnitOfWorkManager _unitOfWorkManager;

    public EfCoreOutboxInspector(
        IDbContextProvider<PlatformDbContext> dbContextProvider,
        ICurrentTenant currentTenant,
        IUnitOfWorkManager unitOfWorkManager)
    {
        _dbContextProvider = dbContextProvider;
        _currentTenant = currentTenant;
        _unitOfWorkManager = unitOfWorkManager;
    }

    public async Task<OutboxSnapshot> InspectAsync(Guid? tenantId, CancellationToken cancellationToken = default)
    {
        using (_currentTenant.Change(tenantId))
        using (var unitOfWork = _unitOfWorkManager.Begin(new AbpUnitOfWorkOptions(), requiresNew: true))
        {
            var dbContext = await _dbContextProvider.GetDbContextAsync();
            var pending = dbContext.OutgoingEvents.AsNoTracking();

            var count = await pending.LongCountAsync(cancellationToken);
            DateTime? oldest = count == 0
                ? null
                : await pending.OrderBy(e => e.CreationTime).Select(e => (DateTime?)e.CreationTime).FirstOrDefaultAsync(cancellationToken);

            await unitOfWork.CompleteAsync(cancellationToken);
            return new OutboxSnapshot(count, oldest);
        }
    }
}
