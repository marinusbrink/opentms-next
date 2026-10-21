using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OpenTms.Platform.ReportDefinitions;

namespace OpenTms.Platform.DocumentRendering;

/// <summary>
/// A rendering provider behind report definitions (design 50, "Provider abstraction"). Platform finds the
/// implementations through dependency injection: the pdfgeneratorapi.com adapter lives in Integrations, a later
/// in-house renderer implements the same interface. The provider chooses the workspace from the current tenant; a
/// caller never passes one. Implementations never log or return their credentials or the payload.
/// </summary>
public interface IReportRenderingProvider
{
    /// <summary>The report definition's <c>Provider</c> value this implementation serves.</summary>
    ReportRenderingProvider Provider { get; }

    /// <summary>Whether the deployment holds the provider's configuration; never calls the provider.</summary>
    Task<ReportRenderingProviderStatus> GetStatusAsync(CancellationToken ct);

    /// <summary>Renders one document from one or more templates (one template per record in a batch).</summary>
    Task<RenderedDocument> RenderAsync(RenderRequest request, CancellationToken ct);

    bool SupportsTemplateEditor { get; }

    /// <summary>A one-time URL of the provider's template editor for the current workspace.</summary>
    Task<string> GetTemplateEditorUrlAsync(string templateId, JsonElement? sampleData, string? language, CancellationToken ct);

    bool SupportsTemplateCopy { get; }

    /// <summary>Copies a template into the current workspace under a new name.</summary>
    Task<ProviderTemplate> CopyTemplateAsync(string templateId, string newName, CancellationToken ct);

    /// <summary>The template's name and whether the current workspace owns it; null when the provider does not know it.</summary>
    Task<ProviderTemplate?> GetTemplateAsync(string templateId, CancellationToken ct);

    /// <summary>The template's own sample data; null when it has none.</summary>
    Task<JsonElement?> GetTemplateSampleDataAsync(string templateId, CancellationToken ct);
}
