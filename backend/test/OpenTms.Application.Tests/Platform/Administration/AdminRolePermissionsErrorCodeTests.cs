using System.Net;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenTms.Platform.Administration.Permissions;
using Shouldly;
using Volo.Abp;
using Volo.Abp.AspNetCore.ExceptionHandling;
using Volo.Abp.Modularity;
using Xunit;

namespace OpenTms.Platform.Administration;

/// <summary>
/// Design 73: refusing a change to the admin role's grants travels as HTTP 409 (a state conflict), not ABP's default 403,
/// which the UI would read as a missing permission. Risk class: Critical (authorization feedback).
/// </summary>
public class AdminRolePermissionsErrorCodeTests
{
    [Fact]
    public void AdminRolePermissionsReadOnly_travels_as_409()
    {
        PlatformErrorCodes.AdminRolePermissionsReadOnly.ShouldBe("OpenTms.Platform:AdminRolePermissionsReadOnly");

        /* The module's real ConfigureServices, against a bare service collection: no ABP application is started. */
        var services = new ServiceCollection();
        services.AddOptions();
        var module = new OpenTmsHttpApiModule();
        typeof(AbpModule).GetProperty("ServiceConfigurationContext", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!
            .SetValue(module, new ServiceConfigurationContext(services));
        module.ConfigureServices(new ServiceConfigurationContext(services));
        var mappings = services.BuildServiceProvider().GetRequiredService<IOptions<AbpExceptionHttpStatusCodeOptions>>().Value.ErrorCodeToHttpStatusCodeMappings;

        mappings[PlatformErrorCodes.AdminRolePermissionsReadOnly].ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public void The_replaced_permission_service_is_not_published_a_second_time_under_api_platform()
    {
        /* ABP's /api/permission-management/permissions serves it; the Platform's conventional controllers must not. */
        typeof(OpenTmsPermissionAppService).GetCustomAttribute<RemoteServiceAttribute>(inherit: false)!.IsEnabled.ShouldBeFalse();
    }
}
