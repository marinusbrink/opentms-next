using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OpenTms.Platform.EntityFrameworkCore;
using OpenTms.Platform.SignIn;
using Shouldly;
using Volo.Abp.Data;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Identity;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace OpenTms.PostgreSql.Platform;

/// <summary>
/// The sign-in lookup against real PostgreSQL with a tenant in its own database (docs/designs/62-sign-in-tenant-detection.md).
/// Risk class: Critical — authentication and tenant isolation: a user changed in the tenant's database must reach the
/// host's lookup (or the sign-in page cannot find them), the lookup must never land in a tenant database, and it may
/// hold hashes and ids only.
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class UserSignInLookupPostgreSqlTests : OpenTmsPostgreSqlTestBase
{
    private readonly IdentityUserManager _userManager;
    private readonly IUserSignInLookupStore _store;
    private readonly ICurrentTenant _currentTenant;

    public UserSignInLookupPostgreSqlTests()
    {
        _userManager = GetRequiredService<IdentityUserManager>();
        _store = GetRequiredService<IUserSignInLookupStore>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
    }

    private static async Task<(long Rows, Guid? TenantId, string? EmailHash)> ReadRowAsync(string database, Guid userId)
    {
        await using var connection = new NpgsqlConnection(PostgreSqlTestDatabases.ConnectionStringFor(database));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT count(*) OVER (), \"UserTenantId\", \"EmailHash\" FROM platform.\"UserSignInLookups\" WHERE \"Id\" = @id",
            connection);
        command.Parameters.AddWithValue("id", userId);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return (0, null, null);
        }

        return (reader.GetInt64(0), reader.IsDBNull(1) ? null : reader.GetGuid(1), reader.GetString(2));
    }

    private static async Task<long> CountAllAsync(string database)
    {
        await using var connection = new NpgsqlConnection(PostgreSqlTestDatabases.ConnectionStringFor(database));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT count(*) FROM platform.\"UserSignInLookups\"", connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private async Task<IdentityUser> CreateUserAsync(Guid? tenantId, string userName)
    {
        IdentityUser? user = null;
        await WithUnitOfWorkAsync(async () =>
        {
            using (_currentTenant.Change(tenantId))
            {
                user = new IdentityUser(Guid.NewGuid(), userName, $"{userName}@example.test", tenantId);
                (await _userManager.CreateAsync(user, "1q2w3E*")).Succeeded.ShouldBeTrue();
            }
        });
        return user!;
    }

    [Fact]
    public async Task A_user_created_in_a_tenant_database_is_found_through_the_host_database_only()
    {
        // constitution rule 1: the lookup is host data; the tenant's own database never holds it.
        var userName = "lookup-" + Guid.NewGuid().ToString("N")[..8];
        var user = await CreateUserAsync(PostgreSqlTestDatabases.TenantAId, userName);

        var row = await ReadRowAsync(PostgreSqlTestDatabases.HostDatabase, user.Id);
        row.Rows.ShouldBe(1);
        row.TenantId.ShouldBe(PostgreSqlTestDatabases.TenantAId);
        row.EmailHash.ShouldBe(UserSignInLookupHash.Of($"{userName}@example.test".ToUpperInvariant()));
        row.EmailHash.ShouldNotContain(userName, Case.Insensitive);
        (await CountAllAsync(PostgreSqlTestDatabases.TenantADatabase)).ShouldBe(0);

        /* Found from anywhere: the sign-in page asks before a tenant is known, other code may ask inside one. */
        using (_currentTenant.Change(PostgreSqlTestDatabases.SharedTenantId))
        {
            (await _store.FindAsync(UserSignInLookupHash.Of($"{userName}@example.test".ToUpperInvariant())))
                .ShouldHaveSingleItem().ShouldBe(new SignInCandidate(PostgreSqlTestDatabases.TenantAId, user.Id));
        }
    }

    [Fact]
    public async Task A_user_whose_address_changes_is_found_by_the_new_one_only_and_a_deleted_user_not_at_all()
    {
        var oldName = "rename-" + Guid.NewGuid().ToString("N")[..8];
        var newName = oldName + "-new";
        var user = await CreateUserAsync(PostgreSqlTestDatabases.TenantAId, oldName);

        await WithUnitOfWorkAsync(async () =>
        {
            using (_currentTenant.Change(PostgreSqlTestDatabases.TenantAId))
            {
                var stored = await _userManager.GetByIdAsync(user.Id);
                (await _userManager.SetEmailAsync(stored, $"{newName}@example.test")).Succeeded.ShouldBeTrue();
            }
        });

        (await _store.FindAsync(UserSignInLookupHash.Of($"{oldName}@example.test".ToUpperInvariant()))).ShouldBeEmpty();
        (await _store.FindAsync(UserSignInLookupHash.Of($"{newName}@example.test".ToUpperInvariant()))).ShouldHaveSingleItem().UserId.ShouldBe(user.Id);

        await WithUnitOfWorkAsync(async () =>
        {
            using (_currentTenant.Change(PostgreSqlTestDatabases.TenantAId))
            {
                (await _userManager.DeleteAsync(await _userManager.GetByIdAsync(user.Id))).Succeeded.ShouldBeTrue();
            }
        });

        (await ReadRowAsync(PostgreSqlTestDatabases.HostDatabase, user.Id)).Rows.ShouldBe(0);
    }

    [Fact]
    public async Task The_seed_rebuilds_one_tenants_rows_and_leaves_the_others_alone()
    {
        var tenantUser = await CreateUserAsync(PostgreSqlTestDatabases.TenantAId, "seed-a-" + Guid.NewGuid().ToString("N")[..8]);
        var sharedUser = await CreateUserAsync(PostgreSqlTestDatabases.SharedTenantId, "seed-s-" + Guid.NewGuid().ToString("N")[..8]);

        /* The rows of both get lost (a crash between the user's commit and the lookup write). */
        await _store.SyncUserAsync(PostgreSqlTestDatabases.TenantAId, tenantUser.Id, null);
        await _store.SyncUserAsync(PostgreSqlTestDatabases.SharedTenantId, sharedUser.Id, null);

        await WithUnitOfWorkAsync(() =>
            GetRequiredService<UserSignInLookupDataSeedContributor>().SeedAsync(new DataSeedContext(PostgreSqlTestDatabases.TenantAId)));

        (await ReadRowAsync(PostgreSqlTestDatabases.HostDatabase, tenantUser.Id)).TenantId.ShouldBe(PostgreSqlTestDatabases.TenantAId);
        (await ReadRowAsync(PostgreSqlTestDatabases.HostDatabase, sharedUser.Id)).Rows
            .ShouldBe(0, "seeding tenant A rebuilds tenant A's rows only; the shared tenant waits for its own seed");

        await WithUnitOfWorkAsync(() =>
            GetRequiredService<UserSignInLookupDataSeedContributor>().SeedAsync(new DataSeedContext(PostgreSqlTestDatabases.SharedTenantId)));

        (await ReadRowAsync(PostgreSqlTestDatabases.HostDatabase, sharedUser.Id)).TenantId.ShouldBe(PostgreSqlTestDatabases.SharedTenantId);
        (await ReadRowAsync(PostgreSqlTestDatabases.HostDatabase, tenantUser.Id)).Rows.ShouldBe(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_migration_is_the_newest_and_nothing_is_pending(bool inTenantDatabase)
    {
        await WithUnitOfWorkAsync(async () =>
        {
            using (_currentTenant.Change(inTenantDatabase ? PostgreSqlTestDatabases.TenantAId : null))
            {
                var context = await GetRequiredService<IDbContextProvider<PlatformDbContext>>().GetDbContextAsync();
                var applied = (await context.Database.GetAppliedMigrationsAsync()).ToList();
                /* Design 63 (user menu) added AddUserAccountLinks on top of this design's migration, design 64 EmailDelivery. */
                applied.Last().ShouldBe("20261030134014_EmailDelivery", "the newest Platform migration");
                applied[^2].ShouldBe("20261029133121_AddUserAccountLinks");
                applied[^3].ShouldBe("20261027092916_AddUserSignInLookups");
                applied[^4].ShouldBe("20261025154657_PlatformMasterData");
                (await context.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
                context.Database.HasPendingModelChanges().ShouldBeFalse();
            }
        });
    }

    [Fact]
    public async Task The_migration_seeds_the_host_and_every_tenant()
    {
        /* PostgreSqlTestDatabases ran the DbMigrator's migration service: each place's seeded admin is in the lookup. */
        foreach (var tenantId in new Guid?[] { null, PostgreSqlTestDatabases.TenantAId })
        {
            var admin = await WithUnitOfWorkAsync(async () =>
            {
                using (_currentTenant.Change(tenantId))
                {
                    return (await _userManager.FindByNameAsync("admin")).ShouldNotBeNull();
                }
            });

            (await _store.FindAsync(UserSignInLookupHash.Of(admin.NormalizedEmail)))
                .ShouldContain(new SignInCandidate(tenantId, admin.Id));
        }
    }
}
