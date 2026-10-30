using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Entities.Events;
using Volo.Abp.EventBus;
using Volo.Abp.Identity;
using Volo.Abp.TenantManagement;
using Volo.Abp.Uow;

namespace OpenTms.Platform.Accounts;

/// <summary>
/// Removes the account links of a deleted user or tenant (docs/designs/63-user-menu.md). The links live in the host
/// database, so the write runs once the deleting unit of work has committed, as the sign-in lookup's. A link left behind
/// after a failure is harmless: the menu lists accounts from the sign-in lookup and a switch reads the user again.
/// </summary>
public class UserAccountLinkCleanup :
    ILocalEventHandler<EntityDeletedEventData<IdentityUser>>,
    ILocalEventHandler<EntityDeletedEventData<Tenant>>,
    ITransientDependency
{
    private readonly IUserAccountLinkStore _store;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly ILogger<UserAccountLinkCleanup> _logger;

    public UserAccountLinkCleanup(IUserAccountLinkStore store, IUnitOfWorkManager unitOfWorkManager, ILogger<UserAccountLinkCleanup> logger)
    {
        _store = store;
        _unitOfWorkManager = unitOfWorkManager;
        _logger = logger;
    }

    public Task HandleEventAsync(EntityDeletedEventData<IdentityUser> eventData)
    {
        var tenantId = eventData.Entity.TenantId;
        var userId = eventData.Entity.Id;
        return AfterCommitAsync(store => store.DeleteUserAsync(tenantId, userId), "user {UserId} (tenant {TenantId})", userId, tenantId);
    }

    public Task HandleEventAsync(EntityDeletedEventData<Tenant> eventData)
    {
        var tenantId = eventData.Entity.Id;
        return AfterCommitAsync(store => store.DeleteTenantAsync(tenantId), "tenant {TenantId}", tenantId);
    }

    private Task AfterCommitAsync(Func<IUserAccountLinkStore, Task> write, string subject, params object?[] ids)
    {
        async Task WriteAsync(IUserAccountLinkStore store)
        {
            try
            {
                await write(store);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Account links not removed for " + subject, ids);
            }
        }

        var unitOfWork = _unitOfWorkManager.Current;
        if (unitOfWork == null)
        {
            return WriteAsync(_store);
        }

        var services = unitOfWork.ServiceProvider;
        unitOfWork.OnCompleted(() => WriteAsync(services.GetRequiredService<IUserAccountLinkStore>()));
        return Task.CompletedTask;
    }
}
