using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Localization;
using OpenTms.MasterData.Drivers;
using OpenTms.MasterData.Fleet;
using OpenTms.MasterData.Localization;
using OpenTms.MasterData.Reference;
using OpenTms.MasterData.Regions;
using OpenTms.MasterData.Relations;
using OpenTms.MasterData.Transport;
using OpenTms.Features;
using OpenTms.Platform.Features;
using Shouldly;
using Volo.Abp.Authorization;
using Volo.Abp.FeatureManagement;
using Volo.Abp.Features;
using Volo.Abp.Localization;
using Volo.Abp.Validation;
using Microsoft.Extensions.Options;
using Xunit;

namespace OpenTms.EntityFrameworkCore.MasterData;

/// <summary>
/// The Master Data domain group <c>Apps.MasterData.General</c> (PO decision 2026-10-16, docs/designs/tenants-and-editions.md
/// D15) through the production interceptor and feature checker: with General off and everything else on, every
/// public method of every Master Data service that is not a picker is refused by the feature gate, and every picker
/// (D12) still passes it; a stored tenant value for General applies to that tenant only. Every interface method is
/// invoked by reflection, so a method added later without the General chain fails here. Also the definition of
/// General (parent, default, children) and its nl/en display names through the real localizer. Risk class: Critical
/// (Platform change; feature gating is authorization, feature values are per tenant).
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class MasterDataGeneralFeatureGatingTests : MasterDataAppServiceTestBase
{
    private static readonly Type[] ServiceInterfaces =
    [
        typeof(IAddressTypeAppService), typeof(IRelationCategoryAppService), typeof(IRelationAppService),
        typeof(IProductAppService), typeof(ITransportUnitAppService), typeof(ITemperatureAppService),
        typeof(IProductRelationCodeAppService),
        typeof(IDriverAppService), typeof(IVehicleTypeAppService), typeof(ITransportCharacteristicsAppService),
        typeof(IVehicleAppService), typeof(IVehicleCombinationAppService),
        typeof(ICountryAppService), typeof(ICurrencyAppService), typeof(IGeographicalRegionAppService),
    ];

    /// <summary>The pickers of D12 ("Pickers"), as interface name + method name; "*" = every service.</summary>
    private static readonly HashSet<string> Pickers =
    [
        "*.GetLookupAsync",
        "IRelationAppService.GetLookupPageAsync",
        "IRelationAppService.GetAddressesAsync",
        "IGeographicalRegionAppService.GetMatchesAsync",
        "ICountryAppService.GetDefaultAsync",
        "ITransportCharacteristicsAppService.GetLookupPageAsync",
        "IVehicleAppService.GetLookupPageAsync",
    ];

    private static bool IsPicker(Type service, MethodInfo method) =>
        Pickers.Contains("*." + method.Name) || Pickers.Contains(service.Name + "." + method.Name);

    public static IEnumerable<object[]> Methods() =>
        ServiceInterfaces.SelectMany(service => service.GetMethods().Select(method => new object[] { service.Name, method.Name, method.GetParameters().Length }));

    private static (Type Service, MethodInfo Method) Find(string service, string method, int arity)
    {
        var type = ServiceInterfaces.Single(t => t.Name == service);
        return (type, type.GetMethods().Single(m => m.Name == method && m.GetParameters().Length == arity));
    }

    /// <summary>A plausible argument for a parameter: the gate is checked before the service body runs.</summary>
    private static object? ArgumentFor(Type type)
    {
        if (type == typeof(string))
        {
            return "X";
        }

        if (type == typeof(Guid))
        {
            return Guid.NewGuid();
        }

        if (type == typeof(CancellationToken))
        {
            return CancellationToken.None;
        }

        if (Nullable.GetUnderlyingType(type) != null)
        {
            return null;
        }

        if (type.IsValueType)
        {
            return Activator.CreateInstance(type);
        }

        if (type.IsArray)
        {
            return Array.CreateInstance(type.GetElementType()!, 0);
        }

        if (type.IsGenericType && (type.IsInterface || type.IsAbstract))
        {
            var arguments = type.GetGenericArguments();
            if (arguments.Length == 1)
            {
                return Activator.CreateInstance(typeof(List<>).MakeGenericType(arguments[0]));
            }

            if (arguments.Length == 2)
            {
                return Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(arguments));
            }
        }

        return type.GetConstructor(Type.EmptyTypes) != null ? Activator.CreateInstance(type) : null;
    }

    private async Task<Exception?> InvokeAsync(Type service, MethodInfo method)
    {
        var instance = GetRequiredService(service);
        var arguments = method.GetParameters().Select(p => ArgumentFor(p.ParameterType)).ToArray();
        try
        {
            var result = method.Invoke(instance, arguments);
            if (result is Task task)
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

    private object GetRequiredService(Type type) => ServiceProvider.GetService(type)
        ?? throw new InvalidOperationException($"{type.Name} is not registered");

    private static bool IsFeatureRefusal(Exception? ex) =>
        ex is AbpAuthorizationException authorization && authorization.Code?.StartsWith("Volo.Feature:", StringComparison.Ordinal) == true;

    [Fact]
    public void Every_service_has_methods_and_every_picker_of_D12_exists()
    {
        foreach (var service in ServiceInterfaces)
        {
            service.GetMethods().ShouldNotBeEmpty(service.Name);
            service.GetMethod("GetLookupAsync").ShouldNotBeNull(service.Name);
        }

        foreach (var picker in Pickers.Where(p => !p.StartsWith("*.", StringComparison.Ordinal)))
        {
            var parts = picker.Split('.');
            ServiceInterfaces.Single(t => t.Name == parts[0]).GetMethods().ShouldContain(m => m.Name == parts[1], picker);
        }
    }

    [Theory]
    [MemberData(nameof(Methods))]
    public async Task With_General_off_every_non_picker_method_is_refused_and_every_picker_passes(string serviceName, string methodName, int arity)
    {
        var (service, method) = Find(serviceName, methodName, arity);
        using var _ = InTenantA();

        /* ABP validates the input before the feature interceptor runs, so an empty create/update DTO would stop at
         * validation (400) and never reach the gate. Inputs are not this test's concern: switch DTO validation off
         * for this test's own application instance. */
        GetRequiredService<IOptions<AbpValidationOptions>>().Value.ObjectValidationContributors.Clear();

        Exception? withGeneralOff;
        using (TestFeatureValueProvider.Override(AppFeatures.MasterData.General.Default, "false"))
        {
            (await GetRequiredService<IFeatureChecker>().IsEnabledAsync(AppFeatures.MasterData.Default)).ShouldBeTrue();
            withGeneralOff = await InvokeAsync(service, method);
        }

        if (IsPicker(service, method))
        {
            IsFeatureRefusal(withGeneralOff).ShouldBeFalse($"{serviceName}.{methodName} is a picker (D12) and needs only the app: {withGeneralOff?.Message}");
        }
        else
        {
            IsFeatureRefusal(withGeneralOff).ShouldBeTrue(
                $"{serviceName}.{methodName} must be refused by the feature gate with General off, got {withGeneralOff?.GetType().Name}: {withGeneralOff?.Message}");
            ((AbpAuthorizationException)withGeneralOff!).Message.ShouldNotBeNull();

            /* Control: with General on, the same call gets past the gate, so the refusal above was General's. */
            var withGeneralOn = await InvokeAsync(service, method);
            IsFeatureRefusal(withGeneralOn).ShouldBeFalse($"{serviceName}.{methodName} with everything on: {withGeneralOn?.Message}");
        }
    }

    [Fact]
    public async Task With_General_off_the_lists_are_refused_but_the_four_groups_and_their_lists_are_still_on()
    {
        using var _ = InTenantA();
        var checker = GetRequiredService<IFeatureChecker>();

        using (TestFeatureValueProvider.Override(AppFeatures.MasterData.General.Default, "false"))
        {
            (await checker.IsEnabledAsync(AppFeatures.MasterData.General.Fleet.Default)).ShouldBeTrue();
            (await checker.IsEnabledAsync(AppFeatures.MasterData.General.Fleet.Vehicles)).ShouldBeTrue();
            await Should.ThrowAsync<AbpAuthorizationException>(() => GetRequiredService<IVehicleAppService>().GetListAsync(List<VehicleListRequestDto>()));
            await Should.ThrowAsync<AbpAuthorizationException>(() => GetRequiredService<ICountryAppService>().GetListAsync(List()));
            (await GetRequiredService<ICountryAppService>().GetLookupAsync()).ShouldBeEmpty();
            (await GetRequiredService<IVehicleAppService>().GetLookupPageAsync()).TotalCount.ShouldBe(0);
        }
    }

    [Fact]
    public async Task The_product_relation_code_resolver_needs_only_the_app_feature()
    {
        using var _ = InTenantA();
        var resolver = GetRequiredService<IProductRelationCodeResolver>();

        using (TestFeatureValueProvider.Override(AppFeatures.MasterData.General.Default, "false"))
        {
            await WithUnitOfWorkAsync(async () => (await resolver.ResolveAsync(["X"])).ShouldBeEmpty());
        }
    }

    // ── Tenant isolation of the General value (Critical) ──────────────────────

    [Fact]
    public async Task General_switched_off_for_tenant_A_leaves_tenant_B_on()
    {
        /* The test provider steps aside for General, so the stored tenant value ("T") and the default ("true")
         * decide: the production path. */
        using var _ = TestFeatureValueProvider.Override(AppFeatures.MasterData.General.Default, null);
        await WithUnitOfWorkAsync(() => GetRequiredService<IFeatureManagementStore>()
            .SetAsync(AppFeatures.MasterData.General.Default, "false", TenantFeatureValueProvider.ProviderName, TenantA.ToString()));

        var countries = GetRequiredService<ICountryAppService>();

        using (InTenantA())
        {
            (await GetRequiredService<IFeatureChecker>().IsEnabledAsync(AppFeatures.MasterData.General.Default)).ShouldBeFalse();
            await Should.ThrowAsync<AbpAuthorizationException>(() => countries.GetListAsync(List()));
            (await countries.GetLookupAsync()).ShouldBeEmpty();
        }

        using (InTenantB())
        {
            (await GetRequiredService<IFeatureChecker>().IsEnabledAsync(AppFeatures.MasterData.General.Default)).ShouldBeTrue();
            (await countries.GetListAsync(List())).TotalCount.ShouldBe(0);
        }
    }

    [Fact]
    public async Task Without_a_stored_value_General_is_on_by_default()
    {
        using var _ = TestFeatureValueProvider.Override(AppFeatures.MasterData.General.Default, null);
        using (InTenantB())
        {
            (await GetRequiredService<IFeatureChecker>().IsEnabledAsync(AppFeatures.MasterData.General.Default)).ShouldBeTrue();
        }
    }

    // ── Definition and display names ──────────────────────────────────────────

    [Fact]
    public async Task General_sits_between_the_app_and_the_four_groups_on_by_default_with_the_lists_off()
    {
        var manager = GetRequiredService<IFeatureDefinitionManager>();
        var general = await manager.GetAsync(AppFeatures.MasterData.General.Default);

        general.Parent!.Name.ShouldBe(AppFeatures.MasterData.Default);
        general.DefaultValue.ShouldBe("true");
        general.Children.Select(c => c.Name).ShouldBe(
        [
            AppFeatures.MasterData.General.Relations.Default,
            AppFeatures.MasterData.General.Transport.Default,
            AppFeatures.MasterData.General.Fleet.Default,
            AppFeatures.MasterData.General.Reference.Default,
        ], ignoreOrder: true);
        general.Children.ShouldAllBe(group => group.DefaultValue == "true");
        general.Children.SelectMany(group => group.Children).Count().ShouldBe(15);
        general.Children.SelectMany(group => group.Children).ShouldAllBe(list => list.DefaultValue == "false" && list.Children.Count == 0);

        (await manager.GetAsync(AppFeatures.MasterData.Default)).Children.Select(c => c.Name).ShouldBe([AppFeatures.MasterData.General.Default]);
    }

    [Theory]
    [InlineData("Apps.MasterData.Relations")]
    [InlineData("Apps.MasterData.Relations.Relations")]
    [InlineData("Apps.MasterData.Transport.ProductRelationCodes")]
    [InlineData("Apps.MasterData.Fleet")]
    [InlineData("Apps.MasterData.Fleet.Vehicles")]
    [InlineData("Apps.MasterData.Reference.GeographicalRegions")]
    public async Task The_old_names_are_no_longer_defined(string name)
    {
        (await GetRequiredService<IFeatureDefinitionManager>().GetOrNullAsync(name)).ShouldBeNull();
    }

    [Theory]
    [InlineData("en", "General group", "General")]
    [InlineData("nl", "Groep Algemeen", "Algemeen")]
    public async Task General_has_its_display_name_and_nav_label_in_both_languages(string culture, string displayName, string navLabel)
    {
        var general = await GetRequiredService<IFeatureDefinitionManager>().GetAsync(AppFeatures.MasterData.General.Default);
        var factory = GetRequiredService<IStringLocalizerFactory>();

        using (CultureHelper.Use(CultureInfo.GetCultureInfo(culture)))
        {
            general.DisplayName.Localize(factory).Value.ShouldBe(displayName);
            general.Description!.Localize(factory).Value.ShouldNotBeNullOrWhiteSpace();
            general.Description!.Localize(factory).ResourceNotFound.ShouldBeFalse();

            var masterData = GetRequiredService<IStringLocalizer<MasterDataResource>>();
            masterData["MasterData:Nav.General"].Value.ShouldBe(navLabel);
            masterData["MasterData:Nav.General"].ResourceNotFound.ShouldBeFalse();
        }
    }
}
