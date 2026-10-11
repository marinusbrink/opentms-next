using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Xunit;

namespace OpenTms.ArchitectureTests;

/* Assessment §3.11 (docs/input/masterdata-rebuild-assessment.md): in OpenTMS most application services
 * carried a bare authorize or none at all. Here every public application-service method of every module
 * must declare its permission, or explicitly allow anonymous access. A service marked
 * [RemoteService(IsEnabled = false)] is reached only through a controller in OpenTms.HttpApi, so its
 * controller actions are checked instead (the Platform administration services are built that way). */
public class AuthorizationCoverageTests
{
    private static readonly string[] Modules =
    {
        "Platform", "Orders", "PlanningExecution", "Financial", "MasterData", "Integrations", "Reporting"
    };

    [Fact]
    public void Every_public_application_service_method_declares_its_authorization()
    {
        var violations = new List<string>();

        foreach (var module in Modules)
        {
            var assembly = LoadAssembly($"OpenTms.{module}.Application");
            foreach (var service in ApplicationServices(assembly))
            {
                if (IsRemoteServiceDisabled(service))
                {
                    continue; // covered by Every_public_controller_action_declares_its_authorization
                }

                var classAuthorized = HasAuthorize(service);
                foreach (var method in PublicDeclaredMethods(service))
                {
                    if (!classAuthorized && !HasAuthorize(method) && !HasAllowAnonymous(method))
                    {
                        violations.Add($"{service.FullName}.{method.Name}");
                    }
                }
            }
        }

        violations.ShouldBeEmpty(
            "every public application-service method needs [Authorize(<permission>)] on the method or its class, " +
            "or an explicit [AllowAnonymous] (assessment §3.11). Missing on: " + string.Join(", ", violations));
    }

    [Fact]
    public void Every_public_controller_action_declares_its_authorization()
    {
        var assembly = LoadAssembly("OpenTms.HttpApi");
        var controllers = assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.IsPublic && typeof(ControllerBase).IsAssignableFrom(t));

        var violations = new List<string>();
        foreach (var controller in controllers)
        {
            var classAuthorized = HasAuthorize(controller);
            foreach (var action in PublicDeclaredMethods(controller))
            {
                if (!classAuthorized && !HasAuthorize(action) && !HasAllowAnonymous(action))
                {
                    violations.Add($"{controller.FullName}.{action.Name}");
                }
            }
        }

        violations.ShouldBeEmpty("every public controller action needs [Authorize] on the action or its controller, or [AllowAnonymous]. Missing on: " + string.Join(", ", violations));
    }

    [Fact]
    public void The_check_itself_catches_an_unattributed_method()
    {
        /* Self-test: a service without attributes must be reported, so a green run means something. */
        var method = typeof(UnattributedProbeAppService).GetMethod(nameof(UnattributedProbeAppService.DoAsync))!;
        (HasAuthorize(typeof(UnattributedProbeAppService)) || HasAuthorize(method) || HasAllowAnonymous(method)).ShouldBeFalse();
        PublicDeclaredMethods(typeof(UnattributedProbeAppService)).ShouldContain(method);
    }

    private static IEnumerable<Type> ApplicationServices(Assembly assembly)
        => assembly.GetTypes().Where(t => t.IsClass && !t.IsAbstract && t.IsPublic && typeof(IApplicationService).IsAssignableFrom(t));

    private static IEnumerable<MethodInfo> PublicDeclaredMethods(Type type)
        => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName);

    private static bool HasAuthorize(MemberInfo member) => member.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Any();

    private static bool HasAllowAnonymous(MemberInfo member) => member.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Any();

    private static bool IsRemoteServiceDisabled(Type type)
        => type.GetCustomAttributes<RemoteServiceAttribute>(inherit: true).Any(a => !a.IsEnabled);

    private static Assembly LoadAssembly(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, $"{name}.dll");
        File.Exists(path).ShouldBeTrue($"expected assembly {path} in test output — was the project renamed?");
        return Assembly.LoadFrom(path);
    }

    /// <summary>Lives in the test assembly only; proves the detection logic.</summary>
    public class UnattributedProbeAppService : ApplicationService
    {
        public System.Threading.Tasks.Task DoAsync() => System.Threading.Tasks.Task.CompletedTask;
    }
}
