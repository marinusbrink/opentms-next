using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Platform.Grid;
using Shouldly;
using Volo.Abp.MultiTenancy;
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

    private readonly IGridSettingsAppService _service;
    private readonly ICurrentTenant _currentTenant;

    public GridUserSettingsPostgreSqlTests()
    {
        _service = GetRequiredService<IGridSettingsAppService>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
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

    /// <summary>Every column kept, the first one sorted — what a user who resized all columns sends.</summary>
    private static GridSettingsDto Personalised(string[] columns) => new()
    {
        ColumnStates = columns
            .Select((colId, index) => new ColumnStateDto { ColId = colId, Visible = true, Order = index, Width = 180 + index * 10 })
            .ToList(),
        SortModels = new List<SortModel> { new(columns[0], "asc") },
    };
}
