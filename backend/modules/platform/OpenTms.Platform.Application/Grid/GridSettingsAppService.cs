using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.SettingManagement;

namespace OpenTms.Platform.Grid;

[Authorize]
public class GridSettingsAppService : PlatformAppServiceBase, IGridSettingsAppService
{
    private static readonly Regex GridIdPattern = new("^[a-zA-Z0-9_.-]{1,100}$", RegexOptions.Compiled);
    private static readonly Regex ColIdPattern = new("^[a-zA-Z0-9_.-]{1,64}$", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ISettingManager _settingManager;

    public GridSettingsAppService(ISettingManager settingManager)
    {
        _settingManager = settingManager;
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

        var blob = await ReadBlobAsync();
        blob[MakeBlobKey(gridId)] = input;
        await WriteBlobAsync(blob);
    }

    public async Task ResetAsync(string gridId)
    {
        ValidateGridId(gridId);

        var blob = await ReadBlobAsync();
        if (blob.Remove(MakeBlobKey(gridId)))
        {
            await WriteBlobAsync(blob);
        }
    }

    // ABP user-level settings are keyed by UserId only, not by (TenantId, UserId).
    // Incorporating the tenant ID into the blob key ensures settings from tenant A
    // never resolve in tenant B (constitution rule 1). See design §Assumptions #3.
    private string MakeBlobKey(string gridId)
    {
        var tenantPrefix = CurrentTenant.Id?.ToString("N") ?? "host";
        return $"{tenantPrefix}:{gridId}";
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
