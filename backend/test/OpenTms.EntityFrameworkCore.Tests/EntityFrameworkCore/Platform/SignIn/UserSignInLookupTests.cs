using System;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Platform.SignIn;
using Shouldly;
using Volo.Abp.Account.Settings;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Settings;
using Volo.Abp.TenantManagement;
using Xunit;

namespace OpenTms.EntityFrameworkCore.Platform.SignIn;

/// <summary>
/// The sign-in lookup's bookkeeping and self-registration being off (docs/designs/62-sign-in-tenant-detection.md), on
/// in-memory SQLite. The database-per-tenant crossing is covered by UserSignInLookupPostgreSqlTests.
/// Risk class: Critical — authentication and tenant isolation.
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class UserSignInLookupTests : OpenTmsEntityFrameworkCoreTestBase
{
    private readonly IUserSignInLookupStore _store;
    private readonly IRepository<UserSignInLookup, Guid> _rows;
    private readonly ICurrentTenant _currentTenant;

    public UserSignInLookupTests()
    {
        _store = GetRequiredService<IUserSignInLookupStore>();
        _rows = GetRequiredService<IRepository<UserSignInLookup, Guid>>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
    }

    private Task<UserSignInLookup[]> RowsOfAsync(Guid? tenantId) =>
        WithUnitOfWorkAsync(async () => (await _rows.GetListAsync(r => r.UserTenantId == tenantId)).ToArray());

    [Fact]
    public async Task A_user_created_in_a_tenant_gets_a_row_with_the_hash_only()
    {
        var tenantId = Guid.NewGuid();
        var user = new IdentityUser(Guid.NewGuid(), "planner.jan", "jan@example.test", tenantId);
        await WithUnitOfWorkAsync(async () =>
        {
            using (_currentTenant.Change(tenantId))
            {
                (await GetRequiredService<IdentityUserManager>().CreateAsync(user, "1q2w3E*")).Succeeded.ShouldBeTrue();
            }
        });

        var row = (await RowsOfAsync(tenantId)).ShouldHaveSingleItem();
        row.Id.ShouldBe(user.Id);
        row.EmailHash.ShouldBe(UserSignInLookupHash.Of("JAN@EXAMPLE.TEST"));
        row.EmailHash.Length.ShouldBe(UserSignInLookup.HashLength);
        row.EmailHash.ShouldNotContain("jan");
    }

    [Fact]
    public async Task Replacing_one_tenants_rows_leaves_every_other_tenant_and_the_host_alone()
    {
        // constitution rule 1: a rebuild of tenant A never touches tenant B's rows.
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var userB = Guid.NewGuid();
        var hostUser = Guid.NewGuid();
        await _store.SyncUserAsync(tenantA, Guid.NewGuid(), UserSignInLookupHash.Of("OLD"));
        await _store.SyncUserAsync(tenantB, userB, UserSignInLookupHash.Of("B"));
        await _store.SyncUserAsync(null, hostUser, UserSignInLookupHash.Of("HOST"));

        var userA = Guid.NewGuid();
        await _store.ReplaceTenantAsync(tenantA, [new UserSignInLookupEntry(userA, UserSignInLookupHash.Of("NEW"))]);

        (await RowsOfAsync(tenantA)).Select(r => r.Id).ShouldBe([userA]);
        (await RowsOfAsync(tenantB)).Select(r => r.Id).ShouldBe([userB]);
        (await RowsOfAsync(null)).Select(r => r.Id).ShouldContain(hostUser);
    }

    [Fact]
    public async Task The_same_address_in_two_tenants_gives_two_candidates()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var hash = UserSignInLookupHash.Of("SHARED-" + Guid.NewGuid().ToString("N"));
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        await _store.SyncUserAsync(tenantA, userA, hash);
        await _store.SyncUserAsync(tenantB, userB, hash);

        (await _store.FindAsync(hash)).ShouldBe([new SignInCandidate(tenantA, userA), new SignInCandidate(tenantB, userB)], ignoreOrder: true);
    }

    [Fact]
    public async Task A_deleted_tenant_loses_its_rows()
    {
        Tenant? tenant = null;
        await WithUnitOfWorkAsync(async () =>
        {
            tenant = await GetRequiredService<ITenantManager>().CreateAsync("sign-in-" + Guid.NewGuid().ToString("N")[..8]);
            await GetRequiredService<ITenantRepository>().InsertAsync(tenant);
        });
        await _store.SyncUserAsync(tenant!.Id, Guid.NewGuid(), UserSignInLookupHash.Of("GONE"));

        await WithUnitOfWorkAsync(() => GetRequiredService<ITenantRepository>().DeleteAsync(tenant.Id));

        (await RowsOfAsync(tenant.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Self_registration_is_off()
    {
        (await GetRequiredService<ISettingProvider>().IsTrueAsync(AccountSettingNames.IsSelfRegistrationEnabled)).ShouldBeFalse();
        GetRequiredService<ISettingDefinitionManager>().GetAsync(AccountSettingNames.IsSelfRegistrationEnabled).Result
            .IsVisibleToClients.ShouldBeFalse();
    }
}
