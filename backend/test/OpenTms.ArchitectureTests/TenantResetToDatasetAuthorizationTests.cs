using System;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenTms.Controllers.Platform;
using OpenTms.Platform.Permissions;
using OpenTms.Platform.Tenants;
using Shouldly;
using Xunit;

namespace OpenTms.ArchitectureTests;

/// <summary>
/// "Reset to dataset" (docs/designs/65-tenant-test-datasets.md, Q2) is reachable only through
/// <c>POST api/platform/tenants/{id}/reset-to-dataset</c> and only with <c>Platform.Tenants.ResetToDataset</c>, on the
/// controller action and on the (remote-disabled) application service alike. The generic coverage test only demands
/// some authorization; this one pins the exact permission. Risk class: Critical — authorization of a destructive host action.
/// </summary>
public class TenantResetToDatasetAuthorizationTests
{
    private const string Permission = "Platform.Tenants.ResetToDataset";

    [Fact]
    public void The_permission_name_is_stable()
    {
        PlatformPermissions.Tenants.ResetToDataset.ShouldBe(Permission);
    }

    [Fact]
    public void The_controller_action_is_a_POST_on_reset_to_dataset_requiring_the_permission()
    {
        var action = typeof(TenantAdministrationController).GetMethod("ResetToDatasetAsync").ShouldNotBeNull();

        action.GetCustomAttributes<AuthorizeAttribute>().Select(a => a.Policy).ShouldBe(new[] { Permission });
        action.GetCustomAttribute<HttpPostAttribute>().ShouldNotBeNull().Template.ShouldBe("{id:guid}/reset-to-dataset");
        action.GetCustomAttribute<AllowAnonymousAttribute>().ShouldBeNull();
        action.GetParameters().Single(p => p.ParameterType == typeof(TenantResetToDatasetDto))
            .GetCustomAttribute<FromBodyAttribute>().ShouldNotBeNull();
    }

    [Fact]
    public void The_application_service_method_requires_the_permission()
    {
        var method = typeof(TenantAdministrationAppService).GetMethod(nameof(ITenantAdministrationAppService.ResetToDatasetAsync)).ShouldNotBeNull();

        method.GetCustomAttributes<AuthorizeAttribute>().Select(a => a.Policy).ShouldBe(new[] { Permission });
        method.GetCustomAttribute<AllowAnonymousAttribute>().ShouldBeNull();
    }

    [Fact]
    public void The_controller_class_is_under_the_platform_tenants_route()
    {
        var route = typeof(TenantAdministrationController).GetCustomAttribute<RouteAttribute>().ShouldNotBeNull();
        route.Template.ShouldBe("api/platform/tenants");
    }
}
