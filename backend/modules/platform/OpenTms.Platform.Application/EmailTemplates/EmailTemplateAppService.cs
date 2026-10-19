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
    private readonly IReadOnlyCollection<string> _cultures;

    public EmailTemplateAppService(
        IRepository<EmailTemplate, Guid> repository,
        IOptions<AbpLocalizationOptions> localization)
        : base(repository)
    {
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
            "culture" => Order(query, t => t.Culture, descending),
            "isdefault" => Order(query, t => t.IsDefault, descending),
            _ => throw UnknownSortColumn(sortBy)
        };

    protected override Task<List<EmailTemplateRowDto>> ToRowsAsync(List<EmailTemplate> entities)
        => Task.FromResult(entities.Select(t => new EmailTemplateRowDto
        {
            Id = t.Id,
            Name = t.Name,
            Purpose = t.Purpose,
            Culture = t.Culture,
            IsDefault = t.IsDefault
        }).ToList());

    /* The list and the lookup project their columns: a body holds up to 200,000 characters and neither shows it. */
    protected override async Task<List<EmailTemplateRowDto>> ReadRowsAsync(IQueryable<EmailTemplate> page)
        => await AsyncExecuter.ToListAsync(page.Select(t => new EmailTemplateRowDto
        {
            Id = t.Id,
            Name = t.Name,
            Purpose = t.Purpose,
            Culture = t.Culture,
            IsDefault = t.IsDefault
        }));

    protected override async Task<List<PlatformMasterDataLookupDto>> ReadLookupsAsync(IQueryable<EmailTemplate> query)
        => await AsyncExecuter.ToListAsync(query.Select(t => new PlatformMasterDataLookupDto { Id = t.Id, Name = t.Name }));

    protected override Task<EmailTemplateDto> ToDtoAsync(EmailTemplate t)
    {
        var dto = new EmailTemplateDto
        {
            Name = t.Name,
            Purpose = t.Purpose,
            Culture = t.Culture,
            IsDefault = t.IsDefault,
            SenderAddress = t.SenderAddress,
            ReplyTo = t.ReplyTo,
            To = t.To,
            Cc = t.Cc,
            Bcc = t.Bcc,
            SendCopyToSender = t.SendCopyToSender,
            Subject = t.Subject,
            HtmlBody = t.HtmlBody
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

        var template = new EmailTemplate(
            GuidGenerator.Create(), CurrentTenant.Id, input.Name, input.Purpose, input.Culture, _cultures, input.Subject, input.HtmlBody);
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
        template.SetCulture(input.Culture, _cultures);
        template.SetContent(input.Subject, input.HtmlBody);
        await ApplyAsync(template, input);
    }

    private async Task ApplyAsync(EmailTemplate template, EmailTemplateCreateDto input)
    {
        template.SetAddresses(input.SenderAddress, input.ReplyTo, input.To, input.Cc, input.Bcc, input.SendCopyToSender);
        template.SetDescription(input.Description);

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

    /// <summary>
    /// Every Scriban field parses (design 49); the error names the field as the screen labels it, and the line. The
    /// template is one language, so no field carries a language suffix.
    /// </summary>
    private void Validate(EmailTemplateCreateDto input)
    {
        ScribanTemplateValidator.EnsureValid(input.SenderAddress, "senderAddress", L, labelKey: "Platform:From");
        ScribanTemplateValidator.EnsureValid(input.ReplyTo, "replyTo", L);
        ScribanTemplateValidator.EnsureValid(input.To, "to", L);
        ScribanTemplateValidator.EnsureValid(input.Cc, "cc", L);
        ScribanTemplateValidator.EnsureValid(input.Bcc, "bcc", L);
        ScribanTemplateValidator.EnsureValid(input.Subject, "subject", L);
        ScribanTemplateValidator.EnsureValid(input.HtmlBody, "htmlBody", L);
    }
}
