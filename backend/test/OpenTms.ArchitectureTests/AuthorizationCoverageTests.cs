using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenTms.ArchitectureTests.Probes;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Xunit;

namespace OpenTms.ArchitectureTests;

/* Assessment §3.11 (docs/input/masterdata-rebuild-assessment.md): in OpenTMS most application services
 * carried a bare authorize or none at all. Here every public application-service method of every module
 * must declare its permission, or explicitly allow anonymous access. A service marked
 * [RemoteService(IsEnabled = false)] is reached only through a controller in OpenTms.HttpApi, so its
 * controller actions are checked instead (the Platform administration services are built that way).
 *
 * Background paths (design 56 build notes): outbox handlers and Hangfire derived-value jobs run without a current user.
 * The cross-module published readers they call (interfaces in an OpenTms.*.Published namespace) are therefore
 * in-process and anonymous, and nothing anonymous may reach HTTP except the allow-listed endpoints below. */
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

    /// <summary>
    /// The deliberate anonymous HTTP endpoints of our own assemblies ("Type.Method", or "Type" for a whole class). The
    /// other anonymous endpoints of the product (account login pages, application configuration, localization, health)
    /// are ABP's or mapped endpoints, not ours. Adding one here is a design decision.
    /// </summary>
    private static readonly HashSet<string> AnonymousHttpEndpoints = new(StringComparer.Ordinal)
    {
        /* Design 64: Postmark's delivery webhooks; Postmark has no OpenTMS identity, the processor checks its Basic credentials. */
        "OpenTms.Controllers.Integrations.PostmarkWebhookController.ReceiveAsync"
    };

    /// <summary>The published readers known today; the guard fails when it no longer finds them (a moved namespace).</summary>
    private static readonly string[] KnownPublishedReaders =
    {
        "MasterDataReferenceLookup", "OrderPricingReader", "TransportOrderLookup", "FinancialReferenceLookup", "RateLineReader",
        "PricePreviewService", "LegReader", "TransportTripLookup"
    };

    [Fact]
    public void Published_readers_never_require_a_user()
    {
        var readers = Modules.SelectMany(m => LoadAssembly($"OpenTms.{m}.Application").GetTypes())
            .Where(t => t.IsClass && !t.IsAbstract && ImplementsPublishedInterface(t))
            .ToList();
        KnownPublishedReaders.ShouldBeSubsetOf(readers.Select(t => t.Name), "the guard must see every published reader");

        var violations = readers.SelectMany(PublishedReaderViolations).ToList();
        violations.ShouldBeEmpty(
            "a published cross-module reader (an implementation of an OpenTms.*.Published interface) is called by outbox handlers " +
            "and derived-value jobs, which run without a current user: [Authorize] there stalls the tenant's outbox for ever. " +
            "Make it [RemoteService(IsEnabled = false)] [AllowAnonymous] and authorize in the HTTP-facing caller. Found on: " +
            string.Join(", ", violations));
    }

    [Fact]
    public void Anonymous_application_services_and_controller_actions_stay_off_http()
    {
        var violations = Modules.SelectMany(m => ApplicationServices(LoadAssembly($"OpenTms.{m}.Application")))
            .SelectMany(AnonymousServiceViolations)
            .ToList();

        var controllers = LoadAssembly("OpenTms.HttpApi").GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.IsPublic && typeof(ControllerBase).IsAssignableFrom(t));
        violations.AddRange(controllers.SelectMany(c => AnonymousMembers(c).Where(name => !AnonymousHttpEndpoints.Contains(name))));

        violations.ShouldBeEmpty(
            "[AllowAnonymous] is for in-process services only ([RemoteService(IsEnabled = false)], e.g. the published readers " +
            "that outbox handlers and jobs call without a user); an anonymous HTTP endpoint must be in AnonymousHttpEndpoints. " +
            "Anonymous on HTTP: " + string.Join(", ", violations));
    }

    [Fact]
    public void The_background_path_guards_catch_an_authorized_reader_and_an_anonymous_http_service()
    {
        /* Self-test: the bug of the browser run and the leaks the second guard exists for are reported; the intended shape is not. */
        PublishedReaderViolations(typeof(AuthorizedProbeReader)).ShouldNotBeEmpty();
        PublishedReaderViolations(typeof(MethodAuthorizedProbeReader)).ShouldNotBeEmpty();
        PublishedReaderViolations(typeof(AnonymousProbeReader)).ShouldBeEmpty();
        PublishedReaderViolations(typeof(UnattributedProbeAppService)).ShouldBeEmpty("not a published reader");

        AnonymousServiceViolations(typeof(RemoteAnonymousProbeAppService)).ShouldNotBeEmpty();
        AnonymousServiceViolations(typeof(MethodAnonymousProbeAppService)).ShouldNotBeEmpty();
        AnonymousServiceViolations(typeof(AnonymousProbeReader)).ShouldBeEmpty("in-process only");
    }

    [Fact]
    public void The_check_itself_catches_an_unattributed_method()
    {
        /* Self-test: a service without attributes must be reported, so a green run means something. */
        var method = typeof(UnattributedProbeAppService).GetMethod(nameof(UnattributedProbeAppService.DoAsync))!;
        (HasAuthorize(typeof(UnattributedProbeAppService)) || HasAuthorize(method) || HasAllowAnonymous(method)).ShouldBeFalse();
        PublicDeclaredMethods(typeof(UnattributedProbeAppService)).ShouldContain(method);
    }

    private static bool ImplementsPublishedInterface(Type type)
        => type.GetInterfaces().Any(i => i.Namespace is { } ns && ns.StartsWith("OpenTms.", StringComparison.Ordinal)
            && (ns.EndsWith(".Published", StringComparison.Ordinal) || ns.Contains(".Published.", StringComparison.Ordinal)));

    /// <summary>A published reader with [Authorize] on the class (or a base class) or on any of its methods.</summary>
    private static IEnumerable<string> PublishedReaderViolations(Type type)
    {
        if (!ImplementsPublishedInterface(type))
        {
            yield break;
        }

        if (HasAuthorize(type))
        {
            yield return type.FullName!;
        }

        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Where(m => !m.IsSpecialName && HasAuthorize(m)))
        {
            yield return $"{type.FullName}.{method.Name}";
        }
    }

    /// <summary>An application service reachable over HTTP with [AllowAnonymous] on the class or a method, unless allow-listed.</summary>
    private static IEnumerable<string> AnonymousServiceViolations(Type service)
        => IsRemoteServiceDisabled(service)
            ? []
            : AnonymousMembers(service).Where(name => !AnonymousHttpEndpoints.Contains(name));

    private static IEnumerable<string> AnonymousMembers(Type type)
    {
        if (HasAllowAnonymous(type))
        {
            yield return type.FullName!;
        }

        foreach (var method in PublicDeclaredMethods(type).Where(HasAllowAnonymous))
        {
            yield return $"{type.FullName}.{method.Name}";
        }
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
