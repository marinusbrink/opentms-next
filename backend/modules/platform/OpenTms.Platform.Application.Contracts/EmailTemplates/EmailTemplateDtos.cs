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
    public bool IsDefault { get; set; }
    /// <summary>The languages with content, sorted and joined by ", " (e.g. "en, nl").</summary>
    public string Cultures { get; set; } = string.Empty;
}

public class EmailTemplateContentDto
{
    public string Culture { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string HtmlBody { get; set; } = string.Empty;
}

public class EmailTemplateDto : PlatformMasterDataDetailDtoBase
{
    public string Name { get; set; } = string.Empty;
    public EmailTemplatePurpose Purpose { get; set; }
    public bool IsDefault { get; set; }
    public string? SenderAddress { get; set; }
    public string? SenderName { get; set; }
    public string? ReplyTo { get; set; }
    public string? To { get; set; }
    public string? Cc { get; set; }
    public string? Bcc { get; set; }
    public bool SendCopyToSender { get; set; }
    /// <summary>One per language, ordered by culture.</summary>
    public List<EmailTemplateContentDto> Contents { get; set; } = new();
}

public class EmailTemplateContentInputDto
{
    /// <summary>A configured language ("nl", "en").</summary>
    [Required, MaxLength(PlatformMasterDataConsts.MaxCultureLength)]
    public string Culture { get; set; } = string.Empty;

    /// <summary>Scriban, parsed on save.</summary>
    [Required, MaxLength(PlatformMasterDataConsts.MaxSubjectLength)]
    public string Subject { get; set; } = string.Empty;

    /// <summary>HTML source with Scriban expressions, parsed on save.</summary>
    [Required, MaxLength(PlatformMasterDataConsts.MaxHtmlBodyLength)]
    public string HtmlBody { get; set; } = string.Empty;
}

public class EmailTemplateCreateDto
{
    [Required, MaxLength(PlatformMasterDataConsts.MaxNameLength)]
    public string Name { get; set; } = string.Empty;

    [Required, EnumDataType(typeof(EmailTemplatePurpose))]
    public EmailTemplatePurpose Purpose { get; set; }

    /// <summary>True makes this the purpose's default and clears the previous one.</summary>
    public bool IsDefault { get; set; }

    [MaxLength(PlatformMasterDataConsts.MaxEmailAddressFieldLength)]
    public string? SenderAddress { get; set; }

    [MaxLength(PlatformMasterDataConsts.MaxEmailAddressFieldLength)]
    public string? SenderName { get; set; }

    [MaxLength(PlatformMasterDataConsts.MaxEmailAddressFieldLength)]
    public string? ReplyTo { get; set; }

    /// <summary>Empty: the consumer's recipient.</summary>
    [MaxLength(PlatformMasterDataConsts.MaxEmailAddressFieldLength)]
    public string? To { get; set; }

    [MaxLength(PlatformMasterDataConsts.MaxEmailAddressFieldLength)]
    public string? Cc { get; set; }

    [MaxLength(PlatformMasterDataConsts.MaxEmailAddressFieldLength)]
    public string? Bcc { get; set; }

    public bool SendCopyToSender { get; set; }

    [MaxLength(PlatformMasterDataConsts.MaxDescriptionLength)]
    public string? Description { get; set; }

    /// <summary>One per language; "en" is required.</summary>
    [MaxLength(PlatformMasterDataConsts.MaxEmailTemplateContents)]
    public List<EmailTemplateContentInputDto> Contents { get; set; } = new();
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
}
