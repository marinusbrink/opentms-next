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

namespace OpenTms.Platform.SignIn;

/// <summary>
/// Keeps the sign-in lookup in step with the users (docs/designs/62-sign-in-tenant-detection.md): every created, changed
/// or deleted user rewrites its row, a deleted tenant loses its rows. The lookup lives in the host database and a user
/// in the tenant's own, so the two never share a transaction: the row is written once the user's unit of work has
/// committed, and a row that is wrong after a crash costs nothing worse than a stale hint (the sign-in page checks the
/// credentials in the tenant's database) or a user the page cannot find until the next DbMigrator run rebuilds the
/// lookup (<see cref="UserSignInLookupDataSeedContributor"/>).
/// </summary>
public class UserSignInLookupSynchronizer :
    ILocalEventHandler<EntityChangedEventData<IdentityUser>>,
    ILocalEventHandler<EntityDeletedEventData<Tenant>>,
    ITransientDependency
{
    private readonly IUserSignInLookupStore _store;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly ILogger<UserSignInLookupSynchronizer> _logger;

    public UserSignInLookupSynchronizer(
        IUserSignInLookupStore store,
        IUnitOfWorkManager unitOfWorkManager,
        ILogger<UserSignInLookupSynchronizer> logger)
    {
        _store = store;
        _unitOfWorkManager = unitOfWorkManager;
        _logger = logger;
    }

    public Task HandleEventAsync(EntityChangedEventData<IdentityUser> eventData)
    {
        var user = eventData.Entity;
        var removed = eventData is EntityDeletedEventData<IdentityUser> || user.IsDeleted;
        var tenantId = user.TenantId;
        var userId = user.Id;
        var emailHash = removed ? null : UserSignInLookupHash.OfOptional(user.NormalizedEmail);

        return AfterCommitAsync(
            store => store.SyncUserAsync(tenantId, userId, emailHash),
            "user {UserId} (tenant {TenantId})", userId, tenantId);
    }

    public Task HandleEventAsync(EntityDeletedEventData<Tenant> eventData)
    {
        var tenantId = eventData.Entity.Id;
        return AfterCommitAsync(store => store.DeleteTenantAsync(tenantId), "tenant {TenantId}", tenantId);
    }

    /// <summary>
    /// Runs the write once the current unit of work has committed (now, without one); a failure is logged, never thrown at
    /// a committed change. The store comes from the unit of work's own services then: this handler's scope is gone by then.
    /// </summary>
    private Task AfterCommitAsync(Func<IUserSignInLookupStore, Task> write, string subject, params object?[] ids)
    {
        async Task WriteAsync(IUserSignInLookupStore store)
        {
            try
            {
                await write(store);
            }
            catch (Exception exception)
            {
                /* The user cannot sign in until the next DbMigrator run rebuilds the lookup: someone has to act. */
                _logger.LogError(exception, "Sign-in lookup not updated for " + subject, ids);
            }
        }

        var unitOfWork = _unitOfWorkManager.Current;
        if (unitOfWork == null)
        {
            return WriteAsync(_store);
        }

        var services = unitOfWork.ServiceProvider;
        unitOfWork.OnCompleted(() => WriteAsync(services.GetRequiredService<IUserSignInLookupStore>()));
        return Task.CompletedTask;
    }
}
