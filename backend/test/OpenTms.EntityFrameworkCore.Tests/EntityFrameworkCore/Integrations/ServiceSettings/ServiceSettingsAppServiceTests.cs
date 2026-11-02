using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using OpenTms.EntityFrameworkCore.Integrations.Routing;
using OpenTms.Integrations.DocumentRendering.PdfGeneratorApi;
using OpenTms.Integrations.Routing;
using OpenTms.Integrations.ServiceSettings;
using OpenTms.Platform.Maintenance;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.MultiTenancy;
using Volo.Abp.SettingManagement;
using Volo.Abp.Settings;
using Xunit;

namespace OpenTms.EntityFrameworkCore.Integrations.ServiceSettings;

/// <summary>
/// Administration → Services (docs/designs/maintenance-view.md, "Services"), modelled on the host e-mail settings: host
/// only (a tenant is refused even where authorization is relaxed), global values in the host database, keys and secrets
/// write-only and encrypted at rest, an empty secret keeps the stored one, an empty field falls back to the deployment's
/// configuration, a save reaches the adapters without a restart, and the test runs the services' own health checks.
/// Over the routing test module (OSRM and the route function configured, answered by a recording handler). Risk class
/// Critical (secrets; one host-side configuration of every tenant's documents and routing), High (test).
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class ServiceSettingsAppServiceTests : OpenTmsTestBase<RoutingTestModule>
{
    private const string Key = "qq-pdf-key-91f2";
    private const string Secret = "qq-pdf-secret-7d40";
    private const string FunctionKey = "qq-function-key-33aa";

    private readonly IServiceSettingsAppService _service;
    private readonly ICurrentTenant _currentTenant;
    private readonly RoutingHttpHandler _handler;

    public ServiceSettingsAppServiceTests()
    {
        _service = GetRequiredService<IServiceSettingsAppService>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
        _handler = GetRequiredService<RoutingHttpHandler>();
    }

    private static UpdateServiceSettingsDto Full() => new()
    {
        PdfGeneratorApiBaseUrl = "https://eu1.pdfgeneratorapi.com/api/v4/",
        PdfGeneratorApiKey = Key,
        PdfGeneratorApiSecret = Secret,
        RoutingProvider = RoutingConsts.OsrmProviderName,
        OsrmBaseUrl = "https://osrm-saved.invalid/",
        RouteFunctionBaseUrl = "https://function-saved.invalid/route",
        RouteFunctionApiKey = FunctionKey,
        VroomBaseUrl = "https://vroom-saved.invalid/"
    };

    private Task<string?> RawAsync(string name) => WithUnitOfWorkAsync(async () =>
        (await GetRequiredService<ISettingRepository>().FindAsync(name, GlobalSettingValueProvider.ProviderName, null))?.Value);

    private Task<string?> StoredAsync(string name) => WithUnitOfWorkAsync(() =>
        GetRequiredService<ISettingManager>().GetOrNullGlobalAsync(name, fallback: false));

    [Fact]
    public async Task Saved_settings_come_back_without_the_secrets_and_the_secrets_are_encrypted_at_rest()
    {
        await _service.UpdateAsync(Full());
        var saved = await _service.GetAsync();

        saved.PdfGeneratorApiBaseUrl.ShouldBe("https://eu1.pdfgeneratorapi.com/api/v4/");
        saved.HasPdfGeneratorApiKey.ShouldBeTrue();
        saved.HasPdfGeneratorApiSecret.ShouldBeTrue();
        saved.RoutingProvider.ShouldBe("Osrm");
        saved.OsrmBaseUrl.ShouldBe("https://osrm-saved.invalid/");
        saved.HasRouteFunctionApiKey.ShouldBeTrue();
        saved.VroomBaseUrl.ShouldBe("https://vroom-saved.invalid/");
        var json = JsonSerializer.Serialize(saved);
        json.ShouldNotContain(Key);
        json.ShouldNotContain(Secret);
        json.ShouldNotContain(FunctionKey);

        (await RawAsync(ServiceSettingNames.PdfGeneratorApi.Key)).ShouldNotBe(Key, "the key is stored encrypted");
        (await RawAsync(ServiceSettingNames.PdfGeneratorApi.Secret)).ShouldNotBe(Secret);
        (await RawAsync(ServiceSettingNames.Routing.RouteFunctionApiKey)).ShouldNotBe(FunctionKey);
        (await StoredAsync(ServiceSettingNames.PdfGeneratorApi.Key)).ShouldBe(Key, "and decrypts for the adapter");
    }

    [Fact]
    public async Task The_defaults_show_the_configuration_and_never_a_configured_secret()
    {
        var defaults = (await _service.GetAsync()).Defaults;

        defaults.OsrmBaseUrl.ShouldBeNull("the test module configures OSRM in code, not in IConfiguration");
        defaults.RoutingProvider.ShouldBe(RoutingConsts.OsrmProviderName);
        defaults.PdfGeneratorApiBaseUrl.ShouldNotBeNullOrWhiteSpace();
        defaults.HasPdfGeneratorApiKey.ShouldBeFalse();
    }

    [Fact]
    public async Task An_empty_secret_keeps_the_stored_one_and_clear_removes_it()
    {
        await _service.UpdateAsync(Full());

        await _service.UpdateAsync(Full() with { PdfGeneratorApiKey = "", PdfGeneratorApiSecret = null, RouteFunctionApiKey = "  " });
        (await StoredAsync(ServiceSettingNames.PdfGeneratorApi.Key)).ShouldBe(Key);
        (await StoredAsync(ServiceSettingNames.PdfGeneratorApi.Secret)).ShouldBe(Secret);
        (await StoredAsync(ServiceSettingNames.Routing.RouteFunctionApiKey)).ShouldBe(FunctionKey);

        await _service.UpdateAsync(Full() with
        {
            PdfGeneratorApiKey = null,
            ClearPdfGeneratorApiKey = true,
            PdfGeneratorApiSecret = null,
            ClearPdfGeneratorApiSecret = true,
            RouteFunctionApiKey = null,
            ClearRouteFunctionApiKey = true
        });
        var result = await _service.GetAsync();

        (await RawAsync(ServiceSettingNames.PdfGeneratorApi.Key)).ShouldBeNull();
        result.HasPdfGeneratorApiKey.ShouldBeFalse();
        result.HasPdfGeneratorApiSecret.ShouldBeFalse();
        result.HasRouteFunctionApiKey.ShouldBeFalse();
    }

    [Fact]
    public async Task A_save_reaches_the_adapters_without_a_restart_and_an_empty_field_hands_back_to_the_configuration()
    {
        await _service.UpdateAsync(Full());

        var routing = GetRequiredService<IOptions<RoutingIntegrationOptions>>().Value;
        routing.Osrm.BaseUrl.ShouldBe("https://osrm-saved.invalid/");
        routing.RouteFunction.ApiKey.ShouldBe(FunctionKey);
        routing.Vroom.BaseUrl.ShouldBe("https://vroom-saved.invalid/");
        routing.TimeoutSeconds.ShouldBe(2, "what the settings do not cover keeps its configured value");
        var pdf = GetRequiredService<IOptions<PdfGeneratorApiOptions>>().Value;
        pdf.Key.ShouldBe(Key);
        pdf.BaseUrl.ShouldBe("https://eu1.pdfgeneratorapi.com/api/v4/");

        await _service.UpdateAsync(new UpdateServiceSettingsDto());

        GetRequiredService<IOptions<RoutingIntegrationOptions>>().Value.Osrm.BaseUrl.ShouldBe(RoutingHarness.OsrmBaseUrl);
        GetRequiredService<IOptions<PdfGeneratorApiOptions>>().Value.Key.ShouldBe(Key, "a secret is only removed explicitly");
    }

    [Fact]
    public async Task A_save_made_on_another_instance_arrives_at_the_next_refresh_whatever_this_instance_cached()
    {
        await _service.UpdateAsync(new UpdateServiceSettingsDto { OsrmBaseUrl = "https://osrm-first.invalid/" });
        (await StoredAsync(ServiceSettingNames.Routing.OsrmBaseUrl)).ShouldBe("https://osrm-first.invalid/", "this instance now caches the value");

        /* Another instance saves: the row changes in the database, this instance's setting cache does not. */
        await WithUnitOfWorkAsync(async () =>
        {
            var repository = GetRequiredService<ISettingRepository>();
            var row = await repository.FindAsync(ServiceSettingNames.Routing.OsrmBaseUrl, GlobalSettingValueProvider.ProviderName, null);
            await repository.DeleteAsync(row!);
            await repository.InsertAsync(new Setting(Guid.NewGuid(), ServiceSettingNames.Routing.OsrmBaseUrl, "https://osrm-second.invalid/", GlobalSettingValueProvider.ProviderName));
        });
        await GetRequiredService<ServiceSettingsOverlay>().RefreshAsync();

        GetRequiredService<IOptions<RoutingIntegrationOptions>>().Value.Osrm.BaseUrl.ShouldBe("https://osrm-second.invalid/");
    }

    [Fact]
    public async Task An_address_that_is_not_http_or_https_is_refused()
    {
        await Should.ThrowAsync<Exception>(() => _service.UpdateAsync(new UpdateServiceSettingsDto { OsrmBaseUrl = "ftp://osrm.invalid/" }));
        (await StoredAsync(ServiceSettingNames.Routing.OsrmBaseUrl)).ShouldBeNull();
    }

    [Fact]
    public async Task The_routing_test_asks_the_saved_address_and_reports_every_routing_component()
    {
        await _service.UpdateAsync(new UpdateServiceSettingsDto { OsrmBaseUrl = "https://osrm-saved.invalid/" });
        _handler.Requests.Clear();

        var entries = await _service.TestAsync(ServiceSettingsConsts.RoutingService);

        entries.Select(e => e.Name).ShouldBe([RoutingHealthCheck.Name, RouteFunctionHealthCheck.Name, RouteOptimizerHealthCheck.Name]);
        entries[0].Target.ShouldBe("osrm-saved.invalid");
        entries[0].Area.ShouldBe("External");
        _handler.Requests.ShouldContain(r => r.Uri.Host == "osrm-saved.invalid", "the probe went to the saved address, not the cached one");
        var optimizer = entries.Single(e => e.Name == RouteOptimizerHealthCheck.Name);
        optimizer.Reason.ShouldBe(HealthCheckFacts.Reasons.NotConfigured);
        optimizer.IsOptional.ShouldBeTrue();
    }

    [Fact]
    public async Task The_documents_test_without_key_and_secret_says_not_configured_and_names_the_settings()
    {
        var entry = (await _service.TestAsync(ServiceSettingsConsts.DocumentsService)).ShouldHaveSingleItem();

        entry.Name.ShouldBe(PdfGeneratorApiHealthCheck.Name);
        entry.Status.ShouldBe("Degraded");
        entry.Reason.ShouldBe(HealthCheckFacts.Reasons.NotConfigured);
        entry.Settings.ShouldBe(["Integrations:PdfGeneratorApi:Key", "Integrations:PdfGeneratorApi:Secret"]);
    }

    [Fact]
    public async Task An_unknown_provider_or_service_is_refused()
    {
        await Should.ThrowAsync<UserFriendlyException>(() => _service.UpdateAsync(new UpdateServiceSettingsDto { RoutingProvider = "GoogleMaps" }));
        await Should.ThrowAsync<UserFriendlyException>(() => _service.TestAsync("weather"));
    }

    [Fact]
    public async Task A_tenant_can_neither_read_change_nor_test_the_service_settings()
    {
        using (_currentTenant.Change(Guid.NewGuid()))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.GetAsync());
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.UpdateAsync(Full()));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.TestAsync(ServiceSettingsConsts.RoutingService));
        }

        (await StoredAsync(ServiceSettingNames.PdfGeneratorApi.Key)).ShouldBeNull("nothing was written");
    }
}
