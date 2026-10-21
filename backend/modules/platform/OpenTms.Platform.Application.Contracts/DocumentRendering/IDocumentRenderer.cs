using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OpenTms.Platform.DocumentRendering;

/// <summary>
/// Renders a document from a report definition (design 50), published for the modules that print records (Orders,
/// Planning, Financial). Not an application service: it has no HTTP endpoint. It reads the definition in the current
/// tenant (another tenant's is not found), refuses an archived one, checks the feature
/// <see cref="Features.PlatformFeatures.DocumentRendering"/>, picks the provider, composes the file name from the
/// definition's pattern and returns the bytes; the caller stores them (AD-09).
/// </summary>
public interface IDocumentRenderer
{
    /// <param name="data">The payload of the definition's purpose, in the shape of the report data catalogue.</param>
    Task<RenderedDocument> RenderAsync(Guid reportDefinitionId, JsonElement data, ReportRenderFormat format = ReportRenderFormat.Pdf, CancellationToken cancellationToken = default);

    /// <summary>One document of several records (one template per record); the file name follows the first record.</summary>
    Task<RenderedDocument> RenderAsync(Guid reportDefinitionId, IReadOnlyList<JsonElement> data, ReportRenderFormat format = ReportRenderFormat.Pdf, CancellationToken cancellationToken = default);
}
