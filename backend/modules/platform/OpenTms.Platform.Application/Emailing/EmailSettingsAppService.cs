using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.UI.Navigation.Urls;
using OpenTms.Platform.Permissions;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.SettingManagement;
using Volo.Abp.Uow;

namespace OpenTms.Platform.Emailing;

/// <summary>
/// The host's e-mail settings (design 64, "Host e-mail settings"): global settings in the host database, one configuration
/// of the mail service for every tenant. An empty value falls back to the deployment's configuration. The token and the
/// webhook password are write-only: stored encrypted, never returned, kept when the form sends them empty. The test mail
/// goes through the send log (host) and is sent at once, without a retry, so the page shows the mail service's answer.
/// </summary>
[RemoteService(IsEnabled = false)]
[Authorize(PlatformPermissions.Administration.EmailSettings.Default)]
public class EmailSettingsAppService : PlatformAppServiceBase, IEmailSettingsAppService
{
    private readonly ISettingManager _settings;
    private readonly IEmailDeliveryProvider _provider;
    private readonly IRepository<EmailMessage, Guid> _messages;
    private readonly EmailDispatcher _dispatcher;
    private readonly IAppUrlProvider _appUrls;

    public EmailSettingsAppService(
        ISettingManager settings,
        IEmailDeliveryProvider provider,
        IRepository<EmailMessage, Guid> messages,
        EmailDispatcher dispatcher,
        IAppUrlProvider appUrls)
    {
        _settings = settings;
        _provider = provider;
        _messages = messages;
        _dispatcher = dispatcher;
        _appUrls = appUrls;
    }

    public async Task<EmailSettingsDto> GetAsync()
    {
        EnsureHostSide();
        var trackOpens = await GetAsync(EmailSettingNames.TrackOpens);
        return new EmailSettingsDto
        {
            HasServerToken = !string.IsNullOrEmpty(await GetAsync(EmailSettingNames.ServerToken)),
            FromAddress = await GetAsync(EmailSettingNames.FromAddress),
            FromName = await GetAsync(EmailSettingNames.FromName),
            TrackOpens = bool.TryParse(trackOpens, out var opens) ? opens : null,
            TrackLinks = await GetAsync(EmailSettingNames.TrackLinks),
            WebhookUsername = await GetAsync(EmailSettingNames.WebhookUsername),
            HasWebhookPassword = !string.IsNullOrEmpty(await GetAsync(EmailSettingNames.WebhookPassword)),
            WebhookUrl = await WebhookUrlAsync(),
            Effective = await _provider.GetStatusAsync()
        };
    }

    [Authorize(PlatformPermissions.Administration.EmailSettings.Update)]
    public async Task UpdateAsync(UpdateEmailSettingsDto input)
    {
        EnsureHostSide();
        if (!string.IsNullOrWhiteSpace(input.TrackLinks) && !EmailSettingsConsts.TrackLinksValues.Contains(input.TrackLinks))
        {
            throw new UserFriendlyException(L["Email:InvalidTrackLinks"]);
        }

        if (input.ClearServerToken)
        {
            await SetAsync(EmailSettingNames.ServerToken, null);
        }
        else if (!string.IsNullOrWhiteSpace(input.ServerToken))
        {
            await SetAsync(EmailSettingNames.ServerToken, input.ServerToken.Trim());
        }

        if (input.ClearWebhookPassword)
        {
            await SetAsync(EmailSettingNames.WebhookPassword, null);
        }
        else if (!string.IsNullOrWhiteSpace(input.WebhookPassword))
        {
            await SetAsync(EmailSettingNames.WebhookPassword, input.WebhookPassword);
        }

        await SetAsync(EmailSettingNames.FromAddress, input.FromAddress);
        await SetAsync(EmailSettingNames.FromName, input.FromName);
        await SetAsync(EmailSettingNames.TrackOpens, input.TrackOpens?.ToString().ToLowerInvariant());
        await SetAsync(EmailSettingNames.TrackLinks, input.TrackLinks);
        await SetAsync(EmailSettingNames.WebhookUsername, input.WebhookUsername);
        /* Nothing is read back here: ABP's settings cache is unit-of-work aware, and a read in this unit of work would see
         * the values before this save. The page reads the settings again afterwards. */
    }

    [Authorize(PlatformPermissions.Administration.EmailSettings.SendTest)]
    public async Task<TestEmailResultDto> SendTestAsync(SendTestEmailDto input)
    {
        EnsureHostSide();
        if (!(await _provider.GetStatusAsync()).IsConfigured)
        {
            return new TestEmailResultDto { Status = EmailMessageStatus.Failed, Error = L["Email:NotConfigured"] };
        }

        var id = GuidGenerator.Create();
        using (var unitOfWork = UnitOfWorkManager.Begin(new AbpUnitOfWorkOptions(isTransactional: true), requiresNew: true))
        {
            /* Straight onto the send log, without the queue event: the page sends it now and waits for the answer. */
            await _messages.InsertAsync(new EmailMessage(
                id, null, input.To, null, null, null, L["Email:TestSubject"], L["Email:TestBody"], null, EmailSettingsConsts.TestEmailTag));
            await unitOfWork.CompleteAsync();
        }

        await _dispatcher.SendAsync(new EmailSendArgs { TenantId = null, EmailMessageId = id }, retryable: false);

        var message = await _messages.GetAsync(id);
        return new TestEmailResultDto
        {
            Succeeded = message.Status is EmailMessageStatus.Sent or EmailMessageStatus.Delivered,
            Status = message.Status,
            ProviderMessageId = message.ProviderMessageId,
            Error = message.Status == EmailMessageStatus.Failed ? message.LastError : null
        };
    }

    private void EnsureHostSide()
    {
        if (CurrentTenant.IsAvailable)
        {
            throw new AbpAuthorizationException("The e-mail settings are available on the host side only.");
        }
    }

    private Task<string?> GetAsync(string name) => _settings.GetOrNullGlobalAsync(name, fallback: false);

    private Task SetAsync(string name, string? value) =>
        _settings.SetGlobalAsync(name, string.IsNullOrWhiteSpace(value) ? null : value.Trim());

    /// <summary>The API host's own URL: the "MVC" application of AppUrlOptions (App:SelfUrl), as the invitation mail uses.</summary>
    private async Task<string?> WebhookUrlAsync()
    {
        try
        {
            var root = await _appUrls.GetUrlAsync("MVC");
            return string.IsNullOrWhiteSpace(root) ? null : root.TrimEnd('/') + "/" + EmailSettingsConsts.WebhookPath;
        }
        catch (AbpException)
        {
            return null;
        }
    }
}
