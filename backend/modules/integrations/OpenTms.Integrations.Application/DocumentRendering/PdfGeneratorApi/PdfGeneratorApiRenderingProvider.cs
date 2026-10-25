using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OpenTms.Platform;
using OpenTms.Platform.DocumentRendering;
using OpenTms.Platform.ReportDefinitions;
using Volo.Abp;

namespace OpenTms.Integrations.DocumentRendering.PdfGeneratorApi;

/// <summary>
/// The pdfgeneratorapi.com adapter behind Platform's rendering abstraction (design 55), API v4 throughout:
/// <c>POST documents/generate</c> and <c>documents/generate/batch</c> (<c>output: base64</c>, <c>testing</c> for test
/// renders), <c>POST templates/{id}/editor</c>, <c>POST templates/{id}/copy</c>, <c>GET templates/{id}</c>,
/// <c>GET templates/{id}/data</c>. The workspace follows the current tenant (<see cref="PdfGeneratorApiClient"/>).
/// A render takes the request's <see cref="RenderRequest.CallMode"/>; the template operations are always interactive
/// (a user waits on them): one attempt, no retries.
/// </summary>
public class PdfGeneratorApiRenderingProvider : IReportRenderingProvider
{
    private const int TemplateListPageSize = 100;

    private readonly PdfGeneratorApiClient _client;

    public PdfGeneratorApiRenderingProvider(PdfGeneratorApiClient client)
    {
        _client = client;
    }

    public ReportRenderingProvider Provider => ReportRenderingProvider.PdfGeneratorApi;

    public bool SupportsTemplateEditor => true;

    public bool SupportsTemplateCopy => true;

    public Task<ReportRenderingProviderStatus> GetStatusAsync(CancellationToken ct)
        => Task.FromResult(new ReportRenderingProviderStatus
        {
            IsConfigured = _client.IsConfigured,
            Message = _client.IsConfigured ? null : $"{PdfGeneratorApiOptions.SectionName}:Key and :Secret are not configured."
        });

    public async Task<RenderedDocument> RenderAsync(RenderRequest request, CancellationToken ct)
    {
        Check.NotNull(request, nameof(request));
        if (request.Templates.Count == 0)
        {
            throw new ArgumentException("At least one template is needed.", nameof(request));
        }

        var single = request.Templates.Count == 1;
        var body = new Dictionary<string, object?>
        {
            ["template"] = single ? TemplateParameter(request.Templates[0]) : request.Templates.Select(TemplateParameter).ToList(),
            ["format"] = FormatOf(request.Format),
            ["output"] = "base64",
            ["name"] = request.FileName,
            ["testing"] = request.IsTest
        };

        var root = await _client.SendAsync(
            HttpMethod.Post, single ? "documents/generate" : "documents/generate/batch", body, single ? "generate" : "generate-batch",
            request.Templates[0].TemplateId, request.CallMode, ct);

        var base64 = Text(root, "response");
        if (string.IsNullOrEmpty(base64))
        {
            throw PdfGeneratorApiClient.Failed("The answer holds no document.");
        }

        byte[] content;
        try
        {
            content = Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            throw PdfGeneratorApiClient.Failed("The answer holds no readable document.");
        }

        var meta = Object(root, "meta");
        return new RenderedDocument
        {
            Content = content,
            FileName = request.FileName,
            ContentType = Text(meta, "content_type") is { Length: > 0 } contentType ? contentType : ContentTypeOf(request.Format)
        };
    }

    public async Task<string> GetTemplateEditorUrlAsync(string templateId, JsonElement? sampleData, string? language, CancellationToken ct)
    {
        /* Only the fields that have a value: dictionary entries are serialized even when null. */
        var body = new Dictionary<string, object?>();
        if (sampleData is { } data)
        {
            body["data"] = data;
        }

        if (!string.IsNullOrWhiteSpace(language))
        {
            body["language"] = language;
        }

        var root = await _client.SendAsync(HttpMethod.Post, $"templates/{Escape(templateId)}/editor", body, "editor", templateId, RenderCallMode.Interactive, ct);
        var url = Text(root, "response");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw PdfGeneratorApiClient.Failed("The answer holds no editor URL.");
        }

