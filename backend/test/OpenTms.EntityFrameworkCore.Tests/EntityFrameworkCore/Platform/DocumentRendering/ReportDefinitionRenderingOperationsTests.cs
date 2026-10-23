using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Localization;
using OpenTms.Platform;
using OpenTms.Platform.DocumentRendering;
using OpenTms.Platform.Features;
using OpenTms.Platform.Permissions;
using OpenTms.Platform.ReportDefinitions;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Features;
using Volo.Abp.Localization;
using Xunit;

namespace OpenTms.EntityFrameworkCore.Platform.DocumentRendering;

/// <summary>
/// The four report-definition operations of docs/designs/50-report-rendering.md ("Report-definition operations") on
/// SQLite with the fake provider: the rendering status per situation (with the feature off it never asks the provider
/// about the template), the template editor (refused for a template the workspace does not own; the template's sample
/// data else the catalogue sample; the definition's language), the copy ("(copy)", "(copy 2)", Dutch "(kopie)"; a new
/// definition with the same purpose, language, provider and pattern, not default, not archived, pointing at the new
/// template; refused without Create), the test render (testing, the file with its name and content type); archived and
/// feature-off refusals; another tenant's definition not found before any provider call. Risk class: High (design);
/// authorization and tenant isolation Critical in the department matrix.
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class ReportDefinitionRenderingOperationsTests : ReportRenderingTestBase<ReportRenderingTestModule>
{
    private readonly FakeReportRenderingProvider _provider;

    public ReportDefinitionRenderingOperationsTests()
    {
        _provider = GetRequiredService<FakeReportRenderingProvider>();
        _provider.CurrentTenant = CurrentTenant;
    }

    private static string CatalogueSample(ReportPurpose purpose) => JsonSerializer.Serialize(ReportDataCatalogue.SampleFor(purpose));

    private static JsonElement TemplateSample => JsonDocument.Parse("""{"Entity":{"EntityNumber":"T-9","InvoiceNumber":"T-9"},"EntityType":"Invoice"}""").RootElement.Clone();

    // ── Feature definition ─────────────────────────────────────────────────────

    [Fact]
    public async Task Platform_DocumentRendering_is_defined_off_by_default_outside_the_app_tree_with_names_in_both_languages()
    {
        var definition = await GetRequiredService<IFeatureDefinitionManager>().GetAsync(PlatformFeatures.DocumentRendering);
        definition.DefaultValue.ShouldBe("false", "constitution rule 4: new behaviour per tenant, default off");
        definition.Parent.ShouldBeNull();
        definition.Name.ShouldNotStartWith("Apps.");

        var factory = GetRequiredService<IStringLocalizerFactory>();
        foreach (var culture in new[] { "en", "nl" })
        {
            using (CultureHelper.Use(CultureInfo.GetCultureInfo(culture)))
            {
                definition.DisplayName.Localize(factory).ResourceNotFound.ShouldBeFalse(culture);
                definition.Description!.Localize(factory).ResourceNotFound.ShouldBeFalse(culture);
            }
        }
    }

    // ── Rendering status ───────────────────────────────────────────────────────

    [Fact]
    public async Task With_the_feature_off_the_status_says_so_and_never_asks_the_provider_about_the_template()
    {
        using var _ = InTenantA();
        var definition = await CreateDefinitionAsync();

        var status = await Definitions.GetRenderingStatusAsync(definition.Id);

        status.FeatureEnabled.ShouldBeFalse();
        status.ProviderAvailable.ShouldBeTrue();
        status.ProviderConfigured.ShouldBeTrue();
        status.SupportsTemplateEditor.ShouldBeTrue();
        status.SupportsTemplateCopy.ShouldBeTrue();
        status.TemplateFound.ShouldBeNull();
        status.TemplateName.ShouldBeNull();
        status.TemplateOwnedByWorkspace.ShouldBeNull();
        _provider.RemoteCalls.ShouldBeEmpty("feature off: no provider call");
    }

    [Fact]
    public async Task Not_configured_the_status_says_so_without_asking_about_the_template()
    {
        using var _ = InTenantA();
        using var on = RenderingOn();
        var definition = await CreateDefinitionAsync();
        _provider.IsConfigured = false;

        var status = await Definitions.GetRenderingStatusAsync(definition.Id);

        status.FeatureEnabled.ShouldBeTrue();
        status.ProviderAvailable.ShouldBeTrue();
        status.ProviderConfigured.ShouldBeFalse();
        status.TemplateFound.ShouldBeNull();
        _provider.RemoteCalls.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Configured_and_on_the_status_names_the_template_and_whether_the_workspace_owns_it(bool owned)
    {
        using var _ = InTenantA();
        using var on = RenderingOn();
        var definition = await CreateDefinitionAsync(templateId: "123456");
        _provider.Template = new ProviderTemplate { Id = "123456", Name = "FiLogic factuur", OwnedByWorkspace = owned };

        var status = await Definitions.GetRenderingStatusAsync(definition.Id);

        status.TemplateFound.ShouldBe(true);
        status.TemplateName.ShouldBe("FiLogic factuur");
        status.TemplateOwnedByWorkspace.ShouldBe(owned);
        status.Message.ShouldBeNull();
        _provider.RemoteCalls.ShouldHaveSingleItem().ShouldBe(new ProviderCall("get-template", TenantA, "123456"));
    }

    [Fact]
    public async Task An_unknown_template_is_reported_as_not_found()
    {
        using var _ = InTenantA();
        using var on = RenderingOn();
        var definition = await CreateDefinitionAsync();
        _provider.Template = null;

        var status = await Definitions.GetRenderingStatusAsync(definition.Id);

        status.TemplateFound.ShouldBe(false);
        status.TemplateName.ShouldBeNull();
        status.TemplateOwnedByWorkspace.ShouldBeNull();
    }

    [Fact]
    public async Task A_provider_failure_leaves_the_template_unknown_with_the_localized_reason()
    {
        using var _ = InTenantA();
        using var on = RenderingOn();
        using var culture = new CultureScope("en");
        var definition = await CreateDefinitionAsync();
        _provider.GetTemplateFailure = new BusinessException(PlatformErrorCodes.DocumentRenderingFailed).WithData("message", "No answer within 30 seconds.");

        var status = await Definitions.GetRenderingStatusAsync(definition.Id);

        status.TemplateFound.ShouldBeNull();
        status.Message.ShouldBe("The rendering provider could not handle the request: No answer within 30 seconds.");
    }

    [Fact]
    public async Task Without_a_provider_implementation_the_status_says_unavailable()
    {
        using var _ = InTenantA();
        using var on = RenderingOn();
        var definition = await CreateDefinitionAsync();
        _provider.Provider = (ReportRenderingProvider)99;

        var status = await Definitions.GetRenderingStatusAsync(definition.Id);

        status.ProviderAvailable.ShouldBeFalse();
        status.ProviderConfigured.ShouldBeFalse();
        status.FeatureEnabled.ShouldBeTrue();
        _provider.Calls.ShouldBeEmpty();
    }

    // ── Template editor ────────────────────────────────────────────────────────

    [Fact]
    public async Task The_editor_of_an_owned_template_gets_the_templates_sample_data_and_the_definitions_language()
    {
        using var _ = InTenantA();
        using var on = RenderingOn();
        var definition = await CreateDefinitionAsync(culture: "nl", templateId: "123456");
        _provider.SampleData = TemplateSample;

        var editor = await Definitions.OpenTemplateEditorAsync(definition.Id);

        editor.Url.ShouldBe(_provider.EditorUrl);
        var call = _provider.Calls.Single(c => c.Operation == "editor");
        call.TemplateId.ShouldBe("123456");
        call.Language.ShouldBe("nl");
        call.Tenant.ShouldBe(TenantA);
        ((JsonElement?)call.Argument)!.Value.GetRawText().ShouldBe(TemplateSample.GetRawText());
    }

    [Theory]
    [InlineData(ReportPurpose.Invoice, "en")]
    [InlineData(ReportPurpose.TransportUnitBalance, "nl")]
    public async Task Without_template_sample_data_the_editor_gets_the_catalogue_sample_of_the_purpose(ReportPurpose purpose, string language)
    {
        using var _ = InTenantA();
        using var on = RenderingOn();
        var definition = await CreateDefinitionAsync(purpose: purpose, culture: language);
        _provider.SampleData = null;

        await Definitions.OpenTemplateEditorAsync(definition.Id);

        var call = _provider.Calls.Single(c => c.Operation == "editor");
        ((JsonElement?)call.Argument)!.Value.GetRawText().ShouldBe(CatalogueSample(purpose));
        call.Language.ShouldBe(language);
    }

    [Fact]
    public async Task A_template_the_workspace_does_not_own_is_refused_as_TemplateNotOwned_before_the_editor_is_asked()
    {
        using var _ = InTenantA();
        using var on = RenderingOn();
        var definition = await CreateDefinitionAsync(templateId: "1001");
        _provider.Template = new ProviderTemplate { Id = "1001", Name = "FiLogic standaard factuur", OwnedByWorkspace = false };

        var ex = await ShouldFailAsync(() => Definitions.OpenTemplateEditorAsync(definition.Id), PlatformErrorCodes.TemplateNotOwned);

        ex.Data["templateId"].ShouldBe("1001");
        _provider.RemoteCalls.Select(c => c.Operation).ShouldBe(["get-template"], "never an editor URL for an organization template");
    }

    [Fact]
    public async Task An_unknown_template_is_TemplateNotFound()
    {
        using var _ = InTenantA();
        using var on = RenderingOn();
        var definition = await CreateDefinitionAsync();
        _provider.Template = null;

        await ShouldFailAsync(() => Definitions.OpenTemplateEditorAsync(definition.Id), PlatformErrorCodes.TemplateNotFound);
        _provider.Calls.ShouldNotContain(c => c.Operation == "editor");
    }

    [Fact]
    public async Task A_provider_without_an_editor_is_RenderingProviderUnavailable()
    {
        using var _ = InTenantA();
        using var on = RenderingOn();
        var definition = await CreateDefinitionAsync();
        _provider.SupportsTemplateEditor = false;

        await ShouldFailAsync(() => Definitions.OpenTemplateEditorAsync(definition.Id), PlatformErrorCodes.RenderingProviderUnavailable);
        _provider.RemoteCalls.ShouldBeEmpty();
    }

    // ── Copy ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_copy_creates_a_new_definition_pointing_at_the_copied_template()
    {
        using var _ = InTenantA();
        using var on = RenderingOn();
        using var culture = new CultureScope("en");
        var source = await CreateDefinitionAsync(name: "Factuur NL", purpose: ReportPurpose.Invoice, culture: "nl", templateId: "1001", pattern: "factuur-{{ number }}", isDefault: true);
        _provider.CopiedTemplateId = "2002";

        var copy = await Definitions.CopyTemplateAsync(source.Id);

        _provider.Calls.Single(c => c.Operation == "copy").ShouldBe(new ProviderCall("copy", TenantA, "1001", "Factuur NL (copy)"));
        copy.Id.ShouldNotBe(source.Id);
        copy.Name.ShouldBe("Factuur NL (copy)");
        copy.Purpose.ShouldBe(ReportPurpose.Invoice);
        copy.Culture.ShouldBe("nl");
        copy.Provider.ShouldBe(ReportRenderingProvider.PdfGeneratorApi);
        copy.FileNamePattern.ShouldBe(source.FileNamePattern);
        copy.ProviderTemplateId.ShouldBe("2002");
        copy.IsDefault.ShouldBeFalse("the source stays the default");
        copy.IsArchived.ShouldBeFalse();

        var stored = await Definitions.GetAsync(copy.Id);
        stored.ProviderTemplateId.ShouldBe("2002");
        (await Definitions.GetAsync(source.Id)).IsDefault.ShouldBeTrue("the copy does not take the default away");
        (await Definitions.GetAsync(source.Id)).ProviderTemplateId.ShouldBe("1001", "the source still points at the standard template");
    }

    [Fact]
    public async Task A_second_and_third_copy_are_numbered()
    {
        using var _ = InTenantA();
        using var on = RenderingOn();
        using var culture = new CultureScope("en");
        var source = await CreateDefinitionAsync(name: "Orderbevestiging");

        (await Definitions.CopyTemplateAsync(source.Id)).Name.ShouldBe("Orderbevestiging (copy)");
        (await Definitions.CopyTemplateAsync(source.Id)).Name.ShouldBe("Orderbevestiging (copy 2)");
        (await Definitions.CopyTemplateAsync(source.Id)).Name.ShouldBe("Orderbevestiging (copy 3)");
    }

    [Fact]
    public async Task Under_a_Dutch_user_interface_the_copy_is_called_kopie()
    {
        using var _ = InTenantA();
        using var on = RenderingOn();
        using var culture = new CultureScope("nl");
        var source = await CreateDefinitionAsync(name: "Factuur NL");

        (await Definitions.CopyTemplateAsync(source.Id)).Name.ShouldBe("Factuur NL (kopie)");
        (await Definitions.CopyTemplateAsync(source.Id)).Name.ShouldBe("Factuur NL (kopie 2)");
    }

    [Fact]
    public async Task A_name_at_the_maximum_length_is_shortened_so_the_copy_fits()
    {
        using var _ = InTenantA();
        using var on = RenderingOn();
        using var culture = new CultureScope("en");
        var source = await CreateDefinitionAsync(name: new string('L', PlatformMasterDataConsts.MaxNameLength));

        var copy = await Definitions.CopyTemplateAsync(source.Id);

        copy.Name.Length.ShouldBeLessThanOrEqualTo(PlatformMasterDataConsts.MaxNameLength);
        copy.Name.ShouldEndWith(" (copy)");
    }

    [Fact]
    public async Task The_copy_name_counts_only_the_tenants_own_definitions()
    {
        using var on = RenderingOn();
        using var culture = new CultureScope("en");
        using (InTenantB())
        {
            await CreateDefinitionAsync(name: "Factuur NL (copy)");
        }

        using (InTenantA())
        {
            var source = await CreateDefinitionAsync(name: "Factuur NL");
            (await Definitions.CopyTemplateAsync(source.Id)).Name.ShouldBe("Factuur NL (copy)", "tenant B's names are invisible");
        }
    }

    [Fact]
    public async Task Without_the_Create_permission_the_copy_is_refused_before_the_provider_copies_anything()
    {
        using var _ = InTenantA();
        using var on = RenderingOn();
        var source = await CreateDefinitionAsync();

        using (DenyingAuthorizationService.Deny(PlatformPermissions.MasterData.ReportDefinitions.Create))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => Definitions.CopyTemplateAsync(source.Id));
        }

        _provider.RemoteCalls.ShouldBeEmpty();
        (await Definitions.GetListAsync(new ReportDefinitionListRequestDto { StartRow = 0, EndRow = 100, IncludeArchived = true })).TotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task A_provider_that_cannot_copy_is_RenderingProviderUnavailable()
    {
        using var _ = InTenantA();
        using var on = RenderingOn();
        var source = await CreateDefinitionAsync();
        _provider.SupportsTemplateCopy = false;

        await ShouldFailAsync(() => Definitions.CopyTemplateAsync(source.Id), PlatformErrorCodes.RenderingProviderUnavailable);
        _provider.RemoteCalls.ShouldBeEmpty();
    }

    // ── Test render ────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_test_render_sends_the_catalogue_sample_as_a_test_and_returns_the_file()
    {
        using var _ = InTenantA();
        using var on = RenderingOn();
        var definition = await CreateDefinitionAsync(templateId: "123456", pattern: "factuur-{{ number }}");
        _provider.SampleData = null;
        _provider.Document = "%PDF-1.7 test"u8.ToArray();
        var sample = ReportDataCatalogue.SampleFor(ReportPurpose.Invoice);
        var number = sample["Entity"]!["EntityNumber"]!.GetValue<string>();

        var file = await Definitions.TestRenderAsync(definition.Id);

        var request = _provider.LastRender.ShouldNotBeNull();
        request.IsTest.ShouldBeTrue("design 50: testing, not billed");
        request.Format.ShouldBe(ReportRenderFormat.Pdf);
        request.Templates.ShouldHaveSingleItem().TemplateId.ShouldBe("123456");
        request.Templates[0].Data.GetRawText().ShouldBe(CatalogueSample(ReportPurpose.Invoice));
        file.FileName.ShouldBe($"factuur-{number}.pdf");
        file.ContentType.ShouldBe("application/pdf");
        await using var stream = file.GetStream();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        memory.ToArray().ShouldBe(_provider.Document);
    }

    [Fact]
    public async Task The_test_render_prefers_the_templates_own_sample_data()
    {
        using var _ = InTenantA();
        using var on = RenderingOn();
        var definition = await CreateDefinitionAsync();
        _provider.SampleData = TemplateSample;

        var file = await Definitions.TestRenderAsync(definition.Id);

        _provider.LastRender!.Templates[0].Data.GetRawText().ShouldBe(TemplateSample.GetRawText());
        file.FileName.ShouldBe("factuur-T-9.pdf");
    }

    // ── Refusals shared by the three commands ──────────────────────────────────

    public static TheoryData<string> Commands => new() { "editor", "copy", "test-render" };

    private Task Run(string command, Guid id) => command switch
    {
        "editor" => Definitions.OpenTemplateEditorAsync(id),
        "copy" => Definitions.CopyTemplateAsync(id),
        "test-render" => Definitions.TestRenderAsync(id),
        "status" => Definitions.GetRenderingStatusAsync(id),
        _ => throw new ArgumentOutOfRangeException(nameof(command))
    };

    [Theory]
    [MemberData(nameof(Commands))]
    public async Task With_the_feature_off_a_command_is_refused_without_any_provider_call(string command)
    {
        using var _ = InTenantA();
        var definition = await CreateDefinitionAsync();

        await ShouldBeFeatureRefusalAsync(() => Run(command, definition.Id), PlatformFeatures.DocumentRendering);
        _provider.Calls.ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Commands))]
    public async Task An_archived_definition_is_refused_without_any_provider_call(string command)
    {
        using var _ = InTenantA();
        using var on = RenderingOn();
        var definition = await CreateDefinitionAsync();
        await Definitions.ArchiveAsync(definition.Id);

        await ShouldFailAsync(() => Run(command, definition.Id), PlatformErrorCodes.ReportDefinitionArchived);
        _provider.Calls.ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Commands))]
    public async Task Not_configured_a_command_is_RenderingNotConfigured_without_a_remote_call(string command)
    {
        using var _ = InTenantA();
        using var on = RenderingOn();
        var definition = await CreateDefinitionAsync();
        _provider.IsConfigured = false;

        await ShouldFailAsync(() => Run(command, definition.Id), PlatformErrorCodes.RenderingNotConfigured);
        _provider.RemoteCalls.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("status")]
    [InlineData("editor")]
    [InlineData("copy")]
    [InlineData("test-render")]
    public async Task Another_tenants_definition_is_not_found_before_any_provider_call(string command)
    {
        Guid ofA;
        using (InTenantA())
        {
            ofA = (await CreateDefinitionAsync()).Id;
        }

        using var on = RenderingOn();
        foreach (var other in new Guid?[] { TenantB, null })
        {
            using (CurrentTenant.Change(other))
            {
                await Should.ThrowAsync<EntityNotFoundException>(() => Run(command, ofA), $"{command} from {(other == null ? "the host" : "tenant B")}");
            }
        }

        _provider.Calls.ShouldBeEmpty();
        using (InTenantB())
        {
            (await Definitions.GetListAsync(new ReportDefinitionListRequestDto { StartRow = 0, EndRow = 100, IncludeArchived = true })).TotalCount
                .ShouldBe(0, "a refused copy created nothing in tenant B");
        }
    }
}
