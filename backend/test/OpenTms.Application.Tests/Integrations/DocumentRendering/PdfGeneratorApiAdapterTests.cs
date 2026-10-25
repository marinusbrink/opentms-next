using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenTms.Platform;
using OpenTms.Platform.DocumentRendering;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace OpenTms.Integrations.DocumentRendering.PdfGeneratorApi;

/// <summary>
/// The pdfgeneratorapi.com adapter of docs/designs/55-report-rendering.md on a fake transport, a manual clock and a
/// recording retry delay: no live call (the account is FiLogic's production account). Risk classes from the design's
/// test risk analysis: Critical for the JWT (claims, HS256, Base64Url), the workspace per tenant and host, never editing
/// an organization template (owner unknown is not owned) and secrets never returned or logged; High for the endpoints'
/// request and response contract, retry/backoff (429 and 5xx only, 3/6/12/24 s, a fresh token per attempt), timeouts,
/// error mapping, cancellation and the not-configured state.
/// </summary>
public class PdfGeneratorApiAdapterTests
{
    private const string PayloadMarker = "QQ-PAYLOAD-MARKER-Jansen-Transport";
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7 QQ test document");
    private static readonly string PdfBase64 = Convert.ToBase64String(Pdf);

    private static JsonElement Data(string marker = PayloadMarker) =>
        JsonDocument.Parse($$"""{"Entity":{"EntityNumber":"F-2026-0001","Remarks":"{{marker}}"},"EntityType":"Invoice"}""").RootElement.Clone();

    private static RenderRequest Single(string templateId = "123456", ReportRenderFormat format = ReportRenderFormat.Pdf, bool isTest = false) => new()
    {
        Templates = [new RenderTemplate { TemplateId = templateId, Data = Data() }],
        Format = format,
        FileName = "factuur-F-2026-0001.pdf",
        IsTest = isTest
    };

    private static string Generated(string? contentType = "application/pdf") => contentType == null
        ? $$$"""{"response":"{{{PdfBase64}}}","meta":{"name":"factuur.pdf","display_name":"factuur"}}"""
        : $$$"""{"response":"{{{PdfBase64}}}","meta":{"name":"factuur.pdf","display_name":"factuur","encoding":"base64","content_type":"{{{contentType}}}"}}""";

    private static async Task<BusinessException> FailsWith(Func<Task> action, string code)
    {
        var ex = await Should.ThrowAsync<BusinessException>(action);
        ex.Code.ShouldBe(code, ex.Message);
        return ex;
    }

    // ── JWT (Critical) ─────────────────────────────────────────────────────────

    [Fact]
    public void The_token_is_HS256_with_iss_the_key_sub_the_workspace_and_exp_sixty_seconds_from_now()
    {
        var token = PdfGeneratorApiJwt.Create(PdfHarness.Key, PdfHarness.Secret, "workspace-tenant-x@opentms.nl", PdfHarness.Start);
        var (header, payload, _) = PdfHarness.Decode(token);

        header.GetProperty("alg").GetString().ShouldBe("HS256");
        header.GetProperty("typ").GetString().ShouldBe("JWT");
        payload.EnumerateObject().Select(p => p.Name).ShouldBe(["iss", "sub", "exp"], ignoreOrder: true);
        payload.GetProperty("iss").GetString().ShouldBe(PdfHarness.Key);
        payload.GetProperty("sub").GetString().ShouldBe("workspace-tenant-x@opentms.nl");
        payload.GetProperty("exp").GetInt64().ShouldBe(PdfHarness.Start.AddSeconds(60).ToUnixTimeSeconds());
        PdfGeneratorApiJwt.Lifetime.ShouldBe(TimeSpan.FromSeconds(60), "design 55: now + 60 s (OpenTMS used 15 minutes)");
    }

