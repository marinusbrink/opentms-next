using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace OpenTms.Platform.Accounts;

/// <summary>
/// Reads and writes the account links in the host database, whatever tenant is current (docs/designs/63-user-menu.md).
/// Each call switches to the host and runs in a unit of work of its own, as <see cref="SignIn.HostUserSignInLookupStore"/>.
/// Cross-tenant by design: the rows are host data without a tenant filter, and they carry ids and stamps only.
/// </summary>
public class HostUserAccountLinkStore : IUserAccountLinkStore, ITransientDependency
{
    private readonly IRepository<UserAccountLink, Guid> _repository;
    private readonly ICurrentTenant _currentTenant;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly IGuidGenerator _guidGenerator;
    private readonly IClock _clock;

    public HostUserAccountLinkStore(
        IRepository<UserAccountLink, Guid> repository,
        ICurrentTenant currentTenant,
        IUnitOfWorkManager unitOfWorkManager,
        IGuidGenerator guidGenerator,
        IClock clock)
    {
        _repository = repository;
        _currentTenant = currentTenant;
        _unitOfWorkManager = unitOfWorkManager;
        _guidGenerator = guidGenerator;
        _clock = clock;
    }

    public async Task<IReadOnlyList<LinkedAccount>> GetLinkedAsync(Guid? tenantId, Guid userId)
    {
        // Cross-tenant by design: a link joins two tenants' accounts of one address; rows hold ids and stamps only —
        // docs/designs/63-user-menu.md, DEVIATION(constitution-1).
        using (_currentTenant.Change(null))
        using (var unitOfWork = _unitOfWorkManager.Begin(new AbpUnitOfWorkOptions { IsTransactional = false }, requiresNew: true))
        {
            var rows = await _repository.GetListAsync(l => l.UserTenantId == tenantId && l.UserId == userId);
            await unitOfWork.CompleteAsync();
            return rows.Select(l => new LinkedAccount(l.LinkedTenantId, l.LinkedUserId, l.LinkedSecurityStamp)).ToList();
        }
    }

    public async Task RememberAsync(LinkedAccount first, LinkedAccount second)
    {
        using (_currentTenant.Change(null))
        using (var unitOfWork = _unitOfWorkManager.Begin(new AbpUnitOfWorkOptions { IsTransactional = true }, requiresNew: true))
        {
            await UpsertAsync(first, second);
            await UpsertAsync(second, first);
            await unitOfWork.CompleteAsync();
        }
    }

    private async Task UpsertAsync(LinkedAccount from, LinkedAccount to)
    {
        var row = await _repository.FirstOrDefaultAsync(l =>
            l.UserTenantId == from.TenantId && l.UserId == from.UserId && l.LinkedTenantId == to.TenantId);
        if (row == null)
        {
            await _repository.InsertAsync(new UserAccountLink(
                _guidGenerator.Create(), from.TenantId, from.UserId, to.TenantId, to.UserId, to.SecurityStamp, _clock.Now));
        }
        else
        {
            row.Renew(to.UserId, to.SecurityStamp, _clock.Now);
            await _repository.UpdateAsync(row);
        }
    }

    public async Task ForgetAsync(Guid? tenantId, Guid userId, Guid? linkedTenantId)
    {
        using (_currentTenant.Change(null))
        using (var unitOfWork = _unitOfWorkManager.Begin(new AbpUnitOfWorkOptions { IsTransactional = true }, requiresNew: true))
        {
            var forward = await _repository.GetListAsync(l =>
                l.UserTenantId == tenantId && l.UserId == userId && l.LinkedTenantId == linkedTenantId);
            foreach (var row in forward)
            {
                var linkedUserId = row.LinkedUserId;
                await _repository.DeleteDirectAsync(l =>
                    l.UserTenantId == linkedTenantId && l.UserId == linkedUserId && l.LinkedTenantId == tenantId);
                await _repository.DeleteAsync(row);
            }

            await unitOfWork.CompleteAsync();
        }
    }

    public async Task DeleteUserAsync(Guid? tenantId, Guid userId)
    {
        using (_currentTenant.Change(null))
        using (var unitOfWork = _unitOfWorkManager.Begin(new AbpUnitOfWorkOptions { IsTransactional = true }, requiresNew: true))
        {
            await _repository.DeleteDirectAsync(l =>
                (l.UserTenantId == tenantId && l.UserId == userId) || (l.LinkedTenantId == tenantId && l.LinkedUserId == userId));
            await unitOfWork.CompleteAsync();
        }
    }

    public async Task DeleteTenantAsync(Guid tenantId)
    {
        using (_currentTenant.Change(null))
        using (var unitOfWork = _unitOfWorkManager.Begin(new AbpUnitOfWorkOptions { IsTransactional = true }, requiresNew: true))
        {
            await _repository.DeleteDirectAsync(l => l.UserTenantId == tenantId || l.LinkedTenantId == tenantId);
            await unitOfWork.CompleteAsync();
        }
    }
}
