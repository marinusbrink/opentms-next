using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTms.Controllers.Platform;
using OpenTms.EntityFrameworkCore.Platform.Tenants;
using OpenTms.Platform.Administration;
using OpenTms.Platform.Grid;
using OpenTms.Platform.Impersonation;
using OpenTms.Platform.Permissions;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Identity;
using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.PermissionManagement;
using Volo.Abp.Security.Claims;
using Volo.Abp.TenantManagement;
using Volo.Abp.UI.Navigation.Urls;
using Xunit;

namespace OpenTms.EntityFrameworkCore.Platform.Impersonation;

/// <summary>Records every ticket instead of encrypting it; the production adapter is tested in OpenTms.HttpApi.Host.Tests.</summary>
public sealed class RecordingTicketProtector : IImpersonationTicketProtector
{
    public List<ImpersonationTicket> Protected { get; } = new();

    public string Protect(ImpersonationTicket ticket)
    {
        Protected.Add(ticket);
        return $"ticket/{Protected.Count}+x=y";
    }

    public ImpersonationTicket? Unprotect(string protectedTicket) => null;
}

/// <summary>Keeps every log line (rendered message and its structured values) of the process.</summary>
public sealed class RecordingLoggerProvider : ILoggerProvider
{
    public ConcurrentQueue<(string Category, LogLevel Level, string Message, string Values)> Entries { get; } = new();

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    public void Dispose()
    {
    }

    private sealed class Logger(RecordingLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var values = state is IEnumerable<KeyValuePair<string, object?>> pairs
                ? string.Join(";", pairs.Select(p => $"{p.Key}={p.Value}"))
                : string.Empty;
            if (exception != null)
            {
                values += ";exception=" + exception;
            }
            owner.Entries.Enqueue((category, logLevel, formatter(state, exception), values));
        }
    }
}

/// <summary>The Tenants test application plus the two ports "Log in as" needs: the ticket protector and the auth server URL.</summary>
[DependsOn(typeof(TenantAdministrationTestModule))]
public class ImpersonationTestModule : AbpModule
{
    public const string AuthServerUrl = "https://auth.opentms.test";

    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var protector = new RecordingTicketProtector();
        context.Services.AddSingleton(protector);
        context.Services.AddSingleton<IImpersonationTicketProtector>(protector);

        var logs = new RecordingLoggerProvider();
        context.Services.AddSingleton(logs);
        context.Services.AddLogging(builder => builder.AddProvider(logs).SetMinimumLevel(LogLevel.Trace));

        Configure<AppUrlOptions>(options => options.Applications["MVC"].RootUrl = AuthServerUrl);
    }
}

