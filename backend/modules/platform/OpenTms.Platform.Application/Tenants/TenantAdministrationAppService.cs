using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using OpenTms.Platform.Editions;
using OpenTms.Platform.Grid;
using OpenTms.Platform.Permissions;
using Volo.Abp;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.TenantManagement;

namespace OpenTms.Platform.Tenants;

/* The Tenants app (host only): docs/designs/tenants-and-editions.md. A tenant created here always gets its own
 * database (PO decision D1); the database, its schemas, the seed and the administrator's invitation are done by
 * TenantProvisioningJob, never in the request. Deleting a tenant soft-deletes it and drops its database in
 * TenantDatabaseDropJob (D5). Jobs are queued only after the unit of work commits, so a job never sees a tenant
 * that a failed request rolled back; the jobs re-check the tenant regardless. */
[RemoteService(IsEnabled = false)]
[Authorize(PlatformPermissions.Tenants.Default)]
public class TenantAdministrationAppService : PlatformAppServiceBase, ITenantAdministrationAppService
{
    private readonly ITenantRepository _tenantRepository;
    private readonly ITenantManager _tenantManager;
    private readonly IRepository<Edition, Guid> _editionRepository;
    private readonly ITenantDatabaseProvisioner _provisioner;
    private readonly ITenantEditionStore _editionStore;
    private readonly IBackgroundJobManager _backgroundJobManager;

    public TenantAdministrationAppService(
        ITenantRepository tenantRepository,
        ITenantManager tenantManager,
        IRepository<Edition, Guid> editionRepository,
        ITenantDatabaseProvisioner provisioner,
        ITenantEditionStore editionStore,
        IBackgroundJobManager backgroundJobManager)
    {
        _tenantRepository = tenantRepository;
        _tenantManager = tenantManager;
        _editionRepository = editionRepository;
        _provisioner = provisioner;
        _editionStore = editionStore;
        _backgroundJobManager = backgroundJobManager;
    }

    public async Task<GridResponse<TenantRowDto>> GetListAsync(GridRequest request)
    {
        HostOnlyGuard.Ensure(CurrentTenant);

        var filter = BuildFilter(request);
        var totalCount = await _tenantRepository.GetCountAsync();
        var filteredCount = filter == null ? totalCount : await _tenantRepository.GetCountAsync(filter);

        var tenants = await _tenantRepository.GetListAsync(
            sorting: BuildSorting(request),
            maxResultCount: Math.Max(0, request.EndRow - request.StartRow),
            skipCount: request.StartRow,
            filter: filter,
            includeDetails: true);

        var editionNames = await GetEditionNamesAsync(tenants.Select(EditionIdOf));
        return new GridResponse<TenantRowDto>
        {
            Rows = tenants.Select(t => MapRow(t, editionNames)).ToList(),
            TotalCount = totalCount,
            FilteredCount = filteredCount
        };
    }

    public async Task<TenantDto> GetAsync(Guid id)
    {
        HostOnlyGuard.Ensure(CurrentTenant);
        return await MapAsync(await _tenantRepository.GetAsync(id));
    }

