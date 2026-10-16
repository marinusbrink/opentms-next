using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenTms.Platform.DocumentTypes;
using OpenTms.Platform.EmailTemplates;
using OpenTms.Platform.Features;
using OpenTms.Platform.NumberSequences;
using OpenTms.Platform.Permissions;
using OpenTms.Platform.ReportDefinitions;
using Shouldly;
using Volo.Abp.AspNetCore.ExceptionHandling;
using Volo.Abp.Features;
using Volo.Abp.Modularity;
using Xunit;

namespace OpenTms.Platform.PlatformMasterData;

/// <summary>
/// The permission and feature attributes of the four Platform master-data services and of the number generator, read by
/// reflection (docs/designs/49-platform-master-data.md, "Feature flags and permissions" and the security quickscan:
/// sixteen permissions, every public method attributed, the screen methods behind the Platform group and their leaf, the
/// pickers behind the app feature only, the generator behind neither). Also the error-code → HTTP status mapping that
/// OpenTmsHttpApiModule configures (409 for the conflict codes, 400 for the rest). Risk class: High (design: "Permissions
/// and feature chain on every method"); authorization is Critical in the department matrix. The SQLite tests run with
/// authorization always allowed, so this is where a wrong or missing permission attribute is caught; the feature gate
/// itself runs through the interceptor in PlatformMasterDataFeatureGatingTests.
/// </summary>
public class PlatformMasterDataAuthorizationTests
{
    public static IEnumerable<object[]> PermissionCases()
    {
        foreach (var (service, view, create, update, delete, leaf) in Services)
        {
            yield return [service.Name, "GetListAsync", view];
            yield return [service.Name, "GetAsync", view];
            yield return [service.Name, "GetLookupAsync", view];
            yield return [service.Name, "CreateAsync", create];
            yield return [service.Name, "UpdateAsync", update];
            yield return [service.Name, "DeleteAsync", delete];
            yield return [service.Name, "BulkDeleteAsync", delete];
            if (service == typeof(ReportDefinitionAppService) || service == typeof(DocumentTypeAppService))
            {
                yield return [service.Name, "ArchiveAsync", update];
                yield return [service.Name, "UnarchiveAsync", update];
            }
        }
    }

    private static readonly (Type Service, string View, string Create, string Update, string Delete, string Leaf)[] Services =
    [
        (typeof(EmailTemplateAppService), PlatformPermissions.MasterData.EmailTemplates.Default, PlatformPermissions.MasterData.EmailTemplates.Create,
            PlatformPermissions.MasterData.EmailTemplates.Update, PlatformPermissions.MasterData.EmailTemplates.Delete, AppFeatures.MasterData.Platform.EmailTemplates),
        (typeof(ReportDefinitionAppService), PlatformPermissions.MasterData.ReportDefinitions.Default, PlatformPermissions.MasterData.ReportDefinitions.Create,
            PlatformPermissions.MasterData.ReportDefinitions.Update, PlatformPermissions.MasterData.ReportDefinitions.Delete, AppFeatures.MasterData.Platform.ReportDefinitions),
        (typeof(DocumentTypeAppService), PlatformPermissions.MasterData.DocumentTypes.Default, PlatformPermissions.MasterData.DocumentTypes.Create,
            PlatformPermissions.MasterData.DocumentTypes.Update, PlatformPermissions.MasterData.DocumentTypes.Delete, AppFeatures.MasterData.Platform.DocumentTypes),
        (typeof(NumberSequenceAppService), PlatformPermissions.MasterData.NumberSequences.Default, PlatformPermissions.MasterData.NumberSequences.Create,
            PlatformPermissions.MasterData.NumberSequences.Update, PlatformPermissions.MasterData.NumberSequences.Delete, AppFeatures.MasterData.Platform.NumberSequences),
    ];

    private static Type ServiceNamed(string name) => Services.Single(s => s.Service.Name == name).Service;

    private static IEnumerable<MethodInfo> PublicMethods(Type service) => service
        .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
        .Where(m => !m.IsSpecialName);

