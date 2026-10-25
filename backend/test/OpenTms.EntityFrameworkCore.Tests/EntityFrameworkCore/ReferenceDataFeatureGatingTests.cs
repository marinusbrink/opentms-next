using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using OpenTms.Features;
using OpenTms.Orders.TransportOrders;
using OpenTms.Financial.Invoicing;
using OpenTms.Financial.Tariffs;
using OpenTms.PlanningExecution.TransportTrips;
using OpenTms.Platform.Features;
using Shouldly;
using Volo.Abp.Authorization;
using Volo.Abp.Validation;
using Xunit;

namespace OpenTms.EntityFrameworkCore;

/// <summary>
/// The app-tree features of the Orders, Financial and Planning &amp; Execution lists (designs 51–53) through the
/// production interceptor: with the list's node off, or its domain group off, every public method of its service is
/// refused by the feature gate except the picker (<c>GetLookupAsync</c>, D12), which needs only the app. Every
/// interface method is invoked by reflection, so a method added later without the chain fails here.
/// Risk class: Critical (feature gating is authorization).
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class ReferenceDataFeatureGatingTests : ReferenceDataAppServiceTestBase
{
    /// <summary>Service → (its list node, its domain group). Every reference-data service of designs 51–53 is listed.</summary>
    private static readonly Dictionary<Type, (string Node, string DomainGroup)> Services = new()
    {
        [typeof(ITransportTripTypeAppService)] = (AppFeatures.MasterData.PlanningExecution.Trips.TransportTripTypes, AppFeatures.MasterData.PlanningExecution.Default),
        [typeof(ITransportOrderTypeAppService)] = (AppFeatures.MasterData.Orders.TransportOrders.TransportOrderTypes, AppFeatures.MasterData.Orders.Default),
        [typeof(IWaypointTypeAppService)] = (AppFeatures.MasterData.Orders.TransportOrders.WaypointTypes, AppFeatures.MasterData.Orders.Default),
        [typeof(ITimeSlotAppService)] = (AppFeatures.MasterData.Orders.TransportOrders.TimeSlots, AppFeatures.MasterData.Orders.Default),
        [typeof(IVatPercentageAppService)] = (AppFeatures.MasterData.Financial.Invoicing.VatPercentages, AppFeatures.MasterData.Financial.Default),
        [typeof(IOutboundInvoiceTypeAppService)] = (AppFeatures.MasterData.Financial.Invoicing.OutboundInvoiceTypes, AppFeatures.MasterData.Financial.Default),
        [typeof(IInboundInvoiceTypeAppService)] = (AppFeatures.MasterData.Financial.Invoicing.InboundInvoiceTypes, AppFeatures.MasterData.Financial.Default),
        [typeof(IAdditionalServiceAppService)] = (AppFeatures.MasterData.Financial.Pricing.AdditionalServices, AppFeatures.MasterData.Financial.Default),
        [typeof(IDiscountOrSurchargeAppService)] = (AppFeatures.MasterData.Financial.Pricing.DiscountsOrSurcharges, AppFeatures.MasterData.Financial.Default),
    };

    private static readonly HashSet<string> Pickers = ["GetLookupAsync", "GetLookupPageAsync"];

    public static IEnumerable<object[]> Methods() =>
        Services.Keys.SelectMany(service => service.GetMethods().Select(method => new object[] { service.Name, method.Name, method.GetParameters().Length }));

    private static (Type Service, MethodInfo Method) Find(string service, string method, int arity)
    {
        var type = Services.Keys.Single(t => t.Name == service);
        return (type, type.GetMethods().Single(m => m.Name == method && m.GetParameters().Length == arity));
    }

    private static object? ArgumentFor(Type type)
    {
        if (type == typeof(string)) return "X";
        if (type == typeof(Guid)) return Guid.NewGuid();
        if (type == typeof(CancellationToken)) return CancellationToken.None;
        if (Nullable.GetUnderlyingType(type) != null) return null;
        if (type.IsValueType) return Activator.CreateInstance(type);
        if (type.IsArray) return Array.CreateInstance(type.GetElementType()!, 0);
        if (type.IsGenericType && (type.IsInterface || type.IsAbstract))
        {
            var arguments = type.GetGenericArguments();
            return arguments.Length == 1
                ? Activator.CreateInstance(typeof(List<>).MakeGenericType(arguments[0]))
                : Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(arguments));
        }

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
    public async Task With_the_node_or_the_domain_group_off_every_non_picker_method_is_refused(string serviceName, string methodName, int arity)
    {
        var (service, method) = Find(serviceName, methodName, arity);
        var (node, domainGroup) = Services[service];
        using var _ = InTenantA();

        /* ABP validates the input before the feature interceptor runs; inputs are not this test's concern. */
        GetRequiredService<IOptions<AbpValidationOptions>>().Value.ObjectValidationContributors.Clear();

        foreach (var feature in new[] { node, domainGroup })
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

    [Fact]
    public async Task With_the_app_off_even_the_picker_is_refused()
    {
        using var _ = InTenantA();
        foreach (var service in Services.Keys)
        {
            var lookup = service.GetMethods().Single(m => m.Name == "GetLookupAsync");
            using (TestFeatureValueProvider.Override(AppFeatures.MasterData.Default, "false"))
            {
                IsFeatureRefusal(await InvokeAsync(service, lookup)).ShouldBeTrue(service.Name);
            }
        }
    }
}
