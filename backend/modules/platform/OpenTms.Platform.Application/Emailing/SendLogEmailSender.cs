using System;
using System.Linq;
using System.Net.Mail;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenTms.Platform.Features;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.Emailing;
using Volo.Abp.Features;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;

namespace OpenTms.Platform.Emailing;

/// <summary>
/// ABP's <see cref="IEmailSender"/> (invitations, password resets) routed through the send log (design 64). A tenant's
/// mail goes there when the mail service is configured and the tenant has <see cref="PlatformFeatures.EmailDelivery"/>;
/// host mail whenever the service is configured (the host has no feature values). Otherwise the mail is logged, not
/// sent, as the null sender did before. ABP's settings-based From is ignored: the mail service sets the From, and the
/// mail's first Reply-To is kept. Attachments are not supported yet and are refused rather than dropped.
/// </summary>
public class SendLogEmailSender : EmailSenderBase
{
    private readonly IEmailQueue _queue;
    private readonly IEmailDeliveryProvider _provider;
    private readonly IFeatureChecker _featureChecker;
    private readonly IUnitOfWorkManager _unitOfWorkManager;

    public SendLogEmailSender(
        ICurrentTenant currentTenant,
        IEmailSenderConfiguration configuration,
        IBackgroundJobManager backgroundJobManager,
        IEmailQueue queue,
        IEmailDeliveryProvider provider,
        IFeatureChecker featureChecker,
        IUnitOfWorkManager unitOfWorkManager)
        : base(currentTenant, configuration, backgroundJobManager)
    {
        _queue = queue;
        _provider = provider;
        _featureChecker = featureChecker;
        _unitOfWorkManager = unitOfWorkManager;
    }

    protected override async Task SendEmailAsync(MailMessage mail)
    {
        if (!await IsEnabledAsync())
        {
            Logger.LogWarning("Mail \"{Tag}\" was not sent: e-mail delivery is not configured or not switched on for this tenant.", mail.Headers["X-OpenTms-Tag"] ?? "untagged");
            return;
        }

        if (mail.Attachments.Count > 0)
        {
            throw new NotSupportedException("The send log does not take attachments yet (design 64).");
        }

        var email = new QueuedEmail
        {
            To = string.Join(", ", mail.To.Select(a => a.Address)),
            Cc = mail.CC.Count > 0 ? string.Join(", ", mail.CC.Select(a => a.Address)) : null,
            Bcc = mail.Bcc.Count > 0 ? string.Join(", ", mail.Bcc.Select(a => a.Address)) : null,
            ReplyTo = mail.ReplyToList.FirstOrDefault()?.Address,
            Subject = mail.Subject ?? string.Empty,
            HtmlBody = mail.IsBodyHtml ? mail.Body : null,
            TextBody = mail.IsBodyHtml ? null : mail.Body,
            Tag = mail.Headers["X-OpenTms-Tag"]
        };

        if (_unitOfWorkManager.Current is { Options.IsTransactional: true })
        {
            await _queue.QueueAsync(email);
            return;
        }

        /* A caller without a transaction of its own (a page, a job): the mail gets one. */
        using var unitOfWork = _unitOfWorkManager.Begin(new AbpUnitOfWorkOptions(isTransactional: true), requiresNew: true);
        await _queue.QueueAsync(email);
        await unitOfWork.CompleteAsync();
    }

    private async Task<bool> IsEnabledAsync()
    {
        if (!(await _provider.GetStatusAsync()).IsConfigured)
        {
            return false;
        }

        return CurrentTenant.Id == null || await _featureChecker.IsEnabledAsync(PlatformFeatures.EmailDelivery);
    }
}

/// <summary>The provider when no mail service is wired (the DbMigrator, a host without Integrations): nothing is configured.</summary>
public class NotConfiguredEmailDeliveryProvider : IEmailDeliveryProvider
{
    public Task<EmailDeliveryStatus> GetStatusAsync() => Task.FromResult(new EmailDeliveryStatus());

    public Task<string> SendAsync(OutgoingEmail email, System.Threading.CancellationToken cancellationToken)
        => throw new EmailDeliveryUnavailableException("No mail service is configured.");
}
