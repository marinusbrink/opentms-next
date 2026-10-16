using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using OpenTms.Platform.Features;
using OpenTms.Platform.Grid;
using OpenTms.Platform.Permissions;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Features;
using Volo.Abp.Localization;

namespace OpenTms.Platform.EmailTemplates;

/* Pickers need only the app feature; the list's own screen needs its group and node (PO 2026-10-14, design 49). */
[RequiresFeature(AppFeatures.MasterData.Default)]
public class EmailTemplateAppService
    : PlatformCrudAppServiceBase<EmailTemplate, EmailTemplateRowDto, EmailTemplateDto, EmailTemplateCreateDto, EmailTemplateUpdateDto, EmailTemplateListRequestDto>,
      IEmailTemplateAppService
{
    private readonly IRepository<EmailTemplateContent> _contents;
    private readonly IReadOnlyCollection<string> _cultures;

    public EmailTemplateAppService(
        IRepository<EmailTemplate, Guid> repository,
        IRepository<EmailTemplateContent> contents,
        IOptions<AbpLocalizationOptions> localization)
        : base(repository)
    {
        _contents = contents;
        _cultures = localization.Value.Languages.Select(l => l.CultureName.ToLowerInvariant()).ToList();
    }

    [RequiresFeature(AppFeatures.MasterData.Platform.Default, AppFeatures.MasterData.Platform.EmailTemplates, RequiresAll = true)]
    [Authorize(PlatformPermissions.MasterData.EmailTemplates.Default)]
    public Task<GridResponse<EmailTemplateRowDto>> GetListAsync(EmailTemplateListRequestDto input) => GetListInternalAsync(input);

    [RequiresFeature(AppFeatures.MasterData.Platform.Default, AppFeatures.MasterData.Platform.EmailTemplates, RequiresAll = true)]
    [Authorize(PlatformPermissions.MasterData.EmailTemplates.Default)]
    public Task<EmailTemplateDto> GetAsync(Guid id) => GetInternalAsync(id);

    [Authorize(PlatformPermissions.MasterData.EmailTemplates.Default)]
    public Task<List<PlatformMasterDataLookupDto>> GetLookupAsync(string? term = null, int take = PlatformMasterDataConsts.MaxLookupTake) => GetLookupInternalAsync(term, take);

    [RequiresFeature(AppFeatures.MasterData.Platform.Default, AppFeatures.MasterData.Platform.EmailTemplates, RequiresAll = true)]
    [Authorize(PlatformPermissions.MasterData.EmailTemplates.Create)]
    public Task<EmailTemplateDto> CreateAsync(EmailTemplateCreateDto input) => CreateInternalAsync(input);

    [RequiresFeature(AppFeatures.MasterData.Platform.Default, AppFeatures.MasterData.Platform.EmailTemplates, RequiresAll = true)]
    [Authorize(PlatformPermissions.MasterData.EmailTemplates.Update)]
    public Task<EmailTemplateDto> UpdateAsync(Guid id, EmailTemplateUpdateDto input) => UpdateInternalAsync(id, input);

    [RequiresFeature(AppFeatures.MasterData.Platform.Default, AppFeatures.MasterData.Platform.EmailTemplates, RequiresAll = true)]
    [Authorize(PlatformPermissions.MasterData.EmailTemplates.Delete)]
    public Task DeleteAsync(Guid id) => DeleteInternalAsync(id);

    [RequiresFeature(AppFeatures.MasterData.Platform.Default, AppFeatures.MasterData.Platform.EmailTemplates, RequiresAll = true)]
    [Authorize(PlatformPermissions.MasterData.EmailTemplates.Delete)]
    public Task<PlatformMasterDataBulkDeleteResultDto> BulkDeleteAsync(EmailTemplateBulkDeleteRequestDto input)
        => BulkDeleteInternalAsync(input.Selection, q => ApplyFilters(q, input.Purpose));

    // ── list hooks ────────────────────────────────────────────────────────────

    protected override IQueryable<EmailTemplate> ApplyFilters(IQueryable<EmailTemplate> query, EmailTemplateListRequestDto request)
        => ApplyFilters(query, request.Purpose);

    private static IQueryable<EmailTemplate> ApplyFilters(IQueryable<EmailTemplate> query, EmailTemplatePurpose? purpose)
        => purpose == null ? query : query.Where(t => t.Purpose == purpose.Value);

    protected override IQueryable<EmailTemplate> ApplySearch(IQueryable<EmailTemplate> query, string term)
        => query.Where(t => t.Name.ToLower().Contains(term) || (t.Description != null && t.Description.ToLower().Contains(term)));

    protected override IOrderedQueryable<EmailTemplate> ApplySort(IQueryable<EmailTemplate> query, string? sortBy, bool descending)
        => (sortBy?.ToLowerInvariant()) switch
        {
            null or "" or "name" => Order(query, t => t.Name, descending),
            "purpose" => Order(query, t => t.Purpose, descending),
            "isdefault" => Order(query, t => t.IsDefault, descending),
            _ => throw UnknownSortColumn(sortBy)
        };

    protected override async Task<List<EmailTemplateRowDto>> ToRowsAsync(List<EmailTemplate> entities)
    {
        var ids = entities.Select(t => t.Id).ToList();
        /* Only the language of each content row: the bodies (up to 200,000 characters each) are not read for a list. */
        var contents = ids.Count == 0
            ? []
            : await AsyncExecuter.ToListAsync(
                (await _contents.GetQueryableAsync())
                    .Where(c => ids.Contains(c.EmailTemplateId))
                    .Select(c => new { c.EmailTemplateId, c.Culture }));
        var cultures = contents
            .GroupBy(c => c.EmailTemplateId)
            .ToDictionary(g => g.Key, g => string.Join(", ", g.Select(c => c.Culture).OrderBy(c => c, StringComparer.Ordinal)));

        return entities.Select(t => new EmailTemplateRowDto
        {
            Id = t.Id,
            Name = t.Name,
            Purpose = t.Purpose,
            IsDefault = t.IsDefault,
            Cultures = cultures.GetValueOrDefault(t.Id) ?? string.Empty
        }).ToList();
    }

    protected override Task<EmailTemplateDto> ToDtoAsync(EmailTemplate t)
    {
        var dto = new EmailTemplateDto
        {
            Name = t.Name,
            Purpose = t.Purpose,
            IsDefault = t.IsDefault,
            SenderAddress = t.SenderAddress,
            SenderName = t.SenderName,
            ReplyTo = t.ReplyTo,
            To = t.To,
            Cc = t.Cc,
            Bcc = t.Bcc,
            SendCopyToSender = t.SendCopyToSender,
            Contents = t.Contents
                .OrderBy(c => c.Culture, StringComparer.Ordinal)
                .Select(c => new EmailTemplateContentDto { Culture = c.Culture, Subject = c.Subject, HtmlBody = c.HtmlBody })
                .ToList()
        };
        MapAudit(t, dto);
        return Task.FromResult(dto);
    }

    protected override PlatformMasterDataLookupDto ToLookup(EmailTemplate t) => new() { Id = t.Id, Name = t.Name };

    protected override string DisplayNameOf(EmailTemplate t) => t.Name;

    // ── write hooks ───────────────────────────────────────────────────────────

    protected override async Task<EmailTemplate> CreateEntityAsync(EmailTemplateCreateDto input)
    {
        Validate(input);
        var normalized = EmailTemplate.NormalizedNameOf(input.Name);
        await EnsureUniqueAsync(t => t.NormalizedName == normalized, "name", input.Name.Trim());

        var template = new EmailTemplate(GuidGenerator.Create(), CurrentTenant.Id, input.Name, input.Purpose);
        await ApplyAsync(template, input);
        return template;
    }

    protected override async Task ApplyUpdateAsync(EmailTemplate template, EmailTemplateUpdateDto input)
    {
        Validate(input);
        var normalized = EmailTemplate.NormalizedNameOf(input.Name);
        if (normalized != template.NormalizedName)
        {
            await EnsureUniqueAsync(t => t.NormalizedName == normalized && t.Id != template.Id, "name", input.Name.Trim());
        }

        template.SetName(input.Name);
        template.SetPurpose(input.Purpose);
        await ApplyAsync(template, input);
    }

    private async Task ApplyAsync(EmailTemplate template, EmailTemplateCreateDto input)
    {
        template.SetAddresses(input.SenderAddress, input.SenderName, input.ReplyTo, input.To, input.Cc, input.Bcc, input.SendCopyToSender);
        template.SetDescription(input.Description);
        template.SetContents(input.Contents.Select(c => (c.Culture, c.Subject, c.HtmlBody)).ToList(), _cultures);

        if (input.IsDefault)
        {
            /* Out of the default index while the previous default is cleared and saved. */
            template.SetDefault(false);
            await ClearOtherDefaultsAsync(template);
        }

        template.SetDefault(input.IsDefault);
    }

    /// <summary>
    /// At most one default per purpose: the previous default is cleared and saved first, in the same transaction, so it
    /// has left the partial unique index before this template enters it.
    /// </summary>
    private async Task ClearOtherDefaultsAsync(EmailTemplate template)
    {
        var purpose = template.Purpose;
        var id = template.Id;
        var previous = await Repository.GetListAsync(t => t.Purpose == purpose && t.IsDefault && t.Id != id);
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

    /// <summary>Every Scriban field parses (design 49); the error names the field, the language and the line.</summary>
    private void Validate(EmailTemplateCreateDto input)
    {
        ScribanTemplateValidator.EnsureValid(input.SenderAddress, "senderAddress", L);
        ScribanTemplateValidator.EnsureValid(input.SenderName, "senderName", L);
        ScribanTemplateValidator.EnsureValid(input.ReplyTo, "replyTo", L);
        ScribanTemplateValidator.EnsureValid(input.To, "to", L);
        ScribanTemplateValidator.EnsureValid(input.Cc, "cc", L);
        ScribanTemplateValidator.EnsureValid(input.Bcc, "bcc", L);
        foreach (var content in input.Contents)
        {
            var culture = content.Culture?.Trim().ToLowerInvariant();
            ScribanTemplateValidator.EnsureValid(content.Subject, "subject", L, culture);
            ScribanTemplateValidator.EnsureValid(content.HtmlBody, "htmlBody", L, culture);
        }
    }
}
