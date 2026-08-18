using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.EntityFrameworkCore;
using OpenTms.Platform.Administration;
using OpenTms.Platform.Grid;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Identity;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace OpenTms.EntityFrameworkCore.Platform.Administration;

/// <summary>
/// Integration tests for UserAppService against in-memory SQLite.
/// Risk classification per design §Test risk analysis:
///   Critical — self-delete guard, last-admin-role guard, tenant isolation
///   High     — bulk delete, reset password
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class UserAppServiceIntegrationTests : OpenTmsEntityFrameworkCoreTestBase
{
    // FakeCurrentPrincipalAccessor hardcodes this ID as CurrentUser.Id across all tests.
    private static readonly Guid FakeCurrentUserId = Guid.Parse("2e701e62-0953-4dd3-910b-dc6cc93ccb0d");

    private readonly IUserAppService _userAppService;
    private readonly ICurrentTenant _currentTenant;
    private readonly IdentityUserManager _userManager;
    private readonly IdentityRoleManager _roleManager;
    private readonly IIdentityUserRepository _userRepository;

    public UserAppServiceIntegrationTests()
    {
        _userAppService = GetRequiredService<IUserAppService>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
        _userManager = GetRequiredService<IdentityUserManager>();
        _roleManager = GetRequiredService<IdentityRoleManager>();
        _userRepository = GetRequiredService<IIdentityUserRepository>();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static void EnsureSucceeded(Microsoft.AspNetCore.Identity.IdentityResult result)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException(
                string.Join("; ", result.Errors.Select(e => e.Description)));
    }

    // Creates the static "admin" role in the given tenant — mimics ABP's seed for isolated tenant contexts.
    private async Task<IdentityRole> CreateStaticAdminRoleAsync(Guid tenantId)
    {
        IdentityRole? role = null;
        await WithUnitOfWorkAsync(async () =>
        {
            using (_currentTenant.Change(tenantId))
            {
                role = new IdentityRole(Guid.NewGuid(), "admin", tenantId) { IsStatic = true, IsPublic = true };
                EnsureSucceeded(await _roleManager.CreateAsync(role));
            }
        });
        return role!;
    }

    // Creates a plain test user (not the fake current user).
    private async Task<IdentityUser> CreateTestUserAsync(Guid tenantId, IEnumerable<string>? roleNames = null)
    {
        IdentityUser? user = null;
        await WithUnitOfWorkAsync(async () =>
        {
            using (_currentTenant.Change(tenantId))
            {
                var suffix = Guid.NewGuid().ToString("N").Substring(0, 8);
                user = new IdentityUser(Guid.NewGuid(), $"u-{suffix}", $"u{suffix}@t.nl", tenantId);
                EnsureSucceeded(await _userManager.CreateAsync(user));
                EnsureSucceeded(await _userManager.AddPasswordAsync(user, "TestP@ss1"));
                if (roleNames != null)
                    EnsureSucceeded(await _userManager.SetRolesAsync(user, roleNames));
            }
        });
        return user!;
    }

    private async Task<IdentityUser> SetupSoleAdminAsync(Guid tenantId)
    {
        await CreateStaticAdminRoleAsync(tenantId);
        return await CreateTestUserAsync(tenantId, new[] { "admin" });
    }

    // ── Critical: self-delete guard ──────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_throws_SelfDeletionNotAllowed_before_loading_user()
    {
        // Guard fires before FindUserOrThrowAsync — the user need not exist in the DB.
        var ex = await Should.ThrowAsync<UserFriendlyException>(
            () => _userAppService.DeleteAsync(FakeCurrentUserId));

        // Must use the documented error key (localized on the frontend).
        ex.Message.ShouldContain("Administration:SelfDeletionNotAllowed");
    }

    // ── Critical: last-admin-role guard on delete ────────────────────────────

    [Fact]
    public async Task DeleteAsync_throws_LastAdminRoleHolder_for_sole_admin()
    {
        var tenantId = Guid.NewGuid();
        var adminUser = await SetupSoleAdminAsync(tenantId);

        using (_currentTenant.Change(tenantId))
        {
            var ex = await Should.ThrowAsync<UserFriendlyException>(
                () => _userAppService.DeleteAsync(adminUser.Id));

            ex.Message.ShouldContain("Administration:LastAdminRoleHolder");
        }
    }

    [Fact]
    public async Task DeleteAsync_succeeds_when_a_second_admin_exists()
    {
        var tenantId = Guid.NewGuid();
        await CreateStaticAdminRoleAsync(tenantId);
        var firstAdmin = await CreateTestUserAsync(tenantId, new[] { "admin" });
        await CreateTestUserAsync(tenantId, new[] { "admin" }); // second admin

        using (_currentTenant.Change(tenantId))
        {
            // First admin is not the last holder — delete must succeed.
            await _userAppService.DeleteAsync(firstAdmin.Id);

            var remaining = await _userRepository.GetCountAsync();
            remaining.ShouldBe(1);
        }
    }

    // ── Critical: last-admin-role guard on update ────────────────────────────

    [Fact]
    public async Task UpdateAsync_throws_LastAdminRoleHolder_when_removing_admin_role_from_sole_holder()
    {
        var tenantId = Guid.NewGuid();
        var adminUser = await SetupSoleAdminAsync(tenantId);

        using (_currentTenant.Change(tenantId))
        {
            var dto = new AdministrationUserUpdateDto
            {
                UserName = adminUser.UserName,
                Email = adminUser.Email,
                IsActive = true,
                RoleNames = new List<string>() // intentionally removing the admin role
            };

            var ex = await Should.ThrowAsync<UserFriendlyException>(
                () => _userAppService.UpdateAsync(adminUser.Id, dto));

            ex.Message.ShouldContain("Administration:LastAdminRoleHolder");
        }
    }

    [Fact]
    public async Task UpdateAsync_succeeds_when_admin_role_is_retained()
    {
        var tenantId = Guid.NewGuid();
        var adminUser = await SetupSoleAdminAsync(tenantId);

        using (_currentTenant.Change(tenantId))
        {
            var dto = new AdministrationUserUpdateDto
            {
                UserName = adminUser.UserName,
                Email = adminUser.Email,
                Name = "Updated",
                IsActive = true,
                RoleNames = new List<string> { "admin" }
            };

            var result = await _userAppService.UpdateAsync(adminUser.Id, dto);
            result.Name.ShouldBe("Updated");
            result.RoleNames.ShouldContain("admin");
        }
    }

    // ── Critical: tenant isolation ───────────────────────────────────────────

    [Fact]
    public async Task GetListAsync_tenant_isolation_users_from_other_tenant_not_visible()
    {
        // constitution rule 1: tenant isolation is sacred
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await CreateTestUserAsync(tenantA);

        using (_currentTenant.Change(tenantB))
        {
            var result = await _userAppService.GetListAsync(new GridRequest { StartRow = 0, EndRow = 50 });
            result.TotalCount.ShouldBe(0,
                "users from tenant A must not be visible in tenant B");
            result.Rows.ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task GetListAsync_tenant_isolation_host_users_not_visible_in_tenant_context()
    {
        // constitution rule 1: host-context users must be invisible in any tenant context.
        var newTenantId = Guid.NewGuid();

        // The host (null-tenant) users come from ABP seed; we verify a fresh tenant sees none of them.
        using (_currentTenant.Change(newTenantId))
        {
            var result = await _userAppService.GetListAsync(new GridRequest { StartRow = 0, EndRow = 50 });
            result.TotalCount.ShouldBe(0,
                "host-level users must not be visible in a tenant context");
        }
    }

    // ── High: bulk delete happy path ─────────────────────────────────────────

    [Fact]
    public async Task BulkDeleteAsync_deletes_all_users_in_explicit_selection()
    {
        var tenantId = Guid.NewGuid();
        var user1 = await CreateTestUserAsync(tenantId);
        var user2 = await CreateTestUserAsync(tenantId);

        using (_currentTenant.Change(tenantId))
        {
            var request = new BulkDeleteUsersRequestDto
            {
                Selection = new GridSelectionDto
                {
                    Mode = "Explicit",
                    ExplicitIds = new List<Guid> { user1.Id, user2.Id }
                }
            };

            var result = await _userAppService.BulkDeleteAsync(request);

            result.DeletedCount.ShouldBe(2);
            result.SkippedRows.ShouldBeEmpty();
            (await _userRepository.GetCountAsync()).ShouldBe(0);
        }
    }

    // ── High: bulk delete skips current user ─────────────────────────────────

    [Fact]
    public async Task BulkDeleteAsync_skips_current_user_with_SelfDeletionNotAllowed_reason()
    {
        // Self-delete guard fires before FindAsync — fake current user need not exist in DB.
        var tenantId = Guid.NewGuid();
        var otherUser = await CreateTestUserAsync(tenantId);

        using (_currentTenant.Change(tenantId))
        {
            var request = new BulkDeleteUsersRequestDto
            {
                Selection = new GridSelectionDto
                {
                    Mode = "Explicit",
                    ExplicitIds = new List<Guid> { FakeCurrentUserId, otherUser.Id }
                }
            };

            var result = await _userAppService.BulkDeleteAsync(request);

            result.DeletedCount.ShouldBe(1);
            result.SkippedRows.Count.ShouldBe(1);
            result.SkippedRows[0].Id.ShouldBe(FakeCurrentUserId);
            result.SkippedRows[0].Reason.ShouldBe("Administration:SelfDeletionNotAllowed");
        }
    }

    // ── High: bulk delete skips last admin ───────────────────────────────────

    [Fact]
    public async Task BulkDeleteAsync_skips_sole_admin_with_LastAdminRoleHolder_reason()
    {
        var tenantId = Guid.NewGuid();
        var adminUser = await SetupSoleAdminAsync(tenantId);
        var regularUser = await CreateTestUserAsync(tenantId);

        using (_currentTenant.Change(tenantId))
        {
            var request = new BulkDeleteUsersRequestDto
            {
                Selection = new GridSelectionDto
                {
                    Mode = "Explicit",
                    ExplicitIds = new List<Guid> { adminUser.Id, regularUser.Id }
                }
            };

            var result = await _userAppService.BulkDeleteAsync(request);

            result.DeletedCount.ShouldBe(1);
            result.SkippedRows.Count.ShouldBe(1);
            result.SkippedRows[0].Id.ShouldBe(adminUser.Id);
            result.SkippedRows[0].Reason.ShouldBe("Administration:LastAdminRoleHolder");
        }
    }

    // ── High: bulk delete idempotency ────────────────────────────────────────

    [Fact]
    public async Task BulkDeleteAsync_already_deleted_user_counted_as_deleted_not_skipped()
    {
        // Design: already-absent user counted in deletedCount (idempotent per spec).
        var tenantId = Guid.NewGuid();
        var phantomId = Guid.NewGuid();

        using (_currentTenant.Change(tenantId))
        {
            var request = new BulkDeleteUsersRequestDto
            {
                Selection = new GridSelectionDto
                {
                    Mode = "Explicit",
                    ExplicitIds = new List<Guid> { phantomId }
                }
            };

            var result = await _userAppService.BulkDeleteAsync(request);

            result.DeletedCount.ShouldBe(1);
            result.SkippedRows.ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task BulkDeleteAsync_re_running_same_selection_produces_same_deletedCount()
    {
        var tenantId = Guid.NewGuid();
        var user = await CreateTestUserAsync(tenantId);

        var request = new BulkDeleteUsersRequestDto
        {
            Selection = new GridSelectionDto
            {
                Mode = "Explicit",
                ExplicitIds = new List<Guid> { user.Id }
            }
        };

        BulkDeleteUsersResponseDto first, second;
        using (_currentTenant.Change(tenantId))
            first = await _userAppService.BulkDeleteAsync(request);
        using (_currentTenant.Change(tenantId))
            second = await _userAppService.BulkDeleteAsync(request);

        first.DeletedCount.ShouldBe(1);
        first.SkippedRows.ShouldBeEmpty();
        second.DeletedCount.ShouldBe(1);
        second.SkippedRows.ShouldBeEmpty();
    }

    // ── High: reset password ─────────────────────────────────────────────────

    [Fact]
    public async Task ResetPasswordAsync_succeeds_with_policy_compliant_password()
    {
        var tenantId = Guid.NewGuid();
        var user = await CreateTestUserAsync(tenantId);

        using (_currentTenant.Change(tenantId))
        {
            // Should not throw — strong password satisfies ABP Identity policy.
            await _userAppService.ResetPasswordAsync(user.Id, new AdministrationResetPasswordDto
            {
                NewPassword = "NewStr0ng!Pass99"
            });
        }
    }

    [Fact]
    public async Task ResetPasswordAsync_uses_token_based_path_second_reset_also_succeeds()
    {
        // Design: implemented via GeneratePasswordResetTokenAsync then ResetPasswordAsync.
        // Verifies a fresh token is generated on each call (not a one-time token).
        var tenantId = Guid.NewGuid();
        var user = await CreateTestUserAsync(tenantId);

        using (_currentTenant.Change(tenantId))
        {
            await _userAppService.ResetPasswordAsync(user.Id, new AdministrationResetPasswordDto
            {
                NewPassword = "First!Reset9Pass"
            });
            await _userAppService.ResetPasswordAsync(user.Id, new AdministrationResetPasswordDto
            {
                NewPassword = "Second!Reset9Pass"
            });
        }
    }

    [Fact]
    public async Task ResetPasswordAsync_fails_for_policy_violating_password()
    {
        var tenantId = Guid.NewGuid();
        var user = await CreateTestUserAsync(tenantId);

        using (_currentTenant.Change(tenantId))
        {
            var ex = await Record.ExceptionAsync(() =>
                _userAppService.ResetPasswordAsync(user.Id, new AdministrationResetPasswordDto
                {
                    NewPassword = "ab" // too short — violates minimum length policy
                }));

            ex.ShouldNotBeNull();
        }
    }

    [Fact]
    public async Task ResetPasswordAsync_throws_EntityNotFoundException_for_nonexistent_user()
    {
        var tenantId = Guid.NewGuid();

        using (_currentTenant.Change(tenantId))
        {
            await Should.ThrowAsync<EntityNotFoundException>(() =>
                _userAppService.ResetPasswordAsync(Guid.NewGuid(), new AdministrationResetPasswordDto
                {
                    NewPassword = "NewStr0ng!Pass99"
                }));
        }
    }

    // ── Grid list and field mapping ───────────────────────────────────────────

    [Fact]
    public async Task GetListAsync_returns_created_user_with_correct_fields()
    {
        var tenantId = Guid.NewGuid();

        await WithUnitOfWorkAsync(async () =>
        {
            using (_currentTenant.Change(tenantId))
            {
                var u = new IdentityUser(Guid.NewGuid(), "j.vries", "j.vries@test.nl", tenantId)
                {
                    Name = "Jan",
                    Surname = "de Vries"
                };
                EnsureSucceeded(await _userManager.CreateAsync(u));
                EnsureSucceeded(await _userManager.AddPasswordAsync(u, "TestP@ss1"));
            }
        });

        using (_currentTenant.Change(tenantId))
        {
            var result = await _userAppService.GetListAsync(new GridRequest { StartRow = 0, EndRow = 50 });

            result.TotalCount.ShouldBe(1);
            result.Rows.Count.ShouldBe(1);
            var row = result.Rows[0];
            row.UserName.ShouldBe("j.vries");
            row.Email.ShouldBe("j.vries@test.nl");
            row.Name.ShouldBe("Jan");
            row.Surname.ShouldBe("de Vries");
        }
    }

    [Fact]
    public async Task GetListAsync_filteredCount_matches_wildcard_search_and_totalCount_includes_all()
    {
        var tenantId = Guid.NewGuid();

        await WithUnitOfWorkAsync(async () =>
        {
            using (_currentTenant.Change(tenantId))
            {
                foreach (var (name, email) in new[]
                {
                    ("alice1", "alice1@test.nl"),
                    ("alice2", "alice2@test.nl"),
                    ("bob", "bob@test.nl")
                })
                {
                    var u = new IdentityUser(Guid.NewGuid(), name, email, tenantId);
                    EnsureSucceeded(await _userManager.CreateAsync(u));
                    EnsureSucceeded(await _userManager.AddPasswordAsync(u, "TestP@ss1"));
                }
            }
        });

        using (_currentTenant.Change(tenantId))
        {
            var result = await _userAppService.GetListAsync(new GridRequest
            {
                StartRow = 0,
                EndRow = 50,
                WildcardSearch = "ali"
            });

            result.TotalCount.ShouldBe(3);
            result.FilteredCount.ShouldBe(2);
            result.Rows.Count.ShouldBe(2);
        }
    }

    [Fact]
    public async Task CreateAsync_throws_UserFriendlyException_for_nonexistent_role_name()
    {
        var tenantId = Guid.NewGuid();

        using (_currentTenant.Change(tenantId))
        {
            await Should.ThrowAsync<UserFriendlyException>(() =>
                _userAppService.CreateAsync(new AdministrationUserCreateDto
                {
                    UserName = "newuser",
                    Email = "newuser@test.nl",
                    Password = "TestP@ss1",
                    RoleNames = new List<string> { "nonexistent-role" }
                }));
        }
    }
}
