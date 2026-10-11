using System;
using Volo.Abp;
using Volo.Abp.Auditing;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace OpenTms.Platform.Grid;

/// <summary>
/// One user's saved layout of one grid: the column states and the sort of design #6, serialised as
/// JSON without a length limit (docs/designs/grid-settings-storage.md). One row per
/// (tenant, user, grid), in platform."GridUserSettings" of the tenant's own database; the ABP data
/// filter scopes every query to the current tenant (constitution rule 1).
/// Kept out of the functional logbook: a column resize is machine traffic, not a fact a key user
/// looks up (see FunctionalLogbookEntitySelector).
/// </summary>
[DisableAuditing]
public class GridUserSetting : AuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }

    /// <summary>The owner; every query filters on the current user.</summary>
    public Guid UserId { get; private set; }

    public string GridId { get; private set; } = default!;

    /// <summary>The GridSettingsDto, serialised with System.Text.Json web defaults.</summary>
    public string SettingsJson { get; private set; } = default!;

    protected GridUserSetting()
    {
    }

    public GridUserSetting(Guid id, Guid? tenantId, Guid userId, string gridId, string settingsJson)
        : base(id)
    {
        TenantId = tenantId;
        UserId = userId;
        GridId = Check.NotNullOrWhiteSpace(gridId, nameof(gridId), GridUserSettingConsts.MaxGridIdLength);
        SetSettingsJson(settingsJson);
    }

    public void SetSettingsJson(string settingsJson)
    {
        SettingsJson = Check.NotNullOrWhiteSpace(settingsJson, nameof(settingsJson));
    }
}
