using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using OpenTms.Platform.Emailing;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.SettingManagement;
using IEmailSettingsAppService = OpenTms.Platform.Emailing.IEmailSettingsAppService;
using UpdateEmailSettingsDto = OpenTms.Platform.Emailing.UpdateEmailSettingsDto;

using Volo.Abp.Settings;
using Xunit;

namespace OpenTms.EntityFrameworkCore.Platform.Emailing;

/// <summary>
/// The host's e-mail settings (docs/designs/64-email-delivery-postmark.md, "Host e-mail settings"): host only (a tenant is
/// refused even where authorization is relaxed), global values in the host database, the token and the webhook password
/// write-only and encrypted at rest, an empty secret keeps the stored one, and the test mail goes through the host's send
/// log at once with the mail service's answer. Risk class Critical (secrets, host-only configuration of every tenant's
/// mail), High (test mail).
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class EmailSettingsAppServiceTests : EmailDeliveryTestBase
{
    private const string Token = "qq-host-token-5c1e9a";
    private const string Password = "qq-host-webhook-pass-42";

    private IEmailSettingsAppService Service => GetRequiredService<IEmailSettingsAppService>();

    private static UpdateEmailSettingsDto Full() => new()
    {
        ServerToken = Token,
        FromAddress = "noreply@ryd-software.com",
        FromName = "Ryd",
        TrackOpens = false,
        TrackLinks = "HtmlOnly",
        WebhookUsername = "postmark",
        WebhookPassword = Password
    };

    private Task<string?> RawAsync(string name) => WithUnitOfWorkAsync(async () =>
        (await GetRequiredService<ISettingRepository>().FindAsync(name, GlobalSettingValueProvider.ProviderName, null))?.Value);

    [Fact]
    public async Task Saved_settings_come_back_without_the_secrets_and_the_secrets_are_encrypted_at_rest()
    {
        using var _ = InHost();

        await Service.UpdateAsync(Full());
        var saved = await Service.GetAsync();

        saved.HasServerToken.ShouldBeTrue();
        saved.HasWebhookPassword.ShouldBeTrue();
        saved.FromAddress.ShouldBe("noreply@ryd-software.com");
        saved.FromName.ShouldBe("Ryd");
        saved.TrackOpens.ShouldBe(false);
        saved.TrackLinks.ShouldBe("HtmlOnly");
        saved.WebhookUsername.ShouldBe("postmark");
        saved.WebhookUrl.ShouldBe("https://api.opentms.example.test/" + EmailSettingsConsts.WebhookPath);
        var json = JsonSerializer.Serialize(await Service.GetAsync());
        json.ShouldNotContain(Token);
        json.ShouldNotContain(Password);

        var rawToken = await RawAsync(EmailSettingNames.ServerToken);
        rawToken.ShouldNotBeNullOrEmpty();
        rawToken.ShouldNotBe(Token, "the token is stored encrypted");
        (await RawAsync(EmailSettingNames.WebhookPassword)).ShouldNotBe(Password);
        (await GetRequiredService<ISettingManager>().GetOrNullGlobalAsync(EmailSettingNames.ServerToken)).ShouldBe(Token, "and decrypts for the mail service");
    }

    [Fact]
    public async Task An_empty_secret_keeps_the_stored_one_and_clear_removes_it()
    {
        using var _ = InHost();
        await Service.UpdateAsync(Full());

        var kept = Full();
        kept.ServerToken = "";
        kept.WebhookPassword = null;
        await Service.UpdateAsync(kept);
        (await GetRequiredService<ISettingManager>().GetOrNullGlobalAsync(EmailSettingNames.ServerToken)).ShouldBe(Token);

        var cleared = Full();
        cleared.ServerToken = null;
        cleared.ClearServerToken = true;
        cleared.ClearWebhookPassword = true;
        await Service.UpdateAsync(cleared);
        var result = await Service.GetAsync();

        (await RawAsync(EmailSettingNames.ServerToken)).ShouldBeNull();
        result.HasServerToken.ShouldBeFalse();
        result.HasWebhookPassword.ShouldBeFalse();
    }

    [Fact]
    public async Task An_empty_field_falls_back_to_the_configuration()
    {
        using var _ = InHost();
        await Service.UpdateAsync(Full());

        await Service.UpdateAsync(new UpdateEmailSettingsDto());
        var result = await Service.GetAsync();

        result.FromAddress.ShouldBeNull();
        result.FromName.ShouldBeNull();
        result.TrackOpens.ShouldBeNull();
        result.HasServerToken.ShouldBeTrue("the token is only removed explicitly");
    }

    [Fact]
    public async Task An_unknown_link_tracking_mode_is_refused()
    {
        using var _ = InHost();
        var input = Full();
        input.TrackLinks = "Everything";

        await Should.ThrowAsync<UserFriendlyException>(() => Service.UpdateAsync(input));
    }

    [Fact]
    public async Task A_tenant_can_neither_read_change_nor_test_the_hosts_settings()
    {
        using var _ = InTenantA();

        await Should.ThrowAsync<AbpAuthorizationException>(() => Service.GetAsync());
        await Should.ThrowAsync<AbpAuthorizationException>(() => Service.UpdateAsync(Full()));
        await Should.ThrowAsync<AbpAuthorizationException>(() => Service.SendTestAsync(new SendTestEmailDto { To = "a@klant.example" }));
        Provider.All.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_test_mail_is_sent_at_once_from_the_hosts_send_log()
    {
        using var _ = InHost();

        var result = await Service.SendTestAsync(new SendTestEmailDto { To = "beheer@ryd-software.example" });

        result.Succeeded.ShouldBeTrue();
        result.Status.ShouldBe(EmailMessageStatus.Sent);
        result.ProviderMessageId.ShouldNotBeNullOrEmpty();
        var send = Provider.All.ShouldHaveSingleItem();
        send.Tenant.ShouldBeNull();
        send.Email.To.ShouldBe("beheer@ryd-software.example");
        send.Email.Tag.ShouldBe(EmailSettingsConsts.TestEmailTag);
        send.Email.Subject.ShouldNotBeNullOrWhiteSpace();
        Jobs.Of<EmailSendArgs>().ShouldBeEmpty("the test mail is not queued for a job");
    }

    [Fact]
    public async Task A_refused_or_unreachable_test_mail_reports_the_error_and_is_not_left_in_the_queue()
    {
        using var _ = InHost();
        Provider.Failure = new EmailRejectedException("400", "The 'From' address you supplied is not a Sender Signature on your account.");

        var refused = await Service.SendTestAsync(new SendTestEmailDto { To = "beheer@ryd-software.example" });

        refused.Succeeded.ShouldBeFalse();
        refused.Status.ShouldBe(EmailMessageStatus.Failed);
        refused.Error.ShouldNotBeNull().ShouldContain("Sender Signature");

        Provider.Failure = new EmailDeliveryUnavailableException("Postmark answered HTTP 503: busy");
        var unavailable = await Service.SendTestAsync(new SendTestEmailDto { To = "beheer@ryd-software.example" });

        unavailable.Status.ShouldBe(EmailMessageStatus.Failed, "no job retries a test mail");
        unavailable.Error.ShouldNotBeNull().ShouldContain("503");
    }

    [Fact]
    public async Task Without_a_configured_mail_service_the_test_mail_is_not_attempted()
    {
        using var _ = InHost();
        Provider.IsConfigured = false;
        try
        {
            var result = await Service.SendTestAsync(new SendTestEmailDto { To = "beheer@ryd-software.example" });

            result.Succeeded.ShouldBeFalse();
            result.Error.ShouldNotBeNullOrWhiteSpace();
            Provider.All.ShouldBeEmpty();
            (await WithUnitOfWorkAsync(() => GetRequiredService<IRepository<EmailMessage, Guid>>().GetCountAsync())).ShouldBe(0);
        }
        finally
        {
            Provider.IsConfigured = true;
        }
    }
}
