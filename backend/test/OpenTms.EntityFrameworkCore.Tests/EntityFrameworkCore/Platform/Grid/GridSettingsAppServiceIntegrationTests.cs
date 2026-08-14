using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using OpenTms.EntityFrameworkCore;
using OpenTms.Platform.Grid;
using Shouldly;
using Volo.Abp;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace OpenTms.EntityFrameworkCore.Platform.Grid;

/// <summary>
/// Integration tests for GridSettingsAppService backed by an in-memory SQLite database.
/// Risk class: Critical — tenant isolation and data integrity of grid settings.
///
/// Note: SQLite is used in place of PostgreSQL per the CI test infrastructure. The
/// critical path (ABP ISettingManager with EF Core backing) is identical; only the
/// SQL dialect differs. PostgreSQL integration is validated by the production health-
/// tenant smoke test (availability SLO synthetic journey).
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class GridSettingsAppServiceIntegrationTests : OpenTmsEntityFrameworkCoreTestBase
{
    private readonly IGridSettingsAppService _service;
    private readonly ICurrentTenant _currentTenant;

    public GridSettingsAppServiceIntegrationTests()
    {
        _service = GetRequiredService<IGridSettingsAppService>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
    }

    // ── Happy path ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAsync_returns_null_when_no_settings_have_been_saved()
    {
        var result = await _service.GetAsync("never-saved-grid-" + Guid.NewGuid().ToString("N"));
        result.ShouldBeNull();
    }

    [Fact]
    public async Task SetAsync_then_GetAsync_returns_persisted_settings()
    {
        var gridId = "grid-roundtrip-" + Guid.NewGuid().ToString("N");
        var settings = new GridSettingsDto
        {
            ColumnStates = new List<ColumnStateDto>
            {
                new() { ColId = "reference", Visible = true, Order = 0, Width = 180 },
                new() { ColId = "status",    Visible = false, Order = 1, Width = null },
            },
            SortModels = new List<SortModel>
            {
                new("reference", "asc"),
            },
        };

        await _service.SetAsync(gridId, settings);

        var loaded = await _service.GetAsync(gridId);

        loaded.ShouldNotBeNull();
        loaded!.ColumnStates.Count.ShouldBe(2);
        loaded.ColumnStates[0].ColId.ShouldBe("reference");
        loaded.ColumnStates[0].Visible.ShouldBeTrue();
        loaded.ColumnStates[0].Width.ShouldBe(180);
        loaded.ColumnStates[1].ColId.ShouldBe("status");
        loaded.ColumnStates[1].Visible.ShouldBeFalse();
        loaded.ColumnStates[1].Width.ShouldBeNull();
        loaded.SortModels.Count.ShouldBe(1);
        loaded.SortModels[0].ColId.ShouldBe("reference");
        loaded.SortModels[0].Sort.ShouldBe("asc");
    }

    [Fact]
    public async Task SetAsync_stores_multiple_gridIds_independently_in_same_blob()
    {
        var gridId1 = "grid-multi-A-" + Guid.NewGuid().ToString("N");
        var gridId2 = "grid-multi-B-" + Guid.NewGuid().ToString("N");

        var settings1 = new GridSettingsDto
        {
            ColumnStates = new List<ColumnStateDto> { new() { ColId = "colA", Visible = true, Order = 0 } },
        };
        var settings2 = new GridSettingsDto
        {
            ColumnStates = new List<ColumnStateDto> { new() { ColId = "colB", Visible = false, Order = 0 } },
        };

        await _service.SetAsync(gridId1, settings1);
        await _service.SetAsync(gridId2, settings2);

        var loaded1 = await _service.GetAsync(gridId1);
        var loaded2 = await _service.GetAsync(gridId2);

        loaded1.ShouldNotBeNull();
        loaded1!.ColumnStates[0].ColId.ShouldBe("colA");

        loaded2.ShouldNotBeNull();
        loaded2!.ColumnStates[0].ColId.ShouldBe("colB");
    }

    [Fact]
    public async Task ResetAsync_removes_saved_settings_so_GetAsync_returns_null()
    {
        var gridId = "grid-reset-" + Guid.NewGuid().ToString("N");
        var settings = new GridSettingsDto
        {
            ColumnStates = new List<ColumnStateDto> { new() { ColId = "x", Visible = true, Order = 0 } },
        };

        await _service.SetAsync(gridId, settings);
        (await _service.GetAsync(gridId)).ShouldNotBeNull();

        await _service.ResetAsync(gridId);

        (await _service.GetAsync(gridId)).ShouldBeNull();
    }

    [Fact]
    public async Task ResetAsync_is_idempotent_on_nonexistent_gridId()
    {
        var gridId = "grid-reset-nonexist-" + Guid.NewGuid().ToString("N");

        // Should not throw when called on a grid that has never been saved.
        await _service.ResetAsync(gridId);
        await _service.ResetAsync(gridId);
    }

    [Fact]
    public async Task ResetAsync_removes_only_the_specified_gridId_leaving_others_intact()
    {
        var gridId1 = "grid-reset-partial-A-" + Guid.NewGuid().ToString("N");
        var gridId2 = "grid-reset-partial-B-" + Guid.NewGuid().ToString("N");

        await _service.SetAsync(gridId1, new GridSettingsDto
        {
            ColumnStates = new List<ColumnStateDto> { new() { ColId = "a", Visible = true, Order = 0 } },
        });
        await _service.SetAsync(gridId2, new GridSettingsDto
        {
            ColumnStates = new List<ColumnStateDto> { new() { ColId = "b", Visible = true, Order = 0 } },
        });

        await _service.ResetAsync(gridId1);

        (await _service.GetAsync(gridId1)).ShouldBeNull();
        (await _service.GetAsync(gridId2)).ShouldNotBeNull();
    }

    // ── Tenant isolation (Critical) ───────────────────────────────────────────

    [Fact]
    public async Task Tenant_isolation_settings_written_in_tenant_A_are_invisible_in_tenant_B()
    {
        // The ABP ISettingManager keys user-level settings by (TenantId, UserId).
        // Settings written in tenant A must not be readable in tenant B context.
        // A bug here would be a critical SaaS violation (constitution rule 1).

        var gridId = "grid-isolation-" + Guid.NewGuid().ToString("N");
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        using (_currentTenant.Change(tenantA))
        {
            await _service.SetAsync(gridId, new GridSettingsDto
            {
                ColumnStates = new List<ColumnStateDto>
                {
                    new() { ColId = "tenant-a-col", Visible = true, Order = 0 },
                },
            });
        }

        GridSettingsDto? result;
        using (_currentTenant.Change(tenantB))
        {
            result = await _service.GetAsync(gridId);
        }

        result.ShouldBeNull(
            "Settings from tenant A must not be visible in tenant B (constitution rule 1: tenant isolation is sacred).");
    }

    [Fact]
    public async Task Tenant_isolation_settings_written_in_host_context_are_invisible_in_tenant_context()
    {
        var gridId = "grid-isolation-host-" + Guid.NewGuid().ToString("N");
        var tenantId = Guid.NewGuid();

        // Write in host context (null tenant)
        await _service.SetAsync(gridId, new GridSettingsDto
        {
            ColumnStates = new List<ColumnStateDto>
            {
                new() { ColId = "host-col", Visible = true, Order = 0 },
            },
        });

        GridSettingsDto? result;
        using (_currentTenant.Change(tenantId))
        {
            result = await _service.GetAsync(gridId);
        }

        result.ShouldBeNull(
            "Host-context settings must not be visible in a tenant context.");
    }

    // ── Validation ────────────────────────────────────────────────────────────
    // Note: ABP validation fires before service code for [Required]-annotated properties,
    // so some inputs throw AbpValidationException instead of UserFriendlyException.
    // We therefore assert only that SOME exception is thrown, not its specific type.

    [Theory]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("has/slash")]
    // 101 chars — one over the {1,100} limit ("ab"×50 + "c" = 101):
    [InlineData("ababababababababababababababababababababababababababababababababababababababababababababababababababababababc")]
    public async Task GetAsync_throws_for_invalid_gridId(string invalidId)
    {
        var ex = await Record.ExceptionAsync(() => _service.GetAsync(invalidId));
        ex.ShouldNotBeNull($"expected an exception for gridId '{invalidId}' but none was thrown");
    }

    [Fact]
    public async Task SetAsync_throws_when_column_states_exceed_50()
    {
        var states = new List<ColumnStateDto>();
        for (int i = 0; i < 51; i++)
            states.Add(new ColumnStateDto { ColId = $"col{i}", Visible = true, Order = i });

        await Should.ThrowAsync<UserFriendlyException>(() =>
            _service.SetAsync("valid-grid", new GridSettingsDto { ColumnStates = states }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("has/slash")]
    // 65 chars — one over the {1,64} colId limit ("ab"×32 + "c" = 65):
    [InlineData("ababababababababababababababababababababababababababababababababababc")]
    public async Task SetAsync_throws_for_invalid_colId(string invalidColId)
    {
        var ex = await Record.ExceptionAsync(() =>
            _service.SetAsync("valid-grid", new GridSettingsDto
            {
                ColumnStates = new List<ColumnStateDto>
                {
                    new() { ColId = invalidColId, Visible = true, Order = 0 },
                },
            }));
        ex.ShouldNotBeNull($"expected an exception for colId '{invalidColId}' but none was thrown");
    }

    [Fact]
    public async Task SetAsync_accepts_exactly_50_column_states()
    {
        var gridId = "grid-50cols-" + Guid.NewGuid().ToString("N");
        var states = new List<ColumnStateDto>();
        for (int i = 0; i < 50; i++)
            states.Add(new ColumnStateDto { ColId = $"col{i}", Visible = true, Order = i });

        // Should not throw
        await _service.SetAsync(gridId, new GridSettingsDto { ColumnStates = states });
        (await _service.GetAsync(gridId)).ShouldNotBeNull();
    }

    [Fact]
    public async Task SetAsync_accepts_colId_at_max_length_64()
    {
        var gridId = "grid-colid64-" + Guid.NewGuid().ToString("N");
        var colId64 = new string('a', 64);

        await _service.SetAsync(gridId, new GridSettingsDto
        {
            ColumnStates = new List<ColumnStateDto>
            {
                new() { ColId = colId64, Visible = true, Order = 0 },
            },
        });

        var loaded = await _service.GetAsync(gridId);
        loaded!.ColumnStates[0].ColId.ShouldBe(colId64);
    }

    [Fact]
    public async Task SetAsync_throws_for_colId_of_65_chars()
    {
        await Should.ThrowAsync<UserFriendlyException>(() =>
            _service.SetAsync("valid-grid", new GridSettingsDto
            {
                ColumnStates = new List<ColumnStateDto>
                {
                    new() { ColId = new string('a', 65), Visible = true, Order = 0 },
                },
            }));
    }
}
