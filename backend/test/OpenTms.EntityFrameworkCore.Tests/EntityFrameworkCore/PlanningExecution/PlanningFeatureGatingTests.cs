using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using OpenTms.Features;
using OpenTms.PlanningExecution.Legs;
using OpenTms.PlanningExecution.TransportTrips;
using OpenTms.PlanningExecution.ViaDepots;
using OpenTms.Platform.Features;
using Shouldly;
using Volo.Abp.Authorization;
using Volo.Abp.Validation;
using Xunit;

namespace OpenTms.EntityFrameworkCore.PlanningExecution;

/// <summary>
/// Design 56 "Flag &amp; rollout plan" for design 59's services, as design 68 D7 changed it: every method of the trip and
/// leg services needs (the Transport app AND its Trips node) OR the Planboard app — with the Planboard off, the Transport
/// app or its Trips node off refuses; with the Planboard on, the Transport side off still allows. The via-depot rules and
/// trip attribute definitions follow the Master data pattern (their node or the Planning &amp; Execution group off refuses
/// everything but the picker). Risk class: Critical (features gate the endpoints).
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class PlanningFeatureGatingTests : ReferenceDataAppServiceTestBase
{
    private static readonly Dictionary<Type, (string Node, string Group)> Services = new()
    {
        [typeof(ITransportTripAppService)] = (AppFeatures.TransportApp.Trips, AppFeatures.Transport),
        [typeof(ILegAppService)] = (AppFeatures.TransportApp.Trips, AppFeatures.Transport),
        [typeof(IViaDepotRuleAppService)] = (AppFeatures.MasterData.PlanningExecution.Trips.ViaDepotRules, AppFeatures.MasterData.PlanningExecution.Default),
        [typeof(ITripAttributeDefinitionAppService)] = (AppFeatures.MasterData.PlanningExecution.Trips.TripAttributeDefinitions, AppFeatures.MasterData.PlanningExecution.Default)
    };

    private static readonly HashSet<string> Pickers = ["GetLookupAsync"];

    /// <summary>The services design 68 D7 opened to the Planboard app: (Transport AND Trips) OR Planboard.</summary>
    private static readonly HashSet<Type> PlanboardAlternative = [typeof(ITransportTripAppService), typeof(ILegAppService)];

    public static IEnumerable<object[]> Methods() =>
        Services.Keys.SelectMany(service => service.GetMethods().Select(method => new object[] { service.Name, method.Name, method.GetParameters().Length }));

    public static IEnumerable<object[]> PlanboardMethods() =>
        PlanboardAlternative.SelectMany(service => service.GetMethods().Select(method => new object[] { service.Name, method.Name, method.GetParameters().Length }));

    private static object? ArgumentFor(Type type)
    {
        if (type == typeof(string)) return "X";
        if (type == typeof(CancellationToken)) return CancellationToken.None;
        if (Nullable.GetUnderlyingType(type) != null) return null;
        if (type.IsValueType) return Activator.CreateInstance(type);
        return type.GetConstructor(Type.EmptyTypes) != null ? Activator.CreateInstance(type) : null;
    }

    private async Task<Exception?> InvokeAsync(Type service, MethodInfo method)
    {
        var instance = ServiceProvider.GetService(service) ?? throw new InvalidOperationException($"{service.Name} is not registered");
        var arguments = method.GetParameters().Select(p => ArgumentFor(p.ParameterType)).ToArray();
        try
        {
            if (method.Invoke(instance, arguments) is Task task)
            {
                await task;
            }

            return null;
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            return ex.InnerException;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private static bool IsFeatureRefusal(Exception? ex) =>
        ex is AbpAuthorizationException authorization && authorization.Code?.StartsWith("Volo.Feature:", StringComparison.Ordinal) == true;

    [Theory]
    [MemberData(nameof(Methods))]
    public async Task With_the_node_or_the_app_off_every_non_picker_method_is_refused(string serviceName, string methodName, int arity)
    {
        var service = Services.Keys.Single(t => t.Name == serviceName);
        var method = service.GetMethods().Single(m => m.Name == methodName && m.GetParameters().Length == arity);
        var (node, group) = Services[service];
        using var _ = InTenantA();

        /* ABP validates the input before the feature interceptor runs; inputs are not this test's concern. */
        GetRequiredService<IOptions<AbpValidationOptions>>().Value.ObjectValidationContributors.Clear();

        foreach (var feature in new[] { node, group })
        {
            Exception? result;
            using (TestFeatureValueProvider.Override(feature, "false"))
            /* Design 68 D7: the trip and leg services are also the Planboard's; the Transport side alone gates them only with the Planboard off. */
            using (PlanboardAlternative.Contains(service) ? TestFeatureValueProvider.Override(AppFeatures.Planboard, "false") : null)
            {
                result = await InvokeAsync(service, method);
            }

            if (Pickers.Contains(method.Name))
            {
                IsFeatureRefusal(result).ShouldBeFalse($"{serviceName}.{methodName} is a picker and needs only the app (feature {feature} off): {result?.Message}");
            }
            else
            {
                IsFeatureRefusal(result).ShouldBeTrue($"{serviceName}.{methodName} must be refused with {feature} off, got {result?.GetType().Name}: {result?.Message}");
            }
        }
    }

    [Theory]
    [MemberData(nameof(PlanboardMethods))]
    public async Task With_the_Planboard_on_the_trip_and_leg_services_are_allowed_even_when_the_Transport_app_and_its_Trips_node_are_off(string serviceName, string methodName, int arity)
    {
        var service = PlanboardAlternative.Single(t => t.Name == serviceName);
        var method = service.GetMethods().Single(m => m.Name == methodName && m.GetParameters().Length == arity);
        using var _ = InTenantA();
        GetRequiredService<IOptions<AbpValidationOptions>>().Value.ObjectValidationContributors.Clear();

        foreach (var off in new[] { new[] { AppFeatures.Transport }, new[] { AppFeatures.TransportApp.Trips }, new[] { AppFeatures.Transport, AppFeatures.TransportApp.Trips } })
        {
            Exception? result;
            using (TestFeatureValueProvider.Override(AppFeatures.Planboard, "true"))
            using (TestFeatureValueProvider.Override(off[0], "false"))
            using (off.Length > 1 ? TestFeatureValueProvider.Override(off[1], "false") : null)
            {
                result = await InvokeAsync(service, method);
            }

            IsFeatureRefusal(result).ShouldBeFalse($"{serviceName}.{methodName} must pass the feature check with the Planboard on and {string.Join(" + ", off)} off: {result?.Message}");
        }
    }

    [Theory]
    [MemberData(nameof(PlanboardMethods))]
    public async Task With_the_Transport_side_and_the_Planboard_both_off_the_trip_and_leg_services_are_refused(string serviceName, string methodName, int arity)
    {
        var service = PlanboardAlternative.Single(t => t.Name == serviceName);
        var method = service.GetMethods().Single(m => m.Name == methodName && m.GetParameters().Length == arity);
        using var _ = InTenantA();
        GetRequiredService<IOptions<AbpValidationOptions>>().Value.ObjectValidationContributors.Clear();

        Exception? result;
        using (TestFeatureValueProvider.Override(AppFeatures.Planboard, "false"))
        using (TestFeatureValueProvider.Override(AppFeatures.Transport, "false"))
        using (TestFeatureValueProvider.Override(AppFeatures.TransportApp.Trips, "false"))
        {
            result = await InvokeAsync(service, method);
        }

        IsFeatureRefusal(result).ShouldBeTrue($"{serviceName}.{methodName} must be refused with Transport, Trips and Planboard off, got {result?.GetType().Name}: {result?.Message}");
    }
}
