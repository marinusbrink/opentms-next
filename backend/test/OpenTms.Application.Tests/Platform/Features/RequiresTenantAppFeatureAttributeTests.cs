using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using OpenTms.Controllers.Platform;
using OpenTms.Features;
using Shouldly;
using Volo.Abp.Authorization;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Features;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace OpenTms.Platform.Features;

/// <summary>
/// The Administration app's feature gate on its controllers (PO 2026-10-14, docs/designs/tenants-and-editions.md,
/// "App features"): <see cref="RequiresTenantAppFeatureAttribute"/> requires a node and all its ancestors when a
/// tenant is current and lets the host through, and every Administration action carries the right chain (pickers
/// excepted). The filter runs with a real ActionExecutingContext and the production CurrentTenant; only the
/// feature values are a fake. Risk class: High (feature gating of every screen) on a Critical boundary
/// (authorization: an off node must refuse the API, not only hide the screen).
/// </summary>
public class RequiresTenantAppFeatureAttributeTests
{
    /* Not conventionally registered: FeatureCheckerBase is an ITransientDependency, and ABP would otherwise make this
     * fake the IFeatureChecker of every test application that scans this assembly. */
    [DisableConventionalRegistration]
    private sealed class FakeFeatureChecker : FeatureCheckerBase
    {
        public Dictionary<string, string> Values { get; } = new();

        public List<string> Asked { get; } = new();

        public override Task<string?> GetOrNullAsync(string name)
        {
            Asked.Add(name);
            return Task.FromResult(Values.TryGetValue(name, out var value) ? value : "false");
        }
    }

    private readonly FakeFeatureChecker _features = new();
    private readonly ICurrentTenant _currentTenant = new CurrentTenant(AsyncLocalCurrentTenantAccessor.Instance);

    private ActionExecutingContext Context()
    {
        var services = new ServiceCollection()
            .AddSingleton<IFeatureChecker>(_features)
            .AddSingleton(_currentTenant)
            .BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        return new ActionExecutingContext(
            new ActionContext(http, new RouteData(), new ActionDescriptor()),
            new List<IFilterMetadata>(),
            new Dictionary<string, object?>(),
            controller: new object());
    }

    private static (ActionExecutionDelegate Next, Func<bool> Called) Next(ActionExecutingContext context)
    {
        var called = false;
        return (() =>
        {
            called = true;
            return Task.FromResult(new ActionExecutedContext(context, new List<IFilterMetadata>(), new object()));
        }, () => called);
    }

    private static readonly RequiresTenantAppFeatureAttribute UsersGate =
        new(AppFeatures.Administration.Default, AppFeatures.Administration.Users);