    [Theory]
    [MemberData(nameof(PermissionCases))]
    public void Every_service_method_carries_its_permission(string service, string method, string permission)
    {
        var info = ServiceNamed(service).GetMethod(method, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        info.ShouldNotBeNull($"{service}.{method} must be a public method");
        info.GetCustomAttribute<AuthorizeAttribute>()?.Policy.ShouldBe(permission, $"{service}.{method}");
        info.GetCustomAttribute<AuthorizeAttribute>().ShouldNotBeNull($"{service}.{method} needs [Authorize({permission})]");
    }

    [Fact]
    public void The_cases_above_cover_every_public_method_of_the_four_services()
    {
        var covered = PermissionCases().Select(c => $"{c[0]}.{c[1]}").ToHashSet();
        foreach (var (service, _, _, _, _, _) in Services)
        {
            PublicMethods(service).Select(m => $"{service.Name}.{m.Name}").Where(m => !covered.Contains(m))
                .ShouldBeEmpty($"{service.Name}: a public method without a pinned permission");
        }
    }

    [Theory]
    [InlineData(typeof(EmailTemplateAppService))]
    [InlineData(typeof(ReportDefinitionAppService))]
    [InlineData(typeof(DocumentTypeAppService))]
    [InlineData(typeof(NumberSequenceAppService))]
    public void No_public_method_is_without_a_permission_and_every_service_sits_behind_the_Master_data_app_feature(Type service)
    {
        PublicMethods(service)
            .Where(m => m.GetCustomAttribute<AuthorizeAttribute>()?.Policy == null)
            .Select(m => m.Name)
            .ShouldBeEmpty($"{service.Name}: every public method needs [Authorize(<permission>)]");

        var feature = service.GetCustomAttribute<RequiresFeatureAttribute>();
        feature.ShouldNotBeNull($"{service.Name} needs the app feature on the class");
        feature.Features.ShouldBe([AppFeatures.MasterData.Default]);
    }

    [Theory]
    [InlineData(typeof(EmailTemplateAppService))]
    [InlineData(typeof(ReportDefinitionAppService))]
    [InlineData(typeof(DocumentTypeAppService))]
    [InlineData(typeof(NumberSequenceAppService))]
    public void The_screen_methods_need_the_Platform_group_and_their_leaf_and_the_lookup_only_the_app(Type service)
    {
        var leaf = Services.Single(s => s.Service == service).Leaf;
        foreach (var method in PublicMethods(service))
        {
            var attribute = method.GetCustomAttribute<RequiresFeatureAttribute>();
            if (method.Name == "GetLookupAsync")
            {
                attribute.ShouldBeNull($"{service.Name}.GetLookupAsync is a picker (D12): only the app feature");
                continue;
            }

            attribute.ShouldNotBeNull($"{service.Name}.{method.Name} needs the Platform group and its leaf");
            attribute.RequiresAll.ShouldBeTrue($"{service.Name}.{method.Name}: the chain, not one of them");
            attribute.Features.ShouldBe([AppFeatures.MasterData.Platform.Default, leaf], ignoreOrder: true, $"{service.Name}.{method.Name}");
        }
    }

    [Fact]
    public void The_number_generator_is_neither_feature_gated_nor_an_application_service()
    {
        /* Design 49: "INumberGenerator is not feature-gated: it is called by other modules' services", no HTTP endpoint. */
        typeof(NumberGenerator).GetCustomAttribute<RequiresFeatureAttribute>().ShouldBeNull();
        typeof(NumberGenerator).GetMethod(nameof(NumberGenerator.GetNextAsync))!.GetCustomAttribute<RequiresFeatureAttribute>().ShouldBeNull();
        typeof(Volo.Abp.Application.Services.IApplicationService).IsAssignableFrom(typeof(NumberGenerator)).ShouldBeFalse();
        typeof(Volo.Abp.Application.Services.IApplicationService).IsAssignableFrom(typeof(INumberGenerator)).ShouldBeFalse();
        typeof(INumberGenerator).IsAssignableFrom(typeof(NumberGenerator)).ShouldBeTrue();
    }

    [Fact]
    public void The_sixteen_permission_names_are_the_designed_ones()
    {
        PlatformPermissions.MasterData.All.SelectMany(p => new[] { p.Default, p.Create, p.Update, p.Delete }).ShouldBe(
        [
            "Platform.EmailTemplates", "Platform.EmailTemplates.Create", "Platform.EmailTemplates.Update", "Platform.EmailTemplates.Delete",
            "Platform.ReportDefinitions", "Platform.ReportDefinitions.Create", "Platform.ReportDefinitions.Update", "Platform.ReportDefinitions.Delete",
            "Platform.DocumentTypes", "Platform.DocumentTypes.Create", "Platform.DocumentTypes.Update", "Platform.DocumentTypes.Delete",
            "Platform.NumberSequences", "Platform.NumberSequences.Create", "Platform.NumberSequences.Update", "Platform.NumberSequences.Delete",
        ]);
    }

    [Fact]
    public void The_five_features_are_the_designed_ones()
    {
        AppFeatures.MasterData.Platform.Default.ShouldBe("Apps.MasterData.Platform");
        AppFeatures.MasterData.Platform.EmailTemplates.ShouldBe("Apps.MasterData.Platform.EmailTemplates");
        AppFeatures.MasterData.Platform.ReportDefinitions.ShouldBe("Apps.MasterData.Platform.ReportDefinitions");
        AppFeatures.MasterData.Platform.DocumentTypes.ShouldBe("Apps.MasterData.Platform.DocumentTypes");
        AppFeatures.MasterData.Platform.NumberSequences.ShouldBe("Apps.MasterData.Platform.NumberSequences");
    }

    // ── Error codes → HTTP status (API contract) ─────────────────────────────

    [Fact]
    public void Every_Platform_error_code_is_either_a_conflict_or_a_bad_request()
    {
        var all = typeof(PlatformErrorCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string) && f.Name != nameof(PlatformErrorCodes.Namespace))
            .Select(f => (string)f.GetRawConstantValue()!)
            .Where(code => code.StartsWith(PlatformErrorCodes.Namespace + ":", StringComparison.Ordinal))
            .ToList();

        /* The twelve codes of the API contract (the Platform module had OutboxTenantNotFound before design 49). */
        foreach (var code in new[]
                 {
                     PlatformErrorCodes.DuplicateKey, PlatformErrorCodes.InUse, PlatformErrorCodes.TemplateSyntaxError,
                     PlatformErrorCodes.DefaultLanguageContentRequired, PlatformErrorCodes.UnsupportedCulture, PlatformErrorCodes.InvalidRegex,
                     PlatformErrorCodes.TransformationArgumentRequired, PlatformErrorCodes.DuplicateTransformation, PlatformErrorCodes.NumberSequenceInUse,
                     PlatformErrorCodes.UnknownSortColumn, PlatformErrorCodes.TooManyRows, PlatformErrorCodes.NameRequired
                 })
        {
            all.ShouldContain(code);
            (PlatformErrorCodes.ConflictCodes.Contains(code) ^ PlatformErrorCodes.BadRequestCodes.Contains(code)).ShouldBeTrue($"{code} is in exactly one list");
        }

        PlatformErrorCodes.ConflictCodes.ShouldBe([PlatformErrorCodes.DuplicateKey, PlatformErrorCodes.InUse, PlatformErrorCodes.NumberSequenceInUse], ignoreOrder: true);
        PlatformErrorCodes.ConflictCodes.Intersect(PlatformErrorCodes.BadRequestCodes).ShouldBeEmpty();
    }

