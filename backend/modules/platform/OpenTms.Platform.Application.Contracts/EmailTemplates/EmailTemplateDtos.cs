using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using OpenTms.Platform.Grid;
using Volo.Abp.Application.Services;

namespace OpenTms.Platform.EmailTemplates;

public class EmailTemplateRowDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public EmailTemplatePurpose Purpose { get; set; }
    /// <summary>The template's language ("nl", "en"); null: all languages.</summary>
    public string? Culture { get; set; }
    public bool IsDefault { get; set; }
}

public class EmailTemplateDto : PlatformMasterDataDetailDtoBase
{
    public string Name { get; set; } = string.Empty;
    public EmailTemplatePurpose Purpose { get; set; }
    /// <summary>The template's language ("nl", "en"); null: all languages.</summary>
    public string? Culture { get; set; }
    public bool IsDefault { get; set; }
    /// <summary>"From".</summary>
    public string? SenderAddress { get; set; }
    public string? ReplyTo { get; set; }
    public string? To { get; set; }
    public string? Cc { get; set; }
    public string? Bcc { get; set; }
    /// <summary>"Send me a copy".</summary>
    public bool SendCopyToSender { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string HtmlBody { get; set; } = string.Empty;
}

public class EmailTemplateCreateDto
{
    [Required, MaxLength(PlatformMasterDataConsts.MaxNameLength)]
    public string Name { get; set; } = string.Empty;

    [Required, EnumDataType(typeof(EmailTemplatePurpose))]
    public EmailTemplatePurpose Purpose { get; set; }

    /// <summary>A configured language ("nl", "en"); null or empty: all languages.</summary>
    [MaxLength(PlatformMasterDataConsts.MaxCultureLength)]
    public string? Culture { get; set; }

    /// <summary>True makes this the purpose's default and clears the previous one.</summary>
    public bool IsDefault { get; set; }

    /// <summary>"From"; Scriban, parsed on save.</summary>
    [MaxLength(PlatformMasterDataConsts.MaxEmailAddressFieldLength)]
    public string? SenderAddress { get; set; }

    [MaxLength(PlatformMasterDataConsts.MaxEmailAddressFieldLength)]
    public string? ReplyTo { get; set; }

    /// <summary>Empty: the consumer's recipient.</summary>
    [MaxLength(PlatformMasterDataConsts.MaxEmailAddressFieldLength)]
    public string? To { get; set; }

    [MaxLength(PlatformMasterDataConsts.MaxEmailAddressFieldLength)]
    public string? Cc { get; set; }

    [MaxLength(PlatformMasterDataConsts.MaxEmailAddressFieldLength)]
    public string? Bcc { get; set; }

    /// <summary>"Send me a copy".</summary>
    public bool SendCopyToSender { get; set; }

    /// <summary>Scriban, parsed on save.</summary>
    [Required, MaxLength(PlatformMasterDataConsts.MaxSubjectLength)]
    public string Subject { get; set; } = string.Empty;

    /// <summary>HTML source with Scriban expressions, parsed on save.</summary>
    [Required, MaxLength(PlatformMasterDataConsts.MaxHtmlBodyLength)]
    public string HtmlBody { get; set; } = string.Empty;

    [MaxLength(PlatformMasterDataConsts.MaxDescriptionLength)]
    public string? Description { get; set; }
}

public class EmailTemplateUpdateDto : EmailTemplateCreateDto, IPlatformMasterDataUpdateDto
{
    [Required]
    public string ConcurrencyStamp { get; set; } = string.Empty;
}

public class EmailTemplateListRequestDto : PlatformMasterDataListRequestDto
{
    [EnumDataType(typeof(EmailTemplatePurpose))]
    public EmailTemplatePurpose? Purpose { get; set; }
}

public class EmailTemplateBulkDeleteRequestDto : PlatformMasterDataBulkDeleteRequestDto
{
    /// <summary>The list's purpose filter, applied in FilterBased mode.</summary>
    [EnumDataType(typeof(EmailTemplatePurpose))]
    public EmailTemplatePurpose? Purpose { get; set; }
}

public interface IEmailTemplateAppService : IApplicationService
{
    Task<GridResponse<EmailTemplateRowDto>> GetListAsync(EmailTemplateListRequestDto input);
    Task<EmailTemplateDto> GetAsync(Guid id);
    Task<List<PlatformMasterDataLookupDto>> GetLookupAsync(string? term = null, int take = PlatformMasterDataConsts.MaxLookupTake);
    Task<EmailTemplateDto> CreateAsync(EmailTemplateCreateDto input);
    Task<EmailTemplateDto> UpdateAsync(Guid id, EmailTemplateUpdateDto input);
    Task DeleteAsync(Guid id);
    Task<PlatformMasterDataBulkDeleteResultDto> BulkDeleteAsync(EmailTemplateBulkDeleteRequestDto input);

    /// <summary>The tag catalogue of a purpose, localized in the UI language (design 49, "Tags and default content").</summary>
    Task<List<EmailTemplateTagGroupDto>> GetTagsAsync(EmailTemplatePurpose purpose);

    /// <summary>
    /// From, Reply-to, Subject and HTML body for a new template of a purpose; <paramref name="culture"/> "nl" or "en",
    /// anything else (or empty) gives the UI language.
    /// </summary>
    Task<EmailTemplateDefaultsDto> GetDefaultsAsync(EmailTemplatePurpose purpose, string? culture = null);
}