    [Fact]
    public void The_signature_is_HMAC_SHA256_of_header_dot_payload_with_the_secret_and_verifies_only_with_that_secret()
    {
        var token = PdfGeneratorApiJwt.Create(PdfHarness.Key, PdfHarness.Secret, "default-workspace@opentms.nl", PdfHarness.Start);
        var parts = token.Split('.');
        var signingInput = Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]);

        var expected = System.Buffers.Text.Base64Url.EncodeToString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(PdfHarness.Secret), signingInput));
        parts[2].ShouldBe(expected);

        var forged = System.Buffers.Text.Base64Url.EncodeToString(HMACSHA256.HashData(Encoding.UTF8.GetBytes("another-secret"), signingInput));
        parts[2].ShouldNotBe(forged);
        token.ShouldNotContain(PdfHarness.Secret, Case.Sensitive, "the secret signs the token, it is never in it");
    }

    [Fact]
    public void Every_part_is_Base64Url_without_plus_slash_or_padding()
    {
        /* A 32-byte HMAC in standard Base64 always ends in "=": its absence alone proves the URL alphabet without padding.
         * Many workspaces and times make "+" and "/" likely in standard encoding of the parts as well. */
        var shape = new Regex("^[A-Za-z0-9_-]+\\.[A-Za-z0-9_-]+\\.[A-Za-z0-9_-]+$");
        var sawStandardOnlyCharacters = false;
        for (var i = 0; i < 300; i++)
        {
            var workspace = $"workspace-tenant-{Guid.NewGuid()}@opentms.nl?>~{i}";
            var token = PdfGeneratorApiJwt.Create(PdfHarness.Key + i, PdfHarness.Secret, workspace, PdfHarness.Start.AddSeconds(i * 7));
            shape.IsMatch(token).ShouldBeTrue(token);

            var signature = System.Buffers.Text.Base64Url.DecodeFromChars(token.Split('.')[2]);
            signature.Length.ShouldBe(32);
            var standard = Convert.ToBase64String(signature);
            sawStandardOnlyCharacters |= standard.Contains('+') || standard.Contains('/');
        }

        sawStandardOnlyCharacters.ShouldBeTrue("the sample must include signatures whose standard Base64 has + or /");
    }

    // ── Workspace per tenant and host (Critical) ───────────────────────────────

    [Fact]
    public async Task The_host_works_in_the_default_workspace_and_a_tenant_in_its_own()
    {
        var harness = new PdfHarness();
        harness.Handler.Fallback = (_, _) => Task.FromResult(ScriptedPdfHandler.Json("""{"response":{"id":123456,"name":"Factuur","owner":true}}"""));
        var provider = harness.Provider();
        var tenant = Guid.Parse("3a1f2c55-0d5e-4c4b-9f43-6b8f1e2d7a10");

        await provider.GetTemplateAsync("123456", CancellationToken.None);
        using (harness.CurrentTenant.Change(tenant))
        {
            await provider.GetTemplateAsync("123456", CancellationToken.None);
        }

        PdfHarness.Decode(harness.Handler.Requests[0].Bearer!).Payload.GetProperty("sub").GetString().ShouldBe("default-workspace@opentms.nl");
        PdfHarness.Decode(harness.Handler.Requests[1].Bearer!).Payload.GetProperty("sub").GetString()
            .ShouldBe("workspace-tenant-3a1f2c55-0d5e-4c4b-9f43-6b8f1e2d7a10@opentms.nl");
    }

    [Fact]
    public async Task The_configured_workspace_formats_are_used_with_string_Format_of_the_tenant_id()
    {
        var harness = new PdfHarness(o =>
        {
            o.DefaultWorkspace = "host@example.test";
            o.TenantWorkspace = "t-{0}@example.test";
        });
        harness.Handler.Fallback = (_, _) => Task.FromResult(ScriptedPdfHandler.Json("""{"response":{"id":1,"name":"X","owner":true}}"""));
        var tenant = Guid.NewGuid();

        var client = harness.Client();
        client.CurrentWorkspace.ShouldBe("host@example.test");
        using (harness.CurrentTenant.Change(tenant))
        {
            client.CurrentWorkspace.ShouldBe(string.Format("t-{0}@example.test", tenant));
            await harness.Provider().GetTemplateAsync("1", CancellationToken.None);
        }

        PdfHarness.Decode(harness.Handler.Requests.Single().Bearer!).Payload.GetProperty("sub").GetString().ShouldBe($"t-{tenant}@example.test");
    }

    [Fact]
    public void The_defaults_are_the_designed_ones()
    {
        var options = new PdfGeneratorApiOptions();
        options.BaseUrl.ShouldBe("https://us1.pdfgeneratorapi.com/api/v4/");
        options.DefaultWorkspace.ShouldBe("default-workspace@opentms.nl");
        options.TenantWorkspace.ShouldBe("workspace-tenant-{0}@opentms.nl");
        options.Key.ShouldBeNull("never in an appsettings default");
        options.Secret.ShouldBeNull("never in an appsettings default");
        PdfGeneratorApiOptions.SectionName.ShouldBe("Integrations:PdfGeneratorApi");
        PdfGeneratorApiClient.Timeout.ShouldBe(TimeSpan.FromSeconds(30));
    }

    // ── Endpoints: request and response contract (High) ───────────────────────

    [Fact]
    public async Task A_single_render_posts_documents_generate_with_one_template_base64_output_name_and_testing()
    {
        var harness = new PdfHarness();
        harness.Handler.ThenJson(Generated());

        var document = await harness.Provider().RenderAsync(Single(isTest: true), CancellationToken.None);

        var request = harness.Handler.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Post);
        request.Relative.ShouldBe("documents/generate");
        request.Accept.ShouldContain("application/json");
        var body = request.Json;
        body.EnumerateObject().Select(p => p.Name).ShouldBe(["template", "format", "output", "name", "testing"], ignoreOrder: true);
        body.GetProperty("template").ValueKind.ShouldBe(JsonValueKind.Object, "a single render sends one template object");
        body.GetProperty("template").GetProperty("id").ValueKind.ShouldBe(JsonValueKind.Number, "v4 template ids are integers");
        body.GetProperty("template").GetProperty("id").GetInt64().ShouldBe(123456);
        body.GetProperty("template").GetProperty("data").GetProperty("Entity").GetProperty("EntityNumber").GetString().ShouldBe("F-2026-0001");
        body.GetProperty("format").GetString().ShouldBe("pdf");
        body.GetProperty("output").GetString().ShouldBe("base64");
        body.GetProperty("name").GetString().ShouldBe("factuur-F-2026-0001.pdf");
        body.GetProperty("testing").GetBoolean().ShouldBeTrue();

        document.Content.ShouldBe(Pdf);
        document.ContentType.ShouldBe("application/pdf");
        document.FileName.ShouldBe("factuur-F-2026-0001.pdf");
    }

    [Fact]
    public async Task A_batch_posts_documents_generate_batch_with_one_template_per_record()
    {
        var harness = new PdfHarness();
        harness.Handler.ThenJson(Generated());
        var request = new RenderRequest
        {
            Templates = [new RenderTemplate { TemplateId = "77", Data = Data("A") }, new RenderTemplate { TemplateId = "77", Data = Data("B") }],
            FileName = "batch.pdf"
        };

        var document = await harness.Provider().RenderAsync(request, CancellationToken.None);

        var sent = harness.Handler.Requests.ShouldHaveSingleItem();
        sent.Relative.ShouldBe("documents/generate/batch");
        var templates = sent.Json.GetProperty("template");
        templates.ValueKind.ShouldBe(JsonValueKind.Array);
        templates.GetArrayLength().ShouldBe(2);
        templates[0].GetProperty("id").GetInt64().ShouldBe(77);
        templates[0].GetProperty("data").GetProperty("Entity").GetProperty("Remarks").GetString().ShouldBe("A");
        templates[1].GetProperty("data").GetProperty("Entity").GetProperty("Remarks").GetString().ShouldBe("B");
        sent.Json.GetProperty("testing").GetBoolean().ShouldBeFalse("a real render is not a test");
        document.Content.ShouldBe(Pdf);
    }

    [Fact]
    public async Task A_non_numeric_template_id_is_sent_as_text_and_the_content_type_falls_back_to_the_format()
    {
        var harness = new PdfHarness();
        harness.Handler.ThenJson(Generated(contentType: null));

        var document = await harness.Provider().RenderAsync(Single(templateId: "public-abc", format: ReportRenderFormat.Html), CancellationToken.None);

        var body = harness.Handler.Requests.Single().Json;
        body.GetProperty("template").GetProperty("id").GetString().ShouldBe("public-abc");
        body.GetProperty("format").GetString().ShouldBe("html");
        document.ContentType.ShouldBe("text/html");
    }

    [Theory]
    [InlineData("""{"response":""}""")]
    [InlineData("""{"meta":{}}""")]
    [InlineData("""{"response":"%%% not base64 %%%"}""")]
    public async Task An_answer_without_a_readable_document_is_DocumentRenderingFailed(string answer)
    {
        var harness = new PdfHarness();
        harness.Handler.ThenJson(answer);

        await FailsWith(() => harness.Provider().RenderAsync(Single(), CancellationToken.None), PlatformErrorCodes.DocumentRenderingFailed);
    }

    [Fact]
    public async Task An_answer_that_is_not_JSON_is_DocumentRenderingFailed()
    {
        var harness = new PdfHarness();
        harness.Handler.Then(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>gateway</html>") });

        await FailsWith(() => harness.Provider().RenderAsync(Single(), CancellationToken.None), PlatformErrorCodes.DocumentRenderingFailed);
    }

    [Fact]
    public async Task The_editor_posts_the_sample_data_and_the_language_and_returns_the_url()
    {
        var harness = new PdfHarness();
        harness.Handler.ThenJson("""{"response":"https://us1.pdfgeneratorapi.com/editor/?token=abc"}""");

        var url = await harness.Provider().GetTemplateEditorUrlAsync("123456", Data(), "nl", CancellationToken.None);

        url.ShouldBe("https://us1.pdfgeneratorapi.com/editor/?token=abc");
        var request = harness.Handler.Requests.Single();
        request.Method.ShouldBe(HttpMethod.Post);
        request.Relative.ShouldBe("templates/123456/editor");
        request.Json.EnumerateObject().Select(p => p.Name).ShouldBe(["data", "language"], ignoreOrder: true);
        request.Json.GetProperty("data").GetProperty("Entity").GetProperty("EntityNumber").GetString().ShouldBe("F-2026-0001");
        request.Json.GetProperty("language").GetString().ShouldBe("nl");
    }

    [Fact]
    public async Task The_editor_without_sample_data_or_language_sends_neither_field()
    {
        var harness = new PdfHarness();
        harness.Handler.ThenJson("""{"response":"https://us1.pdfgeneratorapi.com/editor/?token=abc"}""");

        await harness.Provider().GetTemplateEditorUrlAsync("123456", null, " ", CancellationToken.None);

        harness.Handler.Requests.Single().Json.EnumerateObject().ShouldBeEmpty();
    }

    [Theory]
    [InlineData("""{"response":"not a url"}""")]
    [InlineData("""{"response":"javascript:alert(1)"}""")]
    [InlineData("""{"response":{}}""")]
    public async Task An_editor_answer_without_an_http_url_is_DocumentRenderingFailed(string answer)
    {
        var harness = new PdfHarness();
        harness.Handler.ThenJson(answer);

        await FailsWith(() => harness.Provider().GetTemplateEditorUrlAsync("1", null, null, CancellationToken.None), PlatformErrorCodes.DocumentRenderingFailed);
    }

    [Fact]
    public async Task A_template_id_is_escaped_in_the_path()
    {
        var harness = new PdfHarness();
        harness.Handler.ThenJson("""{"response":"https://us1.pdfgeneratorapi.com/editor/?token=abc"}""");

        await harness.Provider().GetTemplateEditorUrlAsync(" ../12 3 ", null, null, CancellationToken.None);

        harness.Handler.Requests.Single().Uri.AbsolutePath.ShouldBe("/api/v4/templates/..%2F12%203/editor", "the id cannot leave the templates path");
    }

    [Fact]
    public async Task A_copy_posts_the_new_name_and_returns_the_new_template_owned_by_the_workspace()
    {
        var harness = new PdfHarness();
        harness.Handler.ThenJson("""{"response":{"id":654321,"name":"Factuur NL (copy)","owner":true}}""");

        var copy = await harness.Provider().CopyTemplateAsync("123456", "Factuur NL (copy)", CancellationToken.None);

        var request = harness.Handler.Requests.Single();
        request.Method.ShouldBe(HttpMethod.Post);
        request.Relative.ShouldBe("templates/123456/copy");
        request.Json.EnumerateObject().Select(p => p.Name).ShouldBe(["name"]);
        request.Json.GetProperty("name").GetString().ShouldBe("Factuur NL (copy)");
        copy.Id.ShouldBe("654321");
        copy.Name.ShouldBe("Factuur NL (copy)");
        copy.OwnedByWorkspace.ShouldBeTrue();
    }

    [Fact]
    public async Task A_copy_answer_without_an_id_is_DocumentRenderingFailed()
    {
        var harness = new PdfHarness();
        harness.Handler.ThenJson("""{"response":{"name":"X"}}""");

        await FailsWith(() => harness.Provider().CopyTemplateAsync("1", "X", CancellationToken.None), PlatformErrorCodes.DocumentRenderingFailed);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public async Task A_single_template_with_owner_answers_from_that_flag_in_one_call(string owner, bool owned)
    {
        var harness = new PdfHarness();
        harness.Handler.ThenJson($$$"""{"response":{"id":123456,"name":"Factuur NL","owner":{{{owner}}}}}""");

        var template = await harness.Provider().GetTemplateAsync("123456", CancellationToken.None);

        template.ShouldNotBeNull();
        template.Id.ShouldBe("123456");
        template.Name.ShouldBe("Factuur NL");
        template.OwnedByWorkspace.ShouldBe(owned);
        var request = harness.Handler.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Get);
        request.Relative.ShouldBe("templates/123456");
        request.Body.ShouldBeNull();
    }

    [Fact]
    public async Task Without_owner_on_the_single_template_the_list_filtered_by_name_decides()
    {
        var harness = new PdfHarness();
        harness.Handler
            .ThenJson("""{"response":{"id":123456,"name":"Factuur NL"}}""")
            .ThenJson("""{"response":[{"id":999,"name":"Factuur NL","owner":false},{"id":123456,"name":"Factuur NL","owner":true}]}""");

        var template = await harness.Provider().GetTemplateAsync("123456", CancellationToken.None);

        template!.OwnedByWorkspace.ShouldBeTrue();
        harness.Handler.Requests.Count.ShouldBe(2);
        harness.Handler.Requests[1].Method.ShouldBe(HttpMethod.Get);
        harness.Handler.Requests[1].Relative.ShouldBe("templates?name=Factuur%20NL&per_page=100");
    }

    [Theory]
    [InlineData("""{"response":[{"id":123456,"name":"Factuur NL","owner":false}]}""", "listed as not owned")]
    [InlineData("""{"response":[{"id":123456,"name":"Factuur NL"}]}""", "listed without an owner flag: unknown")]
    [InlineData("""{"response":[{"id":999,"name":"Factuur NL","owner":true}]}""", "another template of that name is owned")]
    [InlineData("""{"response":[]}""", "not listed")]
    [InlineData("""{"response":{}}""", "an answer of another shape")]
    public async Task Unknown_ownership_counts_as_not_owned_so_an_organization_template_is_never_edited_in_place(string list, string reason)
    {
        var harness = new PdfHarness();
        harness.Handler
            .ThenJson("""{"response":{"id":123456,"name":"Factuur NL","owner":null}}""")
            .ThenJson(list);

        var template = await harness.Provider().GetTemplateAsync("123456", CancellationToken.None);

        template!.OwnedByWorkspace.ShouldBeFalse(reason);
    }

    [Fact]
    public async Task A_template_without_a_name_is_looked_up_among_the_workspaces_private_templates()
    {
        var harness = new PdfHarness();
        harness.Handler
            .ThenJson("""{"response":{"id":123456}}""")
            .ThenJson("""{"response":[{"id":123456}]}""");

        var template = await harness.Provider().GetTemplateAsync("123456", CancellationToken.None);

        harness.Handler.Requests[1].Relative.ShouldBe("templates?access=private&per_page=100");
        template!.OwnedByWorkspace.ShouldBeTrue("listed among the private templates of the workspace");
        template.Name.ShouldBe(string.Empty);
    }

    [Fact]
    public async Task An_unknown_template_is_null()
    {
        var harness = new PdfHarness();
        harness.Handler.ThenStatus(HttpStatusCode.NotFound, """{"message":"Template not found"}""");

        (await harness.Provider().GetTemplateAsync("404404", CancellationToken.None)).ShouldBeNull();
        harness.Handler.Requests.Count.ShouldBe(1, "a 404 is not retried");
    }

    [Fact]
    public async Task The_template_sample_data_is_the_answers_response_and_empty_or_missing_is_null()
    {
        var harness = new PdfHarness();
        harness.Handler
            .ThenJson("""{"response":{"Entity":{"InvoiceNumber":"F-1"}}}""")
            .ThenJson("""{"response":{}}""")
            .ThenJson("""{"response":[]}""")
            .ThenStatus(HttpStatusCode.NotFound);
        var provider = harness.Provider();

        var data = await provider.GetTemplateSampleDataAsync("123456", CancellationToken.None);
        data!.Value.GetProperty("Entity").GetProperty("InvoiceNumber").GetString().ShouldBe("F-1");
        (await provider.GetTemplateSampleDataAsync("123456", CancellationToken.None)).ShouldBeNull("an empty object");
        (await provider.GetTemplateSampleDataAsync("123456", CancellationToken.None)).ShouldBeNull("an empty array");
        (await provider.GetTemplateSampleDataAsync("123456", CancellationToken.None)).ShouldBeNull("not found");

        harness.Handler.Requests.ShouldAllBe(r => r.Method == HttpMethod.Get && r.Relative == "templates/123456/data");
    }

    [Fact]
    public async Task The_correlation_id_travels_on_the_request()
    {
        var harness = new PdfHarness();
        harness.CorrelationIds.Value = "corr-50-abc";
        harness.Handler.ThenJson(Generated());

        await harness.Provider().RenderAsync(Single(), CancellationToken.None);

        harness.Handler.Requests.Single().CorrelationId.ShouldBe("corr-50-abc");
    }

    // ── Retry, backoff, timeout, error mapping, cancellation (High) ────────────

    [Fact]
    public async Task Status_429_and_5xx_are_retried_after_3_6_12_and_24_seconds_with_a_fresh_token_per_attempt()
    {
        var harness = new PdfHarness();
        harness.Handler
            .ThenStatus(HttpStatusCode.ServiceUnavailable)
            .ThenStatus(HttpStatusCode.TooManyRequests)
            .ThenStatus(HttpStatusCode.InternalServerError)
            .ThenStatus(HttpStatusCode.BadGateway)
            .ThenJson(Generated());

        var document = await harness.Provider().RenderAsync(Single(), CancellationToken.None);

        document.Content.ShouldBe(Pdf);
        harness.Handler.Requests.Count.ShouldBe(5, "one attempt plus four retries");
        harness.Delay.Delays.ShouldBe([TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(24)]);

        var expiries = harness.Handler.Requests.Select(r => PdfHarness.Decode(r.Bearer!).Payload.GetProperty("exp").GetInt64()).ToList();
        var attemptTimes = new[] { 0, 3, 9, 21, 45 }.Select(s => PdfHarness.Start.AddSeconds(s + 60).ToUnixTimeSeconds());
        expiries.ShouldBe(attemptTimes, "each attempt signs a new token valid for 60 s from that attempt");
        harness.Handler.Requests.Select(r => r.Bearer).Distinct().Count().ShouldBe(5);
        harness.Handler.Requests.Select(r => r.Body).Distinct().Count().ShouldBe(1, "the same body every attempt");
    }

    [Fact]
    public async Task After_four_retries_the_last_5xx_is_DocumentRenderingFailed_with_its_status()
    {
        var harness = new PdfHarness();
        harness.Handler.Always(HttpStatusCode.ServiceUnavailable, """{"message":"Service temporarily unavailable"}""");

        var ex = await FailsWith(() => harness.Provider().RenderAsync(Single(), CancellationToken.None), PlatformErrorCodes.DocumentRenderingFailed);

        harness.Handler.Requests.Count.ShouldBe(5);
        harness.Delay.Delays.Count.ShouldBe(4);
        ex.Data["status"].ShouldBe(503);
        ex.Data["message"].ShouldBe("Service temporarily unavailable");
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    [InlineData(HttpStatusCode.Conflict)]
    public async Task Another_4xx_fails_at_once_with_the_providers_message(HttpStatusCode status)
    {
        var harness = new PdfHarness();
        harness.Handler.Always(status, """{"message":"Template data is invalid: Entity missing"}""");

        var ex = await FailsWith(() => harness.Provider().RenderAsync(Single(), CancellationToken.None), PlatformErrorCodes.DocumentRenderingFailed);

        harness.Handler.Requests.Count.ShouldBe(1, $"{(int)status} is not retried");
        harness.Delay.Delays.ShouldBeEmpty();
        ex.Data["message"].ShouldBe("Template data is invalid: Entity missing");
        ex.Data["status"].ShouldBe((int)status);
    }

    [Fact]
    public async Task The_error_field_is_read_when_there_is_no_message_and_a_bare_status_otherwise()
    {
        var harness = new PdfHarness();
        harness.Handler
            .ThenStatus(HttpStatusCode.BadRequest, """{"error":"Invalid output"}""")
            .ThenStatus(HttpStatusCode.BadRequest, "not json at all");
        var provider = harness.Provider();

        (await FailsWith(() => provider.RenderAsync(Single(), CancellationToken.None), PlatformErrorCodes.DocumentRenderingFailed))
            .Data["message"].ShouldBe("Invalid output");
        (await FailsWith(() => provider.RenderAsync(Single(), CancellationToken.None), PlatformErrorCodes.DocumentRenderingFailed))
            .Data["message"].ShouldBe("HTTP 400 Bad Request");
    }

    [Fact]
    public async Task A_long_provider_message_is_shortened_to_500_characters()
    {
        var harness = new PdfHarness();
        harness.Handler.Always(HttpStatusCode.BadRequest, JsonSerializer.Serialize(new { message = new string('x', 2000) }));

        var ex = await FailsWith(() => harness.Provider().RenderAsync(Single(), CancellationToken.None), PlatformErrorCodes.DocumentRenderingFailed);

        ((string)ex.Data["message"]!).ShouldBe(new string('x', 500) + "…");
    }

    [Fact]
    public async Task A_404_on_a_call_that_names_a_template_is_TemplateNotFound_without_retry()
    {
        var harness = new PdfHarness();
        harness.Handler.Always(HttpStatusCode.NotFound, """{"message":"Not found"}""");

        var ex = await FailsWith(() => harness.Provider().RenderAsync(Single("404404"), CancellationToken.None), PlatformErrorCodes.TemplateNotFound);

        ex.Data["templateId"].ShouldBe("404404");
        harness.Handler.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_timeout_fails_at_once_as_DocumentRenderingFailed_without_retry()
    {
        var harness = new PdfHarness { Timeout = TimeSpan.FromMilliseconds(100) };
        harness.Handler.Fallback = async (_, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new InvalidOperationException("unreachable");
        };

        var ex = await FailsWith(() => harness.Provider().RenderAsync(Single(), CancellationToken.None), PlatformErrorCodes.DocumentRenderingFailed);

        harness.Handler.Requests.Count.ShouldBe(1, "a timeout is not retried");
        harness.Delay.Delays.ShouldBeEmpty();
        ((string)ex.Data["message"]!).ShouldStartWith("No answer within");
    }

    [Fact]
    public async Task An_unreachable_host_fails_at_once_as_DocumentRenderingFailed()
    {
        var harness = new PdfHarness();
        harness.Handler.Fallback = (_, _) => throw new HttpRequestException(HttpRequestError.NameResolutionError, "no such host us1.pdfgeneratorapi.com");

        var ex = await FailsWith(() => harness.Provider().RenderAsync(Single(), CancellationToken.None), PlatformErrorCodes.DocumentRenderingFailed);

        harness.Handler.Requests.Count.ShouldBe(1);
        ex.Data["message"].ShouldBe("The service could not be reached.");
    }

    [Fact]
    public async Task A_cancelled_caller_stops_the_retries_with_OperationCanceledException()
    {
        var harness = new PdfHarness();
        harness.Handler.Always(HttpStatusCode.ServiceUnavailable);
        using var cancellation = new CancellationTokenSource();
        harness.Delay.OnDelay = cancellation.Cancel;

        await Should.ThrowAsync<OperationCanceledException>(() => harness.Provider().RenderAsync(Single(), cancellation.Token));

        harness.Handler.Requests.Count.ShouldBe(1, "no attempt after the caller cancelled during the backoff");
    }

    [Fact]
    public async Task A_caller_cancelled_before_the_call_gets_OperationCanceledException_not_a_provider_failure()
    {
        var harness = new PdfHarness();
        harness.Handler.Fallback = async (_, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            await Task.Yield();
            return ScriptedPdfHandler.Json(Generated());
        };
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var ex = await Should.ThrowAsync<Exception>(() => harness.Provider().RenderAsync(Single(), cancellation.Token));

        ex.ShouldBeAssignableTo<OperationCanceledException>();
        harness.Delay.Delays.ShouldBeEmpty();
    }

    // ── Not configured (High) ──────────────────────────────────────────────────

    public static TheoryData<string?, string?> MissingCredentials => new()
    {
        { null, PdfHarness.Secret },
        { PdfHarness.Key, null },
        { "  ", PdfHarness.Secret },
        { PdfHarness.Key, "" },
        { null, null },
    };

    [Theory]
    [MemberData(nameof(MissingCredentials))]
    public async Task Without_key_or_secret_the_status_is_not_configured_and_no_call_leaves_the_process(string? key, string? secret)
    {
        var harness = new PdfHarness(o =>
        {
            o.Key = key;
            o.Secret = secret;
        });
        var provider = harness.Provider();

        var status = await provider.GetStatusAsync(CancellationToken.None);
        status.IsConfigured.ShouldBeFalse();
        status.IsReachable.ShouldBeNull();
        status.Message.ShouldNotBeNullOrWhiteSpace();
        status.Message.ShouldNotContain(PdfHarness.Secret);
        status.Message.ShouldNotContain(PdfHarness.Key);

        await FailsWith(() => provider.RenderAsync(Single(), CancellationToken.None), PlatformErrorCodes.RenderingNotConfigured);
        await FailsWith(() => provider.GetTemplateEditorUrlAsync("1", null, null, CancellationToken.None), PlatformErrorCodes.RenderingNotConfigured);
        await FailsWith(() => provider.CopyTemplateAsync("1", "X", CancellationToken.None), PlatformErrorCodes.RenderingNotConfigured);
        await FailsWith(() => provider.GetTemplateAsync("1", CancellationToken.None), PlatformErrorCodes.RenderingNotConfigured);
        await FailsWith(() => provider.GetTemplateSampleDataAsync("1", CancellationToken.None), PlatformErrorCodes.RenderingNotConfigured);

        harness.Handler.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Configured_the_status_says_so_without_a_call_and_without_a_credential()
    {
        var harness = new PdfHarness();
        var provider = harness.Provider();

        var status = await provider.GetStatusAsync(CancellationToken.None);

        status.IsConfigured.ShouldBeTrue();
        status.Message.ShouldBeNull();
        harness.Handler.Requests.ShouldBeEmpty("the status never calls the provider");
        provider.Provider.ShouldBe(OpenTms.Platform.ReportDefinitions.ReportRenderingProvider.PdfGeneratorApi);
        provider.SupportsTemplateEditor.ShouldBeTrue();
        provider.SupportsTemplateCopy.ShouldBeTrue();
    }

    // ── Secrets and payload never logged (Critical) ────────────────────────────

    [Fact]
    public async Task No_log_line_holds_the_key_the_secret_a_token_the_payload_or_the_document()
    {
        var harness = new PdfHarness();
        var tenant = Guid.NewGuid();
        harness.Handler
            .ThenStatus(HttpStatusCode.TooManyRequests, $$"""{"message":"slow down {{PayloadMarker}}"}""")
            .ThenJson(Generated())
            .ThenStatus(HttpStatusCode.BadRequest, $$"""{"message":"bad data {{PayloadMarker}} {{PdfHarness.Secret}}"}""")
            .ThenJson("""{"response":"https://us1.pdfgeneratorapi.com/editor/?token=QQ-EDITOR-TOKEN"}""");
        harness.Timeout = TimeSpan.FromMilliseconds(100);
        var provider = harness.Provider();

        using (harness.CurrentTenant.Change(tenant))
        {
            await provider.RenderAsync(Single(isTest: true), CancellationToken.None);
            await Should.ThrowAsync<BusinessException>(() => provider.RenderAsync(Single(), CancellationToken.None));
            await provider.GetTemplateEditorUrlAsync("123456", Data(), "nl", CancellationToken.None);
            harness.Handler.Fallback = async (_, ct) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                throw new InvalidOperationException("unreachable");
            };
            await Should.ThrowAsync<BusinessException>(() => provider.CopyTemplateAsync("123456", "Name " + PayloadMarker, CancellationToken.None));
            harness.Handler.Fallback = (_, _) => throw new HttpRequestException("connection refused " + PdfHarness.Secret);
            await Should.ThrowAsync<BusinessException>(() => provider.GetTemplateAsync("123456", CancellationToken.None));
        }

        var entries = harness.Log.Entries;
        entries.ShouldNotBeEmpty("the retries, the failures, the timeout and the unreachable host are logged");
        var tokens = harness.Handler.Requests.Select(r => r.Bearer!).ToList();
        var signatures = tokens.Select(t => t.Split('.')[2]).ToList();
        foreach (var entry in entries)
        {
            var text = entry.Everything;
            text.ShouldNotContain(PdfHarness.Key, Case.Sensitive, entry.Message);
            text.ShouldNotContain(PdfHarness.Secret, Case.Sensitive, entry.Message);
            text.ShouldNotContain(PayloadMarker, Case.Sensitive, entry.Message);
            text.ShouldNotContain(PdfBase64, Case.Sensitive, entry.Message);
            text.ShouldNotContain("QQ-EDITOR-TOKEN", Case.Sensitive, entry.Message);
            text.ShouldNotContain("Bearer", Case.Insensitive, entry.Message);
            foreach (var signature in signatures)
            {
                text.ShouldNotContain(signature, Case.Sensitive, entry.Message);
            }
        }

        /* What the logs do carry: the operation, the template id and the tenant id (design 55, "Privacy note"). */
        entries.Where(e => e.Level >= LogLevel.Warning).ShouldAllBe(e => e.Message.Contains(tenant.ToString()) && e.Message.Contains("123456"));
    }

    [Fact]
    public async Task The_exceptions_carry_no_credential_or_payload()
    {
        var harness = new PdfHarness();
        harness.Handler.Always(HttpStatusCode.BadRequest, """{"message":"invalid template"}""");

        var ex = await Should.ThrowAsync<BusinessException>(() => harness.Provider().RenderAsync(Single(), CancellationToken.None));

        var all = ex.ToString() + string.Join("|", ex.Data.Values.Cast<object?>());
        all.ShouldNotContain(PdfHarness.Key);
        all.ShouldNotContain(PdfHarness.Secret);
        all.ShouldNotContain(PayloadMarker);
    }
}
