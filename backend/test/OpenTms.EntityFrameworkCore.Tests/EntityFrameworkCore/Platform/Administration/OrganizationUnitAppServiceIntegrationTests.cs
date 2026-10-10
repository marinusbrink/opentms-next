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
/// Integration tests for OrganizationUnitAppService against in-memory SQLite.
/// Risk class: Critical for tenant isolation (every call reads or writes tenant data) and for
/// the cascading delete; High for membership changes (idempotent, at-least-once safe).
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class OrganizationUnitAppServiceIntegrationTests : OpenTmsEntityFrameworkCoreTestBase
{
    private readonly IOrganizationUnitAppService _service;
    private readonly ICurrentTenant _currentTenant;
    private readonly IdentityUserManager _userManager;

    public OrganizationUnitAppServiceIntegrationTests()
    {
        _service = GetRequiredService<IOrganizationUnitAppService>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
        _userManager = GetRequiredService<IdentityUserManager>();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static GridRequest Page(string? search = null) => new() { StartRow = 0, EndRow = 50, WildcardSearch = search };

    private static string UniqueName(string prefix) => prefix + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);

    private async Task<AdministrationOrganizationUnitRowDto> CreateUnitAsync(Guid tenantId, string name, Guid? parentId = null)
    {
        AdministrationOrganizationUnitRowDto? row = null;
        await WithUnitOfWorkAsync(async () =>
        {
            using (_currentTenant.Change(tenantId))
            {
                row = await _service.CreateAsync(new AdministrationOrganizationUnitCreateDto { DisplayName = name, ParentId = parentId });
            }
        });
        return row!;
    }

    private async Task<IdentityUser> CreateUserAsync(Guid tenantId)
    {
        IdentityUser? user = null;
        await WithUnitOfWorkAsync(async () =>
        {
            using (_currentTenant.Change(tenantId))
            {
                var suffix = Guid.NewGuid().ToString("N").Substring(0, 8);
                user = new IdentityUser(Guid.NewGuid(), $"ou-{suffix}", $"ou{suffix}@t.nl", tenantId);
                var result = await _userManager.CreateAsync(user);
                if (!result.Succeeded)
                    throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));
            }
        });
        return user!;
    }

    private static GridSelectionDto Explicit(params Guid[] ids) => new() { Mode = "Explicit", ExplicitIds = ids.ToList() };

    private async Task<OrganizationUnitMembersResponseDto> AddAsync(Guid tenantId, Guid unitId, params Guid[] userIds)
    {
        OrganizationUnitMembersResponseDto? result = null;
        await WithUnitOfWorkAsync(async () =>
        {
            using (_currentTenant.Change(tenantId))
            {
                result = await _service.AddMembersAsync(unitId, new OrganizationUnitMembersRequestDto { Selection = Explicit(userIds) });
            }
        });
        return result!;
    }

    // ── Tree listing ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetListAsync_returns_roots_with_child_flags_and_children_per_parent()
    {
        var tenantId = Guid.NewGuid();
        var root = await CreateUnitAsync(tenantId, UniqueName("root"));
        var child = await CreateUnitAsync(tenantId, UniqueName("child"), root.Id);
        var leafRoot = await CreateUnitAsync(tenantId, UniqueName("leaf"));

        using (_currentTenant.Change(tenantId))
        {
            var roots = await _service.GetListAsync(Page(), parentId: null);
            roots.Rows.Select(r => r.Id).ShouldBe(new[] { root.Id, leafRoot.Id }, ignoreOrder: true);
            roots.Rows.Single(r => r.Id == root.Id).HasChildren.ShouldBeTrue();
            roots.Rows.Single(r => r.Id == leafRoot.Id).HasChildren.ShouldBeFalse();
            roots.Rows.Single(r => r.Id == root.Id).Path.ShouldBe(root.DisplayName);

            var children = await _service.GetListAsync(Page(), parentId: root.Id);
            children.Rows.Select(r => r.Id).ShouldBe(new[] { child.Id });
            children.Rows[0].ParentDisplayName.ShouldBe(root.DisplayName);
            children.Rows[0].Path.ShouldBe($"{root.DisplayName} / {child.DisplayName}");
            children.FilteredCount.ShouldBe(1);
        }
    }

    [Fact]
    public async Task GetListAsync_with_search_returns_matches_from_every_level_as_a_flat_list()
    {
        var tenantId = Guid.NewGuid();
        var term = "zoek" + Guid.NewGuid().ToString("N").Substring(0, 6);
        var root = await CreateUnitAsync(tenantId, UniqueName("root"));
        var match = await CreateUnitAsync(tenantId, $"Afdeling {term} Rotterdam", root.Id);
        await CreateUnitAsync(tenantId, UniqueName("other"), root.Id);

        using (_currentTenant.Change(tenantId))
        {
            var result = await _service.GetListAsync(Page(term.ToUpperInvariant()), parentId: null);
            result.Rows.Select(r => r.Id).ShouldBe(new[] { match.Id });
            result.Rows[0].HasChildren.ShouldBeFalse("search results are leaves: the path column shows where they sit");
            result.FilteredCount.ShouldBe(1);
            result.TotalCount.ShouldBe(3);
        }
    }

    [Fact]
    public async Task GetListAsync_counts_members_per_unit()
    {
        var tenantId = Guid.NewGuid();
        var unit = await CreateUnitAsync(tenantId, UniqueName("unit"));
        var user1 = await CreateUserAsync(tenantId);
        var user2 = await CreateUserAsync(tenantId);
        await AddAsync(tenantId, unit.Id, user1.Id, user2.Id);

        using (_currentTenant.Change(tenantId))
        {
            var result = await _service.GetListAsync(Page(), parentId: null);
            result.Rows.Single(r => r.Id == unit.Id).MemberCount.ShouldBe(2);
        }
    }

    // ── Create / update ───────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_rejects_a_duplicate_display_name_and_an_unknown_parent()
    {
        var tenantId = Guid.NewGuid();
        var name = UniqueName("dup");
        await CreateUnitAsync(tenantId, name);

        using (_currentTenant.Change(tenantId))
        {
            await Should.ThrowAsync<BusinessException>(
                () => _service.CreateAsync(new AdministrationOrganizationUnitCreateDto { DisplayName = name }));

            await Should.ThrowAsync<EntityNotFoundException>(
                () => _service.CreateAsync(new AdministrationOrganizationUnitCreateDto { DisplayName = UniqueName("x"), ParentId = Guid.NewGuid() }));
        }
    }

    [Fact]
    public async Task UpdateAsync_renames_the_unit_and_keeps_its_place_in_the_tree()
    {
        var tenantId = Guid.NewGuid();
        var root = await CreateUnitAsync(tenantId, UniqueName("root"));
        var child = await CreateUnitAsync(tenantId, UniqueName("child"), root.Id);
        var newName = UniqueName("renamed");

        using (_currentTenant.Change(tenantId))
        {
            // ParentId is "the parent after the update": the caller repeats it to keep the unit in place.
            var updated = await _service.UpdateAsync(child.Id, new AdministrationOrganizationUnitUpdateDto { DisplayName = "  " + newName + " ", ParentId = root.Id });
            updated.DisplayName.ShouldBe(newName);
            updated.ParentId.ShouldBe(root.Id);
            updated.Code.ShouldBe(child.Code);
        }
    }

    [Fact]
    public async Task UpdateAsync_moves_the_unit_with_its_subtree_under_another_parent()
    {
        var tenantId = Guid.NewGuid();
        var rootA = await CreateUnitAsync(tenantId, UniqueName("a"));
        var rootB = await CreateUnitAsync(tenantId, UniqueName("b"));
        var child = await CreateUnitAsync(tenantId, UniqueName("child"), rootA.Id);
        var grandChild = await CreateUnitAsync(tenantId, UniqueName("grandchild"), child.Id);

        await WithUnitOfWorkAsync(async () =>
        {
            using (_currentTenant.Change(tenantId))
            {
                var moved = await _service.UpdateAsync(child.Id, new AdministrationOrganizationUnitUpdateDto { DisplayName = child.DisplayName, ParentId = rootB.Id });
                moved.ParentId.ShouldBe(rootB.Id);
                moved.Code.ShouldStartWith(rootB.Code + ".");
            }
        });

        using (_currentTenant.Change(tenantId))
        {
            var underB = await _service.GetListAsync(Page(), parentId: rootB.Id);
            underB.Rows.Select(r => r.Id).ShouldBe(new[] { child.Id });
            var underChild = await _service.GetListAsync(Page(), parentId: child.Id);
            underChild.Rows.Single().Id.ShouldBe(grandChild.Id);
            underChild.Rows.Single().Path.ShouldBe($"{rootB.DisplayName} / {child.DisplayName} / {grandChild.DisplayName}");

            var toRoot = await _service.UpdateAsync(child.Id, new AdministrationOrganizationUnitUpdateDto { DisplayName = child.DisplayName, ParentId = null });
            toRoot.ParentId.ShouldBeNull();
        }
    }

    [Fact]
    public async Task UpdateAsync_refuses_to_move_a_unit_under_itself_or_its_descendant_or_a_foreign_parent()
    {
        var tenantId = Guid.NewGuid();
        var root = await CreateUnitAsync(tenantId, UniqueName("root"));
        var child = await CreateUnitAsync(tenantId, UniqueName("child"), root.Id);
        var foreign = await CreateUnitAsync(Guid.NewGuid(), UniqueName("foreign"));

        using (_currentTenant.Change(tenantId))
        {
            await Should.ThrowAsync<OrganizationUnitMoveException>(
                () => _service.UpdateAsync(root.Id, new AdministrationOrganizationUnitUpdateDto { DisplayName = root.DisplayName, ParentId = root.Id }));
            await Should.ThrowAsync<OrganizationUnitMoveException>(
                () => _service.UpdateAsync(root.Id, new AdministrationOrganizationUnitUpdateDto { DisplayName = root.DisplayName, ParentId = child.Id }));
            await Should.ThrowAsync<EntityNotFoundException>(
                () => _service.UpdateAsync(root.Id, new AdministrationOrganizationUnitUpdateDto { DisplayName = root.DisplayName, ParentId = foreign.Id }));

            var all = await _service.GetAllAsync();
            all.Select(u => u.Id).ShouldBe(new[] { root.Id, child.Id }, "tree order, nothing moved, nothing foreign");
        }
    }

    // ── Delete (two-phase) ────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_deletes_an_empty_unit_immediately()
    {
        var tenantId = Guid.NewGuid();
        var unit = await CreateUnitAsync(tenantId, UniqueName("empty"));

        using (_currentTenant.Change(tenantId))
        {
            await _service.DeleteAsync(unit.Id, force: false);
            var roots = await _service.GetListAsync(Page(), parentId: null);
            roots.Rows.ShouldNotContain(r => r.Id == unit.Id);
        }
    }

    [Fact]
    public async Task DeleteAsync_reports_sub_units_and_members_unless_forced_then_cascades()
    {
        var tenantId = Guid.NewGuid();
        var root = await CreateUnitAsync(tenantId, UniqueName("root"));
        var child = await CreateUnitAsync(tenantId, UniqueName("child"), root.Id);
        var grandChild = await CreateUnitAsync(tenantId, UniqueName("grandchild"), child.Id);
        var user = await CreateUserAsync(tenantId);
        await AddAsync(tenantId, root.Id, user.Id);

        using (_currentTenant.Change(tenantId))
        {
            var ex = await Should.ThrowAsync<OrganizationUnitInUseException>(() => _service.DeleteAsync(root.Id, force: false));
            ex.DisplayName.ShouldBe(root.DisplayName);
            ex.ChildCount.ShouldBe(2, "both levels of sub-units count");
            ex.MemberCount.ShouldBe(1);
        }

        await WithUnitOfWorkAsync(async () =>
        {
            using (_currentTenant.Change(tenantId))
            {
                await _service.DeleteAsync(root.Id, force: true);
            }
        });

        using (_currentTenant.Change(tenantId))
        {
            var roots = await _service.GetListAsync(Page(), parentId: null);
            roots.Rows.ShouldNotContain(r => r.Id == root.Id);
            await Should.ThrowAsync<EntityNotFoundException>(() => _service.GetMembersAsync(child.Id, Page()));
            await Should.ThrowAsync<EntityNotFoundException>(() => _service.GetMembersAsync(grandChild.Id, Page()));

            var units = await _userManager.GetOrganizationUnitsAsync(await _userManager.GetByIdAsync(user.Id));
            units.ShouldBeEmpty("the membership goes with the unit");
        }
    }

    [Fact]
    public async Task DeleteAsync_throws_EntityNotFoundException_for_an_unknown_unit()
    {
        using (_currentTenant.Change(Guid.NewGuid()))
        {
            await Should.ThrowAsync<EntityNotFoundException>(() => _service.DeleteAsync(Guid.NewGuid()));
        }
    }

    // ── Members ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddMembersAsync_adds_once_and_is_idempotent()
    {
        var tenantId = Guid.NewGuid();
        var unit = await CreateUnitAsync(tenantId, UniqueName("unit"));
        var user = await CreateUserAsync(tenantId);

        var first = await AddAsync(tenantId, unit.Id, user.Id, user.Id);
        first.AffectedCount.ShouldBe(1, "a user listed twice is added once");

        var second = await AddAsync(tenantId, unit.Id, user.Id);
        second.AffectedCount.ShouldBe(0, "re-running the same request changes nothing (rule 3)");

        using (_currentTenant.Change(tenantId))
        {
            var members = await _service.GetMembersAsync(unit.Id, Page());
            members.Rows.Select(r => r.Id).ShouldBe(new[] { user.Id });
            members.TotalCount.ShouldBe(1);

            var unadded = await _service.GetUnaddedUsersAsync(unit.Id, Page());
            unadded.Rows.ShouldNotContain(r => r.Id == user.Id);
        }
    }

    [Fact]
    public async Task RemoveMembersAsync_removes_members_and_ignores_non_members()
    {
        var tenantId = Guid.NewGuid();
        var unit = await CreateUnitAsync(tenantId, UniqueName("unit"));
        var member = await CreateUserAsync(tenantId);
        var outsider = await CreateUserAsync(tenantId);
        await AddAsync(tenantId, unit.Id, member.Id);

        OrganizationUnitMembersResponseDto? result = null;
        await WithUnitOfWorkAsync(async () =>
        {
            using (_currentTenant.Change(tenantId))
            {
                result = await _service.RemoveMembersAsync(unit.Id,
                    new OrganizationUnitMembersRequestDto { Selection = Explicit(member.Id, outsider.Id, Guid.NewGuid()) });
            }
        });
        result!.AffectedCount.ShouldBe(1);

        using (_currentTenant.Change(tenantId))
        {
            var members = await _service.GetMembersAsync(unit.Id, Page());
            members.Rows.ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task GetMembersAsync_filters_by_wildcard_search()
    {
        var tenantId = Guid.NewGuid();
        var unit = await CreateUnitAsync(tenantId, UniqueName("unit"));
        var user1 = await CreateUserAsync(tenantId);
        var user2 = await CreateUserAsync(tenantId);
        await AddAsync(tenantId, unit.Id, user1.Id, user2.Id);

        using (_currentTenant.Change(tenantId))
        {
            var result = await _service.GetMembersAsync(unit.Id, Page(user1.UserName));
            result.Rows.Select(r => r.Id).ShouldBe(new[] { user1.Id });
            result.FilteredCount.ShouldBe(1);
            result.TotalCount.ShouldBe(2);
        }
    }

    [Fact]
    public async Task AddMembersAsync_FilterBased_selection_adds_every_unadded_user_matching_the_filter()
    {
        var tenantId = Guid.NewGuid();
        var unit = await CreateUnitAsync(tenantId, UniqueName("unit"));
        var user1 = await CreateUserAsync(tenantId);
        var user2 = await CreateUserAsync(tenantId);

        OrganizationUnitMembersResponseDto? result = null;
        await WithUnitOfWorkAsync(async () =>
        {
            using (_currentTenant.Change(tenantId))
            {
                result = await _service.AddMembersAsync(unit.Id, new OrganizationUnitMembersRequestDto
                {
                    Selection = new GridSelectionDto
                    {
                        Mode = "FilterBased",
                        FilterRequest = new GridRequest { StartRow = 0, EndRow = 100, WildcardSearch = "ou-" },
                        ExcludedIds = new List<Guid> { user2.Id }
                    }
                });
            }
        });

        result!.AffectedCount.ShouldBe(1, "user2 is excluded from the select-all");
        using (_currentTenant.Change(tenantId))
        {
            var members = await _service.GetMembersAsync(unit.Id, Page());
            members.Rows.Select(r => r.Id).ShouldBe(new[] { user1.Id });
        }
    }

    // ── Tenant isolation (constitution rule 1) ────────────────────────────────

    [Fact]
    public async Task Units_of_another_tenant_are_invisible_and_untouchable()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var unitInB = await CreateUnitAsync(tenantB, UniqueName("b-unit"));

        using (_currentTenant.Change(tenantA))
        {
            var roots = await _service.GetListAsync(Page(), parentId: null);
            roots.Rows.ShouldNotContain(r => r.Id == unitInB.Id, "tenant B's unit must not be listed in tenant A");

            var search = await _service.GetListAsync(Page(unitInB.DisplayName), parentId: null);
            search.Rows.ShouldBeEmpty();

            await Should.ThrowAsync<EntityNotFoundException>(() => _service.GetMembersAsync(unitInB.Id, Page()));
            await Should.ThrowAsync<EntityNotFoundException>(
                () => _service.UpdateAsync(unitInB.Id, new AdministrationOrganizationUnitUpdateDto { DisplayName = "hacked" }));
            await Should.ThrowAsync<EntityNotFoundException>(() => _service.DeleteAsync(unitInB.Id, force: true));
            await Should.ThrowAsync<EntityNotFoundException>(
                () => _service.CreateAsync(new AdministrationOrganizationUnitCreateDto { DisplayName = UniqueName("a-child"), ParentId = unitInB.Id }));
        }

        using (_currentTenant.Change(tenantB))
        {
            var roots = await _service.GetListAsync(Page(), parentId: null);
            roots.Rows.Single(r => r.Id == unitInB.Id).DisplayName.ShouldBe(unitInB.DisplayName);
        }
    }

    [Fact]
    public async Task AddMembersAsync_cannot_add_a_user_of_another_tenant()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var unitInA = await CreateUnitAsync(tenantA, UniqueName("a-unit"));
        var userInB = await CreateUserAsync(tenantB);

        var result = await AddAsync(tenantA, unitInA.Id, userInB.Id);
        result.AffectedCount.ShouldBe(0, "a user of tenant B is not found in tenant A's context");

        using (_currentTenant.Change(tenantA))
        {
            var members = await _service.GetMembersAsync(unitInA.Id, Page());
            members.Rows.ShouldBeEmpty();
        }

        using (_currentTenant.Change(tenantB))
        {
            var units = await _userManager.GetOrganizationUnitsAsync(await _userManager.GetByIdAsync(userInB.Id));
            units.ShouldBeEmpty();
        }
    }
}
