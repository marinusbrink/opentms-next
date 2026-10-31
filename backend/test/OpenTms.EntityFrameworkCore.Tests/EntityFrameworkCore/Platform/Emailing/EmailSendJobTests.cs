using System;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Platform.Emailing;
using Shouldly;
using Xunit;

namespace OpenTms.EntityFrameworkCore.Platform.Emailing;

/// <summary>
/// Sending from the send log (docs/designs/64-email-delivery-postmark.md, "Sending"): the queue writes the row and its
/// event in the tenant, the event enqueues the job with the tenant, and the job's state machine sends once: only a Queued
/// mail, a permanent rejection fails without a retry, a transient failure goes back to the queue and rethrows, a doubtful
/// outcome is never sent again. The job never sends another tenant's mail. Risk class Critical (idempotent job, at most
/// once on doubt, tenant isolation).
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class EmailSendJobTests : EmailDeliveryTestBase
{
    [Fact]
    public async Task A_queued_mail_is_sent_once_by_the_job_its_event_enqueued_and_routed_to_its_tenant()
    {
        Guid id;
        using (InTenantA())
        {
            id = await QueueAsync();
            (await GetAsync(id)).Status.ShouldBe(EmailMessageStatus.Queued);
        }

        var job = Jobs.Of<EmailSendArgs>().ShouldHaveSingleItem();
        job.TenantId.ShouldBe(TenantA);
        job.EmailMessageId.ShouldBe(id);

        await RunSendJobsAsync();

        var send = Provider.All.ShouldHaveSingleItem();
        send.Tenant.ShouldBe(TenantA, "the job runs in the queuing tenant");
        send.Email.EmailMessageId.ShouldBe(id);
        send.Email.To.ShouldBe("planner@klant.example");
        send.Email.ReplyTo.ShouldBe("planning@bakker.example");
        send.Email.Tag.ShouldBe("TransportOrder");
        using (InTenantA())
        {
            var row = await GetAsync(id);
            row.Status.ShouldBe(EmailMessageStatus.Sent);
            row.Attempts.ShouldBe(1);
            row.SentAt.ShouldNotBeNull();
            var route = await GetRequiredService<IEmailMessageRouteStore>().FindAsync(row.ProviderMessageId!);
            route.ShouldNotBeNull();
            route.MessageTenantId.ShouldBe(TenantA);
            route.EmailMessageId.ShouldBe(id);
        }
    }

    [Fact]
    public async Task A_repeated_job_sends_nothing_again()
    {
        using var _ = InTenantA();
        var id = await QueueAsync();
        var args = Jobs.Drain<EmailSendArgs>().Single();

        await Job.ExecuteAsync(args);
        await Job.ExecuteAsync(args);
        await Job.ExecuteAsync(args);

        Provider.All.Count.ShouldBe(1);
        (await GetAsync(id)).Attempts.ShouldBe(1);
    }

    [Fact]
    public async Task A_permanent_rejection_fails_the_mail_without_a_retry()
    {
        using var _ = InTenantA();
        var id = await QueueAsync();
        Provider.Failure = new EmailRejectedException("406", "inactive recipient");

        await RunSendJobsAsync();

        var row = await GetAsync(id);
        row.Status.ShouldBe(EmailMessageStatus.Failed);
        row.LastError.ShouldBe("406: inactive recipient");
        row.ProviderMessageId.ShouldBeNull();
    }

    [Fact]
    public async Task A_transient_failure_goes_back_to_the_queue_and_the_retry_sends_it()
    {
        using var _ = InTenantA();
        var id = await QueueAsync();
        var args = Jobs.Drain<EmailSendArgs>().Single();
        Provider.Failure = new EmailDeliveryUnavailableException("Postmark answered HTTP 503: busy");

        await Should.ThrowAsync<EmailDeliveryUnavailableException>(() => Job.ExecuteAsync(args));
        var row = await GetAsync(id);
        row.Status.ShouldBe(EmailMessageStatus.Queued);
        row.LastError.ShouldContain("503");

        Provider.Failure = null;
        await Job.ExecuteAsync(args);

        row = await GetAsync(id);
        row.Status.ShouldBe(EmailMessageStatus.Sent);
        row.Attempts.ShouldBe(2);
        row.LastError.ShouldBeNull();
    }

    [Fact]
    public async Task A_doubtful_outcome_is_never_sent_a_second_time()
    {
        using var _ = InTenantA();
        var id = await QueueAsync();
        var args = Jobs.Drain<EmailSendArgs>().Single();
        Provider.Failure = new EmailDeliveryUnavailableException("timed out", outcomeUnknown: true);

        await Should.ThrowAsync<EmailDeliveryUnavailableException>(() => Job.ExecuteAsync(args));
        (await GetAsync(id)).Status.ShouldBe(EmailMessageStatus.Sending);

        Provider.Failure = null;
        await Job.ExecuteAsync(args);

        Provider.All.Count.ShouldBe(1, "the mail may have left on the first attempt");
        var row = await GetAsync(id);
        row.Status.ShouldBe(EmailMessageStatus.Failed);
        row.LastError.ShouldStartWith(EmailMessageConsts.OutcomeUnknownCode);
    }

    [Fact]
    public async Task A_job_naming_another_tenant_never_sends_the_mail()
    {
        Guid id;
        using (InTenantA())
        {
            id = await QueueAsync();
        }

        Jobs.Clear();
        await Job.ExecuteAsync(new EmailSendArgs { TenantId = TenantB, EmailMessageId = id });
        await Job.ExecuteAsync(new EmailSendArgs { TenantId = null, EmailMessageId = id });

        Provider.All.ShouldBeEmpty();
        using (InTenantA())
        {
            (await GetAsync(id)).Status.ShouldBe(EmailMessageStatus.Queued);
        }

        using (InTenantB())
        {
            (await FindAsync(id)).ShouldBeNull("tenant B does not see tenant A's send log");
        }
    }

    [Fact]
    public async Task A_mail_needs_a_recipient_and_a_body()
    {
        using var _ = InTenantA();

        await Should.ThrowAsync<ArgumentException>(() => WithUnitOfWorkAsync(() => Queue.QueueAsync(new QueuedEmail { To = "", Subject = "x", TextBody = "y" })));
        await Should.ThrowAsync<ArgumentException>(() => WithUnitOfWorkAsync(() => Queue.QueueAsync(new QueuedEmail { To = "a@b.example", Subject = "x" })));
        Jobs.Of<EmailSendArgs>().ShouldBeEmpty();
    }
}

