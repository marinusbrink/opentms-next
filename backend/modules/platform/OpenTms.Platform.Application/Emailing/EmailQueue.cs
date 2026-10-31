using System;
using System.Threading.Tasks;
using OpenTms.Platform.Events;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;

namespace OpenTms.Platform.Emailing;

/// <summary>
/// Puts a mail on the send log in the caller's transaction (design 64, AP-11): the row and the outbox event that enqueues
/// the send job commit together with the fact, or not at all. Outside a transaction the outbox refuses the event
/// (<c>OutboxRequiresTransactionException</c>), so the mail cannot be queued without its fact.
/// </summary>
public class EmailQueue : IEmailQueue, ITransientDependency
{
    private readonly IRepository<EmailMessage, Guid> _repository;
    private readonly IDistributedEventBus _eventBus;
    private readonly IGuidGenerator _guidGenerator;
    private readonly ICurrentTenant _currentTenant;

    public EmailQueue(
        IRepository<EmailMessage, Guid> repository,
        IDistributedEventBus eventBus,
        IGuidGenerator guidGenerator,
        ICurrentTenant currentTenant)
    {
        _repository = repository;
        _eventBus = eventBus;
        _guidGenerator = guidGenerator;
        _currentTenant = currentTenant;
    }

    public async Task<Guid> QueueAsync(QueuedEmail email)
    {
        var message = new EmailMessage(
            _guidGenerator.Create(), _currentTenant.Id, email.To, email.Cc, email.Bcc, email.ReplyTo, email.Subject,
            email.HtmlBody, email.TextBody, email.Tag);
        await _repository.InsertAsync(message);
        await _eventBus.PublishAsync(new EmailQueuedEto { EmailMessageId = message.Id });
        return message.Id;
    }
}
