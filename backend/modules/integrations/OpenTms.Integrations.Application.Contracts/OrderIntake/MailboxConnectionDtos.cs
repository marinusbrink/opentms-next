using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using OpenTms.Platform.Grid;
using OpenTms.Platform.ReferenceData;
using Volo.Abp.Application.Services;

namespace OpenTms.Integrations.OrderIntake;

/// <summary>
/// Master data → Integrations → Order intake → Mailboxes (design 72 "Channels"), route root
/// <c>/api/integrations/mailbox-connections</c>. Create returns the provider's authorize URL; the anonymous OAuth
/// callback (<c>GET /api/integrations/mailbox-oauth/callback</c>) is a controller of its own, not part of this service.
/// The refresh token is never returned.
/// </summary>
public interface IMailboxConnectionAppService : IApplicationService
{
    Task<GridResponse<MailboxConnectionRowDto>> GetListAsync(MailboxConnectionListRequestDto input);

    Task<MailboxConnectionDto> GetAsync(Guid id);

    /// <summary>Creates the connection (AwaitingConsent) and returns where the browser goes for the provider's consent.</summary>
    Task<MailboxConnectionCreateResultDto> CreateAsync(MailboxConnectionCreateDto input);

    /// <summary>A new authorize URL for a connection in Reconnect (or one whose first consent was abandoned).</summary>
    Task<MailboxAuthorizeUrlDto> ReconnectAsync(Guid id);

    /// <summary>The provider's labels (Gmail: user labels and INBOX) or mail folders (Microsoft: paths to depth 3).</summary>
    Task<List<MailboxFolderDto>> GetFoldersAsync(Guid id);

    /// <summary>Name, folder, defaults and auto-confirm. A new folder takes the provider's current cursor and activates the connection.</summary>
    Task<MailboxConnectionDto> UpdateAsync(Guid id, MailboxConnectionUpdateDto input);

    Task<MailboxConnectionDto> PauseAsync(Guid id);

    Task<MailboxConnectionDto> ResumeAsync(Guid id);

    /// <summary>Removes the connection; revokes nothing at the provider.</summary>
    Task DeleteAsync(Guid id);
}

/// <summary>The design-50 paging; <see cref="ReferenceDataListRequestDto.WildcardSearch"/> searches the name and the address.</summary>
public class MailboxConnectionListRequestDto : ReferenceDataListRequestDto
{
    public List<MailboxStatus> Status { get; set; } = new();
}

public class MailboxConnectionRowDto
{
    public Guid Id { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public MailProvider Provider { get; set; }

    public string? EmailAddress { get; set; }

    public string? FolderName { get; set; }

    public MailboxStatus Status { get; set; }

    public DateTime? LastSyncAt { get; set; }

    public string? LastErrorCode { get; set; }

    public DateTime? LastErrorAt { get; set; }

    public DateTime CreationTime { get; set; }
}

public class MailboxConnectionDto : ReferenceDataDetailDtoBase
{
    public string DisplayName { get; set; } = string.Empty;

    public MailProvider Provider { get; set; }

    public string? EmailAddress { get; set; }

    public string? FolderId { get; set; }

    public string? FolderName { get; set; }

    public MailboxStatus Status { get; set; }

    public DateTime? ConnectedAt { get; set; }

    public DateTime? LastSyncAt { get; set; }

    public string? LastErrorCode { get; set; }

    public DateTime? LastErrorAt { get; set; }

    public Guid? DefaultCustomerRelationId { get; set; }

    public string? DefaultCustomerName { get; set; }

    public Guid? DefaultTransportOrderTypeId { get; set; }

    public string? DefaultTransportOrderTypeName { get; set; }

    public bool AutoConfirm { get; set; }
}

public class MailboxConnectionCreateDto
{
    public MailProvider Provider { get; set; }

    [Required, MaxLength(OrderIntakeConsts.MaxDisplayNameLength)]
    public string DisplayName { get; set; } = string.Empty;
}

public class MailboxConnectionCreateResultDto
{
    public Guid Id { get; set; }

    public string AuthorizeUrl { get; set; } = string.Empty;
}

public class MailboxAuthorizeUrlDto
{
    public string AuthorizeUrl { get; set; } = string.Empty;
}

public class MailboxFolderDto
{
    public string Id { get; set; } = string.Empty;

    /// <summary>The label name, or the folder path ("Inbox/Orders").</summary>
    public string Name { get; set; } = string.Empty;
}

public class MailboxConnectionUpdateDto : IReferenceDataUpdateDto
{
    [Required, MaxLength(OrderIntakeConsts.MaxDisplayNameLength)]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>One of <see cref="IMailboxConnectionAppService.GetFoldersAsync"/>; its name comes from the provider's list.</summary>
    [MaxLength(OrderIntakeConsts.MaxFolderIdLength)]
    public string? FolderId { get; set; }

    public Guid? DefaultCustomerRelationId { get; set; }

    public Guid? DefaultTransportOrderTypeId { get; set; }

    public bool AutoConfirm { get; set; }

    [Required]
    public string ConcurrencyStamp { get; set; } = string.Empty;
}