/// <summary>The send log row's rules on their own (design 64, "Status rules"). Risk class High.</summary>
public class EmailMessageRulesTests
{
    private static readonly DateTime T = new(2026, 10, 30, 10, 0, 0, DateTimeKind.Utc);

    private static EmailMessage Sending()
    {
        var m = new EmailMessage(Guid.NewGuid(), null, "a@b.example", null, null, null, "S", "<p>x</p>", null, null);
        m.MarkSending();
        return m;
    }

    private static EmailMessage Sent()
    {
        var m = Sending();
        m.MarkSent("pm-1", T);
        return m;
    }

    [Fact]
    public void A_report_that_beats_the_jobs_save_keeps_its_status_when_the_mail_is_marked_sent()
    {
        var m = Sending();

        m.ApplyDelivered(T.AddSeconds(1)).ShouldBeTrue();
        m.MarkSent("pm-1", T);

        m.Status.ShouldBe(EmailMessageStatus.Delivered);
        m.ProviderMessageId.ShouldBe("pm-1");
    }

    [Fact]
    public void Each_transition_happens_once()
    {
        var m = Sent();

        m.ApplyDelivered(T).ShouldBeTrue();
        m.ApplyDelivered(T).ShouldBeFalse();
        m.ApplyBounced(T, "HardBounce", "x").ShouldBeTrue();
        m.ApplyBounced(T, "HardBounce", "x").ShouldBeFalse();
        m.ApplySpamComplaint(T).ShouldBeTrue();
        m.ApplySpamComplaint(T).ShouldBeFalse();
        m.ApplyBounced(T, "SoftBounce", null).ShouldBeFalse("a spam complaint is final");
        m.ApplyDelivered(T).ShouldBeFalse();
        m.Status.ShouldBe(EmailMessageStatus.SpamComplaint);
    }

    [Fact]
    public void A_queued_or_failed_mail_takes_no_report()
    {
        var queued = new EmailMessage(Guid.NewGuid(), null, "a@b.example", null, null, null, "S", null, "x", null);
        queued.ApplyDelivered(T).ShouldBeFalse();
        queued.ApplyBounced(T, "HardBounce", null).ShouldBeFalse();
        queued.ApplyOpened(T);
        queued.FirstOpenedAt.ShouldBeNull();

        var failed = Sending();
        failed.MarkFailed("406: x");
        failed.ApplySpamComplaint(T).ShouldBeFalse();
        failed.Status.ShouldBe(EmailMessageStatus.Failed);
    }

    [Fact]
    public void Opens_and_clicks_keep_the_earliest_and_latest_time_in_any_order_and_never_change_the_status()
    {
        var m = Sent();

        m.ApplyOpened(T.AddHours(2));
        m.ApplyOpened(T);
        m.ApplyOpened(T.AddHours(1));
        m.ApplyClicked(T.AddMinutes(5));
        m.ApplyClicked(T.AddMinutes(5));

        m.FirstOpenedAt.ShouldBe(T);
        m.LastOpenedAt.ShouldBe(T.AddHours(2));
        m.FirstClickedAt.ShouldBe(T.AddMinutes(5));
        m.LastClickedAt.ShouldBe(T.AddMinutes(5));
        m.Status.ShouldBe(EmailMessageStatus.Sent);
    }
}
