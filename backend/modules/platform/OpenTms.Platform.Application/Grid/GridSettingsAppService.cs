// DEVIATION(design-6-platform-grid-component.md §Settings storage):
// The design specifies one ABP setting key per gridId (OpenTms.Platform.Grid.Settings.{gridId}).
// This is not feasible: ISettingManager validates every name against ISettingDefinitionManager,
// which requires definitions registered at compile time. Dynamic per-gridId keys would require
// a custom ISettingDefinitionManager — a design gate decision. Instead, all grids for a user
// are stored in a single JSON blob under key "OpenTms.Platform.Grid.Settings", with internal
// keys "{tenantId}:{gridId}" to maintain tenant isolation (constitution rule 1, Assumption 3).
// A design gate amendment citing this file is required before this PR can merge.
// The read-modify-write race (Finding #3) is prevented by a per-user distributed lock.
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.DistributedLocking;
using Volo.Abp.SettingManagement;

namespace OpenTms.Platform.Grid;

[Authorize]
public class GridSettingsAppService : PlatformAppServiceBase, IGridSettingsAppService
{
    private static readonly Regex GridIdPattern = new("^[a-zA-Z0-9_.-]{1,100}$", RegexOptions.Compiled);
    private static readonly Regex ColIdPattern = new("^[a-zA-Z0-9_.-]{1,64}$", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ISettingManager _settingManager;
    private readonly IAbpDistributedLock _distributedLock;

    public GridSettingsAppService(ISettingManager settingManager, IAbpDistributedLock distributedLock)
    {
        _settingManager = settingManager;
        _distributedLock = distributedLock;
    }

    public async Task<GridSettingsDto?> GetAsync(string gridId)
    {
        ValidateGridId(gridId);

        var blob = await ReadBlobAsync();
        return blob.TryGetValue(MakeBlobKey(gridId), out var dto) ? dto : null;
    }

    public async Task SetAsync(string gridId, GridSettingsDto input)
    {
        ValidateGridId(gridId);
        ValidateGridSettingsDto(input);

        await using var handle = await _distributedLock.TryAcquireAsync(
            MakeLockKey(), TimeSpan.FromSeconds(10));

        if (handle is null)
        {
            throw new UserFriendlyException("Could not acquire grid settings lock; please retry.");
        }

        var blob = await ReadBlobAsync();
        blob[MakeBlobKey(gridId)] = input;
        await WriteBlobAsync(blob);
    }

    public async Task ResetAsync(string gridId)
    {
        ValidateGridId(gridId);

        await using var handle = await _distributedLock.TryAcquireAsync(
            MakeLockKey(), TimeSpan.FromSeconds(10));

        if (handle is null)
        {
            throw new UserFriendlyException("Could not acquire grid settings lock; please retry.");
        }

        var blob = await ReadBlobAsync();
        if (blob.Remove(MakeBlobKey(gridId)))
        {
            await WriteBlobAsync(blob);
        }
    }

    // Tenant prefix inside the blob key: ABP user-level settings are keyed only by UserId;
    // the prefix ensures settings from tenant A never resolve in tenant B (rule 1, Assumption 3).
    private string MakeBlobKey(string gridId)
    {
        var tenantPrefix = CurrentTenant.Id?.ToString("N") ?? "host";
        return $"{tenantPrefix}:{gridId}";
    }

    // Lock scoped per user+tenant; prevents concurrent SetAsync/ResetAsync from the same user
    // (e.g. two browser tabs) from interleaving their read-modify-write on the shared blob.
    private string MakeLockKey()
    {
        var tenantPart = CurrentTenant.Id?.ToString("N") ?? "host";
        var userPart = CurrentUser.Id?.ToString("N") ?? "anonymous";
        return $"OpenTms.Platform.GridSettings:{tenantPart}:{userPart}";
    }

    private async Task<Dictionary<string, GridSettingsDto>> ReadBlobAsync()
    {
        var json = await _settingManager.GetOrNullForCurrentUserAsync(PlatformSettings.GridSettings);
        if (json is null)
        {
            return new Dictionary<string, GridSettingsDto>(StringComparer.Ordinal);
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, GridSettingsDto>>(json, JsonOptions)
                ?? new Dictionary<string, GridSettingsDto>(StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            Logger.LogWarning("Failed to parse grid settings blob for user {UserId}; resetting to empty.", CurrentUser.Id);
            return new Dictionary<string, GridSettingsDto>(StringComparer.Ordinal);
        }
    }

    private async Task WriteBlobAsync(Dictionary<string, GridSettingsDto> blob)
    {
        var json = blob.Count > 0
            ? JsonSerializer.Serialize(blob, JsonOptions)
            : null;
        await _settingManager.SetForCurrentUserAsync(PlatformSettings.GridSettings, json);
    }

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
