using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using OpenTms.Features;
using OpenTms.Integrations.DocumentRendering.PdfGeneratorApi;
using OpenTms.Platform;
using OpenTms.Platform.DocumentRendering;
using OpenTms.Platform.Features;
using OpenTms.Platform.ReportDefinitions;
using Shouldly;
using Volo.Abp.Domain.Entities;
using Xunit;

namespace OpenTms.PostgreSql.Platform;

/// <summary>
/// Tenant isolation of report rendering against real PostgreSQL (docs/designs/50-report-rendering.md, security
/// quickscan: "a definition of another tenant is not found before any provider call"; constitution rule 1): a definition
/// of tenant A (own database) or of the shared-tier tenant (host database) is not found by the published renderer nor by
/// the four report-definition operations from any other tenant or the host. The production adapter is registered but
/// unconfigured (the test configuration holds no key or secret, asserted first), so the control in the owning tenant
/// stops at RenderingNotConfigured: proof that the definition was found and the next step is the provider, without a
/// single outbound call. Risk class: Critical (tenant isolation).
/// </summary>
[Collection(PostgreSqlTestCollection.Name)]
public class DocumentRenderingTenantIsolationPostgreSqlTests : PlatformMasterDataPostgreSqlTestBase
{
    private IReportDefinitionAppService Definitions => GetRequiredService<IReportDefinitionAppService>();

    private IDocumentRenderer Renderer => GetRequiredService<IDocumentRenderer>();

    private static readonly JsonElement Data = JsonDocument.Parse("""{"Entity":{"EntityNumber":"F-1"},"EntityType":"Invoice"}""").RootElement.Clone();

    private void NoLiveProvider() =>
        GetRequiredService<IOptions<PdfGeneratorApiOptions>>().Value.IsConfigured
            .ShouldBeFalse("the PostgreSQL tests must never hold pdfgeneratorapi credentials: the account is FiLogic's production account");

    private Task<ReportDefinitionDto> WriteAsync() => Definitions.CreateAsync(new ReportDefinitionCreateDto
    {
        Name = "QQ rendering isolation", Purpose = ReportPurpose.Invoice, Culture = "en", ProviderTemplateId = "qq-render-1",
        Provider = ReportRenderingProvider.PdfGeneratorApi, FileNamePattern = "qq-{{ number }}"
    });

    private async Task NotFoundFromAsync(Guid? other, Guid id)
    {
        using (CurrentTenant.Change(other))
        {
            var who = NameFor(other);
            (await Should.ThrowAsync<EntityNotFoundException>(() => Renderer.RenderAsync(id, Data), who)).Id.ShouldBe(id);
            await Should.ThrowAsync<EntityNotFoundException>(() => Renderer.RenderAsync(id, [Data, Data]), who);
            await Should.ThrowAsync<EntityNotFoundException>(() => Definitions.GetRenderingStatusAsync(id), who);
            await Should.ThrowAsync<EntityNotFoundException>(() => Definitions.OpenTemplateEditorAsync(id), who);
            await Should.ThrowAsync<EntityNotFoundException>(() => Definitions.CopyTemplateAsync(id), who);
            await Should.ThrowAsync<EntityNotFoundException>(() => Definitions.TestRenderAsync(id), who);
        }
    }

    [Fact]
    public async Task A_definition_of_tenant_A_cannot_be_rendered_or_operated_from_the_shared_tenant_or_the_host()
    {
        NoLiveProvider();
        using var on = TestFeatureValueProvider.Override(PlatformFeatures.DocumentRendering, "true");

        await InRolledBackTransactionAsync(TenantA, async () =>
        {
            var definition = await WriteAsync();
            await FlushAsync();

            await NotFoundFromAsync(SharedTenant, definition.Id);
            await NotFoundFromAsync(null, definition.Id);

            /* Control: in its own tenant the definition is found and the renderer moves on to the provider. */
            await ShouldFailAsync(() => Renderer.RenderAsync(definition.Id, Data), PlatformErrorCodes.RenderingNotConfigured);
            (await Definitions.GetRenderingStatusAsync(definition.Id)).ProviderConfigured.ShouldBeFalse();
        });
    }

    [Fact]
    public async Task In_the_shared_host_database_the_shared_tenant_and_the_host_never_reach_each_others_definition()
    {
        NoLiveProvider();
        using var on = TestFeatureValueProvider.Override(PlatformFeatures.DocumentRendering, "true");

        await InRolledBackTransactionAsync(SharedTenant, async () =>
        {
            var ofShared = await WriteAsync();
            await FlushAsync();

            await NotFoundFromAsync(null, ofShared.Id);
            await NotFoundFromAsync(TenantA, ofShared.Id);
            await ShouldFailAsync(() => Renderer.RenderAsync(ofShared.Id, Data), PlatformErrorCodes.RenderingNotConfigured);
        });

        await InRolledBackTransactionAsync(null, async () =>
        {
            var ofHost = await WriteAsync();
            await FlushAsync();

            await NotFoundFromAsync(SharedTenant, ofHost.Id);
            await NotFoundFromAsync(TenantA, ofHost.Id);
            await ShouldFailAsync(() => Renderer.RenderAsync(ofHost.Id, Data), PlatformErrorCodes.RenderingNotConfigured);
        });
    }
}
