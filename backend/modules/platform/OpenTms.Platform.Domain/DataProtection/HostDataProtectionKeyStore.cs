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

namespace OpenTms.Platform.DataProtection;

/// <summary>
/// Reads and adds data-protection keys in the host database, whatever tenant is current
/// (docs/designs/tenants-and-editions.md, "Shared data-protection keys"). The key manager may load its key ring inside a
/// tenant's request; reading the tenant's own database there would give that tenant other keys than every other
/// request, so each call switches to the host and runs in a unit of work of its own.
/// </summary>
public class HostDataProtectionKeyStore : IDataProtectionKeyStore, ITransientDependency
{
    private readonly IRepository<DataProtectionKeyRecord, Guid> _repository;
    private readonly ICurrentTenant _currentTenant;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly IGuidGenerator _guidGenerator;
    private readonly IClock _clock;

    public HostDataProtectionKeyStore(
        IRepository<DataProtectionKeyRecord, Guid> repository,
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

    /// <summary>
    /// Every stored key element, oldest first. Unpaged by design: the key manager needs all of them, and there are a
    /// handful (one new key per 90 days, plus revocations).
    /// </summary>
    public async Task<IReadOnlyList<string>> GetAllAsync()
    {
        using (_currentTenant.Change(null))
        using (var unitOfWork = _unitOfWorkManager.Begin(new AbpUnitOfWorkOptions { IsTransactional = false }, requiresNew: true))
        {
            var records = await _repository.GetListAsync();
            await unitOfWork.CompleteAsync();
            return records.OrderBy(r => r.CreatedAt).ThenBy(r => r.Id).Select(r => r.Xml).ToList();
        }
    }

    public async Task AddAsync(string? friendlyName, string xml)
    {
        using (_currentTenant.Change(null))
        using (var unitOfWork = _unitOfWorkManager.Begin(new AbpUnitOfWorkOptions { IsTransactional = true }, requiresNew: true))
        {
            await _repository.InsertAsync(new DataProtectionKeyRecord(_guidGenerator.Create(), friendlyName, xml, _clock.Now));
            await unitOfWork.CompleteAsync();
        }
    }
}
