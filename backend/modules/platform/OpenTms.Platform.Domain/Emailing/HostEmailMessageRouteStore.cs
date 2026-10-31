using System;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;

namespace OpenTms.Platform.Emailing;

/// <summary>Reads and writes <see cref="EmailMessageRoute"/> in the host database, whatever tenant is current.</summary>
public interface IEmailMessageRouteStore
{
    Task<EmailMessageRoute?> FindAsync(string providerMessageId);

    /// <summary>Idempotent: a route that already exists for the id is kept.</summary>
    Task AddAsync(string providerMessageId, Guid? messageTenantId, Guid emailMessageId);
}

/// <summary>
/// The host-database store of the routes (design 64). Each call switches to the host and runs in a unit of work of its
/// own, like the sign-in lookup: the rows are host data without a tenant filter and hold ids only.
/// </summary>
public class HostEmailMessageRouteStore : IEmailMessageRouteStore, ITransientDependency
{
    private readonly IRepository<EmailMessageRoute, Guid> _repository;
    private readonly ICurrentTenant _currentTenant;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly IGuidGenerator _guidGenerator;

    public HostEmailMessageRouteStore(
        IRepository<EmailMessageRoute, Guid> repository,
        ICurrentTenant currentTenant,
        IUnitOfWorkManager unitOfWorkManager,
        IGuidGenerator guidGenerator)
    {
        _repository = repository;
        _currentTenant = currentTenant;
        _unitOfWorkManager = unitOfWorkManager;
        _guidGenerator = guidGenerator;
    }

    public async Task<EmailMessageRoute?> FindAsync(string providerMessageId)
    {
        // Cross-tenant by design: a delivery report arrives before any tenant is known and the route names it; rows hold
        // ids only — docs/designs/64-email-delivery-postmark.md, DEVIATION(constitution-1).
        using (_currentTenant.Change(null))
        using (var unitOfWork = _unitOfWorkManager.Begin(new AbpUnitOfWorkOptions { IsTransactional = false }, requiresNew: true))
        {
            var route = await _repository.FindAsync(r => r.ProviderMessageId == providerMessageId);
            await unitOfWork.CompleteAsync();
            return route;
        }
    }

    public async Task AddAsync(string providerMessageId, Guid? messageTenantId, Guid emailMessageId)
    {
        using (_currentTenant.Change(null))
        using (var unitOfWork = _unitOfWorkManager.Begin(new AbpUnitOfWorkOptions { IsTransactional = true }, requiresNew: true))
        {
            if (await _repository.FindAsync(r => r.ProviderMessageId == providerMessageId) == null)
            {
                await _repository.InsertAsync(new EmailMessageRoute(_guidGenerator.Create(), providerMessageId, messageTenantId, emailMessageId));
            }

            await unitOfWork.CompleteAsync();
        }
    }
}
