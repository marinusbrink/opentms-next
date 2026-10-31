using System;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;
using OpenTms.Platform.Emailing;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Emailing;
using Xunit;

namespace OpenTms.EntityFrameworkCore.Platform.Emailing;

/// <summary>
/// ABP's IEmailSender routed through the send log (docs/designs/64-email-delivery-postmark.md, "Sending"): a tenant's
/// mail only with Platform.EmailDelivery on and the mail service configured, host mail whenever it is configured,
/// otherwise logged and not sent; ABP's settings-based From is ignored and the Reply-To kept; attachments are refused, not
/// dropped. Risk class High (constitution rule 4: off by default changes nothing).
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class SendLogEmailSenderTests : EmailDeliveryTestBase
{
    private IEmailSender Sender => GetRequiredService<IEmailSender>();

    private Task<long> CountAsync() => WithUnitOfWorkAsync(() => GetRequiredService<IRepository<EmailMessage, Guid>>().GetCountAsync());

    [Fact]
    public void ABPs_sender_is_the_send_log()
    {
        Sender.ShouldBeOfType<SendLogEmailSender>();
    }

    [Fact]
    public async Task With_the_feature_on_a_tenants_mail_is_queued_in_its_send_log()
    {
        using var _ = InTenantA();
        using var on = DeliveryOn();
        var mail = new MailMessage("noreply@abp.io", "beheerder@klant.example", "Uitnodiging", "<p>Welkom</p>") { IsBodyHtml = true };
        mail.ReplyToList.Add("support@bakker.example");

        await Sender.SendAsync(mail);

        (await CountAsync()).ShouldBe(1);
        var job = Jobs.Of<EmailSendArgs>().ShouldHaveSingleItem();
        job.TenantId.ShouldBe(TenantA);
        await RunSendJobsAsync();
        var sent = Provider.All.ShouldHaveSingleItem().Email;
        sent.To.ShouldBe("beheerder@klant.example");
        sent.Subject.ShouldBe("Uitnodiging");
        sent.HtmlBody.ShouldBe("<p>Welkom</p>");
        sent.TextBody.ShouldBeNull();
        sent.ReplyTo.ShouldBe("support@bakker.example", "ABP's default From never becomes the Reply-To");
    }

    [Fact]
    public async Task With_the_feature_off_a_tenants_mail_is_not_sent()
    {
        using var _ = InTenantA();
        using var off = DeliveryOff();

        await Sender.SendAsync("beheerder@klant.example", "Uitnodiging", "Welkom", isBodyHtml: false);

        (await CountAsync()).ShouldBe(0);
        Jobs.Of<EmailSendArgs>().ShouldBeEmpty();
    }

    [Fact]
    public async Task Host_mail_goes_through_the_send_log_without_a_feature()
    {
        using var _ = InHost();
        using var off = DeliveryOff();

        await Sender.SendAsync("admin@ryd-software.example", "Wachtwoord herstellen", "Klik hier", isBodyHtml: false);

        Jobs.Of<EmailSendArgs>().ShouldHaveSingleItem().TenantId.ShouldBeNull();
        await RunSendJobsAsync();
        Provider.All.ShouldHaveSingleItem().Email.TextBody.ShouldBe("Klik hier");
    }

    [Fact]
    public async Task Without_a_configured_mail_service_nothing_is_queued_for_anyone()
    {
        Provider.IsConfigured = false;
        try
        {
            using (InHost())
            {
                await Sender.SendAsync("admin@ryd-software.example", "S", "B");
            }

            using (InTenantA())
            using (DeliveryOn())
            {
                await Sender.SendAsync("a@klant.example", "S", "B");
            }

            Jobs.Of<EmailSendArgs>().ShouldBeEmpty();
        }
        finally
        {
            Provider.IsConfigured = true;
        }
    }

    [Fact]
    public async Task An_attachment_is_refused_rather_than_dropped()
    {
        using var _ = InTenantA();
        using var on = DeliveryOn();
        var mail = new MailMessage("x@y.example", "a@klant.example", "Factuur", "zie bijlage");
        mail.Attachments.Add(new Attachment(new System.IO.MemoryStream(Encoding.UTF8.GetBytes("%PDF")), "factuur.pdf"));

        await Should.ThrowAsync<NotSupportedException>(() => Sender.SendAsync(mail));
        Jobs.Of<EmailSendArgs>().ShouldBeEmpty();
    }
}
