using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using OpenTms.Platform;
using OpenTms.Platform.DocumentRendering;
using OpenTms.Platform.Features;
using OpenTms.Platform.ReportDefinitions;
using Shouldly;
using Volo.Abp.Domain.Entities;
using Xunit;

namespace OpenTms.EntityFrameworkCore.Platform.DocumentRendering;

/// <summary>
/// The published renderer of docs/designs/50-report-rendering.md ("IDocumentRenderer"): it reads the definition in the
/// current tenant, refuses an archived one, checks Platform.DocumentRendering, picks the provider by the definition's
/// Provider, composes the file name and calls the provider. Another tenant's definition is not found before any provider
/// call (tenant isolation, constitution rule 1; the PostgreSQL counterpart is DocumentRenderingTenantIsolationPostgreSqlTests).
/// Risk class: High in the design ("Report-definition operations ... tenant isolation"); the isolation part is Critical
/// in the department matrix. SQLite with the fake provider of <see cref="ReportRenderingTestModule"/>.
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class DocumentRendererTests : ReportRenderingTestBase<ReportRenderingTestModule>
{
    private const string PayloadMarker = "QQ-RENDERER-PAYLOAD-Bakker";

    private readonly FakeReportRenderingProvider _provider;

    public DocumentRendererTests()
    {
        _provider = GetRequiredService<FakeReportRenderingProvider>();
        _provider.CurrentTenant = CurrentTenant;
    }

    private IDocumentRenderer Renderer => GetRequiredService<IDocumentRenderer>();

    private static JsonElement Invoice(string number) =>
        JsonDocument.Parse($$$"""{"Entity":{"EntityNumber":"{{{number}}}","Remarks":"{{{PayloadMarker}}}"},"EntityType":"Invoice"}""").RootElement.Clone();

    [Fact]
    public async Task A_definition_renders_through_its_provider_with_the_composed_file_name()
    {
        using var _ = InTenantA();
        using var on = RenderingOn();
        var definition = await CreateDefinitionAsync(templateId: "123456", pattern: "factuur-{{ number }}");

        var document = await Renderer.RenderAsync(definition.Id, Invoice("F-2026-0001"));

        document.FileName.ShouldBe("factuur-F-2026-0001.pdf");
        document.ContentType.ShouldBe("application/pdf");
        document.Content.ShouldBe(_provider.Document);
        var request = _provider.LastRender.ShouldNotBeNull();
        request.Templates.ShouldHaveSingleItem().TemplateId.ShouldBe("123456");
        request.Templates[0].Data.GetProperty("Entity").GetProperty("EntityNumber").GetString().ShouldBe("F-2026-0001");
        request.Format.ShouldBe(ReportRenderFormat.Pdf);
        request.IsTest.ShouldBeFalse("a real render is billed; only the test render is marked");
        _provider.Calls.Single(c => c.Operation == "render").Tenant.ShouldBe(TenantA, "the provider runs in the caller's tenant");
    }

    [Fact]
    public async Task A_batch_sends_one_template_per_record_and_names_the_file_after_the_first()
    {
        using var _ = InTenantA();
        using var on = RenderingOn();
        var definition = await CreateDefinitionAsync(templateId: "77");

        var document = await Renderer.RenderAsync(definition.Id, [Invoice("F-1"), Invoice("F-2"), Invoice("F-3")], ReportRenderFormat.Zip);

        _provider.LastRender!.Templates.Select(t => t.TemplateId).ShouldBe(["77", "77", "77"]);
        _provider.LastRender.Templates.Select(t => t.Data.GetProperty("Entity").GetProperty("EntityNumber").GetString()).ShouldBe(["F-1", "F-2", "F-3"]);
        _provider.LastRender.Format.ShouldBe(ReportRenderFormat.Zip);
        document.FileName.ShouldBe("factuur-F-1.zip");
    }

    [Fact]
    public async Task No_records_is_refused_before_anything_else()
    {
        using var _ = InTenantA();
        using var on = RenderingOn();

        await Should.ThrowAsync<ArgumentException>(() => Renderer.RenderAsync(Guid.NewGuid(), Array.Empty<JsonElement>()));
        _provider.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task With_the_feature_off_the_render_is_refused_without_any_provider_call()
    {
        using var _ = InTenantA();
        var definition = await CreateDefinitionAsync();

        using (RenderingOff())
        {
            await ShouldBeFeatureRefusalAsync(() => Renderer.RenderAsync(definition.Id, Invoice("F-1")), PlatformFeatures.DocumentRendering);
        }

        /* Off is the default (constitution rule 4): without an override the render is refused as well. */
        await ShouldBeFeatureRefusalAsync(() => Renderer.RenderAsync(definition.Id, Invoice("F-1")), PlatformFeatures.DocumentRendering);
        _provider.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_archived_definition_is_refused_without_a_provider_call()
    {
        using var _ = InTenantA();
        using var on = RenderingOn();
        var definition = await CreateDefinitionAsync();
        await Definitions.ArchiveAsync(definition.Id);

        await ShouldFailAsync(() => Renderer.RenderAsync(definition.Id, Invoice("F-1")), PlatformErrorCodes.ReportDefinitionArchived);
        _provider.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Without_a_provider_for_the_definitions_provider_the_render_is_RenderingProviderUnavailable()
    {
        using var _ = InTenantA();
        using var on = RenderingOn();
        var definition = await CreateDefinitionAsync();
        _provider.Provider = (ReportRenderingProvider)99;

        var ex = await ShouldFailAsync(() => Renderer.RenderAsync(definition.Id, Invoice("F-1")), PlatformErrorCodes.RenderingProviderUnavailable);

        ex.Data["provider"].ShouldBe(nameof(ReportRenderingProvider.PdfGeneratorApi));
        _provider.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_provider_without_configuration_is_RenderingNotConfigured_and_never_asked_to_render()
    {
        using var _ = InTenantA();
        using var on = RenderingOn();
        var definition = await CreateDefinitionAsync();
        _provider.IsConfigured = false;

        await ShouldFailAsync(() => Renderer.RenderAsync(definition.Id, Invoice("F-1")), PlatformErrorCodes.RenderingNotConfigured);
        _provider.RemoteCalls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Another_tenants_definition_is_not_found_before_any_provider_call()
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
                var ex = await Should.ThrowAsync<EntityNotFoundException>(() => Renderer.RenderAsync(ofA, Invoice("F-1")));
                ex.Id.ShouldBe(ofA);
                await Should.ThrowAsync<EntityNotFoundException>(() => Renderer.RenderAsync(ofA, [Invoice("F-1"), Invoice("F-2")]));
            }
        }

        _provider.Calls.ShouldBeEmpty("neither the status nor a render: the definition is not visible outside tenant A");

        using (InTenantA())
        {
            (await Renderer.RenderAsync(ofA, Invoice("F-1"))).FileName.ShouldBe("factuur-F-1.pdf", "the control: in its own tenant it renders");
        }
    }

    [Fact]
    public async Task An_unknown_id_is_not_found()
    {
        using var _ = InTenantA();
        using var on = RenderingOn();

        await Should.ThrowAsync<EntityNotFoundException>(() => Renderer.RenderAsync(Guid.NewGuid(), Invoice("F-1")));
        _provider.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_undefined_format_is_refused_without_a_provider_call()
    {
        using var _ = InTenantA();
        using var on = RenderingOn();
        var definition = await CreateDefinitionAsync();

        await Should.ThrowAsync<ArgumentOutOfRangeException>(() => Renderer.RenderAsync(definition.Id, Invoice("F-1"), (ReportRenderFormat)0));
        _provider.RemoteCalls.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_log_carries_ids_only_never_the_payload_or_the_document()
    {
        using var _ = InTenantA();
        using var on = RenderingOn();
        var definition = await CreateDefinitionAsync();
        _provider.Document = System.Text.Encoding.ASCII.GetBytes("%PDF QQ-DOCUMENT-BYTES");

        await Renderer.RenderAsync(definition.Id, Invoice("F-SECRET-NUMBER-1"));

        var entries = GetRequiredService<AllLevelsLog>().Snapshot();
        var renderLine = entries.Where(e => e.Message.StartsWith("Rendering report definition", StringComparison.Ordinal)).ShouldHaveSingleItem();
        renderLine.Message.ShouldContain(definition.Id.ToString());
        renderLine.Message.ShouldContain(TenantA.ToString());
        entries.ShouldAllBe(e => !e.Everything.Contains(PayloadMarker) && !e.Everything.Contains("QQ-DOCUMENT-BYTES") && !e.Everything.Contains("F-SECRET-NUMBER-1"));
    }
}
