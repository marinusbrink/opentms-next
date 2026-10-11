// Storage: docs/designs/grid-settings-storage.md, which amends design #6 §Settings storage.
// One row per (tenant, user, grid) in platform."GridUserSettings" (entity GridUserSetting); the ABP
// data filter scopes every query to the current tenant, every query filters on the current user.
// The single 2048-character ABP setting value of design #6 (OpenTms.Platform.Grid.Settings) overflowed
// at five grids; what is left in it is migrated lazily, per user and per tenant, the first time that
// user's settings are touched. Removing the setting definition and its rows is a later contract step.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.DistributedLocking;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.SettingManagement;

namespace OpenTms.Platform.Grid;

[Authorize]
public class GridSettingsAppService : PlatformAppServiceBase, IGridSettingsAppService
{
    private static readonly Regex GridIdPattern = new("^[a-zA-Z0-9_.-]{1,100}$", RegexOptions.Compiled);
    private static readonly Regex ColIdPattern = new("^[a-zA-Z0-9_.-]{1,64}$", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(10);

    private readonly IRepository<GridUserSetting, Guid> _repository;
    private readonly ISettingManager _settingManager;
    private readonly IAbpDistributedLock _distributedLock;

    public GridSettingsAppService(
        IRepository<GridUserSetting, Guid> repository,
        ISettingManager settingManager,
        IAbpDistributedLock distributedLock)
    {
        _repository = repository;
        _settingManager = settingManager;
        _distributedLock = distributedLock;
    }

    public async Task<GridSettingsDto?> GetAsync(string gridId)
    {
        ValidateGridId(gridId);

        var row = await FindAsync(gridId);
        if (row is null)
        {
            await MigrateLegacyBlobAsync();
            row = await FindAsync(gridId);
        }

        return row is null ? null : Deserialize(row);
    }

    public async Task SetAsync(string gridId, GridSettingsDto input)
    {
        ValidateGridId(gridId);
        ValidateGridSettingsDto(input);

        await using var handle = await AcquireLockAsync(GridLockKey(gridId));

        /* First, so a legacy entry for this grid can never resurface after the new row is reset. */
        await MigrateLegacyBlobAsync();

        var json = JsonSerializer.Serialize(input, JsonOptions);
        var row = await FindAsync(gridId);
        if (row is null)
        {
            await _repository.InsertAsync(
                new GridUserSetting(GuidGenerator.Create(), CurrentTenant.Id, RequireUserId(), gridId, json),
                autoSave: true);
        }
        else
        {
            row.SetSettingsJson(json);
            await _repository.UpdateAsync(row, autoSave: true);
        }
    }

    public async Task ResetAsync(string gridId)
    {
        ValidateGridId(gridId);

        await using var handle = await AcquireLockAsync(GridLockKey(gridId));

        await MigrateLegacyBlobAsync();

        var userId = RequireUserId();
        await _repository.DeleteAsync(x => x.UserId == userId && x.GridId == gridId, autoSave: true);
    }

    // ── Rows ──────────────────────────────────────────────────────────────────────

    private async Task<GridUserSetting?> FindAsync(string gridId)
    {
        var userId = RequireUserId();
        return await _repository.FirstOrDefaultAsync(x => x.UserId == userId && x.GridId == gridId);
    }

    private GridSettingsDto? Deserialize(GridUserSetting row)
    {
        try
        {
            return JsonSerializer.Deserialize<GridSettingsDto>(row.SettingsJson, JsonOptions);
        }
        catch (JsonException)
        {
            Logger.LogWarning(
                "Grid settings row {RowId} of user {UserId} in tenant {TenantId} is not valid JSON; treated as absent.",
                row.Id, row.UserId, row.TenantId);
            return null;
        }
    }

    private Guid RequireUserId()
    {
        return CurrentUser.Id ?? throw new AbpAuthorizationException("Grid settings belong to a signed-in user.");
    }

    // ── Legacy blob of design #6 (one ABP setting value for all grids of a user) ───

    /// <summary>
    /// Moves this user's entries for the <em>current</em> tenant out of the legacy setting value into
    /// rows, once. Entries of other tenants sharing the host database stay in the value for their own
    /// tenant context to migrate: no row is ever written for a tenant other than the current one.
    /// </summary>
    private async Task MigrateLegacyBlobAsync()
    {
        /* Fast path without the lock: the setting is cached, and null once this user has nothing left. */
        if (await ReadLegacyBlobJsonAsync() is null)
        {
            return;
        }

        await using var handle = await AcquireLockAsync(UserLockKey());

        var json = await ReadLegacyBlobJsonAsync();
        if (json is null)
        {
            return;
        }

        var userId = RequireUserId();
        Dictionary<string, GridSettingsDto> blob;
        try
        {
            blob = JsonSerializer.Deserialize<Dictionary<string, GridSettingsDto>>(json, JsonOptions)
                ?? new Dictionary<string, GridSettingsDto>(StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            Logger.LogWarning("Legacy grid settings value of user {UserId} is not valid JSON; discarded.", userId);
            await _settingManager.SetForCurrentUserAsync(PlatformSettings.GridSettings, null);
            return;
        }

        var prefix = LegacyKeyPrefix();
        var mine = blob.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList();
        if (mine.Count == 0)
        {
            return;
        }

        foreach (var key in mine)
        {
            var gridId = key.Substring(prefix.Length);
            if (GridIdPattern.IsMatch(gridId) && await FindAsync(gridId) is null)
            {
                await _repository.InsertAsync(
                    new GridUserSetting(GuidGenerator.Create(), CurrentTenant.Id, userId, gridId,
                        JsonSerializer.Serialize(blob[key], JsonOptions)),
                    autoSave: true);
            }

            blob.Remove(key);
        }

        await _settingManager.SetForCurrentUserAsync(
            PlatformSettings.GridSettings,
            blob.Count > 0 ? JsonSerializer.Serialize(blob, JsonOptions) : null);

        Logger.LogInformation(
            "Migrated {Count} legacy grid settings entries of user {UserId} in tenant {TenantId} to rows.",
            mine.Count, userId, CurrentTenant.Id);
    }

    private Task<string?> ReadLegacyBlobJsonAsync()
    {
        return _settingManager.GetOrNullForCurrentUserAsync(PlatformSettings.GridSettings, fallback: false);
    }

    /// <summary>The key prefix design #6 used inside the blob: "{tenantId:N}:" or "host:".</summary>
    private string LegacyKeyPrefix()
    {
        return (CurrentTenant.Id?.ToString("N") ?? "host") + ":";
    }

    // ── Locks ─────────────────────────────────────────────────────────────────────
    // Two tabs saving the same grid are serialised per grid; the legacy migration per user.
    // Always taken in the order grid → user, so the nesting in SetAsync/ResetAsync cannot deadlock.

    private async Task<IAbpDistributedLockHandle> AcquireLockAsync(string key)
    {
        var handle = await _distributedLock.TryAcquireAsync(key, LockTimeout);
        return handle ?? throw new UserFriendlyException("Could not acquire grid settings lock; please retry.");
    }

    private string GridLockKey(string gridId) => $"{UserLockKey()}:{gridId}";

    private string UserLockKey()
    {
        var tenantPart = CurrentTenant.Id?.ToString("N") ?? "host";
        var userPart = CurrentUser.Id?.ToString("N") ?? "anonymous";
        return $"OpenTms.Platform.GridSettings:{tenantPart}:{userPart}";
    }

    // ── Validation ────────────────────────────────────────────────────────────────

    private static void ValidateGridId(string gridId)
    {
        if (!GridIdPattern.IsMatch(gridId))
        {
            throw new UserFriendlyException($"Invalid gridId: must match ^[a-zA-Z0-9_.-]{{1,100}}$.");
        }
    }

    private static void ValidateGridSettingsDto(GridSettingsDto dto)
    {
        if (dto.ColumnStates.Count > 50)
        {
            throw new UserFriendlyException("GridSettingsDto may contain at most 50 column states.");
        }

        foreach (var col in dto.ColumnStates)
        {
            if (!ColIdPattern.IsMatch(col.ColId))
            {
                throw new UserFriendlyException($"Invalid colId '{col.ColId}': must match ^[a-zA-Z0-9_.-]{{1,64}}$.");
            }
        }
    }
}
