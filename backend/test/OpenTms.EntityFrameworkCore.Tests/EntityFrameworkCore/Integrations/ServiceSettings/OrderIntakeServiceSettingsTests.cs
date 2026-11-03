using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using OpenTms.EntityFrameworkCore.Integrations.Routing;
using OpenTms.Integrations.OrderIntake.Channels;
using OpenTms.Integrations.ServiceSettings;
using Shouldly;
using Volo.Abp.MultiTenancy;
using Volo.Abp.SettingManagement;
using Volo.Abp.Settings;
using Xunit;

namespace OpenTms.EntityFrameworkCore.Integrations.ServiceSettings;

/// <summary>
/// The host tab Integrations of design 72 ("Host settings"): the Anthropic key and the Google and Microsoft client secrets are
/// encrypted at rest and write-only (the answer says only whether one is stored), an empty secret keeps the stored one, a
/// clear removes it, the models and client ids come back as saved, the redirect URI to register is shown, and a tenant is
/// refused. Risk class: Medium per the design (settings screen); the secrets part is security-relevant.
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class OrderIntakeServiceSettingsTests : OpenTmsTestBase<RoutingTestModule>
{
    private const string AnthropicKey = "sk-ant-qq-5f1e";
    private const string GoogleSecret = "GOCSPX-qq-77a1";
    private const string MicrosoftSecret = "ms-qq-secret-0b9c";

    private IServiceSettingsAppService Service => GetRequiredService<IServiceSettingsAppService>();

    private Task<string?> RawAsync(string name) => WithUnitOfWorkAsync(async () =>
        (await GetRequiredService<ISettingRepository>().FindAsync(name, GlobalSettingValueProvider.ProviderName, null))?.Value);

    private static UpdateServiceSettingsDto Intake(string? key = AnthropicKey, string? google = GoogleSecret, string? microsoft = MicrosoftSecret,
        bool clearKey = false, bool clearGoogle = false, bool clearMicrosoft = false) => new()
    {
        AiApiKey = key, ClearAiApiKey = clearKey, AiLearningModel = "claude-opus-5-5", AiExtractionModel = "claude-sonnet-5",
        GoogleClientId = "google-qq.apps.googleusercontent.com", GoogleClientSecret = google, ClearGoogleClientSecret = clearGoogle,
        MicrosoftClientId = "8f1e-qq", MicrosoftClientSecret = microsoft, ClearMicrosoftClientSecret = clearMicrosoft
    };

    [Fact]
    public async Task The_secrets_are_encrypted_at_rest_and_never_come_back()
    {
        await Service.UpdateAsync(Intake());
        var saved = await Service.GetAsync();

        (saved.HasAiApiKey, saved.HasGoogleClientSecret, saved.HasMicrosoftClientSecret).ShouldBe((true, true, true));
        saved.AiLearningModel.ShouldBe("claude-opus-5-5");
        saved.AiExtractionModel.ShouldBe("claude-sonnet-5");
        saved.GoogleClientId.ShouldBe("google-qq.apps.googleusercontent.com");
        saved.MicrosoftClientId.ShouldBe("8f1e-qq");
        var json = JsonSerializer.Serialize(saved);
        foreach (var secret in new[] { AnthropicKey, GoogleSecret, MicrosoftSecret })
        {
            json.ShouldNotContain(secret);
        }

        foreach (var (name, secret) in new[]
                 {
                     (ServiceSettingNames.Ai.ApiKey, AnthropicKey), (ServiceSettingNames.Google.ClientSecret, GoogleSecret),
                     (ServiceSettingNames.Microsoft.ClientSecret, MicrosoftSecret)
                 })
        {
            var raw = await RawAsync(name);
            raw.ShouldNotBeNullOrEmpty(name);
            raw.ShouldNotContain(secret, Case.Sensitive, name + " is encrypted at rest");
        }
    }

    [Fact]
    public async Task An_empty_secret_keeps_the_stored_one_and_a_clear_removes_it()
    {
        await Service.UpdateAsync(Intake());
        await Service.UpdateAsync(Intake(key: null, google: "", microsoft: null));

        var kept = await Service.GetAsync();
        (kept.HasAiApiKey, kept.HasGoogleClientSecret, kept.HasMicrosoftClientSecret).ShouldBe((true, true, true));

        await Service.UpdateAsync(Intake(key: null, google: null, microsoft: null, clearKey: true, clearGoogle: true, clearMicrosoft: true));
        var cleared = await Service.GetAsync();
        (cleared.HasAiApiKey, cleared.HasGoogleClientSecret, cleared.HasMicrosoftClientSecret).ShouldBe((false, false, false));
        (await RawAsync(ServiceSettingNames.Ai.ApiKey)).ShouldBeNullOrEmpty();
    }

    [Fact]
    public async Task A_saved_oauth_client_reaches_the_mail_clients_without_a_restart()
    {
        await Service.UpdateAsync(Intake());

        var options = GetRequiredService<IOptionsMonitor<MailOAuthOptions>>().CurrentValue;
        options.Google.ClientId.ShouldBe("google-qq.apps.googleusercontent.com");
        options.Google.ClientSecret.ShouldBe(GoogleSecret, "decrypted for the adapter, never for the screen");
        options.Microsoft.ClientSecret.ShouldBe(MicrosoftSecret);
    }

    [Fact]
    public async Task A_tenant_cannot_read_or_change_the_host_settings()
    {
        using (GetRequiredService<ICurrentTenant>().Change(Guid.NewGuid()))
        {
            await Should.ThrowAsync<Exception>(() => Service.GetAsync());
            await Should.ThrowAsync<Exception>(() => Service.UpdateAsync(Intake()));
        }

        (await RawAsync(ServiceSettingNames.Ai.ApiKey)).ShouldBeNull();
    }
}
