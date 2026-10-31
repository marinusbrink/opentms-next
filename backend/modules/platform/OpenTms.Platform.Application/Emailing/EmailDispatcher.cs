using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Volo.Abp.BackgroundJobs;
using Microsoft.Extensions.Logging.Abstractions;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace OpenTms.Platform.Emailing;

/// <summary>
/// Sends one mail of the send log (design 64, AP-11) in its tenant, for the send job and the host's test mail. Idempotent: only a Queued mail is sent. The mail is
/// marked Sending in a committed transaction before the call, so a crash or a timeout leaves Sending behind; the next run
/// then marks it Failed with <see cref="EmailMessageConsts.OutcomeUnknownCode"/> instead of risking a second copy (at most
/// once on doubt). A permanent rejection fails the mail without a retry; a transient failure puts it back in the queue
/// and rethrows, so Hangfire retries. After the service took the mail, the host route is written before the mail is
/// marked Sent, so a delivery report that arrives at once finds its tenant.
/// </summary>
public class EmailDispatcher : ITransientDependency
{
    public ILogger<EmailDispatcher> Logger { get; set; } = NullLogger<EmailDispatcher>.Instance;

    private readonly IRepository<EmailMessage, Guid> _repository;
    private readonly IEmailDeliveryProvider _provider;
    private readonly IEmailMessageRouteStore _routes;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly ICurrentTenant _currentTenant;
    private readonly IClock _clock;

    public EmailDispatcher(
        IRepository<EmailMessage, Guid> repository,
        IEmailDeliveryProvider provider,
        IEmailMessageRouteStore routes,
        IUnitOfWorkManager unitOfWorkManager,
        ICurrentTenant currentTenant,
        IClock clock)
    {
        _repository = repository;
        _provider = provider;
        _routes = routes;
        _unitOfWorkManager = unitOfWorkManager;
        _currentTenant = currentTenant;
        _clock = clock;
    }

    /// <param name="retryable">The send job: a transient failure goes back to the queue and rethrows. The test mail
    /// (no job behind it): a transient failure fails the mail and returns.</param>
    public async Task SendAsync(EmailSendArgs args, bool retryable = true)
    {
        /* Trusts only ids from its arguments and runs explicitly in the queuing tenant (constitution rule 1). */
        using (_currentTenant.Change(args.TenantId))
        {
            var outgoing = await StartAsync(args.EmailMessageId);
            if (outgoing == null)
            {
                return;
            }

            string providerMessageId;
            try
            {
                providerMessageId = await _provider.SendAsync(outgoing, CancellationToken.None);
            }
            catch (EmailRejectedException e)
            {
                await ChangeAsync(args.EmailMessageId, m => m.MarkFailed(e.ErrorCode + ": " + e.Message));
                Logger.LogWarning("Mail {EmailMessageId} was rejected by the mail service with code {ErrorCode}; not retried.", args.EmailMessageId, e.ErrorCode);
                return;
            }
            catch (EmailDeliveryUnavailableException e) when (!retryable)
            {
                await ChangeAsync(args.EmailMessageId, m => m.MarkFailed((e.OutcomeUnknown ? EmailMessageConsts.OutcomeUnknownCode + ": " : string.Empty) + e.Message));
                Logger.LogWarning("Mail {EmailMessageId}: the mail service is unavailable; not retried.", args.EmailMessageId);
                return;
            }
            catch (EmailDeliveryUnavailableException e) when (!e.OutcomeUnknown)
            {
                await ChangeAsync(args.EmailMessageId, m => m.MarkQueuedAgain(e.Message));
                Logger.LogWarning("Mail {EmailMessageId}: the mail service is unavailable; Hangfire retries.", args.EmailMessageId);
                throw;
            }

            /* Anything else (a timeout after the request left, a crash) leaves Sending: the next run fails it. */
            await _routes.AddAsync(providerMessageId, args.TenantId, args.EmailMessageId);
            await ChangeAsync(args.EmailMessageId, m => m.MarkSent(providerMessageId, _clock.Now));
            Logger.LogInformation("Mail {EmailMessageId} sent as {ProviderMessageId}.", args.EmailMessageId, providerMessageId);
        }
    }

    /// <summary>Marks a Queued mail Sending and returns what to send; null when there is nothing to send.</summary>
    private async Task<OutgoingEmail?> StartAsync(Guid id)
    {
        using var unitOfWork = _unitOfWorkManager.Begin(new AbpUnitOfWorkOptions(isTransactional: true), requiresNew: true);
        var message = await _repository.FindAsync(id);
        if (message == null)
        {
            Logger.LogWarning("Mail {EmailMessageId} is not on the send log; the job is dropped.", id);
            await unitOfWork.CompleteAsync();
            return null;
        }

        if (message.Status == EmailMessageStatus.Sending)
        {
            message.MarkFailed(EmailMessageConsts.OutcomeUnknownCode + ": the previous attempt did not finish; the mail may have been sent and is not sent again.");
            await _repository.UpdateAsync(message);
            await unitOfWork.CompleteAsync();
            Logger.LogWarning("Mail {EmailMessageId}: the previous attempt did not finish; marked failed, not sent again.", id);
            return null;
        }

        if (message.Status != EmailMessageStatus.Queued)
        {
            Logger.LogDebug("Mail {EmailMessageId} is {Status}; skipped.", id, message.Status);
            await unitOfWork.CompleteAsync();
            return null;
        }

        message.MarkSending();
        await _repository.UpdateAsync(message);
        await unitOfWork.CompleteAsync();
        return new OutgoingEmail
        {
            EmailMessageId = message.Id,
            TenantId = message.TenantId,
            To = message.To,
            Cc = message.Cc,
            Bcc = message.Bcc,
            ReplyTo = message.ReplyTo,
            Subject = message.Subject,
            HtmlBody = message.HtmlBody,
            TextBody = message.TextBody,
            Tag = message.Tag
        };
    }

    private async Task ChangeAsync(Guid id, Action<EmailMessage> change)
    {
        using var unitOfWork = _unitOfWorkManager.Begin(new AbpUnitOfWorkOptions(isTransactional: true), requiresNew: true);
        var message = await _repository.GetAsync(id);
        change(message);
        await _repository.UpdateAsync(message);
        await unitOfWork.CompleteAsync();
    }
}

/// <summary>The Hangfire job of a queued mail (design 64): <see cref="EmailDispatcher"/> with retries.</summary>
public class EmailSendJob : AsyncBackgroundJob<EmailSendArgs>, ITransientDependency
{
    private readonly EmailDispatcher _dispatcher;

    public EmailSendJob(EmailDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public override Task ExecuteAsync(EmailSendArgs args) => _dispatcher.SendAsync(args);
}
