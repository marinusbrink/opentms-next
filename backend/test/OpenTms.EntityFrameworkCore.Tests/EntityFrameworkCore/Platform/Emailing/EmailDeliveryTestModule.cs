using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenTms.Integrations.Emailing.Postmark;
using OpenTms.Platform.Emailing;
using OpenTms.Platform.Events;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;

namespace OpenTms.EntityFrameworkCore.Platform.Emailing;

/// <summary>
/// The SQLite test application of design 64 with the mail service replaced by <see cref="FakeEmailDeliveryProvider"/>
/// (the Postmark adapter is removed, so nothing can reach FiLogic's server) and test webhook credentials. Everything
/// else is the production wiring: the send log, the job, the sink, the host route store, ABP's IEmailSender, the feature
/// checker and the tenant filter. Jobs land in the shared <c>RecordingBackgroundJobManager</c>; the e-mail events in
/// <see cref="EmailEventLog"/>.
/// </summary>
[DependsOn(typeof(OpenTmsEntityFrameworkCoreTestModule))]
public class EmailDeliveryTestModule : AbpModule
{
    public const string WebhookUser = "postmark-qq";
    public const string WebhookPassword = "qq-webhook-pass-31";

    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var provider = new FakeEmailDeliveryProvider();
        context.Services.AddSingleton(provider);
        context.Services.Replace(ServiceDescriptor.Singleton<IEmailDeliveryProvider>(provider));
        context.Services.Configure<Volo.Abp.UI.Navigation.Urls.AppUrlOptions>(o => o.Applications["MVC"].RootUrl = "https://api.opentms.example.test");
        context.Services.Configure<PostmarkOptions>(o =>
        {
            o.WebhookUsername = WebhookUser;
            o.WebhookPassword = WebhookPassword;
        });
    }
}

/// <summary>One send the fake mail service received, with the tenant that was current.</summary>
public sealed record FakeSend(OutgoingEmail Email, Guid? Tenant);

/// <summary>A scriptable mail service that records every send and the tenant it ran in.</summary>
public sealed class FakeEmailDeliveryProvider : IEmailDeliveryProvider
{
    private int _counter;

    public ICurrentTenant? CurrentTenant { get; set; }

    public ConcurrentQueue<FakeSend> Sends { get; } = new();

    public bool IsConfigured { get; set; } = true;

    public Task<EmailDeliveryStatus> GetStatusAsync() => Task.FromResult(new EmailDeliveryStatus
    {
        IsConfigured = IsConfigured, ServerTokenSource = IsConfigured ? EmailSettingSource.Configuration : EmailSettingSource.None,
        FromAddress = "noreply@ryd-software.com", FromName = "OpenTMS"
    });

    /// <summary>Thrown by the next sends while set.</summary>
    public Exception? Failure { get; set; }

    /// <summary>Runs inside the send, after it is recorded (to deliver a report "while" the job is between calls).</summary>
    public Func<string, Task>? DuringSend { get; set; }

    public async Task<string> SendAsync(OutgoingEmail email, CancellationToken cancellationToken)
    {
        Sends.Enqueue(new FakeSend(email, CurrentTenant?.Id));
        if (Failure != null)
        {
            throw Failure;
        }

        var id = $"pm-{Interlocked.Increment(ref _counter)}-{Guid.NewGuid():N}";
        if (DuringSend != null)
        {
            await DuringSend(id);
        }

        return id;
    }

    public List<FakeSend> All => Sends.ToList();
}

/// <summary>The e-mail events delivered in the test process, with the tenant they were handled in.</summary>
public sealed class EmailEventLog : ISingletonDependency
{
    public ConcurrentQueue<(object Event, Guid? Tenant)> Events { get; } = new();

    public IReadOnlyList<T> Of<T>() => Events.Select(e => e.Event).OfType<T>().ToList();

    public IReadOnlyList<(T Event, Guid? Tenant)> WithTenant<T>() => Events.Where(e => e.Event is T).Select(e => ((T)e.Event, e.Tenant)).ToList();
}

public sealed class EmailEventRecorder :
    IDistributedEventHandler<EmailBouncedEto>,
    IDistributedEventHandler<EmailSpamComplaintReceivedEto>,
    ITransientDependency
{
    private readonly EmailEventLog _log;
    private readonly ICurrentTenant _currentTenant;

    public EmailEventRecorder(EmailEventLog log, ICurrentTenant currentTenant)
    {
        _log = log;
        _currentTenant = currentTenant;
    }

    public Task HandleEventAsync(EmailBouncedEto eventData)
    {
        _log.Events.Enqueue((eventData, _currentTenant.Id));
        return Task.CompletedTask;
    }

    public Task HandleEventAsync(EmailSpamComplaintReceivedEto eventData)
    {
        _log.Events.Enqueue((eventData, _currentTenant.Id));
        return Task.CompletedTask;
    }
}
