using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;

namespace OpenTms.Platform.SignIn;

/// <summary>
/// Reads and writes the sign-in lookup in the host database, whatever tenant is current
/// (docs/designs/62-sign-in-tenant-detection.md). A user changes in the tenant's own database, so each call switches to
/// the host and runs in a unit of work of its own. Cross-tenant by design: the rows are host data without a tenant
/// filter, and they carry hashes and ids only.
/// </summary>
public class HostUserSignInLookupStore : IUserSignInLookupStore, ITransientDependency
{
    private readonly IRepository<UserSignInLookup, Guid> _repository;
    private readonly ICurrentTenant _currentTenant;
    private readonly IUnitOfWorkManager _unitOfWorkManager;

    public HostUserSignInLookupStore(
        IRepository<UserSignInLookup, Guid> repository,
        ICurrentTenant currentTenant,
        IUnitOfWorkManager unitOfWorkManager)
    {
        _repository = repository;
        _currentTenant = currentTenant;
        _unitOfWorkManager = unitOfWorkManager;
    }

    public async Task<IReadOnlyList<SignInCandidate>> FindAsync(string emailHash)
    {
        // Cross-tenant by design: the sign-in page asks before a tenant is known; rows hold hashes and ids only —
        // docs/designs/62-sign-in-tenant-detection.md, DEVIATION(constitution-1).
        using (_currentTenant.Change(null))
        using (var unitOfWork = _unitOfWorkManager.Begin(new AbpUnitOfWorkOptions { IsTransactional = false }, requiresNew: true))
        {
            var rows = await _repository.GetListAsync(r => r.EmailHash == emailHash);
            await unitOfWork.CompleteAsync();
            return rows.Select(r => new SignInCandidate(r.UserTenantId, r.Id)).ToList();
        }
    }

    public async Task SyncUserAsync(Guid? tenantId, Guid userId, string? emailHash)
    {
        try
        {
            await WriteUserAsync(tenantId, userId, emailHash);
        }
        catch (Exception)
        {
            /* Two changes of one user at once both found no row and both inserted; the loser finds it now. */
            await WriteUserAsync(tenantId, userId, emailHash);
        }
    }

    private async Task WriteUserAsync(Guid? tenantId, Guid userId, string? emailHash)
    {
        using (_currentTenant.Change(null))
        using (var unitOfWork = _unitOfWorkManager.Begin(new AbpUnitOfWorkOptions { IsTransactional = true }, requiresNew: true))
        {
            var row = await _repository.FindAsync(userId);
            if (emailHash == null)
            {
                if (row != null)
                {
                    await _repository.DeleteAsync(row);
                }
            }
            else if (row == null)
            {
                await _repository.InsertAsync(new UserSignInLookup(userId, tenantId, emailHash));
            }
            else if (row.EmailHash != emailHash)
            {
                row.ChangeEmailHash(emailHash);
                await _repository.UpdateAsync(row);
            }

            await unitOfWork.CompleteAsync();
        }
    }

    public async Task ReplaceTenantAsync(Guid? tenantId, IReadOnlyCollection<UserSignInLookupEntry> entries)
    {
        using (_currentTenant.Change(null))
        using (var unitOfWork = _unitOfWorkManager.Begin(new AbpUnitOfWorkOptions { IsTransactional = true }, requiresNew: true))
        {
            await _repository.DeleteDirectAsync(r => r.UserTenantId == tenantId);
            await _repository.InsertManyAsync(entries.Select(e => new UserSignInLookup(e.UserId, tenantId, e.EmailHash)));
            await unitOfWork.CompleteAsync();
        }
    }

    public async Task DeleteTenantAsync(Guid tenantId)
    {
        using (_currentTenant.Change(null))
        using (var unitOfWork = _unitOfWorkManager.Begin(new AbpUnitOfWorkOptions { IsTransactional = true }, requiresNew: true))
        {
            await _repository.DeleteDirectAsync(r => r.UserTenantId == tenantId);
            await unitOfWork.CompleteAsync();
        }
    }
}
