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

    // ── Critical: BulkDeleteAsync — static-role skip guard ───────────────────

    [Fact]
    public async Task BulkDeleteAsync_skips_static_role_and_returns_skip_entry()
    {
        var tenantId = Guid.NewGuid();
        IdentityRole? staticRole = null;
        await WithUnitOfWorkAsync(async () =>
        {
            using (_currentTenant.Change(tenantId))
            {
                staticRole = new IdentityRole(Guid.NewGuid(), "admin", tenantId) { IsStatic = true };
                EnsureSucceeded(await _roleManager.CreateAsync(staticRole));
            }
        });

        using (_currentTenant.Change(tenantId))
        {
            var result = await _roleAppService.BulkDeleteAsync(new BulkDeleteRolesRequestDto
            {
                Selection = new GridSelectionDto
                {
                    Mode = "Explicit",
                    ExplicitIds = new List<Guid> { staticRole!.Id }
                }
            });

            result.DeletedCount.ShouldBe(0);
            result.SkippedRows.Count.ShouldBe(1);
            result.SkippedRows[0].Id.ShouldBe(staticRole.Id);
            result.SkippedRows[0].Name.ShouldBe("admin");
            result.SkippedRows[0].Reason.ShouldBe("Administration:StaticRole");
        }
    }

    [Fact]
    public async Task BulkDeleteAsync_deletes_non_static_roles()
    {
        var tenantId = Guid.NewGuid();
        var role1 = await CreateRoleAsync(tenantId, UniqueRoleName());
        var role2 = await CreateRoleAsync(tenantId, UniqueRoleName());

        using (_currentTenant.Change(tenantId))
        {
            var result = await _roleAppService.BulkDeleteAsync(new BulkDeleteRolesRequestDto
            {
                Selection = new GridSelectionDto
                {
                    Mode = "Explicit",
                    ExplicitIds = new List<Guid> { role1.Id, role2.Id }
                }
            });

            result.DeletedCount.ShouldBe(2);
            result.SkippedRows.ShouldBeEmpty();

            // Verify roles are gone
            var list = await _roleAppService.GetListAsync(new GridRequest { StartRow = 0, EndRow = 50 });
            list.Rows.ShouldNotContain(r => r.Id == role1.Id || r.Id == role2.Id);
        }
    }

    [Fact]
    public async Task BulkDeleteAsync_mixed_selection_deletes_non_static_and_skips_static()
    {
        var tenantId = Guid.NewGuid();
        var normalRole = await CreateRoleAsync(tenantId, UniqueRoleName());
        IdentityRole? staticRole = null;
        await WithUnitOfWorkAsync(async () =>
        {
            using (_currentTenant.Change(tenantId))
            {
                staticRole = new IdentityRole(Guid.NewGuid(), "admin", tenantId) { IsStatic = true };
                EnsureSucceeded(await _roleManager.CreateAsync(staticRole));
            }
        });

        using (_currentTenant.Change(tenantId))
        {
            var result = await _roleAppService.BulkDeleteAsync(new BulkDeleteRolesRequestDto
            {
                Selection = new GridSelectionDto
                {
                    Mode = "Explicit",
                    ExplicitIds = new List<Guid> { normalRole.Id, staticRole!.Id }
                }
            });

            result.DeletedCount.ShouldBe(1);
            result.SkippedRows.Count.ShouldBe(1);
            result.SkippedRows[0].Id.ShouldBe(staticRole.Id);
            result.SkippedRows[0].Reason.ShouldBe("Administration:StaticRole");
        }
    }

    [Fact]
    public async Task BulkDeleteAsync_already_deleted_role_counted_as_deleted_idempotent()
    {
        // Already-deleted role (role not found in DB) is treated as successfully deleted per design.
        var tenantId = Guid.NewGuid();
        var phantomId = Guid.NewGuid();

        using (_currentTenant.Change(tenantId))
        {
            var result = await _roleAppService.BulkDeleteAsync(new BulkDeleteRolesRequestDto
            {
                Selection = new GridSelectionDto
                {
                    Mode = "Explicit",
                    ExplicitIds = new List<Guid> { phantomId }
                }
            });

            result.DeletedCount.ShouldBe(1, "concurrent-deletion scenario: already-gone role counts as deleted");
            result.SkippedRows.ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task BulkDeleteAsync_is_idempotent_running_same_selection_twice()
    {
        var tenantId = Guid.NewGuid();
        var role = await CreateRoleAsync(tenantId, UniqueRoleName());

        using (_currentTenant.Change(tenantId))
        {
            var selection = new BulkDeleteRolesRequestDto
            {
                Selection = new GridSelectionDto
                {
                    Mode = "Explicit",
                    ExplicitIds = new List<Guid> { role.Id }
                }
            };

            var first = await _roleAppService.BulkDeleteAsync(selection);
            first.DeletedCount.ShouldBe(1);

            // Second call: role is already gone — must count as deleted, not throw
            var second = await _roleAppService.BulkDeleteAsync(selection);
            second.DeletedCount.ShouldBe(1, "idempotent: already-deleted role counts as deleted on re-run");
            second.SkippedRows.ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task BulkDeleteAsync_tenant_isolation_cannot_delete_other_tenants_roles()
    {
        // Tenant isolation: passing tenant B's role IDs while in tenant A context must
        // not delete tenant B's data (constitution rule 1).
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var roleInB = await CreateRoleAsync(tenantB, UniqueRoleName());

        using (_currentTenant.Change(tenantA))
        {
            // The role exists in tenantB's scope, not tenantA's. FindAsync returns null → treated as deleted.
            await _roleAppService.BulkDeleteAsync(new BulkDeleteRolesRequestDto
            {
                Selection = new GridSelectionDto
                {
                    Mode = "Explicit",
                    ExplicitIds = new List<Guid> { roleInB.Id }
                }
            });
        }

        // Verify tenant B's role still exists
        using (_currentTenant.Change(tenantB))
        {
            var list = await _roleAppService.GetListAsync(new GridRequest { StartRow = 0, EndRow = 50 });
            list.Rows.ShouldContain(r => r.Id == roleInB.Id,
                "tenant B's role must not be deleted by a request made in tenant A's context (constitution rule 1)");
        }
    }

    // ── High: BulkDeleteAsync — FilterBased mode ──────────────────────────────

    [Fact]
    public async Task BulkDeleteAsync_FilterBased_resolves_roles_by_filter_and_deletes_them()
    {
        var tenantId = Guid.NewGuid();
        var prefix = "filterbased-" + Guid.NewGuid().ToString("N").Substring(0, 6);
        var role1 = await CreateRoleAsync(tenantId, prefix + "-a");
        var role2 = await CreateRoleAsync(tenantId, prefix + "-b");
        // An unrelated role that must not be deleted
        await CreateRoleAsync(tenantId, "unrelated-" + Guid.NewGuid().ToString("N").Substring(0, 6));

        using (_currentTenant.Change(tenantId))
        {
            var result = await _roleAppService.BulkDeleteAsync(new BulkDeleteRolesRequestDto
            {
                Selection = new GridSelectionDto
                {
                    Mode = "FilterBased",
                    FilterRequest = new GridRequest
                    {
                        StartRow = 0,
                        EndRow = 100,
                        WildcardSearch = prefix
                    },
                    ExcludedIds = new List<Guid>()
                }
            });

            result.DeletedCount.ShouldBe(2);
            result.SkippedRows.ShouldBeEmpty();

            var list = await _roleAppService.GetListAsync(new GridRequest { StartRow = 0, EndRow = 50 });
            list.Rows.ShouldNotContain(r => r.Id == role1.Id || r.Id == role2.Id,
                "FilterBased deletion must remove all roles matching the filter");
        }
    }

    [Fact]
    public async Task BulkDeleteAsync_FilterBased_respects_ExcludedIds()
    {
        var tenantId = Guid.NewGuid();
        var prefix = "excl-" + Guid.NewGuid().ToString("N").Substring(0, 6);
        var roleToDelete = await CreateRoleAsync(tenantId, prefix + "-delete");
        var roleToExclude = await CreateRoleAsync(tenantId, prefix + "-keep");

        using (_currentTenant.Change(tenantId))
        {
            var result = await _roleAppService.BulkDeleteAsync(new BulkDeleteRolesRequestDto
            {
                Selection = new GridSelectionDto
                {
                    Mode = "FilterBased",
                    FilterRequest = new GridRequest
                    {
                        StartRow = 0,
                        EndRow = 100,
                        WildcardSearch = prefix
                    },
                    ExcludedIds = new List<Guid> { roleToExclude.Id }
                }
            });

            result.DeletedCount.ShouldBe(1);
            result.SkippedRows.ShouldBeEmpty();

            var list = await _roleAppService.GetListAsync(new GridRequest { StartRow = 0, EndRow = 50 });
            list.Rows.ShouldNotContain(r => r.Id == roleToDelete.Id);
            list.Rows.ShouldContain(r => r.Id == roleToExclude.Id,
                "excluded role must survive the FilterBased bulk delete");
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
