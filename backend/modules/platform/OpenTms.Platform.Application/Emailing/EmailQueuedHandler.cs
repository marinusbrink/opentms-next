using System.Threading.Tasks;
using OpenTms.Platform.Events;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.MultiTenancy;

namespace OpenTms.Platform.Emailing;

/// <summary>
/// Turns a queued mail into a send job (design 64), once the transaction that queued it has committed: the outbox
/// delivers the event afterwards, in the queuing tenant. A duplicate delivery enqueues a second job that skips.
/// </summary>
public class EmailQueuedHandler : IDistributedEventHandler<EmailQueuedEto>, ITransientDependency
{
    private readonly IBackgroundJobManager _jobs;
    private readonly ICurrentTenant _currentTenant;

    public EmailQueuedHandler(IBackgroundJobManager jobs, ICurrentTenant currentTenant)
    {
        _jobs = jobs;
        _currentTenant = currentTenant;
    }

    public async Task HandleEventAsync(EmailQueuedEto eventData)
    {
        await _jobs.EnqueueAsync(new EmailSendArgs { TenantId = _currentTenant.Id, EmailMessageId = eventData.EmailMessageId });
    }
}
