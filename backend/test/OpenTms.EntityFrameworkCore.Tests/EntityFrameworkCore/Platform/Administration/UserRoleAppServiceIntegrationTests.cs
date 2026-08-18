using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.EntityFrameworkCore;
using OpenTms.Platform.Administration;
using OpenTms.Platform.Grid;
using Shouldly;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Identity;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace OpenTms.EntityFrameworkCore.Platform.Administration;

/// <summary>
/// Integration tests for UserRoleAppService against in-memory SQLite.
/// Risk class: High — two-phase role delete (design §Test risk analysis).
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class UserRoleAppServiceIntegrationTests : OpenTmsEntityFrameworkCoreTestBase
{
    private readonly IUserRoleAppService _roleAppService;
    private readonly ICurrentTenant _currentTenant;
    private readonly IdentityRoleManager _roleManager;
    private readonly IdentityUserManager _userManager;

    public UserRoleAppServiceIntegrationTests()
    {
        _roleAppService = GetRequiredService<IUserRoleAppService>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
        _roleManager = GetRequiredService<IdentityRoleManager>();
        _userManager = GetRequiredService<IdentityUserManager>();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void EnsureSucceeded(Microsoft.AspNetCore.Identity.IdentityResult result)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException(
                string.Join("; ", result.Errors.Select(e => e.Description)));
    }

    private async Task<IdentityRole> CreateRoleAsync(Guid tenantId, string name)
    {
        IdentityRole? role = null;
        await WithUnitOfWorkAsync(async () =>
        {
            using (_currentTenant.Change(tenantId))
            {
                role = new IdentityRole(Guid.NewGuid(), name, tenantId);
                EnsureSucceeded(await _roleManager.CreateAsync(role));
            }
        });
        return role!;
    }

    private async Task CreateUserWithRoleAsync(Guid tenantId, string roleName)
    {
        await WithUnitOfWorkAsync(async () =>
        {
            using (_currentTenant.Change(tenantId))
            {
                var suffix = Guid.NewGuid().ToString("N").Substring(0, 8);
                var user = new IdentityUser(Guid.NewGuid(), $"ru-{suffix}", $"ru{suffix}@t.nl", tenantId);
                EnsureSucceeded(await _userManager.CreateAsync(user));
                EnsureSucceeded(await _userManager.AddPasswordAsync(user, "TestP@ss1"));
                EnsureSucceeded(await _userManager.SetRolesAsync(user, new[] { roleName }));
            }
        });
    }

    private static string UniqueRoleName() =>
        ("role-" + Guid.NewGuid().ToString("N")).Substring(0, 14);

    // ── High: role delete two-phase ───────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_deletes_role_immediately_when_no_users_assigned()
    {
        var tenantId = Guid.NewGuid();
        var role = await CreateRoleAsync(tenantId, UniqueRoleName());

        using (_currentTenant.Change(tenantId))
        {
            // Phase 1 with no users → delete immediately (design: 204).
            await _roleAppService.DeleteAsync(role.Id, force: false);
        }
    }

    [Fact]
    public async Task DeleteAsync_throws_RoleHasUsersException_when_role_has_assigned_users()
    {
        var tenantId = Guid.NewGuid();
        var roleName = UniqueRoleName();
        var role = await CreateRoleAsync(tenantId, roleName);
        await CreateUserWithRoleAsync(tenantId, roleName);

        using (_currentTenant.Change(tenantId))
        {
            // Phase 1 with users → must throw (controller maps to HTTP 409).
            var ex = await Should.ThrowAsync<RoleHasUsersException>(
                () => _roleAppService.DeleteAsync(role.Id, force: false));

            ex.RoleName.ShouldBe(roleName);
            ex.UserCount.ShouldBeGreaterThan(0);
        }
    }

    [Fact]
    public async Task DeleteAsync_with_force_true_deletes_role_despite_assigned_users()
    {
        var tenantId = Guid.NewGuid();
        var roleName = UniqueRoleName();
        var role = await CreateRoleAsync(tenantId, roleName);
        await CreateUserWithRoleAsync(tenantId, roleName);

        using (_currentTenant.Change(tenantId))
        {
            // Phase 2 (force=true) must delete unconditionally.
            await _roleAppService.DeleteAsync(role.Id, force: true);
        }
    }

    [Fact]
    public async Task DeleteAsync_two_phase_flow_phase1_returns_impact_then_phase2_force_succeeds()
    {
        var tenantId = Guid.NewGuid();
        var roleName = UniqueRoleName();
        var role = await CreateRoleAsync(tenantId, roleName);
        await CreateUserWithRoleAsync(tenantId, roleName);

        using (_currentTenant.Change(tenantId))
        {
            // Phase 1: surface impact.
            var ex = await Should.ThrowAsync<RoleHasUsersException>(
                () => _roleAppService.DeleteAsync(role.Id, force: false));
            ex.UserCount.ShouldBe(1);

            // Phase 2: confirmed delete.
            await _roleAppService.DeleteAsync(role.Id, force: true);
        }
    }

    [Fact]
    public async Task DeleteAsync_throws_EntityNotFoundException_for_nonexistent_role()
    {
        // Concurrent deletion scenario: role already gone between list and delete click.
        var tenantId = Guid.NewGuid();

        using (_currentTenant.Change(tenantId))
        {
            await Should.ThrowAsync<EntityNotFoundException>(
                () => _roleAppService.DeleteAsync(Guid.NewGuid(), force: false));
        }
    }

    // ── High: userCount in list ───────────────────────────────────────────────

    [Fact]
    public async Task GetListAsync_returns_role_with_correct_userCount()
    {
        var tenantId = Guid.NewGuid();
        var roleName = UniqueRoleName();
        var role = await CreateRoleAsync(tenantId, roleName);
        await CreateUserWithRoleAsync(tenantId, roleName);
        await CreateUserWithRoleAsync(tenantId, roleName);

        using (_currentTenant.Change(tenantId))
        {
            var result = await _roleAppService.GetListAsync(new GridRequest { StartRow = 0, EndRow = 50 });

            var row = result.Rows.Find(r => r.Id == role.Id);
            row.ShouldNotBeNull();
            row!.UserCount.ShouldBe(2);
        }
    }

    // ── High: tenant isolation ────────────────────────────────────────────────

    [Fact]
    public async Task GetListAsync_tenant_isolation_roles_from_other_tenant_not_visible()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await CreateRoleAsync(tenantA, UniqueRoleName());

        using (_currentTenant.Change(tenantB))
        {
            var result = await _roleAppService.GetListAsync(new GridRequest { StartRow = 0, EndRow = 50 });
            result.TotalCount.ShouldBe(0,
                "roles from tenant A must not be visible in tenant B (constitution rule 1)");
        }
    }

    // ── High: create and update smoke ─────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_returns_role_with_correct_fields()
    {
        var tenantId = Guid.NewGuid();

        using (_currentTenant.Change(tenantId))
        {
            var result = await _roleAppService.CreateAsync(new AdministrationRoleCreateUpdateDto
            {
                Name = UniqueRoleName(),
                IsDefault = false,
                IsPublic = true
            });

            result.Id.ShouldNotBe(Guid.Empty);
            result.IsPublic.ShouldBeTrue();
            result.IsDefault.ShouldBeFalse();
            result.UserCount.ShouldBe(0);
            result.IsStatic.ShouldBeFalse();
        }
    }

    [Fact]
    public async Task UpdateAsync_changes_role_isDefault_and_isPublic()
    {
        var tenantId = Guid.NewGuid();

        using (_currentTenant.Change(tenantId))
        {
            var created = await _roleAppService.CreateAsync(new AdministrationRoleCreateUpdateDto
            {
                Name = UniqueRoleName(),
                IsDefault = false,
                IsPublic = false
            });

            var updated = await _roleAppService.UpdateAsync(created.Id, new AdministrationRoleCreateUpdateDto
            {
                Name = created.Name,
                IsDefault = true,
                IsPublic = true
            });

            updated.IsDefault.ShouldBeTrue();
            updated.IsPublic.ShouldBeTrue();
        }
    }
}