/// <summary>
/// "Log in as a tenant user" (docs/designs/tenants-and-editions.md, D14; PO 2026-10-14), the application service through
/// the real container on SQLite: host only, the target tenant's own users only (negative isolation), an inactive or
/// missing user refused, the tickets it issues, the way back only while impersonating, and logs without names or
/// email addresses. Risk class: Critical — authentication (a wrong ticket logs a host user in as anybody).
/// Authorization is relaxed in this test application, so the permission attributes are asserted by reflection.
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class ImpersonationAppServiceTests : OpenTmsTestBase<ImpersonationTestModule>
{
    private static readonly Guid HostAdminId = Guid.NewGuid();

    private readonly IImpersonationAppService _service;
    private readonly ICurrentTenant _currentTenant;
    private readonly ICurrentPrincipalAccessor _principal;
    private readonly RecordingTicketProtector _tickets;
    private readonly RecordingLoggerProvider _logs;

    public ImpersonationAppServiceTests()
    {
        _service = GetRequiredService<IImpersonationAppService>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
        _principal = GetRequiredService<ICurrentPrincipalAccessor>();
        _tickets = GetRequiredService<RecordingTicketProtector>();
        _logs = GetRequiredService<RecordingLoggerProvider>();
    }

    private IDisposable AsUser(Guid userId, Guid? tenantId = null, string userName = "host.admin", Guid? impersonatorId = null, Guid? impersonatorTenantId = null)
    {
        var claims = new List<Claim>
        {
            new(AbpClaimTypes.UserId, userId.ToString()),
            new(AbpClaimTypes.UserName, userName),
            new(AbpClaimTypes.Email, userName + "@example.test")
        };
        if (tenantId != null)
        {
            claims.Add(new Claim(AbpClaimTypes.TenantId, tenantId.Value.ToString()));
        }

        if (impersonatorId != null)
        {
            claims.Add(new Claim(AbpClaimTypes.ImpersonatorUserId, impersonatorId.Value.ToString()));
            claims.Add(new Claim(AbpClaimTypes.ImpersonatorUserName, "host.admin"));
        }

        if (impersonatorTenantId != null)
        {
            claims.Add(new Claim(AbpClaimTypes.ImpersonatorTenantId, impersonatorTenantId.Value.ToString()));
        }

        return _principal.Change(new ClaimsPrincipal(new ClaimsIdentity(claims, "test")));
    }

    /// <summary>A shared-tier tenant (no connection string): its users live in the SQLite test database, filtered by tenant.</summary>
    private Task<Guid> CreateTenantAsync(string name) =>
        WithUnitOfWorkAsync(async () =>
        {
            var tenant = await GetRequiredService<ITenantManager>().CreateAsync(name);
            await GetRequiredService<ITenantRepository>().InsertAsync(tenant, autoSave: true);
            return tenant.Id;
        });

    private Task<Guid> CreateUserAsync(Guid? tenantId, string userName, bool active = true, string? name = null, string? surname = null) =>
        WithUnitOfWorkAsync(async () =>
        {
            using (_currentTenant.Change(tenantId))
            {
                var user = new IdentityUser(Guid.NewGuid(), userName, userName + "@mail.example.test", tenantId);
                user.Name = name;
                user.Surname = surname;
                user.SetIsActive(active);
                (await GetRequiredService<IdentityUserManager>().CreateAsync(user)).Succeeded.ShouldBeTrue();
                return user.Id;
            }
        });

    private static GridRequest Request(string? search = null) => new() { StartRow = 0, EndRow = 50, WildcardSearch = search };

    // ── Host only ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Every_host_action_is_refused_in_a_tenant_context()
    {
        var acme = await CreateTenantAsync("acme");
        var user = await CreateUserAsync(acme, "acme.planner");

        using (AsUser(Guid.NewGuid(), acme))
        using (_currentTenant.Change(acme))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.GetTenantUsersAsync(acme, Request()));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.StartAsync(acme, user));
        }

        _tickets.Protected.ShouldBeEmpty();
    }

    [Fact]
    public async Task Start_without_a_signed_in_user_is_refused()
    {
        var acme = await CreateTenantAsync("acme");
        var user = await CreateUserAsync(acme, "acme.planner");

        using (_principal.Change(new ClaimsPrincipal(new ClaimsIdentity())))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.StartAsync(acme, user));
        }

        _tickets.Protected.ShouldBeEmpty();
    }

    // ── The tenant's own users only (Critical: tenant isolation) ──────────────

    [Fact]
    public async Task The_user_list_holds_the_target_tenants_users_only()
    {
        var acme = await CreateTenantAsync("acme");
        var beta = await CreateTenantAsync("beta");
        await CreateUserAsync(acme, "acme.planner", name: "Piet", surname: "Planner");
        await CreateUserAsync(acme, "acme.driver", active: false);
        await CreateUserAsync(beta, "beta.planner");
        await CreateUserAsync(null, "host.operator");

        using (AsUser(HostAdminId))
        {
            var page = await _service.GetTenantUsersAsync(acme, Request());

            page.Rows.Select(r => r.UserName).ShouldBe(["acme.driver", "acme.planner"], ignoreOrder: true);
            page.TotalCount.ShouldBe(2);
            page.FilteredCount.ShouldBe(2);
            page.Rows.Single(r => r.UserName == "acme.planner").Name.ShouldBe("Piet");
            page.Rows.Single(r => r.UserName == "acme.driver").IsActive.ShouldBeFalse();

            var filtered = await _service.GetTenantUsersAsync(acme, Request("planner"));
            filtered.Rows.ShouldHaveSingleItem().UserName.ShouldBe("acme.planner");
            filtered.TotalCount.ShouldBe(2);
            filtered.FilteredCount.ShouldBe(1);

            (await _service.GetTenantUsersAsync(beta, Request())).Rows.ShouldHaveSingleItem().UserName.ShouldBe("beta.planner");
        }

        /* The call leaves the caller in the host context. */
        _currentTenant.Id.ShouldBeNull();
    }

    [Fact]
    public void The_user_row_carries_no_email_address()
    {
        typeof(TenantUserRowDto).GetProperties().Select(p => p.Name)
            .ShouldBe(["Id", "UserName", "Name", "Surname", "IsActive"], ignoreOrder: true);
    }

    [Fact]
    public async Task An_unknown_tenant_is_not_found()
    {
        using (AsUser(HostAdminId))
        {
            await Should.ThrowAsync<EntityNotFoundException>(() => _service.GetTenantUsersAsync(Guid.NewGuid(), Request()));
            await Should.ThrowAsync<EntityNotFoundException>(() => _service.StartAsync(Guid.NewGuid(), Guid.NewGuid()));
        }

        _tickets.Protected.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_user_of_tenant_B_with_tenant_As_id_is_refused_as_not_available()
    {
        var acme = await CreateTenantAsync("acme");
        var beta = await CreateTenantAsync("beta");
        var betaUser = await CreateUserAsync(beta, "beta.planner");
        var hostUser = await CreateUserAsync(null, "host.operator");

        using (AsUser(HostAdminId))
        {
            var ex = await Should.ThrowAsync<UserFriendlyException>(() => _service.StartAsync(acme, betaUser));
            ex.Message.ShouldNotContain("beta");
            await Should.ThrowAsync<UserFriendlyException>(() => _service.StartAsync(acme, hostUser));
        }

        _tickets.Protected.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_inactive_or_missing_user_is_refused()
    {
        var acme = await CreateTenantAsync("acme");
        var inactive = await CreateUserAsync(acme, "acme.gone", active: false);

        using (AsUser(HostAdminId))
        {
            await Should.ThrowAsync<UserFriendlyException>(() => _service.StartAsync(acme, inactive));
            await Should.ThrowAsync<UserFriendlyException>(() => _service.StartAsync(acme, Guid.NewGuid()));
        }

        _tickets.Protected.ShouldBeEmpty();
    }

    // ── The tickets ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Start_issues_a_ticket_for_this_session_and_the_chosen_user_and_points_at_the_auth_servers_page()
    {
        var acme = await CreateTenantAsync("acme");
        var planner = await CreateUserAsync(acme, "acme.planner");

        ImpersonationRedirectDto redirect;
        using (AsUser(HostAdminId))
        {
            redirect = await _service.StartAsync(acme, planner);
        }

        _tickets.Protected.ShouldHaveSingleItem().ShouldBe(new ImpersonationTicket(
            IsReturn: false, ExpectedUserId: HostAdminId, ExpectedTenantId: null, TargetUserId: planner, TargetTenantId: acme));
        redirect.Url.ShouldBe($"{ImpersonationTestModule.AuthServerUrl}/Account/Impersonate?ticket={Uri.EscapeDataString("ticket/1+x=y")}");
        _currentTenant.Id.ShouldBeNull();
    }

    [Fact]
    public async Task Return_is_refused_without_the_impersonator_claim()
    {
        var acme = await CreateTenantAsync("acme");
        var planner = await CreateUserAsync(acme, "acme.planner");

        using (AsUser(planner, acme))
        using (_currentTenant.Change(acme))
        {
            await Should.ThrowAsync<UserFriendlyException>(() => _service.ReturnAsync());
        }

        _tickets.Protected.ShouldBeEmpty();
    }

    [Fact]
    public async Task Return_issues_a_ticket_from_the_impersonated_session_back_to_the_impersonator()
    {
        var acme = await CreateTenantAsync("acme");
        var planner = await CreateUserAsync(acme, "acme.planner");

        ImpersonationRedirectDto redirect;
        using (AsUser(planner, acme, "acme.planner", impersonatorId: HostAdminId))
        using (_currentTenant.Change(acme))
        {
            redirect = await _service.ReturnAsync();
        }

        _tickets.Protected.ShouldHaveSingleItem().ShouldBe(new ImpersonationTicket(
            IsReturn: true, ExpectedUserId: planner, ExpectedTenantId: acme, TargetUserId: HostAdminId, TargetTenantId: null));
        redirect.Url.ShouldStartWith($"{ImpersonationTestModule.AuthServerUrl}/Account/Impersonate?ticket=");
    }

    [Fact]
    public async Task Return_targets_an_impersonator_in_a_tenant_with_its_tenant()
    {
        var acme = await CreateTenantAsync("acme");
        var planner = await CreateUserAsync(acme, "acme.planner");
        var impersonatorTenant = Guid.NewGuid();
        var impersonator = Guid.NewGuid();

        using (AsUser(planner, acme, impersonatorId: impersonator, impersonatorTenantId: impersonatorTenant))
        using (_currentTenant.Change(acme))
        {
            await _service.ReturnAsync();
        }

        var ticket = _tickets.Protected.ShouldHaveSingleItem();
        ticket.TargetUserId.ShouldBe(impersonator);
        ticket.TargetTenantId.ShouldBe(impersonatorTenant);
    }

    // ── Logs: ids only (constitution rule 5) ──────────────────────────────────

    [Fact]
    public async Task Start_and_return_log_ids_and_no_names_or_email_addresses()
    {
        var acme = await CreateTenantAsync("acme-logistics");
        var planner = await CreateUserAsync(acme, "piet.planner", name: "Pieter", surname: "Plannerman");
        while (_logs.Entries.TryDequeue(out _))
        {
        }

        using (AsUser(HostAdminId, userName: "hostadmin.name"))
        {
            await _service.StartAsync(acme, planner);
        }

        using (AsUser(planner, acme, "piet.planner", impersonatorId: HostAdminId))
        using (_currentTenant.Change(acme))
        {
            await _service.ReturnAsync();
        }

        var lines = _logs.Entries.Where(e => e.Category.Contains("Impersonation")).ToList();
        lines.Count.ShouldBeGreaterThanOrEqualTo(2);
        lines.ShouldContain(l => l.Message.Contains(planner.ToString()) && l.Message.Contains(acme.ToString()) && l.Message.Contains(HostAdminId.ToString()));

        foreach (var line in lines)
        {
            foreach (var personal in new[] { "piet.planner", "Pieter", "Plannerman", "hostadmin.name", "@", "acme-logistics" })
            {
                line.Message.ShouldNotContain(personal, Case.Insensitive, $"log line: {line.Message}");
                line.Values.ShouldNotContain(personal, Case.Insensitive, $"log values: {line.Values}");
            }
        }
    }

    // ── Authorization attributes (relaxed in this test application) ───────────

    [Theory]
    [InlineData(nameof(ImpersonationAppService.GetTenantUsersAsync))]
    [InlineData(nameof(ImpersonationAppService.StartAsync))]
    public void The_host_actions_need_the_impersonation_permission(string method)
    {
        typeof(ImpersonationAppService).GetMethod(method)!.GetCustomAttributes<AuthorizeAttribute>()
            .ShouldHaveSingleItem().Policy.ShouldBe(PlatformPermissions.Tenants.Impersonation);
    }

    [Fact]
    public void Return_needs_a_signed_in_user_and_no_permission()
    {
        typeof(ImpersonationAppService).GetMethod(nameof(ImpersonationAppService.ReturnAsync))!.GetCustomAttributes<AuthorizeAttribute>()
            .ShouldHaveSingleItem().Policy.ShouldBeNull();
        typeof(ImpersonationAppService).GetCustomAttribute<RemoteServiceAttribute>()!.IsEnabled.ShouldBeFalse("only the explicit controllers expose it");
    }

    [Fact]
    public void The_controller_actions_carry_the_same_gates()
    {
        PolicyOf(typeof(TenantAdministrationController), nameof(TenantAdministrationController.GetUsersAsync))
            .ShouldBe(PlatformPermissions.Tenants.Impersonation);
        PolicyOf(typeof(TenantAdministrationController), nameof(TenantAdministrationController.ImpersonateAsync))
            .ShouldBe(PlatformPermissions.Tenants.Impersonation);
        typeof(TenantAdministrationController).GetCustomAttribute<AuthorizeAttribute>()!.Policy.ShouldBe(PlatformPermissions.Tenants.Default);
        typeof(TenantAdministrationController).GetMethod(nameof(TenantAdministrationController.ImpersonateAsync))!
            .GetCustomAttribute<HttpPostAttribute>()!.Template.ShouldBe("{id:guid}/impersonate/{userId:guid}");

        var returnController = typeof(ImpersonationController);
        returnController.GetCustomAttribute<AuthorizeAttribute>().ShouldNotBeNull();
        returnController.GetMethod(nameof(ImpersonationController.ReturnAsync))!.GetCustomAttribute<HttpPostAttribute>()!.Template.ShouldBe("return");
        returnController.GetCustomAttribute<RouteAttribute>()!.Template.ShouldBe("api/platform/impersonation");
    }

    private static string? PolicyOf(Type controller, string action) =>
        controller.GetMethod(action)!.GetCustomAttributes<AuthorizeAttribute>().ShouldHaveSingleItem().Policy;

    // ── Permission definition and seeding ─────────────────────────────────────

    [Fact]
    public async Task The_permission_is_a_host_only_child_of_Tenants()
    {
        var permission = await GetRequiredService<IPermissionDefinitionManager>().GetAsync(PlatformPermissions.Tenants.Impersonation);
        permission.Name.ShouldBe("Platform.Tenants.Impersonation");
        permission.MultiTenancySide.ShouldBe(MultiTenancySides.Host);
        permission.Parent!.Name.ShouldBe(PlatformPermissions.Tenants.Default);
    }

    [Fact]
    public async Task The_seed_grants_it_to_the_host_admin_role_and_never_inside_a_tenant()
    {
        var acme = await CreateTenantAsync("acme");
        var contributor = GetRequiredService<AdministrationPermissionDataSeedContributor>();
        var grants = GetRequiredService<IPermissionGrantRepository>();

        await WithUnitOfWorkAsync(() => contributor.SeedAsync(new DataSeedContext(null)));
        await WithUnitOfWorkAsync(async () =>
        {
            using (_currentTenant.Change(acme))
            {
                await contributor.SeedAsync(new DataSeedContext(acme));
            }
        });

        await WithUnitOfWorkAsync(async () =>
        {
            using (_currentTenant.Change(null))
            {
                (await grants.FindAsync(PlatformPermissions.Tenants.Impersonation, RolePermissionValueProvider.ProviderName, "admin"))
                    .ShouldNotBeNull();
            }

            using (_currentTenant.Change(acme))
            {
                (await grants.FindAsync(PlatformPermissions.Tenants.Impersonation, RolePermissionValueProvider.ProviderName, "admin"))
                    .ShouldBeNull();
            }
        });
    }
}
