using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Npgsql;
using OpenTms.Platform;
using OpenTms.Platform.Grid;
using Shouldly;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Security.Claims;
using Volo.Abp.SettingManagement;
using Volo.Abp.Uow;
using Xunit;

namespace OpenTms.PostgreSql.Grid;

/// <summary>
/// Per-user grid settings against real PostgreSQL databases (docs/designs/grid-settings-storage.md).
/// Risk class: critical — Platform storage and tenant isolation across the host database, a
/// shared-tier tenant in that database and a tenant with its own database.
///
/// The first test is the reproduction of the defect: with the single 2048-character ABP setting
/// value of design #6, a tenant user who had personalised the five Administration grids (25 column
/// states, ≈ 2.1 KB with the tenant prefix) failed with Npgsql 22001 ("value too long for type
/// character varying(2048)"). SQLite does not enforce column lengths, so only PostgreSQL shows it.
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class GridUserSettingsPostgreSqlTests : OpenTmsPostgreSqlTestBase
{
    /* The five grids of the Administration app with their real column ids, so the stored size is
     * the real size (five grids of four columns each stay just under the limit at ≈ 1.9 KB). */
    private static readonly Dictionary<string, string[]> AdministrationGrids = new()
    {
        ["platform.administration.users"] = new[] { "userName", "fullName", "email", "phoneNumber", "roleNames", "isActive", "creationTime" },
        ["platform.administration.roles"] = new[] { "name", "userCount", "isDefault", "isPublic", "isStatic" },
        ["platform.administration.organizationUnits"] = new[] { "displayName", "code", "path", "memberCount", "creationTime" },
        ["platform.administration.organizationUnitMembers"] = new[] { "userName", "fullName", "email", "isActive" },
        ["platform.administration.organizationUnitUnaddedUsers"] = new[] { "userName", "fullName", "email", "isActive" },
    };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IGridSettingsAppService _service;
    private readonly ICurrentTenant _currentTenant;
    private readonly ICurrentPrincipalAccessor _principalAccessor;
    private readonly ISettingManager _settingManager;

    public GridUserSettingsPostgreSqlTests()
    {
        _service = GetRequiredService<IGridSettingsAppService>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
        _principalAccessor = GetRequiredService<ICurrentPrincipalAccessor>();
        _settingManager = GetRequiredService<ISettingManager>();
    }

    // ── Reproduction: the five Administration grids for one tenant user ───────────

    [Fact]
    public async Task A_tenant_user_persists_all_five_Administration_grids_and_reads_them_all_back()
    {
        using (_currentTenant.Change(PostgreSqlTestDatabases.TenantAId))
        {
            foreach (var (gridId, columns) in AdministrationGrids)
            {
                await _service.SetAsync(gridId, Personalised(columns));
            }

            foreach (var (gridId, columns) in AdministrationGrids)
            {
                var loaded = await _service.GetAsync(gridId);

                loaded.ShouldNotBeNull($"grid '{gridId}' was saved but could not be read back");
                loaded!.ColumnStates.Select(c => c.ColId).ShouldBe(columns);
                loaded.ColumnStates[0].Width.ShouldBe(180);
                loaded.SortModels.Single().ColId.ShouldBe(columns[0]);
            }
        }
    }

    [Fact]
    public async Task Several_grids_at_the_validated_maximum_persist_for_one_user()
    {
        /* 50 columns of 64 characters each: ≈ 5 KB per grid, more than twice the old limit on its own. */
        var columns = Enumerable.Range(0, 50).Select(i => new string('c', 60) + i.ToString("D4")).ToArray();
        var gridIds = Enumerable.Range(0, 4).Select(i => $"grid-max-{i}-{Guid.NewGuid():N}").ToList();

        using (_currentTenant.Change(PostgreSqlTestDatabases.TenantAId))
        {
            foreach (var gridId in gridIds)
            {
                await _service.SetAsync(gridId, Personalised(columns));
            }

            foreach (var gridId in gridIds)
            {
                var loaded = await _service.GetAsync(gridId);
                loaded.ShouldNotBeNull();
                loaded!.ColumnStates.Count.ShouldBe(50);
                loaded.ColumnStates.Select(c => c.ColId).ShouldBe(columns);
                loaded.ColumnStates[49].Width.ShouldBe(180 + 49 * 10);
            }
        }
    }

    // ── Tenant isolation (critical): host database, shared-tier tenant, own database ──

    [Fact]
    public async Task Settings_stay_inside_their_tenant_and_land_in_that_tenants_own_database()
    {
        var gridId = "grid-isolation-" + Guid.NewGuid().ToString("N");

        using (_currentTenant.Change(PostgreSqlTestDatabases.TenantAId))
        {
            await _service.SetAsync(gridId, Personalised(new[] { "tenantA" }));
        }

        using (_currentTenant.Change(PostgreSqlTestDatabases.SharedTenantId))
        {
            await _service.SetAsync(gridId, Personalised(new[] { "shared" }));
        }

        await _service.SetAsync(gridId, Personalised(new[] { "host" }));

        using (_currentTenant.Change(PostgreSqlTestDatabases.TenantAId))
        {
            (await _service.GetAsync(gridId))!.ColumnStates.Single().ColId.ShouldBe("tenantA");
        }

        using (_currentTenant.Change(PostgreSqlTestDatabases.SharedTenantId))
        {
            (await _service.GetAsync(gridId))!.ColumnStates.Single().ColId.ShouldBe("shared");
        }

        (await _service.GetAsync(gridId))!.ColumnStates.Single().ColId.ShouldBe("host");

        /* Database per tenant: tenant A's row is in its own database only; the shared-tier tenant and
         * the host share the host database, each with its own row. */
        (await CountRowsAsync(PostgreSqlTestDatabases.TenantADatabase, gridId)).ShouldBe(1);
        (await CountRowsAsync(PostgreSqlTestDatabases.HostDatabase, gridId)).ShouldBe(2);
    }

    [Fact]
    public async Task Resetting_in_one_tenant_leaves_the_other_tenants_settings_untouched()
    {
        var gridId = "grid-reset-isolation-" + Guid.NewGuid().ToString("N");

        using (_currentTenant.Change(PostgreSqlTestDatabases.TenantAId))
        {
            await _service.SetAsync(gridId, Personalised(new[] { "tenantA" }));
        }

        using (_currentTenant.Change(PostgreSqlTestDatabases.SharedTenantId))
        {
            await _service.SetAsync(gridId, Personalised(new[] { "shared" }));
            await _service.ResetAsync(gridId);
            (await _service.GetAsync(gridId)).ShouldBeNull();
        }

        using (_currentTenant.Change(PostgreSqlTestDatabases.TenantAId))
        {
            (await _service.GetAsync(gridId)).ShouldNotBeNull("a reset in the shared tenant must not reach tenant A");
        }
    }

    [Fact]
    public async Task Another_user_of_the_same_tenant_never_sees_or_overwrites_a_users_settings()
    {
        var gridId = "grid-user-isolation-" + Guid.NewGuid().ToString("N");
        var otherUser = Guid.NewGuid();

        using (_currentTenant.Change(PostgreSqlTestDatabases.TenantAId))
        {
            await _service.SetAsync(gridId, Personalised(new[] { "owner" }));

            using (AsUser(otherUser))
            {
                (await _service.GetAsync(gridId)).ShouldBeNull("another user must not read the owner's settings");
                await _service.SetAsync(gridId, Personalised(new[] { "other" }));
                (await _service.GetAsync(gridId))!.ColumnStates.Single().ColId.ShouldBe("other");
            }

            (await _service.GetAsync(gridId))!.ColumnStates.Single().ColId.ShouldBe("owner");
            (await CountRowsAsync(PostgreSqlTestDatabases.TenantADatabase, gridId)).ShouldBe(2);
        }
    }

    // ── One row per (tenant, user, grid) ──────────────────────────────────────────

    [Fact]
    public async Task Saving_the_same_grid_twice_leaves_one_row_with_the_latest_value()
    {
        var gridId = "grid-upsert-" + Guid.NewGuid().ToString("N");

        using (_currentTenant.Change(PostgreSqlTestDatabases.TenantAId))
        {
            await _service.SetAsync(gridId, Personalised(new[] { "first" }));
            await _service.SetAsync(gridId, Personalised(new[] { "second" }));

            (await _service.GetAsync(gridId))!.ColumnStates.Single().ColId.ShouldBe("second");
        }

        (await CountRowsAsync(PostgreSqlTestDatabases.TenantADatabase, gridId)).ShouldBe(1);
    }

    // ── Legacy blob of design #6 in the host database (host user + shared-tier tenant) ──

    [Fact]
    public async Task Legacy_blob_entries_are_migrated_per_tenant_and_never_cross_into_another_tenant()
    {
        var gridId = "grid-legacy-" + Guid.NewGuid().ToString("N");
        var sharedPrefix = PostgreSqlTestDatabases.SharedTenantId.ToString("N");

        /* ABP user settings are keyed by user only: the host user and the same user in a shared-tier
         * tenant read the same AbpSettings row of the host database — the reason design #6 prefixed. */
        var blob = new Dictionary<string, GridSettingsDto>
        {
            [$"host:{gridId}"] = Personalised(new[] { "host" }),
            [$"{sharedPrefix}:{gridId}"] = Personalised(new[] { "shared" }),
        };
        await WithUnitOfWorkAsync(new AbpUnitOfWorkOptions { IsTransactional = true },
            () => _settingManager.SetForCurrentUserAsync(PlatformSettings.GridSettings, JsonSerializer.Serialize(blob, JsonOptions)));

        (await _service.GetAsync(gridId))!.ColumnStates.Single().ColId.ShouldBe("host");

        var remaining = await _settingManager.GetOrNullForCurrentUserAsync(PlatformSettings.GridSettings, fallback: false);
        remaining.ShouldNotBeNull("the shared tenant's entry must stay for its own context to migrate");
        remaining!.ShouldContain(sharedPrefix);
        remaining.ShouldNotContain("host:");

        using (_currentTenant.Change(PostgreSqlTestDatabases.SharedTenantId))
        {
            (await _service.GetAsync(gridId))!.ColumnStates.Single().ColId.ShouldBe("shared");
            (await _settingManager.GetOrNullForCurrentUserAsync(PlatformSettings.GridSettings, fallback: false))
                .ShouldBeNull("the last entry was migrated, so the setting value is cleared");
        }

        (await CountRowsAsync(PostgreSqlTestDatabases.HostDatabase, gridId)).ShouldBe(2);
        (await CountRowsAsync(PostgreSqlTestDatabases.TenantADatabase, gridId)).ShouldBe(0);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────

    /// <summary>Every column kept, the first one sorted — what a user who resized all columns sends.</summary>
    private static GridSettingsDto Personalised(string[] columns) => new()
    {
        ColumnStates = columns
            .Select((colId, index) => new ColumnStateDto { ColId = colId, Visible = true, Order = index, Width = 180 + index * 10 })
            .ToList(),
        SortModels = new List<SortModel> { new(columns[0], "asc") },
    };

    private IDisposable AsUser(Guid userId)
    {
        return _principalAccessor.Change(new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(AbpClaimTypes.UserId, userId.ToString()),
            new Claim(AbpClaimTypes.UserName, "other-user"),
        })));
    }

    private static async Task<long> CountRowsAsync(string database, string gridId)
    {
        await using var connection = new NpgsqlConnection(PostgreSqlTestDatabases.ConnectionStringFor(database));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM platform.\"GridUserSettings\" WHERE \"GridId\" = @gridId", connection);
        command.Parameters.AddWithValue("gridId", gridId);
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
