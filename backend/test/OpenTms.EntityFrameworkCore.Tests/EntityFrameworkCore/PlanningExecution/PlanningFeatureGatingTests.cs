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
/// Design 56 "Flag &amp; rollout plan" for design 59's services: every method of the trip and leg services is refused
/// when the Transport app or its Trips node is off; the via-depot rules and trip attribute definitions follow the Master
/// data pattern (their node or the Planning &amp; Execution group off refuses everything but the picker). Risk class:
/// Critical (features gate the endpoints).
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

    public static IEnumerable<object[]> Methods() =>
        Services.Keys.SelectMany(service => service.GetMethods().Select(method => new object[] { service.Name, method.Name, method.GetParameters().Length }));

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
}
