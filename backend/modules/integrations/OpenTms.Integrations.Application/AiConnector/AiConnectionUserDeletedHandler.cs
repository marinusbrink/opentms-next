using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTms.Integrations.Settings;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Entities.Events;
using Volo.Abp.EventBus;
using Volo.Abp.Identity;
using Volo.Abp.Linq;
using Volo.Abp.MultiTenancy;
using Volo.Abp.SettingManagement;
using Volo.Abp.Settings;
using Volo.Abp.Uow;

namespace OpenTms.Integrations.AiConnector;

/// <summary>
/// Art. 17 of a connection (design 74 "GDPR"): once a user's deletion has committed, the user's connections are revoked
/// and removed, and the acceptance forgets the user. The event is the one <c>UserSignInLookupSynchronizer</c> uses; the
/// work runs after commit because the host and tenant databases never share a transaction. A second delivery finds
/// nothing left to do. A failure is logged, never thrown at a committed change.
/// </summary>
public class AiConnectionUserDeletedHandler : ILocalEventHandler<EntityChangedEventData<IdentityUser>>, ITransientDependency
{
    private const int BatchSize = 100;

    private readonly IServiceProvider _services;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly ILogger<AiConnectionUserDeletedHandler> _logger;

    public AiConnectionUserDeletedHandler(
        IServiceProvider services,
        IUnitOfWorkManager unitOfWorkManager,
        ILogger<AiConnectionUserDeletedHandler> logger)
    {
        _services = services;
        _unitOfWorkManager = unitOfWorkManager;
        _logger = logger;
    }

    public Task HandleEventAsync(EntityChangedEventData<IdentityUser> eventData)
    {
        var user = eventData.Entity;
        if (eventData is not EntityDeletedEventData<IdentityUser> && !user.IsDeleted)
        {
            return Task.CompletedTask;
        }

        var tenantId = user.TenantId;
        var userId = user.Id;

        var unitOfWork = _unitOfWorkManager.Current;
        if (unitOfWork == null)
        {
            return RemoveAsync(_services, tenantId, userId);
        }

        /* This handler's scope is gone once the unit of work has committed; the unit of work's own services are not. */
        var services = unitOfWork.ServiceProvider;
        unitOfWork.OnCompleted(() => RemoveAsync(services, tenantId, userId));
        return Task.CompletedTask;
    }

    private async Task RemoveAsync(IServiceProvider services, Guid? tenantId, Guid userId)
    {
        try
        {
            var currentTenant = services.GetRequiredService<ICurrentTenant>();
            var connections = services.GetRequiredService<IAiConnectionRepository>();
            var revoker = services.GetRequiredService<AiConnectionAuthorizationRevoker>();
            var queries = services.GetRequiredService<IAsyncQueryableExecuter>();
            var unitOfWorks = services.GetRequiredService<IUnitOfWorkManager>();
            var settingProvider = services.GetRequiredService<ISettingProvider>();
            var settingManager = services.GetRequiredService<ISettingManager>();

            using (currentTenant.Change(tenantId))
            {
                var removed = 0;
                while (true)
                {
                    using var unitOfWork = unitOfWorks.Begin(new AbpUnitOfWorkOptions(isTransactional: true), requiresNew: true);
                    var query = (await connections.GetQueryableAsync()).Where(c => c.UserId == userId).OrderBy(c => c.Id).Take(BatchSize);
                    var batch = await queries.ToListAsync(query);
                    foreach (var connection in batch)
                    {
                        await revoker.RevokeAsync(connection.AuthorizationId);
                    }

                    await connections.DeleteManyAsync(batch);
                    await unitOfWork.CompleteAsync();
                    removed += batch.Count;
                    if (batch.Count < BatchSize)
                    {
                        break;
                    }
                }

                if (tenantId != null)
                {
                    using var unitOfWork = unitOfWorks.Begin(new AbpUnitOfWorkOptions(isTransactional: true), requiresNew: true);
                    if (await settingProvider.GetOrNullAsync(IntegrationsSettings.AiConnectorAcceptedByUserId) == userId.ToString())
                    {
                        await settingManager.SetForCurrentTenantAsync(IntegrationsSettings.AiConnectorAcceptedByUserId, null);
                    }

                    await unitOfWork.CompleteAsync();
                }

                if (removed > 0)
                {
                    _logger.LogInformation("Removed {Count} AI connections of deleted user {UserId} (tenant {TenantId})", removed, userId, tenantId);
                }
            }
        }
        catch (Exception exception)
        {
            /* A deleted user's connection row stays until someone repeats this: it blocks nothing, but art. 17 is not met. */
            _logger.LogError(exception, "AI connections of deleted user {UserId} (tenant {TenantId}) were not removed", userId, tenantId);
        }
    }
}
