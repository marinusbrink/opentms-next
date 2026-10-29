using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using OpenTms.EntityFrameworkCore.Platform.Accounts;
using OpenTms.Platform;
using OpenTms.Platform.Accounts;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Identity;
using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Security.Claims;
using Volo.Abp.UI.Navigation.Urls;
using Volo.Abp.Uow;
using Xunit;

namespace OpenTms.PostgreSql.Platform;

/// <summary>The PostgreSQL test application plus the two ports of the account switch: a recording ticket protector and the auth server URL.</summary>
[DependsOn(typeof(OpenTmsPostgreSqlTestModule))]
public class AccountSwitchPostgreSqlTestModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var protector = new RecordingAccountSwitchTicketProtector();
        context.Services.AddSingleton(protector);
        context.Services.Replace(ServiceDescriptor.Singleton<IAccountSwitchTicketProtector>(protector));
        Configure<AppUrlOptions>(options => options.Applications["MVC"].RootUrl = "https://auth.opentms.test");
    }
}

/// <summary>
/// The user menu's account switch against real PostgreSQL with a tenant in its own database (docs/designs/63-user-menu.md):
/// the switch reads the target in that tenant's database, a wrong password is counted there although the request itself
/// fails and rolls back (its own unit of work), the remembered links land in the host database only, a user of another
/// address in the tenant database is never reached, the links of a deleted tenant-database user are removed from the host
/// database, and the user's own language is kept. Every call runs in a transactional unit of work, as a request does.
/// Risk class: Critical — authentication, cross-tenant (database-per-tenant) access.
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class AccountSwitchPostgreSqlTests : OpenTmsTestBase<AccountSwitchPostgreSqlTestModule>
{
    private const string HostPassword = "1q2w3E*";
    private const string TenantPassword = "4r5t6Y&";
    private static readonly AbpUnitOfWorkOptions Transactional = new() { IsTransactional = true };

    private readonly ICurrentTenant _currentTenant;
    private readonly ICurrentPrincipalAccessor _principal;
    private readonly RecordingAccountSwitchTicketProtector _tickets;

    private static Guid TenantA => PostgreSqlTestDatabases.TenantAId;

    public AccountSwitchPostgreSqlTests()
    {
        _currentTenant = GetRequiredService<ICurrentTenant>();
        _principal = GetRequiredService<ICurrentPrincipalAccessor>();
        _tickets = GetRequiredService<RecordingAccountSwitchTicketProtector>();
    }

    private static string Email() => $"switch-{Guid.NewGuid().ToString("N")[..12]}@example.test";

    private Task<Guid> CreateUserAsync(Guid? tenantId, string email, string password) =>
        WithUnitOfWorkAsync(Transactional, async () =>
        {
            using (_currentTenant.Change(tenantId))
            {
                var user = new IdentityUser(Guid.NewGuid(), "u-" + Guid.NewGuid().ToString("N")[..10], email, tenantId);
                (await GetRequiredService<IdentityUserManager>().CreateAsync(user, password)).Succeeded.ShouldBeTrue();
                return user.Id;
            }
        });

    private Task<IdentityUser> ReadUserAsync(Guid? tenantId, Guid userId) =>
        WithUnitOfWorkAsync(Transactional, async () =>
        {
            using (_currentTenant.Change(tenantId))
            {
                return await GetRequiredService<IdentityUserManager>().GetByIdAsync(userId);
            }
        });

    /// <summary>One request: the given user signed in to their tenant, a transactional unit of work around the call.</summary>
    private async Task<T> AsAsync<T>(Guid userId, Guid? tenantId, Func<Task<T>> action)
    {
        using (_principal.Change(AccountPrincipals.For(userId, tenantId)))
        using (_currentTenant.Change(tenantId))
        {
            return await WithUnitOfWorkAsync(Transactional, action);
        }
    }

    private Task AsAsync(Guid userId, Guid? tenantId, Func<Task> action) =>
        AsAsync<bool>(userId, tenantId, async () =>
        {
            await action();
            return true;
        });

    private IMyAccountsAppService Service => GetRequiredService<IMyAccountsAppService>();

    private static async Task<long> CountLinksAsync(string database, Guid userId)
    {
        await using var connection = new NpgsqlConnection(PostgreSqlTestDatabases.ConnectionStringFor(database));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM platform.\"UserAccountLinks\" WHERE \"UserId\" = @id OR \"LinkedUserId\" = @id", connection);
        command.Parameters.AddWithValue("id", userId);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task The_host_user_sees_and_enters_the_account_in_the_tenant_database()
    {
        var email = Email();
        var hostUser = await CreateUserAsync(null, email, HostPassword);
        var tenantUser = await CreateUserAsync(TenantA, email, TenantPassword);

        var list = await AsAsync(hostUser, null, () => Service.GetListAsync());
        list.ShouldHaveSingleItem().TenantId.ShouldBe(TenantA);
        list[0].NeedsPassword.ShouldBeTrue();

        await AsAsync(hostUser, null, () => Service.SwitchAsync(new SwitchAccountDto { TenantId = TenantA, Password = TenantPassword }));

        var ticket = _tickets.Protected.ShouldHaveSingleItem();
        var stampHash = AccountSwitchClaims.StampHash((await ReadUserAsync(TenantA, tenantUser)).SecurityStamp);
        ticket.ShouldBe(new AccountSwitchTicket(hostUser, null, tenantUser, TenantA, $"{TenantA:N}|{tenantUser:N}|{stampHash}", ticket.Nonce));
    }

    [Fact]
    public async Task A_wrong_password_is_counted_in_the_tenant_database_although_the_request_rolls_back()
    {
        var email = Email();
        var hostUser = await CreateUserAsync(null, email, HostPassword);
        var tenantUser = await CreateUserAsync(TenantA, email, TenantPassword);

        var ex = await Should.ThrowAsync<BusinessException>(() =>
            AsAsync(hostUser, null, () => Service.SwitchAsync(new SwitchAccountDto { TenantId = TenantA, Password = HostPassword, RememberPassword = true })));

        ex.Code.ShouldBe(PlatformErrorCodes.AccountPasswordIncorrect);
        (await ReadUserAsync(TenantA, tenantUser)).AccessFailedCount.ShouldBe(1);
        (await ReadUserAsync(null, hostUser)).AccessFailedCount.ShouldBe(0);
        (await CountLinksAsync(PostgreSqlTestDatabases.HostDatabase, hostUser)).ShouldBe(0);
        _tickets.Protected.ShouldBeEmpty();
    }

    [Fact]
    public async Task Remembered_links_land_in_the_host_database_only_and_open_the_switch_without_a_password()
    {
        // constitution rule 1: the links are host data; the tenant's own database never holds them.
        var email = Email();
        var hostUser = await CreateUserAsync(null, email, HostPassword);
        var tenantUser = await CreateUserAsync(TenantA, email, TenantPassword);

        await AsAsync(hostUser, null, () => Service.SwitchAsync(new SwitchAccountDto { TenantId = TenantA, Password = TenantPassword, RememberPassword = true }));

        (await CountLinksAsync(PostgreSqlTestDatabases.HostDatabase, hostUser)).ShouldBe(2, "one row per direction");
        (await CountLinksAsync(PostgreSqlTestDatabases.TenantADatabase, hostUser)).ShouldBe(0);

        /* Both directions, each read from its own side. */
        await AsAsync(hostUser, null, () => Service.SwitchAsync(new SwitchAccountDto { TenantId = TenantA }));
        await AsAsync(tenantUser, TenantA, () => Service.SwitchAsync(new SwitchAccountDto { TenantId = null }));
        (await AsAsync(tenantUser, TenantA, () => Service.GetListAsync())).ShouldHaveSingleItem().IsRemembered.ShouldBeTrue();

        await AsAsync(tenantUser, TenantA, () => Service.ForgetAsync(null));
        (await CountLinksAsync(PostgreSqlTestDatabases.HostDatabase, hostUser)).ShouldBe(0);
    }

    [Fact]
    public async Task A_link_ends_when_either_account_changes_its_password()
    {
        var email = Email();
        var hostUser = await CreateUserAsync(null, email, HostPassword);
        var tenantUser = await CreateUserAsync(TenantA, email, TenantPassword);
        await AsAsync(hostUser, null, () => Service.SwitchAsync(new SwitchAccountDto { TenantId = TenantA, Password = TenantPassword, RememberPassword = true }));

        /* The switch-FROM account (host database) changes its password: the link to tenant A no longer holds (B2). */
        await WithUnitOfWorkAsync(Transactional, async () =>
        {
            var manager = GetRequiredService<IdentityUserManager>();
            var user = await manager.GetByIdAsync(hostUser);
            (await manager.ChangePasswordAsync(user, HostPassword, "Brand-new1!")).Succeeded.ShouldBeTrue();
        });

        var ex = await Should.ThrowAsync<BusinessException>(() => AsAsync(hostUser, null, () => Service.SwitchAsync(new SwitchAccountDto { TenantId = TenantA })));
        ex.Code.ShouldBe(PlatformErrorCodes.AccountPasswordRequired);
        var back = await Should.ThrowAsync<BusinessException>(() => AsAsync(tenantUser, TenantA, () => Service.SwitchAsync(new SwitchAccountDto { TenantId = null })));
        back.Code.ShouldBe(PlatformErrorCodes.AccountPasswordRequired);
    }

    [Fact]
    public async Task A_user_of_another_address_in_the_tenant_database_is_never_reached()
    {
        // constitution rule 1: tenant A holds Piet; Jan in the host cannot list or enter it, even with Piet's password.
        var hostUser = await CreateUserAsync(null, Email(), HostPassword);
        var piet = await CreateUserAsync(TenantA, Email(), TenantPassword);

        (await AsAsync(hostUser, null, () => Service.GetListAsync())).ShouldBeEmpty();
        var ex = await Should.ThrowAsync<BusinessException>(() =>
            AsAsync(hostUser, null, () => Service.SwitchAsync(new SwitchAccountDto { TenantId = TenantA, Password = TenantPassword })));
        ex.Code.ShouldBe(PlatformErrorCodes.AccountNotAvailable);
        (await ReadUserAsync(TenantA, piet)).AccessFailedCount.ShouldBe(0);
        _tickets.Protected.ShouldBeEmpty();
    }

    [Fact]
    public async Task Deleting_the_tenant_database_user_removes_its_links_from_the_host_database()
    {
        var email = Email();
        var hostUser = await CreateUserAsync(null, email, HostPassword);
        var tenantUser = await CreateUserAsync(TenantA, email, TenantPassword);
        await AsAsync(hostUser, null, () => Service.SwitchAsync(new SwitchAccountDto { TenantId = TenantA, Password = TenantPassword, RememberPassword = true }));

        await WithUnitOfWorkAsync(Transactional, async () =>
        {
            using (_currentTenant.Change(TenantA))
            {
                var manager = GetRequiredService<IdentityUserManager>();
                (await manager.DeleteAsync(await manager.GetByIdAsync(tenantUser))).Succeeded.ShouldBeTrue();
            }
        });

        (await CountLinksAsync(PostgreSqlTestDatabases.HostDatabase, hostUser)).ShouldBe(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_links_table_exists_in_every_database_and_holds_one_row_per_user_and_linked_place_also_for_the_host(bool inTenantDatabase)
    {
        /* The migration only adds a table (expand, constitution rule 2). Its unique index treats the host's null tenant as
         * a value (NULLS NOT DISTINCT), so a host user cannot get two links to the same place either. */
        var database = inTenantDatabase ? PostgreSqlTestDatabases.TenantADatabase : PostgreSqlTestDatabases.HostDatabase;
        await using var connection = new NpgsqlConnection(PostgreSqlTestDatabases.ConnectionStringFor(database));
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var userId = Guid.NewGuid();
        const string insert =
            "INSERT INTO platform.\"UserAccountLinks\" (\"Id\", \"UserTenantId\", \"UserId\", \"UserSecurityStamp\", \"LinkedTenantId\", \"LinkedUserId\", \"LinkedSecurityStamp\", \"CreationTime\") " +
            "VALUES (@id, NULL, @user, 'own-stamp', NULL, @linked, 'stamp', now())";
        await using (var first = new NpgsqlCommand(insert, connection, transaction))
        {
            first.Parameters.AddWithValue("id", Guid.NewGuid());
            first.Parameters.AddWithValue("user", userId);
            first.Parameters.AddWithValue("linked", Guid.NewGuid());
            (await first.ExecuteNonQueryAsync()).ShouldBe(1);
        }

        await using (var second = new NpgsqlCommand(insert, connection, transaction))
        {
            second.Parameters.AddWithValue("id", Guid.NewGuid());
            second.Parameters.AddWithValue("user", userId);
            second.Parameters.AddWithValue("linked", Guid.NewGuid());
            (await Should.ThrowAsync<PostgresException>(() => second.ExecuteNonQueryAsync())).SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
        }

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task The_profile_keeps_the_users_language_choice_through_a_request_that_clears_it()
    {
        /* My settings in the tenant's own database, the dialog's order: open (GET), then save. */
        var tenantUser = await CreateUserAsync(TenantA, Email(), TenantPassword);
        var profile = GetRequiredService<IMyProfileAppService>();
        await AsAsync(tenantUser, TenantA, () => profile.UpdateAsync(new UpdateMyProfileDto { Language = "nl" }));
        (await AsAsync(tenantUser, TenantA, () => profile.GetAsync())).Language.ShouldBe("nl");

        var cleared = await AsAsync(tenantUser, TenantA, () => profile.UpdateAsync(new UpdateMyProfileDto { Language = null }));

        (await AsAsync(tenantUser, TenantA, () => profile.GetAsync())).Language.ShouldBeNull("the choice was removed");
        cleared.Language.ShouldBeNull("the update answers what it stored");
    }
}
