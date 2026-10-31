using System;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Features;
using OpenTms.Platform.Emailing;
using OpenTms.Platform.Features;
using OpenTms.TestDoubles;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;

namespace OpenTms.EntityFrameworkCore.Platform.Emailing;

/// <summary>
/// Shared plumbing of the design-64 tests: two tenants, the feature switch, queuing a mail through the production queue,
/// running the send jobs the queue event enqueued, and reading rows past nothing but the tenant filter.
/// </summary>
public abstract class EmailDeliveryTestBase : OpenTmsTestBase<EmailDeliveryTestModule>
{
    protected EmailDeliveryTestBase()
    {
        CurrentTenant = GetRequiredService<ICurrentTenant>();
        Provider = GetRequiredService<FakeEmailDeliveryProvider>();
        Provider.CurrentTenant = CurrentTenant;
        Jobs = GetRequiredService<RecordingBackgroundJobManager>();
        Jobs.Clear();
        Events = GetRequiredService<EmailEventLog>();
        Events.Events.Clear();
    }

    protected ICurrentTenant CurrentTenant { get; }

    protected FakeEmailDeliveryProvider Provider { get; }

    protected RecordingBackgroundJobManager Jobs { get; }

    protected EmailEventLog Events { get; }

    protected Guid TenantA { get; } = Guid.NewGuid();

    protected Guid TenantB { get; } = Guid.NewGuid();

    protected IDisposable InTenantA() => CurrentTenant.Change(TenantA);

    protected IDisposable InTenantB() => CurrentTenant.Change(TenantB);

    protected IDisposable InHost() => CurrentTenant.Change(null);

    protected static IDisposable DeliveryOn() => TestFeatureValueProvider.Override(PlatformFeatures.EmailDelivery, "true");

    protected static IDisposable DeliveryOff() => TestFeatureValueProvider.Override(PlatformFeatures.EmailDelivery, "false");

    protected IEmailQueue Queue => GetRequiredService<IEmailQueue>();

    protected IEmailStatusSink Sink => GetRequiredService<IEmailStatusSink>();

    protected EmailSendJob Job => GetRequiredService<EmailSendJob>();

    protected Task<Guid> QueueAsync(string to = "planner@klant.example", string subject = "Orderbevestiging") =>
        WithUnitOfWorkAsync(() => Queue.QueueAsync(new QueuedEmail
        {
            To = to, ReplyTo = "planning@bakker.example", Subject = subject, HtmlBody = "<p>Hallo</p>", Tag = "TransportOrder"
        }));

    /// <summary>Runs every send job the queue events enqueued, as Hangfire would.</summary>
    protected async Task RunSendJobsAsync()
    {
        foreach (var args in Jobs.Drain<EmailSendArgs>())
        {
            await Job.ExecuteAsync(args);
        }
    }

    /// <summary>The row as the current tenant sees it (the tenant filter applies).</summary>
    protected Task<EmailMessage?> FindAsync(Guid id) =>
        WithUnitOfWorkAsync(() => GetRequiredService<IRepository<EmailMessage, Guid>>().FindAsync(id));

    protected async Task<EmailMessage> GetAsync(Guid id) => (await FindAsync(id))!;

    protected async Task<string> SentIdAsync(Guid id) => (await GetAsync(id)).ProviderMessageId!;

    protected Task<bool> ReportAsync(string providerMessageId, EmailDeliveryReportType type, DateTime? at = null, string? bounceType = null) =>
        Sink.RecordAsync(new EmailDeliveryReport
        {
            ProviderMessageId = providerMessageId, Type = type, OccurredAt = at ?? new DateTime(2026, 10, 30, 10, 0, 0, DateTimeKind.Utc),
            BounceType = bounceType, Description = bounceType == null ? null : "mailbox not found"
        });

    protected int SendsTo(string address) => Provider.All.Count(s => s.Email.To == address);
}
