using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using OpenTms.Features;
using OpenTms.Platform.DocumentTypes;
using OpenTms.Platform.EmailTemplates;
using OpenTms.Platform.Features;
using OpenTms.Platform.Localization;
using OpenTms.Platform.ReportDefinitions;
using Shouldly;
using Volo.Abp.Authorization;
using Volo.Abp.FeatureManagement;
using Volo.Abp.Features;
using Volo.Abp.Localization;
using Volo.Abp.Validation;
using Volo.Abp.Validation.StringValues;
using Xunit;

namespace OpenTms.EntityFrameworkCore.Platform.MasterData;

/// <summary>
/// The feature chain of the Platform master data through the production interceptor and feature checker
/// (docs/designs/54-platform-master-data.md, "Feature flags and permissions": each list's endpoints require app →
/// Apps.MasterData.Platform → its leaf; GetLookupAsync needs only Apps.MasterData). Every
/// interface method is invoked by reflection, so a method added later without the chain fails here. Also the definition
/// of the Platform group (parent, default, children) and its display names in both languages, and a stored tenant value
/// that applies to that tenant only. The report definitions' three template commands (design 55) need
/// Platform.DocumentRendering on top of the chain, a feature outside the app tree that defaults off: these theories switch
/// it on, so "everything on" means everything, and pin that it alone refuses the three commands but not the status. Risk class: High in the design ("Permissions and feature chain on every method");
/// Critical in the department matrix (feature gating is authorization, feature values are per tenant).
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class PlatformMasterDataFeatureGatingTests : PlatformMasterDataAppServiceTestBase
{
    private static readonly (Type Service, string Leaf)[] Services =
    [
        (typeof(IEmailTemplateAppService), AppFeatures.MasterData.Platform.EmailTemplates),
        (typeof(IReportDefinitionAppService), AppFeatures.MasterData.Platform.ReportDefinitions),
        (typeof(IDocumentTypeAppService), AppFeatures.MasterData.Platform.DocumentTypes),
    ];

    private static bool IsPicker(MethodInfo method) => method.Name == "GetLookupAsync";

    /// <summary>The template commands of design 55: the screen's chain plus Platform.DocumentRendering.</summary>
    private static readonly string[] RenderingCommands = ["OpenTemplateEditorAsync", "CopyTemplateAsync", "TestRenderAsync"];

    /// <summary>
    /// Platform.DocumentRendering on: it is not part of the app tree (TestFeatureValueProvider leaves it at its default,
    /// off), so without this the three commands would be refused by it and every "everything on" control would fail.
    /// </summary>
    private static IDisposable DocumentRenderingOn() => TestFeatureValueProvider.Override(PlatformFeatures.DocumentRendering, "true");

    public static IEnumerable<object[]> Methods() =>
        Services.SelectMany(s => s.Service.GetMethods().Select(m => new object[] { s.Service.Name, m.Name }));

    private static (Type Service, string Leaf, MethodInfo Method) Find(string service, string method)
    {
        var entry = Services.Single(s => s.Service.Name == service);
        return (entry.Service, entry.Leaf, entry.Service.GetMethods().Single(m => m.Name == method));
    }

    /// <summary>A plausible argument for a parameter: the gate is checked before the service body runs.</summary>
    private static object? ArgumentFor(ParameterInfo parameter)
    {
        var type = parameter.ParameterType;
        if (parameter.HasDefaultValue)
        {
            return parameter.DefaultValue;
        }

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

        return type.GetConstructor(Type.EmptyTypes) != null ? Activator.CreateInstance(type) : null;
    }

    private async Task<Exception?> InvokeAsync(Type service, MethodInfo method)
    {
        var instance = ServiceProvider.GetService(service) ?? throw new InvalidOperationException($"{service.Name} is not registered");
        var arguments = method.GetParameters().Select(ArgumentFor).ToArray();
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

    private static bool IsFeatureRefusal(Exception? ex) =>
        ex is AbpAuthorizationException authorization && authorization.Code?.StartsWith("Volo.Feature:", StringComparison.Ordinal) == true;

    private void SkipInputValidation()
    {
        /* ABP validates the input before the feature interceptor runs, so an empty DTO would stop at validation (400)
         * and never reach the gate. Inputs are not this test's concern (as MasterDataGeneralFeatureGatingTests). */
        GetRequiredService<IOptions<AbpValidationOptions>>().Value.ObjectValidationContributors.Clear();
    }

    [Theory]
    [MemberData(nameof(Methods))]
    public async Task With_the_Platform_group_or_the_lists_leaf_off_every_screen_method_is_refused_and_the_lookup_passes(string serviceName, string methodName)
    {
        var (service, leaf, method) = Find(serviceName, methodName);
        using var _ = InTenantA();
        using var rendering = DocumentRenderingOn();
        SkipInputValidation();

        foreach (var off in new[] { AppFeatures.MasterData.Platform.Default, leaf })
        {
            Exception? result;
            using (TestFeatureValueProvider.Override(off, "false"))
            {
                result = await InvokeAsync(service, method);
            }

            if (IsPicker(method))
            {
                IsFeatureRefusal(result).ShouldBeFalse($"{serviceName}.{methodName} is a picker (D12) and needs only the app ({off} off): {result?.Message}");
            }
            else
            {
                IsFeatureRefusal(result).ShouldBeTrue($"{serviceName}.{methodName} must be refused with {off} off, got {result?.GetType().Name}: {result?.Message}");
            }
        }

        /* Control: with everything on the same call gets past the gate, so the refusals above were the chain's. */
        IsFeatureRefusal(await InvokeAsync(service, method)).ShouldBeFalse($"{serviceName}.{methodName} with everything on");

        /* The app itself off refuses everything, the lookup included. */
        using (TestFeatureValueProvider.Override(AppFeatures.MasterData.Default, "false"))
        {
            IsFeatureRefusal(await InvokeAsync(service, method)).ShouldBeTrue($"{serviceName}.{methodName} with the Master data app off");
        }
    }

    [Theory]
    [MemberData(nameof(Methods))]
    public async Task Another_lists_leaf_off_does_not_refuse_this_list(string serviceName, string methodName)
    {
        var (service, leaf, method) = Find(serviceName, methodName);
        using var _ = InTenantA();
        using var rendering = DocumentRenderingOn();
        SkipInputValidation();

        foreach (var other in Services.Select(s => s.Leaf).Where(l => l != leaf))
        {
            using (TestFeatureValueProvider.Override(other, "false"))
            {
                var result = await InvokeAsync(service, method);
                IsFeatureRefusal(result).ShouldBeFalse($"{serviceName}.{methodName} with {other} off: {result?.Message}");
            }
        }
    }

    [Theory]
    [InlineData("OpenTemplateEditorAsync", true)]
    [InlineData("CopyTemplateAsync", true)]
    [InlineData("TestRenderAsync", true)]
    [InlineData("GetRenderingStatusAsync", false)]
    public async Task With_the_chain_on_Platform_DocumentRendering_off_refuses_the_three_template_commands_but_not_the_status(string methodName, bool refused)
    {
        /* Design 55: "the three commands also require Platform.DocumentRendering. The status needs only the screen's chain
         * so it can report 'feature off' as a reason". Default value, no override: off. */
        RenderingCommands.Contains(methodName).ShouldBe(refused);
        var (service, _, method) = Find(nameof(IReportDefinitionAppService), methodName);
        using var _ = InTenantA();
        SkipInputValidation();

        (await GetRequiredService<IFeatureChecker>().IsEnabledAsync(PlatformFeatures.DocumentRendering)).ShouldBeFalse("off by default");
        var result = await InvokeAsync(service, method);
        IsFeatureRefusal(result).ShouldBe(refused, $"{methodName} with Platform.DocumentRendering off: {result?.GetType().Name}: {result?.Message}");
        if (refused)
        {
            ((AbpAuthorizationException)result!).Data.Values.Cast<object?>().ShouldContain(PlatformFeatures.DocumentRendering, "the refusal names the rendering feature");
        }
    }

    // ── Definition, defaults, display names ──────────────────────────────────

    [Fact]
    public async Task The_Platform_group_sits_under_the_app_on_by_default_with_its_three_lists_off()
    {
        var manager = GetRequiredService<IFeatureDefinitionManager>();
        var platform = await manager.GetAsync(AppFeatures.MasterData.Platform.Default);

        platform.Parent!.Name.ShouldBe(AppFeatures.MasterData.Default);
        platform.DefaultValue.ShouldBe("true");
        platform.Children.Select(c => c.Name).ShouldBe(
        [
            AppFeatures.MasterData.Platform.EmailTemplates, AppFeatures.MasterData.Platform.ReportDefinitions,
            AppFeatures.MasterData.Platform.DocumentTypes,
        ], ignoreOrder: true);
        platform.Children.ShouldAllBe(list => list.DefaultValue == "false" && list.Children.Count == 0 && list.ValueType is ToggleStringValueType);
    }

    [Fact]
    public async Task Without_a_stored_value_the_group_is_on_and_the_lists_are_off()
    {
        var checker = GetRequiredService<IFeatureChecker>();
        using var group = TestFeatureValueProvider.Override(AppFeatures.MasterData.Platform.Default, null);
        using var leaf = TestFeatureValueProvider.Override(AppFeatures.MasterData.Platform.EmailTemplates, null);
        using (InTenantA())
        {
            (await checker.IsEnabledAsync(AppFeatures.MasterData.Platform.Default)).ShouldBeTrue();
            (await checker.IsEnabledAsync(AppFeatures.MasterData.Platform.EmailTemplates)).ShouldBeFalse();
            await Should.ThrowAsync<AbpAuthorizationException>(() => GetRequiredService<IEmailTemplateAppService>().GetListAsync(new EmailTemplateListRequestDto()));
            (await GetRequiredService<IEmailTemplateAppService>().GetLookupAsync()).ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task The_Platform_group_switched_off_for_tenant_A_leaves_tenant_B_on()
    {
        /* The test provider steps aside for the group, so the stored tenant value and the default decide: production path. */
        using var _ = TestFeatureValueProvider.Override(AppFeatures.MasterData.Platform.Default, null);
        await WithUnitOfWorkAsync(() => GetRequiredService<IFeatureManagementStore>()
            .SetAsync(AppFeatures.MasterData.Platform.Default, "false", TenantFeatureValueProvider.ProviderName, TenantA.ToString()));

        var templates = GetRequiredService<IEmailTemplateAppService>();

        using (InTenantA())
        {
            (await GetRequiredService<IFeatureChecker>().IsEnabledAsync(AppFeatures.MasterData.Platform.Default)).ShouldBeFalse();
            await Should.ThrowAsync<AbpAuthorizationException>(() => templates.GetListAsync(new EmailTemplateListRequestDto()));
            (await templates.GetLookupAsync()).ShouldBeEmpty();
        }

        using (InTenantB())
        {
            (await GetRequiredService<IFeatureChecker>().IsEnabledAsync(AppFeatures.MasterData.Platform.Default)).ShouldBeTrue();
            (await templates.GetListAsync(new EmailTemplateListRequestDto())).TotalCount.ShouldBe(0);
        }
    }

    [Theory]
    [InlineData("en", "Platform group", "E-mail templates")]
    [InlineData("nl", "Groep Platform", null)]
    public async Task The_Platform_features_have_their_display_names_in_both_languages(string culture, string groupName, string? emailTemplatesName)
    {
        var manager = GetRequiredService<IFeatureDefinitionManager>();
        var factory = GetRequiredService<IStringLocalizerFactory>();

        using (CultureHelper.Use(CultureInfo.GetCultureInfo(culture)))
        {
            var platform = await manager.GetAsync(AppFeatures.MasterData.Platform.Default);
            platform.DisplayName.Localize(factory).Value.ShouldBe(groupName);
            platform.Description!.Localize(factory).ResourceNotFound.ShouldBeFalse();

            foreach (var list in platform.Children)
            {
                var name = list.DisplayName.Localize(factory);
                name.ResourceNotFound.ShouldBeFalse(list.Name);
                name.Value.ShouldNotBeNullOrWhiteSpace(list.Name);
                list.Description!.Localize(factory).ResourceNotFound.ShouldBeFalse(list.Name);
            }

            if (emailTemplatesName != null)
            {
                (await manager.GetAsync(AppFeatures.MasterData.Platform.EmailTemplates)).DisplayName.Localize(factory).Value.ShouldBe(emailTemplatesName);
            }

            var texts = GetRequiredService<IStringLocalizer<PlatformResource>>();
            texts["Platform:Nav.Platform"].ResourceNotFound.ShouldBeFalse();
        }
    }
}
