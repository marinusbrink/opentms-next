using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace OpenTms.Platform.Emailing;

/// <summary>
/// The host's e-mail settings page in the Administration app (design 64, "Host e-mail settings"): one configuration of
/// the mail service for every tenant, and a test mail. Host only.
/// </summary>
public interface IEmailSettingsAppService : IApplicationService
{
    Task<EmailSettingsDto> GetAsync();

    /// <summary>Saves the settings; read them again with <see cref="GetAsync"/> afterwards.</summary>
    Task UpdateAsync(UpdateEmailSettingsDto input);

    /// <summary>Sends one mail at once with the saved settings and reports what the mail service answered.</summary>
    Task<TestEmailResultDto> SendTestAsync(SendTestEmailDto input);
}

/// <summary>The stored settings (empty = the deployment's configuration applies) and what is in effect.</summary>
public class EmailSettingsDto
{
    /// <summary>A token is stored in the settings. The token itself is never returned.</summary>
    public bool HasServerToken { get; set; }

    public string? FromAddress { get; set; }

    public string? FromName { get; set; }

    public bool? TrackOpens { get; set; }

    public string? TrackLinks { get; set; }

    public string? WebhookUsername { get; set; }

    /// <summary>A webhook password is stored. The password itself is never returned.</summary>
    public bool HasWebhookPassword { get; set; }

    /// <summary>The URL to enter in the mail service's webhook settings.</summary>
    public string? WebhookUrl { get; set; }

    /// <summary>What the mail service will use: the settings above, else the configuration.</summary>
    public EmailDeliveryStatus Effective { get; set; } = new();
}

public class UpdateEmailSettingsDto
{
    /// <summary>A new token; empty keeps the stored one.</summary>
    [StringLength(EmailSettingsConsts.MaxSecretLength)]
    public string? ServerToken { get; set; }

    /// <summary>Removes the stored token (the configuration's applies again).</summary>
    public bool ClearServerToken { get; set; }

    [EmailAddress]
    [StringLength(EmailMessageConsts.MaxReplyToLength)]
    public string? FromAddress { get; set; }

    [StringLength(EmailSettingsConsts.MaxFromNameLength)]
    public string? FromName { get; set; }

    public bool? TrackOpens { get; set; }

    public string? TrackLinks { get; set; }

    [StringLength(EmailSettingsConsts.MaxSecretLength)]
    public string? WebhookUsername { get; set; }

    /// <summary>A new password; empty keeps the stored one.</summary>
    [StringLength(EmailSettingsConsts.MaxSecretLength)]
    public string? WebhookPassword { get; set; }

    public bool ClearWebhookPassword { get; set; }
}

public class SendTestEmailDto
{
    [Required]
    [EmailAddress]
    [StringLength(EmailMessageConsts.MaxReplyToLength)]
    public string To { get; set; } = string.Empty;
}

public class TestEmailResultDto
{
    public bool Succeeded { get; set; }

    public EmailMessageStatus Status { get; set; }

    /// <summary>The mail service's id of the test mail, to find it in its activity view.</summary>
    public string? ProviderMessageId { get; set; }

    /// <summary>The mail service's error, when it refused or could not be reached.</summary>
    public string? Error { get; set; }
}
