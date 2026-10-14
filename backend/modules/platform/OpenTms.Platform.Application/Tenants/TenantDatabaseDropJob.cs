using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.MultiTenancy;
using Volo.Abp.TenantManagement;
using Volo.Abp.Uow;

namespace OpenTms.Platform.Tenants;

/* Drops a deleted tenant's database and login role (PO decision D5, docs/designs/tenants-and-editions.md).
 * Irreversible, so it trusts nothing in its arguments (Hangfire stores them in plain text): the tenant must exist
 * and be soft-deleted, and what is dropped is the database of that tenant's own connection string, which must match
 * the arguments. The provisioner then refuses the host database and any name it did not generate. */
public class TenantDatabaseDropJob : AsyncBackgroundJob<TenantDatabaseDropArgs>, ITransientDependency
{
    private readonly ITenantRepository _tenantRepository;
    private readonly ITenantDatabaseProvisioner _provisioner;
    private readonly ICurrentTenant _currentTenant;
    private readonly IDataFilter _dataFilter;
    private readonly IUnitOfWorkManager _unitOfWorkManager;

    public TenantDatabaseDropJob(
        ITenantRepository tenantRepository,
        ITenantDatabaseProvisioner provisioner,
        ICurrentTenant currentTenant,
        IDataFilter dataFilter,
        IUnitOfWorkManager unitOfWorkManager)
    {
        _tenantRepository = tenantRepository;
        _provisioner = provisioner;
        _currentTenant = currentTenant;
        _dataFilter = dataFilter;
        _unitOfWorkManager = unitOfWorkManager;
    }

    public override async Task ExecuteAsync(TenantDatabaseDropArgs args)
    {
        var tenant = await FindIncludingDeletedAsync(args.TenantId);
        if (tenant == null || !tenant.IsDeleted)
        {
            Logger.LogWarning("Tenant {TenantId} is missing or not deleted; no database is dropped", args.TenantId);
            return;
        }

        var connectionString = tenant.FindDefaultConnectionString();
        var own = connectionString == null ? null : _provisioner.Parse(connectionString);
        if (own == null || !TenantDatabaseNaming.IsGenerated(own.DatabaseName))
        {
            Logger.LogWarning("Deleted tenant {TenantId} has no generated database of its own; nothing is dropped", args.TenantId);
            return;
        }

        if (!string.Equals(own.DatabaseName, args.DatabaseName, StringComparison.Ordinal)
            || !string.Equals(own.Host, args.Host, StringComparison.OrdinalIgnoreCase)
            || own.Port != args.Port)
        {
            Logger.LogError("Drop job for tenant {TenantId} names a database that is not the tenant's own; refused", args.TenantId);
            return;
        }

        await _provisioner.DropDatabaseAsync(own);
    }

    private async Task<Tenant?> FindIncludingDeletedAsync(Guid tenantId)
    {
        /* Reading a soft-deleted row is the point here; the tenant store is host data, not tenant data. */
        using (_currentTenant.Change(null))
        using (_dataFilter.Disable<ISoftDelete>())
        using (var unitOfWork = _unitOfWorkManager.Begin(requiresNew: true))
        {
            var tenant = await _tenantRepository.FindAsync(tenantId, includeDetails: true);
            await unitOfWork.CompleteAsync();
            return tenant;
        }
    }
}
