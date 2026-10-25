using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTms.Platform.Features;
using OpenTms.Platform.ReportDefinitions;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Features;
using Volo.Abp.MultiTenancy;

namespace OpenTms.Platform.DocumentRendering;

/// <summary>
/// The published renderer (design 55). Reads the definition through the tenant filter, so another tenant's definition
/// is not found before any provider call; refuses an archived one; checks <see cref="PlatformFeatures.DocumentRendering"/>;
/// picks the provider by the definition's <c>Provider</c>; composes the file name; calls the provider. The report
/// definition service uses the same steps for its test render. Logs ids only, never the payload or the document.
/// </summary>
public class DocumentRenderer : IDocumentRenderer, ITransientDependency
{
    private readonly IRepository<ReportDefinition, Guid> _definitions;
    private readonly IEnumerable<IReportRenderingProvider> _providers;
    private readonly IFeatureChecker _featureChecker;
    private readonly ICurrentTenant _currentTenant;

    public ILogger<DocumentRenderer> Logger { get; set; } = NullLogger<DocumentRenderer>.Instance;

    public DocumentRenderer(
        IRepository<ReportDefinition, Guid> definitions,
        IEnumerable<IReportRenderingProvider> providers,
        IFeatureChecker featureChecker,
        ICurrentTenant currentTenant)
    {
        _definitions = definitions;
        _providers = providers;
        _featureChecker = featureChecker;
        _currentTenant = currentTenant;
    }

    public Task<RenderedDocument> RenderAsync(Guid reportDefinitionId, JsonElement data, ReportRenderFormat format = ReportRenderFormat.Pdf, CancellationToken cancellationToken = default)
        => RenderAsync(reportDefinitionId, [data], format, cancellationToken);

    public async Task<RenderedDocument> RenderAsync(Guid reportDefinitionId, IReadOnlyList<JsonElement> data, ReportRenderFormat format = ReportRenderFormat.Pdf, CancellationToken cancellationToken = default)
    {
        Check.NotNull(data, nameof(data));
        if (data.Count == 0)
        {
            throw new ArgumentException("At least one record is needed.", nameof(data));
        }

        await _featureChecker.CheckEnabledAsync(PlatformFeatures.DocumentRendering);
        var definition = await _definitions.FindAsync(reportDefinitionId, cancellationToken: cancellationToken)
                         ?? throw new EntityNotFoundException(typeof(ReportDefinition), reportDefinitionId);
        /* Real renders run in jobs (design 55): nobody waits on them, so they keep the retry schedule. */
        return await RenderDefinitionAsync(definition, data, format, isTest: false, RenderCallMode.Background, cancellationToken);
    }

    /// <summary>The provider implementation for a definition's provider, or null when none is registered.</summary>
    public IReportRenderingProvider? FindProvider(ReportRenderingProvider provider) => _providers.FirstOrDefault(p => p.Provider == provider);

    /// <summary>The provider implementation, refused with RenderingProviderUnavailable when none is registered.</summary>
    public IReportRenderingProvider GetProvider(ReportRenderingProvider provider)
        => FindProvider(provider)
           ?? throw new BusinessException(PlatformErrorCodes.RenderingProviderUnavailable).WithData("provider", provider.ToString());

    /// <summary>The provider, refused with RenderingNotConfigured when the deployment lacks its configuration.</summary>
    public async Task<IReportRenderingProvider> GetConfiguredProviderAsync(ReportRenderingProvider provider, CancellationToken cancellationToken)
    {
        var implementation = GetProvider(provider);
        var status = await implementation.GetStatusAsync(cancellationToken);
        if (!status.IsConfigured)
        {
            throw new BusinessException(PlatformErrorCodes.RenderingNotConfigured).WithData("provider", provider.ToString());
        }

        return implementation;
    }

    /// <summary>
    /// The shared render steps after the definition is read in the tenant and the feature is checked: refuse an
    /// archived definition, compose the file name from the first record, call the provider. A file-name pattern that
    /// fails to render (a row saved before the pattern was checked against <c>{{ number }}</c>) gives a random name and a
    /// warning naming the definition.
    /// </summary>
    /// <param name="callMode">Interactive when a user waits for the document (the test render), Background for jobs.</param>
    public async Task<RenderedDocument> RenderDefinitionAsync(
        ReportDefinition definition,
        IReadOnlyList<JsonElement> data,
        ReportRenderFormat format,
        bool isTest,
        RenderCallMode callMode,
        CancellationToken cancellationToken)
    {
        if (definition.IsArchived)
        {
            throw new BusinessException(PlatformErrorCodes.ReportDefinitionArchived).WithData("id", definition.Id);
        }

        if (!Enum.IsDefined(format))
        {
            throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown render format.");
        }

        var provider = await GetConfiguredProviderAsync(definition.Provider, cancellationToken);
        var fileName = ReportFileNameComposer.Compose(definition.FileNamePattern, data[0], format, out var patternFailed);
        if (patternFailed)
        {
            Logger.LogWarning(
                "The file-name pattern of report definition {ReportDefinitionId} for tenant {TenantId} failed to render; the document gets a random file name",
                definition.Id, _currentTenant.Id);
        }

        var request = new RenderRequest
        {
            Templates = data.Select(d => new RenderTemplate { TemplateId = definition.ProviderTemplateId, Data = d }).ToList(),
            Format = format,
            FileName = fileName,
            IsTest = isTest,
            CallMode = callMode
        };

        Logger.LogInformation(
            "Rendering report definition {ReportDefinitionId} for tenant {TenantId}: {RecordCount} record(s), format {Format}, test {IsTest}, call mode {CallMode}",
            definition.Id, _currentTenant.Id, data.Count, format, isTest, callMode);
        return await provider.RenderAsync(request, cancellationToken);
    }
}