        return uri.AbsoluteUri;
    }

    public async Task<ProviderTemplate> CopyTemplateAsync(string templateId, string newName, CancellationToken ct)
    {
        var body = new Dictionary<string, object?> { ["name"] = newName };
        var root = await _client.SendAsync(HttpMethod.Post, $"templates/{Escape(templateId)}/copy", body, "copy", templateId, RenderCallMode.Interactive, ct);
        var template = Object(root, "response");
        var id = Id(template);
        if (string.IsNullOrEmpty(id))
        {
            throw PdfGeneratorApiClient.Failed("The answer holds no template id.");
        }

        /* A copy always lands in the current workspace. */
        return new ProviderTemplate { Id = id, Name = Text(template, "name") ?? newName, OwnedByWorkspace = true };
    }

    public async Task<ProviderTemplate?> GetTemplateAsync(string templateId, CancellationToken ct)
    {
        var root = await _client.SendAsync(HttpMethod.Get, $"templates/{Escape(templateId)}", null, "get-template", templateId, RenderCallMode.Interactive, ct, allowNotFound: true);
        if (root == null)
        {
            return null;
        }

        var template = Object(root, "response");
        if (template == null)
        {
            return null;
        }

        var name = Text(template, "name") ?? string.Empty;
        /* API v4 documents "owner" on the template list's items, not on the single template: read it here when the
         * answer has it, else from the list. Unknown means not owned, so a standard template is never edited in place. */
        var owned = Bool(template, "owner") ?? await OwnedByListAsync(templateId, name, ct);
        return new ProviderTemplate { Id = Id(template) ?? templateId, Name = name, OwnedByWorkspace = owned };
    }

    public async Task<JsonElement?> GetTemplateSampleDataAsync(string templateId, CancellationToken ct)
    {
        var root = await _client.SendAsync(HttpMethod.Get, $"templates/{Escape(templateId)}/data", null, "template-data", templateId, RenderCallMode.Interactive, ct, allowNotFound: true);
        if (root is not { ValueKind: JsonValueKind.Object } answer || !answer.TryGetProperty("response", out var data))
        {
            return null;
        }

        return data.ValueKind switch
        {
            JsonValueKind.Object when data.EnumerateObject().Any() => data,
            JsonValueKind.Array when data.GetArrayLength() > 0 => data,
            _ => null
        };
    }

    private async Task<bool> OwnedByListAsync(string templateId, string name, CancellationToken ct)
    {
        var query = string.IsNullOrWhiteSpace(name)
            ? $"templates?access=private&per_page={TemplateListPageSize}"
            : $"templates?name={Uri.EscapeDataString(name)}&per_page={TemplateListPageSize}";
        var root = await _client.SendAsync(HttpMethod.Get, query, null, "list-templates", templateId, RenderCallMode.Interactive, ct);
        if (root is not { ValueKind: JsonValueKind.Object } answer
            || !answer.TryGetProperty("response", out var items)
            || items.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var item in items.EnumerateArray())
        {
            if (Id(item) == templateId)
            {
                /* Without a name the list was filtered to the workspace's private templates: listed means owned. */
                return Bool(item, "owner") ?? string.IsNullOrWhiteSpace(name);
            }
        }

        return false;
    }

    /// <summary>API v4 template ids are integers; a public template id is a string.</summary>
    private static object TemplateParameter(RenderTemplate template) => new Dictionary<string, object?>
    {
        ["id"] = long.TryParse(template.TemplateId, out var numeric) ? numeric : template.TemplateId,
        ["data"] = template.Data
    };

    /// <summary>API v4 lists pdf and html; zip and xlsx are passed on as OpenTMS did and refused by the provider if unsupported.</summary>
    private static string FormatOf(ReportRenderFormat format) => format switch
    {
        ReportRenderFormat.Html => "html",
        ReportRenderFormat.Zip => "zip",
        ReportRenderFormat.Xlsx => "xlsx",
        _ => "pdf"
    };

    private static string ContentTypeOf(ReportRenderFormat format) => format switch
    {
        ReportRenderFormat.Html => "text/html",
        ReportRenderFormat.Zip => "application/zip",
        ReportRenderFormat.Xlsx => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        _ => "application/pdf"
    };

    private static string Escape(string templateId) => Uri.EscapeDataString(templateId.Trim());

    private static JsonElement? Object(JsonElement? element, string property)
        => element is { ValueKind: JsonValueKind.Object } e && e.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Object
            ? value
            : null;

    private static string? Text(JsonElement? element, string property)
        => element is { ValueKind: JsonValueKind.Object } e && e.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool? Bool(JsonElement? element, string property)
        => element is { ValueKind: JsonValueKind.Object } e && e.TryGetProperty(property, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null
            }
            : null;

    private static string? Id(JsonElement? element)
        => element is { ValueKind: JsonValueKind.Object } e && e.TryGetProperty("id", out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.Number => value.GetRawText(),
                JsonValueKind.String => value.GetString(),
                _ => null
            }
            : null;
}