    public async Task<IReadOnlyList<EditionLookupDto>> GetEditionLookupAsync()
    {
        HostOnlyGuard.Ensure(CurrentTenant);

        var editions = await _editionRepository.GetListAsync();
        return editions
            .OrderBy(e => e.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .Select(e => new EditionLookupDto { Id = e.Id, DisplayName = e.DisplayName })
            .ToList();
    }

    [Authorize(PlatformPermissions.Tenants.Create)]
    public Task<TenantConnectionPreviewDto> GetConnectionPreviewAsync(string name)
    {
        HostOnlyGuard.Ensure(CurrentTenant);

        /* The real name ends in the tenant id (32 hex), which does not exist yet: show x's in its place. */
        var placeholder = TenantDatabaseNaming.For(name ?? string.Empty, Guid.Empty);
        var shown = placeholder[..^32] + new string('x', 32);
        var connectionString = _provisioner.Mask(_provisioner.BuildConnectionString(placeholder, "preview"))
            .Replace(placeholder, shown, StringComparison.Ordinal);
        var location = _provisioner.Parse(connectionString)!;

        return Task.FromResult(new TenantConnectionPreviewDto
        {
            ConnectionString = connectionString,
            DatabaseName = shown,
            ServerName = $"{location.Host}:{location.Port}"
        });
    }

    [Authorize(PlatformPermissions.Tenants.Create)]
    public async Task<TenantDto> CreateAsync(TenantCreateDto input)
    {
        HostOnlyGuard.Ensure(CurrentTenant);
        await EnsureEditionExistsAsync(input.EditionId);

        var tenant = await _tenantManager.CreateAsync(input.Name.Trim());
        var databaseName = TenantDatabaseNaming.For(tenant.Name, tenant.Id);
        tenant.SetDefaultConnectionString(_provisioner.BuildConnectionString(databaseName, GenerateDatabasePassword()));
        tenant.SetProperty(TenantProperties.EditionId, input.EditionId);
        tenant.SetProperty(TenantProperties.ProvisioningState, TenantProvisioningState.Queued);
        tenant.SetProperty(TenantProperties.PendingAdminEmail, input.AdminEmailAddress.Trim());

        await _tenantRepository.InsertAsync(tenant, autoSave: true);
        QueueAfterCommit(new TenantProvisioningArgs { TenantId = tenant.Id });

        Logger.LogInformation("Tenant {TenantId} created by {ActorId}; provisioning queued", tenant.Id, CurrentUser.Id);
        return await MapAsync(tenant);
    }

    [Authorize(PlatformPermissions.Tenants.Update)]
    public async Task<TenantDto> UpdateAsync(Guid id, TenantUpdateDto input)
    {
        HostOnlyGuard.Ensure(CurrentTenant);
        await EnsureEditionExistsAsync(input.EditionId);

        var tenant = await _tenantRepository.GetAsync(id);
        /* The database keeps the name it was given at creation: renaming a tenant never touches the database. */
        await _tenantManager.ChangeNameAsync(tenant, input.Name.Trim());
        tenant.SetProperty(TenantProperties.EditionId, input.EditionId);
        await _tenantRepository.UpdateAsync(tenant, autoSave: true);
        await _editionStore.RemoveAsync(id);

        Logger.LogInformation("Tenant {TenantId} updated by {ActorId}", id, CurrentUser.Id);
        return await MapAsync(tenant);
    }

    [Authorize(PlatformPermissions.Tenants.Delete)]
    public async Task DeleteAsync(Guid id)
    {
        HostOnlyGuard.Ensure(CurrentTenant);

        var tenant = await _tenantRepository.GetAsync(id);
        var state = tenant.GetProperty<string?>(TenantProperties.ProvisioningState);
        if (state == TenantProvisioningState.Queued || state == TenantProvisioningState.Running)
        {
            /* A drop racing a running provisioning could leave the database behind, recreated after the drop. */
            throw new UserFriendlyException(L["Tenants:DeleteWhileProvisioning"]);
        }

        var location = FindOwnDatabase(tenant);

        /* The address only waits for provisioning; a deleted tenant keeps none of it (GDPR art. 17). Saved before the
         * delete: ABP's soft delete reloads the row, which would drop an unsaved change. */
        tenant.RemoveProperty(TenantProperties.PendingAdminEmail);
        await _tenantRepository.UpdateAsync(tenant, autoSave: true);
        await _tenantRepository.DeleteAsync(tenant, autoSave: true);
        await _editionStore.RemoveAsync(id);

        if (location != null)
        {
            QueueAfterCommit(new TenantDatabaseDropArgs
            {
                TenantId = id,
                Host = location.Host,
                Port = location.Port,
                DatabaseName = location.DatabaseName,
                RoleName = location.UserName
            });
        }

        Logger.LogInformation(
            "Tenant {TenantId} deleted by {ActorId}; database drop {DropState}",
            id, CurrentUser.Id, location == null ? "skipped (no database of its own)" : "queued");
    }

    [Authorize(PlatformPermissions.Tenants.Create)]
    public async Task RetryProvisioningAsync(Guid id)
    {
        HostOnlyGuard.Ensure(CurrentTenant);

        var tenant = await _tenantRepository.GetAsync(id);
        var state = tenant.GetProperty<string?>(TenantProperties.ProvisioningState);
        if (state != TenantProvisioningState.Failed)
        {
            throw new UserFriendlyException(L["Tenants:RetryOnlyWhenFailed"]);
        }

        tenant.SetProperty(TenantProperties.ProvisioningState, TenantProvisioningState.Queued);
        await _tenantRepository.UpdateAsync(tenant, autoSave: true);
        QueueAfterCommit(new TenantProvisioningArgs { TenantId = id });

        Logger.LogInformation("Tenant {TenantId} provisioning re-queued by {ActorId}", id, CurrentUser.Id);
    }

    [Authorize(PlatformPermissions.Tenants.Update)]
    public async Task ResendInvitationAsync(Guid id)
    {
        HostOnlyGuard.Ensure(CurrentTenant);

        var tenant = await _tenantRepository.GetAsync(id);
        if (tenant.GetProperty<string?>(TenantProperties.ProvisioningState) != TenantProvisioningState.Succeeded)
        {
            throw new UserFriendlyException(L["Tenants:InviteOnlyWhenReady"]);
        }

        QueueAfterCommit(new TenantInvitationArgs { TenantId = id, RequestId = GuidGenerator.Create() });

        Logger.LogInformation("Tenant {TenantId} invitation re-queued by {ActorId}", id, CurrentUser.Id);
    }

    /// <summary>The tenant's own database, or null when it has none (shared tier) or it is not one this app created.</summary>
    private TenantDatabaseLocation? FindOwnDatabase(Tenant tenant)
    {
        var connectionString = tenant.FindDefaultConnectionString();
        var location = connectionString == null ? null : _provisioner.Parse(connectionString);
        if (location == null || !TenantDatabaseNaming.IsGenerated(location.DatabaseName))
        {
            return null;
        }

        var host = _provisioner.GetHostLocation();
        var isHostDatabase = string.Equals(location.DatabaseName, host.DatabaseName, StringComparison.Ordinal)
                             && string.Equals(location.Host, host.Host, StringComparison.OrdinalIgnoreCase)
                             && location.Port == host.Port;
        return isHostDatabase ? null : location;
    }

    private void QueueAfterCommit<TArgs>(TArgs args)
    {
        var unitOfWork = CurrentUnitOfWork ?? throw new InvalidOperationException("Queueing a tenant job needs a unit of work.");
        unitOfWork.OnCompleted(async () => await _backgroundJobManager.EnqueueAsync(args));
    }

    private async Task EnsureEditionExistsAsync(Guid? editionId)
    {
        if (editionId is { } id && !await _editionRepository.AnyAsync(e => e.Id == id))
        {
            throw new UserFriendlyException(L["Tenants:EditionNotFound"]);
        }
    }

    private async Task<IReadOnlyDictionary<Guid, string>> GetEditionNamesAsync(IEnumerable<Guid?> editionIds)
    {
        var ids = editionIds.OfType<Guid>().Distinct().ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var editions = await _editionRepository.GetListAsync(e => ids.Contains(e.Id));
        return editions.ToDictionary(e => e.Id, e => e.DisplayName);
    }

    private async Task<TenantDto> MapAsync(Tenant tenant)
    {
        var row = MapRow(tenant, await GetEditionNamesAsync(new[] { EditionIdOf(tenant) }));
        var connectionString = tenant.FindDefaultConnectionString();
        return new TenantDto
        {
            Id = row.Id,
            Name = row.Name,
            EditionId = row.EditionId,
            EditionName = row.EditionName,
            DatabaseName = row.DatabaseName,
            ServerName = row.ServerName,
            ProvisioningState = row.ProvisioningState,
            CreationTime = row.CreationTime,
            ConnectionString = connectionString == null ? null : _provisioner.Mask(connectionString),
            ProvisioningError = tenant.GetProperty<string?>(TenantProperties.ProvisioningError),
            InvitationSentAt = tenant.GetProperty<DateTime?>(TenantProperties.InvitationSentAt)
        };
    }

    private TenantRowDto MapRow(Tenant tenant, IReadOnlyDictionary<Guid, string> editionNames)
    {
        var connectionString = tenant.FindDefaultConnectionString();
        var location = connectionString == null ? null : _provisioner.Parse(connectionString);
        var editionId = EditionIdOf(tenant);
        return new TenantRowDto
        {
            Id = tenant.Id,
            Name = tenant.Name,
            EditionId = editionId,
            EditionName = editionId is { } id && editionNames.TryGetValue(id, out var name) ? name : null,
            DatabaseName = location?.DatabaseName,
            ServerName = location == null ? null : $"{location.Host}:{location.Port}",
            ProvisioningState = tenant.GetProperty<string?>(TenantProperties.ProvisioningState),
            CreationTime = tenant.CreationTime
        };
    }

    private static Guid? EditionIdOf(Tenant tenant) => tenant.GetProperty<Guid?>(TenantProperties.EditionId);

    /// <summary>32 random bytes, URL-safe base64: a password for the tenant's own database role (D4).</summary>
    private static string GenerateDatabasePassword() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string? BuildFilter(GridRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.WildcardSearch))
        {
            return request.WildcardSearch.Trim();
        }

        return request.ColumnFilters.TryGetValue("name", out var nameFilter) && !string.IsNullOrWhiteSpace(nameFilter.Filter)
            ? nameFilter.Filter.Trim()
            : null;
    }

    private static string BuildSorting(GridRequest request)
    {
        var sort = request.SortModels.FirstOrDefault();
        var direction = string.Equals(sort?.Sort, "desc", StringComparison.OrdinalIgnoreCase) ? " desc" : " asc";
        return sort?.ColId switch
        {
            "creationTime" => "CreationTime" + direction,
            "name" => "Name" + direction,
            _ => "Name asc"
        };
    }
}
