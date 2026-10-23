using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using OpenTms.Integrations.DocumentRendering.PdfGeneratorApi;
using OpenTms.Platform;
using OpenTms.Platform.ReportDefinitions;
using Shouldly;
using Xunit;

namespace OpenTms.EntityFrameworkCore.Platform.DocumentRendering;

/// <summary>
/// The report-definition operations through the real pdfgeneratorapi adapter, its typed client and the
/// IHttpClientFactory pipeline (logging handlers included), on the fake primary handler of
/// <see cref="PdfGeneratorApiPipelineTestModule"/> (base URL on a reserved ".invalid" host: no live call). Covers the
/// design-50 Critical rows end to end: the workspace follows the current tenant, an organization template is never
/// opened in the editor (owner false or unknown), and no log line of any level or category, the HttpClient factory's
/// own included, carries the key, the secret, a token, the payload or the document. Also the DI wiring: the retry delay
/// is the injected one.
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class PdfGeneratorApiPipelineTests : ReportRenderingTestBase<PdfGeneratorApiPipelineTestModule>
{
    private const string PayloadMarker = "QQ-PIPELINE-PAYLOAD-deVries";
    private static readonly byte[] Document = Encoding.ASCII.GetBytes("%PDF-1.7 QQ pipeline document");
    private static readonly string DocumentBase64 = Convert.ToBase64String(Document);

    private readonly ScriptedPdfHandler _handler;

    public PdfGeneratorApiPipelineTests()
    {
        _handler = GetRequiredService<ScriptedPdfHandler>();
    }

    private string WorkspaceOf(Guid tenant) => $"workspace-tenant-{tenant}@opentms.nl";

    [Fact]
    public async Task A_test_render_runs_in_the_tenants_workspace_marked_as_test_and_nothing_secret_reaches_any_log()
    {
        using var _ = InTenantA();
        using var on = RenderingOn();
        var definition = await CreateDefinitionAsync(templateId: "123456");
        _handler
            .ThenJson($$$"""{"response":{"Entity":{"EntityNumber":"T-1","Remarks":"{{{PayloadMarker}}}"},"EntityType":"Invoice"}}""")
            .ThenStatus(HttpStatusCode.TooManyRequests, $$$"""{"message":"rate limited {{{PayloadMarker}}}"}""")
            .ThenJson($$$"""{"response":"{{{DocumentBase64}}}","meta":{"content_type":"application/pdf"}}""");

        var file = await Definitions.TestRenderAsync(definition.Id);

        file.FileName.ShouldBe("factuur-T-1.pdf");
        _handler.Requests.Select(r => (r.Method.Method, r.Uri.AbsoluteUri)).ShouldBe(
        [
            ("GET", PdfGeneratorApiPipelineTestModule.BaseUrl + "templates/123456/data"),
            ("POST", PdfGeneratorApiPipelineTestModule.BaseUrl + "documents/generate"),
            ("POST", PdfGeneratorApiPipelineTestModule.BaseUrl + "documents/generate"),
        ]);
        _handler.Requests.ShouldAllBe(r => r.Uri.Host == "pdfgeneratorapi.invalid");
        _handler.Requests[2].Json.GetProperty("testing").GetBoolean().ShouldBeTrue();
        _handler.Requests[2].Json.GetProperty("template").GetProperty("data").GetProperty("Entity").GetProperty("Remarks").GetString().ShouldBe(PayloadMarker);
        foreach (var request in _handler.Requests)
        {
            PdfHarness.Decode(request.Bearer!).Payload.GetProperty("sub").GetString().ShouldBe(WorkspaceOf(TenantA));
            PdfHarness.Decode(request.Bearer!).Payload.GetProperty("iss").GetString().ShouldBe(PdfGeneratorApiPipelineTestModule.Key);
        }

        GetRequiredService<RecordingRetryDelay>().Delays.ShouldBe([TimeSpan.FromSeconds(3)], "the 429 waited through the injected delay");

        var entries = GetRequiredService<AllLevelsLog>().Snapshot();
        entries.ShouldContain(e => e.Category.StartsWith("System.Net.Http.HttpClient", StringComparison.Ordinal),
            "the factory's own logging handlers ran, so their lines are part of the check");
        var signatures = _handler.Requests.Select(r => r.Bearer!.Split('.')[2]).ToList();
        foreach (var entry in entries)
        {
            var text = entry.Everything;
            text.ShouldNotContain(PdfGeneratorApiPipelineTestModule.Key, Case.Sensitive, entry.Category + ": " + entry.Message);
            text.ShouldNotContain(PdfGeneratorApiPipelineTestModule.Secret, Case.Sensitive, entry.Category + ": " + entry.Message);
            text.ShouldNotContain(PayloadMarker, Case.Sensitive, entry.Category + ": " + entry.Message);
            text.ShouldNotContain(DocumentBase64, Case.Sensitive, entry.Category + ": " + entry.Message);
            foreach (var signature in signatures)
            {
                text.ShouldNotContain(signature, Case.Sensitive, entry.Category + ": " + entry.Message);
            }
        }
    }

    [Fact]
    public async Task The_host_works_in_the_default_workspace_and_tenant_B_in_its_own()
    {
        using var on = RenderingOn();
        _handler.Fallback = (_, _) => Task.FromResult(ScriptedPdfHandler.Json("""{"response":{"id":123456,"name":"Factuur","owner":true}}"""));

        var host = await CreateDefinitionAsync(templateId: "123456");
        await Definitions.GetRenderingStatusAsync(host.Id);
        using (InTenantB())
        {
            var ofB = await CreateDefinitionAsync(templateId: "123456");
            (await Definitions.GetRenderingStatusAsync(ofB.Id)).TemplateOwnedByWorkspace.ShouldBe(true);
        }

        PdfHarness.Decode(_handler.Requests[0].Bearer!).Payload.GetProperty("sub").GetString().ShouldBe("default-workspace@opentms.nl");
        PdfHarness.Decode(_handler.Requests[1].Bearer!).Payload.GetProperty("sub").GetString().ShouldBe(WorkspaceOf(TenantB));
    }

    [Fact]
    public async Task A_FiLogic_standard_template_is_never_opened_in_the_editor()
    {
        using var _ = InTenantA();
        using var on = RenderingOn();
        var definition = await CreateDefinitionAsync(templateId: "1001");
        _handler.ThenJson("""{"response":{"id":1001,"name":"FiLogic factuur","owner":false}}""");

        await ShouldFailAsync(() => Definitions.OpenTemplateEditorAsync(definition.Id), PlatformErrorCodes.TemplateNotOwned);

        _handler.Requests.ShouldHaveSingleItem().Method.ShouldBe(HttpMethod.Get);
        _handler.Requests.ShouldNotContain(r => r.Uri.AbsolutePath.EndsWith("/editor", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Unknown_ownership_through_the_whole_chain_is_refused_as_TemplateNotOwned()
    {
        using var _ = InTenantA();
        using var on = RenderingOn();
        var definition = await CreateDefinitionAsync(templateId: "1001");
        _handler
            .ThenJson("""{"response":{"id":1001,"name":"FiLogic factuur"}}""")
            .ThenJson("""{"response":[{"id":1001,"name":"FiLogic factuur"}]}""");

        await ShouldFailAsync(() => Definitions.OpenTemplateEditorAsync(definition.Id), PlatformErrorCodes.TemplateNotOwned);

        _handler.Requests.Count.ShouldBe(2);
        _handler.Requests.ShouldAllBe(r => r.Method == HttpMethod.Get);
    }

    [Fact]
    public async Task An_owned_template_opens_with_the_definitions_language()
    {
        using var _ = InTenantA();
        using var on = RenderingOn();
        var definition = await CreateDefinitionAsync(templateId: "2002", culture: "en");
        _handler
            .ThenJson("""{"response":{"id":2002,"name":"Eigen factuur","owner":true}}""")
            .ThenStatus(HttpStatusCode.NotFound)
            .ThenJson("""{"response":"https://us1.pdfgeneratorapi.com/editor/?t=1"}""");

        var editor = await Definitions.OpenTemplateEditorAsync(definition.Id);

        editor.Url.ShouldBe("https://us1.pdfgeneratorapi.com/editor/?t=1");
        var post = _handler.Requests[2];
        post.Uri.AbsolutePath.ShouldBe("/api/v4/templates/2002/editor");
        post.Json.GetProperty("language").GetString().ShouldBe("en");
        post.Json.GetProperty("data").GetProperty("EntityType").GetString().ShouldBe("Invoice", "no template sample data: the catalogue sample");
    }
}
