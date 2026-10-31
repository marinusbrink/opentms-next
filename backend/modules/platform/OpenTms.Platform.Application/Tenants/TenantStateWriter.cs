using System;
using System.Threading.Tasks;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.MultiTenancy;
using Volo.Abp.TenantManagement;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace OpenTms.Platform.Tenants;

/// <summary>
/// Writes the provisioning bookkeeping on the tenant (host database) in a unit of work of its own, so the state
/// is committed whatever the job does next.
/// </summary>
public class TenantStateWriter : ITransientDependency
{
    private readonly ITenantRepository _tenantRepository;
    private readonly ICurrentTenant _currentTenant;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly IClock _clock;

    public TenantStateWriter(
        ITenantRepository tenantRepository,
        ICurrentTenant currentTenant,
        IUnitOfWorkManager unitOfWorkManager,
        IClock clock)
    {
        _tenantRepository = tenantRepository;
        _currentTenant = currentTenant;
        _unitOfWorkManager = unitOfWorkManager;
        _clock = clock;
    }

    /// <summary>Reads the tenant in host context; null when it is gone (deleted).</summary>
    public async Task<Tenant?> FindAsync(Guid tenantId)
    {
        using (_currentTenant.Change(null))
        using (var unitOfWork = _unitOfWorkManager.Begin(requiresNew: true))
        {
            var tenant = await _tenantRepository.FindAsync(tenantId, includeDetails: true);
            await unitOfWork.CompleteAsync();
            return tenant;
        }
    }

    public Task SetStateAsync(Guid tenantId, string state, string? error = null) =>
        UpdateAsync(tenantId, tenant =>
        {
            tenant.SetProperty(TenantProperties.ProvisioningState, state);
            tenant.SetProperty(TenantProperties.ProvisioningAt, _clock.Now);
            if (error == null)
            {
                tenant.RemoveProperty(TenantProperties.ProvisioningError);
            }
            else
            {
                tenant.SetProperty(TenantProperties.ProvisioningError, error);
            }

            if (state == TenantProvisioningState.Succeeded)
            {
                tenant.RemoveProperty(TenantProperties.PendingAdminEmail);
            }
        });

    /// <summary>Marks the invitation sent and, for a resend, which request sent it (a redelivery then sends nothing).</summary>
    public Task MarkInvitationSentAsync(Guid tenantId, Guid? requestId = null) =>
        UpdateAsync(tenantId, tenant =>
        {
            tenant.SetProperty(TenantProperties.InvitationSentAt, _clock.Now);
            if (requestId != null)
            {
                tenant.SetProperty(TenantProperties.InvitationRequestId, requestId);
            }
        });

    /// <summary>Records where a "Reset to dataset" stands; a finished run also records its request id (rule 3).</summary>
    public Task SetDatasetResetStateAsync(Guid tenantId, string state, Guid? requestId = null, string? error = null) =>
        UpdateAsync(tenantId, tenant =>
        {
            tenant.SetProperty(TenantProperties.DatasetResetState, state);
            tenant.SetProperty(TenantProperties.DatasetResetAt, _clock.Now);
            if (requestId != null)
            {
                tenant.SetProperty(TenantProperties.DatasetResetRequestId, requestId);
            }

            if (error == null)
            {
                tenant.RemoveProperty(TenantProperties.DatasetResetError);
            }
            else
            {
                tenant.SetProperty(TenantProperties.DatasetResetError, error);
            }
        });

    private async Task UpdateAsync(Guid tenantId, Action<Tenant> change)
    {
        using (_currentTenant.Change(null))
        using (var unitOfWork = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true))
        {
            var tenant = await _tenantRepository.FindAsync(tenantId, includeDetails: false);
            if (tenant != null)
            {
                change(tenant);
                await _tenantRepository.UpdateAsync(tenant);
            }

            await unitOfWork.CompleteAsync();
        }
    }
}
