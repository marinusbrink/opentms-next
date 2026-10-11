using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using OpenTms.Platform;
using OpenTms.Platform.Grid;
using Shouldly;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Security.Claims;
using Volo.Abp.SettingManagement;
using Xunit;

namespace OpenTms.EntityFrameworkCore.Platform.Grid;

/// <summary>
/// The per-user grid settings rows of docs/designs/grid-settings-storage.md on SQLite: several
/// grids per user, user isolation, and the lazy migration of the legacy single-blob setting of
/// design #6. The contract tests of the app service stay in GridSettingsAppServiceIntegrationTests;
/// the PostgreSQL-backed suite (PostgreSql/Grid) covers the real schema and database-per-tenant.
/// Risk class: critical (Platform; tenant isolation).
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class GridUserSettingsStorageTests : OpenTmsEntityFrameworkCoreTestBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IGridSettingsAppService _service;
    private readonly ICurrentTenant _currentTenant;
    private readonly ICurrentPrincipalAccessor _principalAccessor;
    private readonly ISettingManager _settingManager;

    public GridUserSettingsStorageTests()
    {
        _service = GetRequiredService<IGridSettingsAppService>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
        _principalAccessor = GetRequiredService<ICurrentPrincipalAccessor>();
        _settingManager = GetRequiredService<ISettingManager>();
    }

    // ── Many grids per user (the defect of design #6 was the fifth grid) ──────────

    [Fact]
    public async Task Five_grids_of_four_columns_each_persist_and_read_back_independently()
    {
        var tenantId = Guid.NewGuid();
        var grids = Enumerable.Range(0, 5)
            .ToDictionary(i => $"grid-five-{i}-{Guid.NewGuid():N}", i => new[] { $"a{i}", $"b{i}", $"c{i}", $"d{i}" });

        using (_currentTenant.Change(tenantId))
        {
            foreach (var (gridId, columns) in grids)
            {
                await _service.SetAsync(gridId, Personalised(columns));
            }

            foreach (var (gridId, columns) in grids)
            {
                var loaded = await _service.GetAsync(gridId);
                loaded.ShouldNotBeNull();
                loaded!.ColumnStates.Select(c => c.ColId).ShouldBe(columns);
            }
        }
    }

    // ── User isolation ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Another_user_never_sees_or_overwrites_a_users_settings()
    {
        var gridId = "grid-user-" + Guid.NewGuid().ToString("N");

        await _service.SetAsync(gridId, Personalised(new[] { "owner" }));

        using (AsUser(Guid.NewGuid()))
        {
            (await _service.GetAsync(gridId)).ShouldBeNull();
            await _service.SetAsync(gridId, Personalised(new[] { "other" }));
            await _service.ResetAsync(gridId);
            (await _service.GetAsync(gridId)).ShouldBeNull();
        }

        (await _service.GetAsync(gridId))!.ColumnStates.Single().ColId.ShouldBe("owner");
    }

    // ── Legacy blob of design #6: migrated lazily, per user, per tenant ───────────

    [Fact]
    public async Task Legacy_entries_of_the_current_tenant_become_rows_and_the_other_tenants_entries_stay_in_the_blob()
    {
        var gridId = "grid-legacy-" + Guid.NewGuid().ToString("N");
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await WriteLegacyBlobAsync(new Dictionary<string, GridSettingsDto>
        {
            [$"{tenantA:N}:{gridId}"] = Personalised(new[] { "tenant-a" }),
            [$"{tenantB:N}:{gridId}"] = Personalised(new[] { "tenant-b" }),
        });

        using (_currentTenant.Change(tenantA))
        {
            (await _service.GetAsync(gridId))!.ColumnStates.Single().ColId.ShouldBe("tenant-a");

            /* A second read finds the row; the blob is untouched from here on. */
            (await _service.GetAsync(gridId))!.ColumnStates.Single().ColId.ShouldBe("tenant-a");
        }

        var remaining = await ReadLegacyBlobAsync();
        remaining.Keys.ShouldHaveSingleItem("only tenant B's entry may remain").ShouldBe($"{tenantB:N}:{gridId}");

        using (_currentTenant.Change(tenantB))
        {
            (await _service.GetAsync(gridId))!.ColumnStates.Single().ColId.ShouldBe("tenant-b");
        }

        (await _settingManager.GetOrNullForCurrentUserAsync(PlatformSettings.GridSettings, fallback: false))
            .ShouldBeNull("the setting value is cleared once nothing is left in it");
    }

    [Fact]
    public async Task A_reset_after_migration_does_not_resurrect_the_legacy_value()
    {
        var gridId = "grid-legacy-reset-" + Guid.NewGuid().ToString("N");
        var tenantId = Guid.NewGuid();

        await WriteLegacyBlobAsync(new Dictionary<string, GridSettingsDto>
        {
            [$"{tenantId:N}:{gridId}"] = Personalised(new[] { "legacy" }),
        });

        using (_currentTenant.Change(tenantId))
        {
            await _service.ResetAsync(gridId);
            (await _service.GetAsync(gridId)).ShouldBeNull();
        }
    }

    [Fact]
    public async Task A_save_wins_over_a_legacy_entry_for_the_same_grid()
    {
        var gridId = "grid-legacy-save-" + Guid.NewGuid().ToString("N");
        var tenantId = Guid.NewGuid();

        await WriteLegacyBlobAsync(new Dictionary<string, GridSettingsDto>
        {
            [$"{tenantId:N}:{gridId}"] = Personalised(new[] { "legacy" }),
        });

        using (_currentTenant.Change(tenantId))
        {
            await _service.SetAsync(gridId, Personalised(new[] { "new" }));
            (await _service.GetAsync(gridId))!.ColumnStates.Single().ColId.ShouldBe("new");

            await _service.ResetAsync(gridId);
            (await _service.GetAsync(gridId)).ShouldBeNull("the legacy entry was consumed by the save");
        }
    }

    [Fact]
    public async Task A_legacy_entry_in_host_context_is_migrated_for_the_host_only()
    {
        var gridId = "grid-legacy-host-" + Guid.NewGuid().ToString("N");

        await WriteLegacyBlobAsync(new Dictionary<string, GridSettingsDto>
        {
            [$"host:{gridId}"] = Personalised(new[] { "host" }),
        });

        using (_currentTenant.Change(Guid.NewGuid()))
        {
            (await _service.GetAsync(gridId)).ShouldBeNull("a tenant must not receive the host's entry");
        }

        (await _service.GetAsync(gridId))!.ColumnStates.Single().ColId.ShouldBe("host");
    }

    [Fact]
    public async Task A_corrupt_legacy_value_is_discarded_instead_of_failing_every_call()
    {
        var gridId = "grid-legacy-corrupt-" + Guid.NewGuid().ToString("N");

        await WithUnitOfWorkAsync(() => _settingManager.SetForCurrentUserAsync(PlatformSettings.GridSettings, "{not json"));

        (await _service.GetAsync(gridId)).ShouldBeNull();
        (await _settingManager.GetOrNullForCurrentUserAsync(PlatformSettings.GridSettings, fallback: false)).ShouldBeNull();

        await _service.SetAsync(gridId, Personalised(new[] { "after" }));
        (await _service.GetAsync(gridId))!.ColumnStates.Single().ColId.ShouldBe("after");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────

    private static GridSettingsDto Personalised(string[] columns) => new()
    {
        ColumnStates = columns
            .Select((colId, index) => new ColumnStateDto { ColId = colId, Visible = true, Order = index, Width = 180 + index * 10 })
            .ToList(),
        SortModels = new List<SortModel> { new(columns[0], "asc") },
    };

    private Task WriteLegacyBlobAsync(Dictionary<string, GridSettingsDto> blob)
    {
        return WithUnitOfWorkAsync(() => _settingManager.SetForCurrentUserAsync(
            PlatformSettings.GridSettings, JsonSerializer.Serialize(blob, JsonOptions)));
    }

    private async Task<Dictionary<string, GridSettingsDto>> ReadLegacyBlobAsync()
    {
        var json = await _settingManager.GetOrNullForCurrentUserAsync(PlatformSettings.GridSettings, fallback: false);
        return json is null
            ? new Dictionary<string, GridSettingsDto>()
            : JsonSerializer.Deserialize<Dictionary<string, GridSettingsDto>>(json, JsonOptions)!;
    }

    private IDisposable AsUser(Guid userId)
    {
        return _principalAccessor.Change(new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(AbpClaimTypes.UserId, userId.ToString()),
            new Claim(AbpClaimTypes.UserName, "other-user"),
        })));
    }
}
