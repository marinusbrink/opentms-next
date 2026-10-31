using System;
using System.Text;
using System.Threading.Tasks;
using OpenTms.Integrations.Emailing;
using OpenTms.Platform.Emailing;
using OpenTms.Platform.Events;
using Shouldly;
using Xunit;

namespace OpenTms.EntityFrameworkCore.Platform.Emailing;

/// <summary>
/// Delivery reports on the send log (docs/designs/64-email-delivery-postmark.md, "Delivery reports"): the sink finds the
/// tenant in Platform's own route, never in the report; a report changes only that tenant's row; repeats change nothing
/// and raise no second event; bounce and spam raise their event once, in the mail's tenant. The last test runs the real
/// Postmark processor over the sink with a payload whose metadata names another tenant. Risk class Critical (tenant
/// routing and isolation), High (status rules and events once).
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class EmailStatusSinkTests : EmailDeliveryTestBase
{
    private async Task<(Guid Id, string ProviderId)> SentMailAsync(string to = "planner@klant.example")
    {
        var id = await QueueAsync(to);
        await RunSendJobsAsync();
        return (id, await SentIdAsync(id));
    }

    [Fact]
    public async Task A_delivery_moves_the_mail_on_once()
    {
        using var _ = InTenantA();
        var (id, providerId) = await SentMailAsync();
        var at = new DateTime(2026, 10, 30, 11, 0, 0, DateTimeKind.Utc);

        (await ReportAsync(providerId, EmailDeliveryReportType.Delivered, at)).ShouldBeTrue();
        (await ReportAsync(providerId, EmailDeliveryReportType.Delivered, at.AddMinutes(1))).ShouldBeTrue();

        var row = await GetAsync(id);
        row.Status.ShouldBe(EmailMessageStatus.Delivered);
        row.DeliveredAt.ShouldBe(at, "a repeated report keeps the first time");
        Events.Events.ShouldBeEmpty("a delivery is not on the fixed list");
    }

    [Fact]
    public async Task A_bounce_raises_its_event_once_in_the_mails_tenant()
    {
        using var _ = InTenantA();
        var (id, providerId) = await SentMailAsync();

        await ReportAsync(providerId, EmailDeliveryReportType.Delivered);
        await ReportAsync(providerId, EmailDeliveryReportType.Bounced, bounceType: "HardBounce");
        await ReportAsync(providerId, EmailDeliveryReportType.Bounced, bounceType: "HardBounce");

        var row = await GetAsync(id);
        row.Status.ShouldBe(EmailMessageStatus.Bounced);
        row.BounceType.ShouldBe("HardBounce");
        row.BounceDescription.ShouldBe("mailbox not found");
        var (bounced, tenant) = Events.WithTenant<EmailBouncedEto>().ShouldHaveSingleItem();
        bounced.EmailMessageId.ShouldBe(id);
        bounced.BounceType.ShouldBe("HardBounce");
        tenant.ShouldBe(TenantA);
    }

    [Fact]
    public async Task A_spam_complaint_after_delivery_raises_its_event_once()
    {
        using var _ = InTenantA();
        var (id, providerId) = await SentMailAsync();

        await ReportAsync(providerId, EmailDeliveryReportType.Delivered);
        await ReportAsync(providerId, EmailDeliveryReportType.SpamComplaint);
        await ReportAsync(providerId, EmailDeliveryReportType.SpamComplaint);

        (await GetAsync(id)).Status.ShouldBe(EmailMessageStatus.SpamComplaint);
        Events.Of<EmailSpamComplaintReceivedEto>().ShouldHaveSingleItem().EmailMessageId.ShouldBe(id);
        Events.Of<EmailBouncedEto>().ShouldBeEmpty();
    }

    [Fact]
    public async Task Opens_and_clicks_record_first_and_last_times_without_an_event_or_status_change()
    {
        using var _ = InTenantA();
        var (id, providerId) = await SentMailAsync();
        var t = new DateTime(2026, 10, 30, 9, 0, 0, DateTimeKind.Utc);

        await ReportAsync(providerId, EmailDeliveryReportType.Opened, t.AddHours(3));
        await ReportAsync(providerId, EmailDeliveryReportType.Opened, t);
        await ReportAsync(providerId, EmailDeliveryReportType.Clicked, t.AddHours(1));

        var row = await GetAsync(id);
        row.Status.ShouldBe(EmailMessageStatus.Sent);
        row.FirstOpenedAt.ShouldBe(t);
        row.LastOpenedAt.ShouldBe(t.AddHours(3));
        row.FirstClickedAt.ShouldBe(t.AddHours(1));
        Events.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_report_for_another_systems_mail_matches_nothing()
    {
        (await ReportAsync("pm-not-ours", EmailDeliveryReportType.Bounced, bounceType: "HardBounce")).ShouldBeFalse();
        (await ReportAsync("", EmailDeliveryReportType.Delivered)).ShouldBeFalse();
        Events.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_report_changes_only_its_own_tenants_mail_whatever_tenant_the_caller_is_in()
    {
        Guid idA, idB;
        string providerA;
        using (InTenantA())
        {
            (idA, providerA) = await SentMailAsync("a@klant.example");
        }

        using (InTenantB())
        {
            (idB, _) = await SentMailAsync("b@klant.example");
        }

        /* The webhook arrives without a tenant; a caller in tenant B gets the same result. */
        using (InHost())
        {
            (await ReportAsync(providerA, EmailDeliveryReportType.Bounced, bounceType: "HardBounce")).ShouldBeTrue();
        }

        using (InTenantB())
        {
            await ReportAsync(providerA, EmailDeliveryReportType.SpamComplaint);
            (await GetAsync(idB)).Status.ShouldBe(EmailMessageStatus.Sent, "tenant B's mail is untouched");
            (await FindAsync(idA)).ShouldBeNull("tenant B never sees tenant A's mail");
        }

        using (InTenantA())
        {
            (await GetAsync(idA)).Status.ShouldBe(EmailMessageStatus.SpamComplaint);
        }

        Events.WithTenant<EmailBouncedEto>().ShouldHaveSingleItem().Tenant.ShouldBe(TenantA);
        Events.WithTenant<EmailSpamComplaintReceivedEto>().ShouldHaveSingleItem().Tenant.ShouldBe(TenantA);
    }

    [Fact]
    public async Task A_route_naming_the_wrong_tenant_cannot_reach_another_tenants_mail()
    {
        Guid idB;
        using (InTenantB())
        {
            (idB, _) = await SentMailAsync();
        }

        /* A route (corrupt, or forged by a bug) that names tenant A for tenant B's row: the tenant filter stops it. */
        await GetRequiredService<IEmailMessageRouteStore>().AddAsync("pm-forged", TenantA, idB);

        (await ReportAsync("pm-forged", EmailDeliveryReportType.Bounced, bounceType: "HardBounce")).ShouldBeFalse();
        using (InTenantB())
        {
            (await GetAsync(idB)).Status.ShouldBe(EmailMessageStatus.Sent);
        }

        Events.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_Postmark_webhook_ignores_a_tenant_named_in_the_payload()
    {
        Guid idA, idB;
        string providerA;
        using (InTenantA())
        {
            (idA, providerA) = await SentMailAsync();
        }

        using (InTenantB())
        {
            (idB, _) = await SentMailAsync();
        }

        var processor = GetRequiredService<IPostmarkWebhookProcessor>();
        var auth = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(EmailDeliveryTestModule.WebhookUser + ":" + EmailDeliveryTestModule.WebhookPassword));
        var body = $$$"""{"RecordType":"Bounce","Type":"HardBounce","MessageID":"{{{providerA}}}","BouncedAt":"2026-10-30T16:09:19Z","Description":"x","Metadata":{"opentms-tenant-id":"{{{TenantB}}}","opentms-message-id":"{{{idB}}}"}}""";

        (await processor.ProcessAsync(auth, body)).ShouldBe(PostmarkWebhookOutcome.Accepted);
        (await processor.ProcessAsync("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("postmark-qq:wrong")), body)).ShouldBe(PostmarkWebhookOutcome.Unauthorized);

        using (InTenantA())
        {
            (await GetAsync(idA)).Status.ShouldBe(EmailMessageStatus.Bounced);
        }

        using (InTenantB())
        {
            (await GetAsync(idB)).Status.ShouldBe(EmailMessageStatus.Sent);
        }
    }
}
