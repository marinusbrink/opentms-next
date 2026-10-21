using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTms.Platform.DocumentRendering;
using OpenTms.Platform.Features;
using OpenTms.Platform.Grid;
using OpenTms.Platform.Permissions;
using Volo.Abp;
using Volo.Abp.Content;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Features;
using Volo.Abp.Localization;
using Volo.Abp.Threading;
using Volo.Abp.Uow;

namespace OpenTms.Platform.ReportDefinitions;

/* Pickers need only the app feature; the list's own screen needs its group and node (PO 2026-10-14, design 49). */
[RequiresFeature(AppFeatures.MasterData.Default)]
public class ReportDefinitionAppService
    : PlatformCrudAppServiceBase<ReportDefinition, ReportDefinitionRowDto, ReportDefinitionDto, ReportDefinitionCreateDto, ReportDefinitionUpdateDto, ReportDefinitionListRequestDto>,
      IReportDefinitionAppService
{
    /// <summary>How many "(copy n)" names are tried before the copy is refused as a duplicate name.</summary>
    private const int MaxCopyNameAttempts = 100;

    private readonly IReadOnlyCollection<string> _cultures;
    private readonly DocumentRenderer _renderer;

    public ReportDefinitionAppService(IRepository<ReportDefinition, Guid> repository, IOptions<AbpLocalizationOptions> localization, DocumentRenderer renderer)
        : base(repository)
    {
        _cultures = localization.Value.Languages.Select(l => l.CultureName.ToLowerInvariant()).ToList();
        _renderer = renderer;
    }

    [RequiresFeature(AppFeatures.MasterData.Platform.Default, AppFeatures.MasterData.Platform.ReportDefinitions, RequiresAll = true)]
    [Authorize(PlatformPermissions.MasterData.ReportDefinitions.Default)]
    public Task<GridResponse<ReportDefinitionRowDto>> GetListAsync(ReportDefinitionListRequestDto input) => GetListInternalAsync(input);

    [RequiresFeature(AppFeatures.MasterData.Platform.Default, AppFeatures.MasterData.Platform.ReportDefinitions, RequiresAll = true)]
    [Authorize(PlatformPermissions.MasterData.ReportDefinitions.Default)]
    public Task<ReportDefinitionDto> GetAsync(Guid id) => GetInternalAsync(id);

    [Authorize(PlatformPermissions.MasterData.ReportDefinitions.Default)]
    public Task<List<PlatformMasterDataLookupDto>> GetLookupAsync(string? term = null, int take = PlatformMasterDataConsts.MaxLookupTake) => GetLookupInternalAsync(term, take);

    [RequiresFeature(AppFeatures.MasterData.Platform.Default, AppFeatures.MasterData.Platform.ReportDefinitions, RequiresAll = true)]
    [Authorize(PlatformPermissions.MasterData.ReportDefinitions.Create)]
    public Task<ReportDefinitionDto> CreateAsync(ReportDefinitionCreateDto input) => CreateInternalAsync(input);

    [RequiresFeature(AppFeatures.MasterData.Platform.Default, AppFeatures.MasterData.Platform.ReportDefinitions, RequiresAll = true)]
    [Authorize(PlatformPermissions.MasterData.ReportDefinitions.Update)]
    public Task<ReportDefinitionDto> UpdateAsync(Guid id, ReportDefinitionUpdateDto input) => UpdateInternalAsync(id, input);

    [RequiresFeature(AppFeatures.MasterData.Platform.Default, AppFeatures.MasterData.Platform.ReportDefinitions, RequiresAll = true)]
    [Authorize(PlatformPermissions.MasterData.ReportDefinitions.Delete)]
    public Task DeleteAsync(Guid id) => DeleteInternalAsync(id);

    [RequiresFeature(AppFeatures.MasterData.Platform.Default, AppFeatures.MasterData.Platform.ReportDefinitions, RequiresAll = true)]
    [Authorize(PlatformPermissions.MasterData.ReportDefinitions.Delete)]
    public Task<PlatformMasterDataBulkDeleteResultDto> BulkDeleteAsync(ReportDefinitionBulkDeleteRequestDto input)
        => BulkDeleteInternalAsync(input.Selection, q => ApplyFilters(q, input.Purpose, input.IncludeArchived));

    /// <summary>Hides the definition from pickers and automatic selection; twice is one effect.</summary>
    [RequiresFeature(AppFeatures.MasterData.Platform.Default, AppFeatures.MasterData.Platform.ReportDefinitions, RequiresAll = true)]
    [Authorize(PlatformPermissions.MasterData.ReportDefinitions.Update)]
    public async Task ArchiveAsync(Guid id)
    {
        var definition = await Repository.GetAsync(id);
        definition.Archive();
        await Repository.UpdateAsync(definition, autoSave: true);
    }

    [RequiresFeature(AppFeatures.MasterData.Platform.Default, AppFeatures.MasterData.Platform.ReportDefinitions, RequiresAll = true)]
    [Authorize(PlatformPermissions.MasterData.ReportDefinitions.Update)]
    public async Task UnarchiveAsync(Guid id)
    {
        var definition = await Repository.GetAsync(id);
        definition.Unarchive();
        await Repository.UpdateAsync(definition, autoSave: true);
    }

    // ── rendering (design 50) ─────────────────────────────────────────────────

    /// <summary>
    /// Drives the template commands' enabled state and reasons. Needs only the screen's feature chain: with
    /// Platform.DocumentRendering off it answers FeatureEnabled = false without calling the provider, so the editor
    /// can show that reason. The template is looked up only when the provider is configured and the feature is on.
    /// </summary>
    [RequiresFeature(AppFeatures.MasterData.Platform.Default, AppFeatures.MasterData.Platform.ReportDefinitions, RequiresAll = true)]
    [Authorize(PlatformPermissions.MasterData.ReportDefinitions.Default)]
    public async Task<ReportRenderingStatusDto> GetRenderingStatusAsync(Guid id)
    {
        var definition = await Repository.GetAsync(id);
        var status = new ReportRenderingStatusDto
        {
            FeatureEnabled = await FeatureChecker.IsEnabledAsync(PlatformFeatures.DocumentRendering)
        };

        var provider = _renderer.FindProvider(definition.Provider);
        if (provider == null)
        {
            return status;
        }

        var ct = RequestCancellation;
        status.ProviderAvailable = true;
        status.SupportsTemplateEditor = provider.SupportsTemplateEditor;
        status.SupportsTemplateCopy = provider.SupportsTemplateCopy;
        status.ProviderConfigured = (await provider.GetStatusAsync(ct)).IsConfigured;
        if (!status.ProviderConfigured || !status.FeatureEnabled)
        {
            return status;
        }

        try
        {
            var template = await provider.GetTemplateAsync(definition.ProviderTemplateId, ct);
            status.TemplateFound = template != null;
            status.TemplateName = template?.Name;
            status.TemplateOwnedByWorkspace = template?.OwnedByWorkspace;
        }
        catch (BusinessException e) when (e.Code == PlatformErrorCodes.DocumentRenderingFailed)
        {
            /* The status is advisory: a provider failure leaves the template unknown and says why. */
            Logger.LogWarning("Rendering status of report definition {ReportDefinitionId} for tenant {TenantId}: the provider failed to answer", definition.Id, CurrentTenant.Id);
            status.Message = L[PlatformErrorCodes.DocumentRenderingFailed].Value.Replace("{message}", e.Data["message"]?.ToString() ?? string.Empty);
        }

        return status;
    }

    /// <summary>
    /// The provider's editor for a template the tenant's workspace owns, with the template's sample data (else the
    /// catalogue sample) and the definition's language. FiLogic's standard templates are refused (TemplateNotOwned).
    /// No database transaction stays open during the provider call.
    /// </summary>
    [RequiresFeature(AppFeatures.MasterData.Platform.Default, AppFeatures.MasterData.Platform.ReportDefinitions, RequiresAll = true)]
    [Authorize(PlatformPermissions.MasterData.ReportDefinitions.EditTemplate)]
    [UnitOfWork(isTransactional: false)]
    public async Task<ReportTemplateEditorDto> OpenTemplateEditorAsync(Guid id)
    {
        await EnsureDocumentRenderingAsync();
        var definition = await Repository.GetAsync(id);
        EnsureNotArchived(definition);
        var ct = RequestCancellation;
        var provider = await _renderer.GetConfiguredProviderAsync(definition.Provider, ct);
        if (!provider.SupportsTemplateEditor)
        {
            throw new BusinessException(PlatformErrorCodes.RenderingProviderUnavailable).WithData("provider", definition.Provider.ToString());
        }

        var template = await provider.GetTemplateAsync(definition.ProviderTemplateId, ct)
                       ?? throw new BusinessException(PlatformErrorCodes.TemplateNotFound).WithData("templateId", definition.ProviderTemplateId);
        if (!template.OwnedByWorkspace)
        {
            throw new BusinessException(PlatformErrorCodes.TemplateNotOwned).WithData("templateId", definition.ProviderTemplateId);
        }

        var sample = await SampleDataAsync(provider, definition, ct);
        var url = await provider.GetTemplateEditorUrlAsync(definition.ProviderTemplateId, sample, definition.Culture, ct);
        Logger.LogInformation("Template editor opened for report definition {ReportDefinitionId} of tenant {TenantId}", definition.Id, CurrentTenant.Id);
        return new ReportTemplateEditorDto { Url = url };
    }

    /// <summary>
    /// Copies the template into the tenant's workspace as "&lt;name&gt; (copy)" and creates a report definition with the
    /// same purpose, language, provider and file-name pattern pointing at the copy: not default, not archived.
    /// </summary>
    [RequiresFeature(AppFeatures.MasterData.Platform.Default, AppFeatures.MasterData.Platform.ReportDefinitions, RequiresAll = true)]
    [Authorize(PlatformPermissions.MasterData.ReportDefinitions.EditTemplate)]
    [UnitOfWork(isTransactional: false)]
    public async Task<ReportDefinitionDto> CopyTemplateAsync(Guid id)
    {
        await EnsureDocumentRenderingAsync();
        /* The copy creates a definition: Create as well (one [Authorize] per method, as everywhere in the module). */
        await AuthorizationService.CheckAsync(PlatformPermissions.MasterData.ReportDefinitions.Create);
        var source = await Repository.GetAsync(id);
        EnsureNotArchived(source);
        var ct = RequestCancellation;
        var provider = await _renderer.GetConfiguredProviderAsync(source.Provider, ct);
        if (!provider.SupportsTemplateCopy)
        {
            throw new BusinessException(PlatformErrorCodes.RenderingProviderUnavailable).WithData("provider", source.Provider.ToString());
        }

        var name = await UniqueCopyNameAsync(source.Name);
        var copied = await provider.CopyTemplateAsync(source.ProviderTemplateId, name, ct);

        var definition = new ReportDefinition(
            GuidGenerator.Create(), CurrentTenant.Id, name, source.Purpose, source.Culture, _cultures,
            source.Provider, copied.Id, source.FileNamePattern);
        await Repository.InsertAsync(definition, autoSave: true, cancellationToken: ct);
        Logger.LogInformation(
            "Template of report definition {ReportDefinitionId} copied to template {TemplateId} for tenant {TenantId}: new report definition {NewReportDefinitionId}",
            source.Id, copied.Id, CurrentTenant.Id, definition.Id);
        return await ToDtoAsync(definition);
    }

    /// <summary>A test PDF from the template's sample data (else the catalogue sample), marked as a test and not billed.</summary>
    [RequiresFeature(AppFeatures.MasterData.Platform.Default, AppFeatures.MasterData.Platform.ReportDefinitions, RequiresAll = true)]
    [Authorize(PlatformPermissions.MasterData.ReportDefinitions.Default)]
    [UnitOfWork(isTransactional: false)]
    public async Task<IRemoteStreamContent> TestRenderAsync(Guid id)
    {
        await EnsureDocumentRenderingAsync();
        var definition = await Repository.GetAsync(id);
        EnsureNotArchived(definition);
        var ct = RequestCancellation;
        var provider = await _renderer.GetConfiguredProviderAsync(definition.Provider, ct);
        var sample = await SampleDataAsync(provider, definition, ct);
        var document = await _renderer.RenderDefinitionAsync(definition, [sample], ReportRenderFormat.Pdf, isTest: true, ct);
        return new RemoteStreamContent(new MemoryStream(document.Content), document.FileName, document.ContentType);
    }

    /// <summary>
    /// The three commands need Platform.DocumentRendering on top of the screen's chain: checked here through the
    /// feature system (403 like the attribute), so the attribute keeps naming exactly the screen's chain.
    /// </summary>
    private Task EnsureDocumentRenderingAsync() => FeatureChecker.CheckEnabledAsync(PlatformFeatures.DocumentRendering);

    private CancellationToken RequestCancellation => LazyServiceProvider.LazyGetRequiredService<ICancellationTokenProvider>().Token;

    private static void EnsureNotArchived(ReportDefinition definition)
    {
        if (definition.IsArchived)
        {
            throw new BusinessException(PlatformErrorCodes.ReportDefinitionArchived).WithData("id", definition.Id);
        }
    }

    private static async Task<JsonElement> SampleDataAsync(IReportRenderingProvider provider, ReportDefinition definition, CancellationToken ct)
        => await provider.GetTemplateSampleDataAsync(definition.ProviderTemplateId, ct)
           ?? JsonSerializer.SerializeToElement(ReportDataCatalogue.SampleFor(definition.Purpose));

    /// <summary>"Name (copy)", then "Name (copy 2)", … in the user's language; the base name is shortened to fit.</summary>
    private async Task<string> UniqueCopyNameAsync(string baseName)
    {
        for (var attempt = 1; attempt <= MaxCopyNameAttempts; attempt++)
        {
            var candidate = CopyName(baseName, attempt);
            var normalized = ReportDefinition.NormalizedNameOf(candidate);
            if (!await Repository.AnyAsync(d => d.NormalizedName == normalized))
            {
                return candidate;
            }
        }

        throw new BusinessException(PlatformErrorCodes.DuplicateKey).WithData("field", "name").WithData("value", CopyName(baseName, 1));
    }

    private string CopyName(string baseName, int attempt)
    {
        string Format(string name) => attempt == 1
            ? L["Platform:CopySuffix", name].Value
            : L["Platform:CopySuffixNumbered", name, attempt].Value;

        var room = PlatformMasterDataConsts.MaxNameLength - Format(string.Empty).Length;
        var trimmed = baseName.Trim();
        return Format(trimmed.Length > room ? trimmed[..room].TrimEnd() : trimmed);
    }

    // ── list hooks ────────────────────────────────────────────────────────────

    protected override IQueryable<ReportDefinition> ApplyFilters(IQueryable<ReportDefinition> query, ReportDefinitionListRequestDto request)
        => ApplyFilters(query, request.Purpose, request.IncludeArchived);

    private static IQueryable<ReportDefinition> ApplyFilters(IQueryable<ReportDefinition> query, ReportPurpose? purpose, bool includeArchived)
    {
        if (purpose != null)
        {
            query = query.Where(d => d.Purpose == purpose.Value);
        }

        return includeArchived ? query : query.Where(d => !d.IsArchived);
    }

    protected override IQueryable<ReportDefinition> ApplySearch(IQueryable<ReportDefinition> query, string term)
        => query.Where(d => d.Name.ToLower().Contains(term) || (d.Description != null && d.Description.ToLower().Contains(term)));

    protected override IOrderedQueryable<ReportDefinition> ApplySort(IQueryable<ReportDefinition> query, string? sortBy, bool descending)
        => (sortBy?.ToLowerInvariant()) switch
        {
            null or "" or "name" => Order(query, d => d.Name, descending),
            "purpose" => Order(query, d => d.Purpose, descending),
            "culture" => Order(query, d => d.Culture, descending),
            "provider" => Order(query, d => d.Provider, descending),
            "isdefault" => Order(query, d => d.IsDefault, descending),
            "isarchived" => Order(query, d => d.IsArchived, descending),
            _ => throw UnknownSortColumn(sortBy)
        };

    protected override IQueryable<ReportDefinition> ApplyLookupFilter(IQueryable<ReportDefinition> query) => query.Where(d => !d.IsArchived);

    protected override Task<List<ReportDefinitionRowDto>> ToRowsAsync(List<ReportDefinition> entities)
        => Task.FromResult(entities.Select(d => new ReportDefinitionRowDto
        {
            Id = d.Id,
            Name = d.Name,
            Purpose = d.Purpose,
            Culture = d.Culture,
            Provider = d.Provider,
            IsDefault = d.IsDefault,
            IsArchived = d.IsArchived
        }).ToList());

    protected override Task<ReportDefinitionDto> ToDtoAsync(ReportDefinition d)
    {
        var dto = new ReportDefinitionDto
        {
            Name = d.Name,
            Purpose = d.Purpose,
            Culture = d.Culture,
            Provider = d.Provider,
            ProviderTemplateId = d.ProviderTemplateId,
            FileNamePattern = d.FileNamePattern,
            IsDefault = d.IsDefault,
            IsArchived = d.IsArchived,
            AvailableInCustomerPortal = d.AvailableInCustomerPortal
        };
        MapAudit(d, dto);
        return Task.FromResult(dto);
    }

    protected override PlatformMasterDataLookupDto ToLookup(ReportDefinition d) => new() { Id = d.Id, Name = d.Name };

    protected override string DisplayNameOf(ReportDefinition d) => d.Name;

    // ── write hooks ───────────────────────────────────────────────────────────

    protected override async Task<ReportDefinition> CreateEntityAsync(ReportDefinitionCreateDto input)
    {
        ScribanTemplateValidator.EnsureValid(input.FileNamePattern, "fileNamePattern", L);
        var normalized = ReportDefinition.NormalizedNameOf(input.Name);
        await EnsureUniqueAsync(d => d.NormalizedName == normalized, "name", input.Name.Trim());

        var definition = new ReportDefinition(
            GuidGenerator.Create(), CurrentTenant.Id, input.Name, input.Purpose, input.Culture, _cultures,
            input.Provider, input.ProviderTemplateId, input.FileNamePattern);
        await ApplyAsync(definition, input);
        return definition;
    }

    protected override async Task ApplyUpdateAsync(ReportDefinition definition, ReportDefinitionUpdateDto input)
    {
        ScribanTemplateValidator.EnsureValid(input.FileNamePattern, "fileNamePattern", L);
        var normalized = ReportDefinition.NormalizedNameOf(input.Name);
        if (normalized != definition.NormalizedName)
        {
            await EnsureUniqueAsync(d => d.NormalizedName == normalized && d.Id != definition.Id, "name", input.Name.Trim());
        }

        definition.SetName(input.Name);
        definition.SetPurpose(input.Purpose, input.Culture, _cultures);
        definition.SetProvider(input.Provider, input.ProviderTemplateId);
        definition.SetFileNamePattern(input.FileNamePattern);
        await ApplyAsync(definition, input);
    }

    private async Task ApplyAsync(ReportDefinition definition, ReportDefinitionCreateDto input)
    {
        definition.SetAvailableInCustomerPortal(input.AvailableInCustomerPortal);
        definition.SetDescription(input.Description);
        if (input.IsArchived)
        {
            definition.Archive();
        }
        else
        {
            definition.Unarchive();
        }

        if (input.IsDefault)
        {
            /* Out of the default index while the previous default is cleared and saved. */
            definition.SetDefault(false);
            await ClearOtherDefaultsAsync(definition);
        }

        definition.SetDefault(input.IsDefault);
    }

    /// <summary>
    /// At most one default per purpose and language: the previous default is cleared and saved first, in the same
    /// transaction, so it has left the partial unique index before this definition enters it.
    /// </summary>
    private async Task ClearOtherDefaultsAsync(ReportDefinition definition)
    {
        var purpose = definition.Purpose;
        var culture = definition.Culture;
        var id = definition.Id;
        var previous = await Repository.GetListAsync(d => d.Purpose == purpose && d.Culture == culture && d.IsDefault && d.Id != id);
        foreach (var other in previous)
        {
            other.SetDefault(false);
            await Repository.UpdateAsync(other);
        }

        if (previous.Count > 0)
        {
            await FlushAsync();
        }
    }
}