    [Fact]
    public async Task On_a_tenant_with_the_node_off_the_action_is_refused_like_abps_feature_check_and_never_runs()
    {
        _features.Values[AppFeatures.Administration.Default] = "true";
        var context = Context();
        var (next, called) = Next(context);

        using (_currentTenant.Change(Guid.NewGuid()))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => UsersGate.OnActionExecutionAsync(context, next));
        }

        called().ShouldBeFalse("the action must not run when its feature is off");
    }

    [Fact]
    public async Task On_a_tenant_with_the_app_off_the_node_alone_does_not_open_the_action()
    {
        _features.Values[AppFeatures.Administration.Users] = "true";
        var context = Context();
        var (next, called) = Next(context);

        using (_currentTenant.Change(Guid.NewGuid()))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => UsersGate.OnActionExecutionAsync(context, next));
        }

        called().ShouldBeFalse();
    }

    [Fact]
    public async Task On_a_tenant_with_the_whole_chain_on_the_action_runs()
    {
        _features.Values[AppFeatures.Administration.Default] = "true";
        _features.Values[AppFeatures.Administration.Users] = "true";
        var context = Context();
        var (next, called) = Next(context);

        using (_currentTenant.Change(Guid.NewGuid()))
        {
            await UsersGate.OnActionExecutionAsync(context, next);
        }

        called().ShouldBeTrue();
        _features.Asked.ShouldBe([AppFeatures.Administration.Default, AppFeatures.Administration.Users], ignoreOrder: true);
    }

    [Fact]
    public async Task On_the_host_the_action_runs_whatever_the_feature_values_and_no_feature_is_read()
    {
        var context = Context();
        var (next, called) = Next(context);

        using (_currentTenant.Change(null))
        {
            await UsersGate.OnActionExecutionAsync(context, next);
        }

        called().ShouldBeTrue();
        _features.Asked.ShouldBeEmpty();
    }

    [Fact]
    public void The_attribute_is_an_mvc_action_filter_usable_on_classes_and_methods()
    {
        typeof(IAsyncActionFilter).IsAssignableFrom(typeof(RequiresTenantAppFeatureAttribute)).ShouldBeTrue();
        typeof(IFilterMetadata).IsAssignableFrom(typeof(RequiresTenantAppFeatureAttribute)).ShouldBeTrue();
        var usage = typeof(RequiresTenantAppFeatureAttribute).GetCustomAttribute<AttributeUsageAttribute>()!;
        usage.ValidOn.HasFlag(AttributeTargets.Class).ShouldBeTrue();
        usage.ValidOn.HasFlag(AttributeTargets.Method).ShouldBeTrue();
    }

    // ── Which chain each Administration action carries ────────────────────────

    private static string[]? Gate(Type controller, string action)
    {
        var method = controller.GetMethod(action, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        method.ShouldNotBeNull($"{controller.Name}.{action}");
        var onMethod = method.GetCustomAttributes<RequiresTenantAppFeatureAttribute>().ToList();
        var onClass = controller.GetCustomAttributes<RequiresTenantAppFeatureAttribute>().ToList();
        (onMethod.Count + onClass.Count).ShouldBeLessThanOrEqualTo(1, $"{controller.Name}.{action} carries more than one gate");
        return onMethod.Concat(onClass).SingleOrDefault()?.Features;
    }

    private static IEnumerable<string> Actions(Type controller) => controller
        .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
        .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any())
        .Select(m => m.Name);

    [Fact]
    public void Every_users_action_needs_the_Users_chain()
    {
        foreach (var action in Actions(typeof(UserAdministrationController)))
        {
            Gate(typeof(UserAdministrationController), action).ShouldBe([AppFeatures.Administration.Default, AppFeatures.Administration.Users], action);
        }
    }

    [Fact]
    public void Every_roles_action_but_the_list_picker_needs_the_Roles_chain()
    {
        /* GET "" is the role picker of the user editor (PO 2026-10-14): a tenant without the Roles screen still assigns roles. */
        Gate(typeof(UserRoleAdministrationController), nameof(UserRoleAdministrationController.GetListAsync)).ShouldBeNull();
        foreach (var action in Actions(typeof(UserRoleAdministrationController)).Where(a => a != nameof(UserRoleAdministrationController.GetListAsync)))
        {
            Gate(typeof(UserRoleAdministrationController), action).ShouldBe([AppFeatures.Administration.Default, AppFeatures.Administration.Roles], action);
        }
    }

    [Fact]
    public void Every_organization_unit_action_but_the_all_picker_needs_the_OrganizationUnits_chain()
    {
        Gate(typeof(OrganizationUnitAdministrationController), nameof(OrganizationUnitAdministrationController.GetAllAsync)).ShouldBeNull();
        Gate(typeof(OrganizationUnitAdministrationController), nameof(OrganizationUnitAdministrationController.GetListAsync))
            .ShouldBe([AppFeatures.Administration.Default, AppFeatures.Administration.OrganizationUnits], "the list (GET \"\") is the screen, not the picker");
        foreach (var action in Actions(typeof(OrganizationUnitAdministrationController)).Where(a => a != nameof(OrganizationUnitAdministrationController.GetAllAsync)))
        {
            Gate(typeof(OrganizationUnitAdministrationController), action).ShouldBe([AppFeatures.Administration.Default, AppFeatures.Administration.OrganizationUnits], action);
        }
    }

    [Theory]
    [InlineData(nameof(LogbookAdministrationController.GetTechnicalListAsync), AppFeatures.Administration.AuditLogs.TechnicalLogbook)]
    [InlineData(nameof(LogbookAdministrationController.GetTechnicalAsync), AppFeatures.Administration.AuditLogs.TechnicalLogbook)]
    [InlineData(nameof(LogbookAdministrationController.GetFunctionalListAsync), AppFeatures.Administration.AuditLogs.FunctionalLogbook)]
    [InlineData(nameof(LogbookAdministrationController.GetFunctionalAsync), AppFeatures.Administration.AuditLogs.FunctionalLogbook)]
    public void Each_logbook_action_needs_its_own_logbook_chain(string action, string node)
    {
        Gate(typeof(LogbookAdministrationController), action)
            .ShouldBe([AppFeatures.Administration.Default, AppFeatures.Administration.AuditLogs.Default, node]);
    }

    [Fact]
    public void Every_logbook_action_is_gated()
    {
        foreach (var action in Actions(typeof(LogbookAdministrationController)))
        {
            Gate(typeof(LogbookAdministrationController), action).ShouldNotBeNull(action);
        }
    }

    [Fact]
    public void Every_maintenance_action_needs_the_Maintenance_chain()
    {
        foreach (var action in Actions(typeof(MaintenanceAdministrationController)))
        {
            Gate(typeof(MaintenanceAdministrationController), action)
                .ShouldBe([AppFeatures.Administration.Default, AppFeatures.Administration.Maintenance], action);
        }
    }

    [Theory]
    [InlineData(typeof(TenantAdministrationController))]
    [InlineData(typeof(EditionAdministrationController))]
    public void The_host_only_apps_have_no_app_feature(Type controller)
    {
        controller.GetCustomAttributes<RequiresTenantAppFeatureAttribute>().ShouldBeEmpty();
        foreach (var action in Actions(controller))
        {
            controller.GetMethod(action, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)!
                .GetCustomAttributes<RequiresTenantAppFeatureAttribute>().ShouldBeEmpty(action);
        }
    }
}
