using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTms.Platform.Events;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;

namespace OpenTms.Platform.Emailing;

/// <summary>
/// The e-mail status sink (design 64, flow 3 ii–iii): the tenant comes from Platform's own route, never from the report;
/// the report applies in one transaction of that tenant, and only a transition raises an event of the fixed list (bounce,
/// spam complaint). Two reports of one mail at once collide on the concurrency stamp; the loser is applied again.
/// </summary>
public class EmailStatusSink : IEmailStatusSink, ITransientDependency
{
    public ILogger<EmailStatusSink> Logger { get; set; } = NullLogger<EmailStatusSink>.Instance;

    private readonly IEmailMessageRouteStore _routes;
    private readonly IRepository<EmailMessage, Guid> _repository;
    private readonly IDistributedEventBus _eventBus;
    private readonly ICurrentTenant _currentTenant;
    private readonly IUnitOfWorkManager _unitOfWorkManager;

    public EmailStatusSink(
        IEmailMessageRouteStore routes,
        IRepository<EmailMessage, Guid> repository,
        IDistributedEventBus eventBus,
        ICurrentTenant currentTenant,
        IUnitOfWorkManager unitOfWorkManager)
    {
        _routes = routes;
        _repository = repository;
        _eventBus = eventBus;
        _currentTenant = currentTenant;
        _unitOfWorkManager = unitOfWorkManager;
    }

    public async Task<bool> RecordAsync(EmailDeliveryReport report)
    {
        if (string.IsNullOrWhiteSpace(report.ProviderMessageId))
        {
            return false;
        }

        var route = await _routes.FindAsync(report.ProviderMessageId);
        if (route == null)
        {
            Logger.LogDebug("Delivery report {ReportType} for {ProviderMessageId}: no mail of this system; ignored.", report.Type, report.ProviderMessageId);
            return false;
        }

        using (_currentTenant.Change(route.MessageTenantId))
        {
            try
            {
                return await ApplyAsync(route.EmailMessageId, report);
            }
            catch (AbpDbConcurrencyException)
            {
                return await ApplyAsync(route.EmailMessageId, report);
            }
        }
    }

    private async Task<bool> ApplyAsync(Guid emailMessageId, EmailDeliveryReport report)
    {
        using var unitOfWork = _unitOfWorkManager.Begin(new AbpUnitOfWorkOptions(isTransactional: true), requiresNew: true);
        var message = await _repository.FindAsync(emailMessageId);
        if (message == null)
        {
            await unitOfWork.CompleteAsync();
            return false;
        }

        switch (report.Type)
        {
            case EmailDeliveryReportType.Delivered:
                message.ApplyDelivered(report.OccurredAt);
                break;
            case EmailDeliveryReportType.Bounced:
                if (message.ApplyBounced(report.OccurredAt, report.BounceType, report.Description))
                {
                    await _eventBus.PublishAsync(new EmailBouncedEto { EmailMessageId = message.Id, BounceType = message.BounceType });
                }

                break;
            case EmailDeliveryReportType.SpamComplaint:
                if (message.ApplySpamComplaint(report.OccurredAt))
                {
                    await _eventBus.PublishAsync(new EmailSpamComplaintReceivedEto { EmailMessageId = message.Id });
                }

                break;
            case EmailDeliveryReportType.Opened:
                message.ApplyOpened(report.OccurredAt);
                break;
            case EmailDeliveryReportType.Clicked:
                message.ApplyClicked(report.OccurredAt);
                break;
        }

        await _repository.UpdateAsync(message);
        await unitOfWork.CompleteAsync();
        Logger.LogInformation("Delivery report {ReportType} applied to mail {EmailMessageId}; status {Status}.", report.Type, message.Id, message.Status);
        return true;
    }
}