    [Fact]
    public void The_HttpApi_module_maps_the_conflict_codes_to_409_and_the_rest_to_400()
    {
        /* The module's real ConfigureServices, against a bare service collection: no ABP application is started. */
        var services = new ServiceCollection();
        services.AddOptions();
        var module = new OpenTmsHttpApiModule();
        typeof(AbpModule).GetProperty("ServiceConfigurationContext", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!
            .SetValue(module, new ServiceConfigurationContext(services));
        module.ConfigureServices(new ServiceConfigurationContext(services));

        var mappings = services.BuildServiceProvider().GetRequiredService<IOptions<AbpExceptionHttpStatusCodeOptions>>().Value.ErrorCodeToHttpStatusCodeMappings;

        foreach (var code in PlatformErrorCodes.ConflictCodes)
        {
            mappings.ShouldContainKey(code);
            mappings[code].ShouldBe(HttpStatusCode.Conflict, code);
        }

        foreach (var code in PlatformErrorCodes.BadRequestCodes)
        {
            mappings.ShouldContainKey(code);
            mappings[code].ShouldBe(HttpStatusCode.BadRequest, code);
        }

        mappings[PlatformErrorCodes.DuplicateKey].ShouldBe(HttpStatusCode.Conflict);
        mappings[PlatformErrorCodes.InUse].ShouldBe(HttpStatusCode.Conflict);
        mappings[PlatformErrorCodes.NumberSequenceInUse].ShouldBe(HttpStatusCode.Conflict);
        mappings[PlatformErrorCodes.TemplateSyntaxError].ShouldBe(HttpStatusCode.BadRequest);
        mappings[PlatformErrorCodes.InvalidRegex].ShouldBe(HttpStatusCode.BadRequest);
    }
}
