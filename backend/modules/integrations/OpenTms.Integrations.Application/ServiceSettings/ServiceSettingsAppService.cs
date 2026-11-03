using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using OpenTms.Integrations.DocumentRendering.PdfGeneratorApi;
using OpenTms.Integrations.Localization;
using OpenTms.Integrations.OrderIntake.Ai;
using OpenTms.Integrations.OrderIntake.Channels;
using OpenTms.Integrations.Permissions;
using OpenTms.Integrations.Routing;
using OpenTms.Platform.Administration.Maintenance;
using OpenTms.Platform.Maintenance;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Authorization;
using Volo.Abp.SettingManagement;

namespace OpenTms.Integrations.ServiceSettings;

/// <summary>
/// Administration → Services (docs/designs/maintenance-view.md, "Services"), modelled on the host e-mail settings of
/// design 64: global settings in the host database, one configuration of pdfgeneratorapi and routing for every tenant.
/// An empty value falls back to the deployment's configuration. Keys and secrets are write-only: stored encrypted, never
/// returned, kept when the form sends them empty. A save reaches this instance's adapters once its unit of work commits
/// (the overlay is read again) and the others within <see cref="ServiceSettingsOverlay.RefreshPeriod"/>. The test runs
/// the service's own health checks, so the page and the Health tab say the same.
/// </summary>
[RemoteService(IsEnabled = false)]
[Authorize(IntegrationsPermissions.ServiceSettings.Default)]
public class ServiceSettingsAppService : ApplicationService, IServiceSettingsAppService
{
    private readonly ISettingManager _settings;
    private readonly ServiceSettingsOverlay _overlay;
    private readonly RoutingProbeCache _probes;
    private readonly IConfiguration _configuration;
    private readonly MailboxOAuthUrls _mailboxUrls;

    public ServiceSettingsAppService(
        ISettingManager settings,
        ServiceSettingsOverlay overlay,
        RoutingProbeCache probes,
        IConfiguration configuration,
        MailboxOAuthUrls mailboxUrls)
    {
        _settings = settings;
        _overlay = overlay;
        _probes = probes;
        _configuration = configuration;
        _mailboxUrls = mailboxUrls;
        LocalizationResource = typeof(IntegrationsResource);
    }

    public async Task<ServiceSettingsDto> GetAsync()
    {
        EnsureHostSide();
        var pdf = _configuration.GetSection(PdfGeneratorApiOptions.SectionName).Get<PdfGeneratorApiOptions>() ?? new PdfGeneratorApiOptions();
        var routing = _configuration.GetSection(RoutingIntegrationOptions.SectionName).Get<RoutingIntegrationOptions>() ?? new RoutingIntegrationOptions();
        var ai = _configuration.GetSection(OrderIntakeAiOptions.SectionName).Get<OrderIntakeAiOptions>() ?? new OrderIntakeAiOptions();
        var mail = _configuration.GetSection(MailOAuthOptions.SectionName).Get<MailOAuthOptions>() ?? new MailOAuthOptions();

        return new ServiceSettingsDto
        {
            PdfGeneratorApiBaseUrl = await GetAsync(ServiceSettingNames.PdfGeneratorApi.BaseUrl),
            HasPdfGeneratorApiKey = !string.IsNullOrEmpty(await GetAsync(ServiceSettingNames.PdfGeneratorApi.Key)),
            HasPdfGeneratorApiSecret = !string.IsNullOrEmpty(await GetAsync(ServiceSettingNames.PdfGeneratorApi.Secret)),
            RoutingProvider = await GetAsync(ServiceSettingNames.Routing.Provider),
            OsrmBaseUrl = await GetAsync(ServiceSettingNames.Routing.OsrmBaseUrl),
            RouteFunctionBaseUrl = await GetAsync(ServiceSettingNames.Routing.RouteFunctionBaseUrl),
            HasRouteFunctionApiKey = !string.IsNullOrEmpty(await GetAsync(ServiceSettingNames.Routing.RouteFunctionApiKey)),
            VroomBaseUrl = await GetAsync(ServiceSettingNames.Routing.VroomBaseUrl),
            HasAiApiKey = !string.IsNullOrEmpty(await GetAsync(ServiceSettingNames.Ai.ApiKey)),
            AiLearningModel = await GetAsync(ServiceSettingNames.Ai.LearningModel),
            AiExtractionModel = await GetAsync(ServiceSettingNames.Ai.ExtractionModel),
            GoogleClientId = await GetAsync(ServiceSettingNames.Google.ClientId),
            HasGoogleClientSecret = !string.IsNullOrEmpty(await GetAsync(ServiceSettingNames.Google.ClientSecret)),
            MicrosoftClientId = await GetAsync(ServiceSettingNames.Microsoft.ClientId),
            HasMicrosoftClientSecret = !string.IsNullOrEmpty(await GetAsync(ServiceSettingNames.Microsoft.ClientSecret)),
            MailboxRedirectUri = await _mailboxUrls.FindRedirectUriAsync(),
            Defaults = new ServiceSettingsDefaultsDto
            {
                PdfGeneratorApiBaseUrl = pdf.BaseUrl,
                HasPdfGeneratorApiKey = !string.IsNullOrWhiteSpace(pdf.Key),
                HasPdfGeneratorApiSecret = !string.IsNullOrWhiteSpace(pdf.Secret),
                RoutingProvider = routing.Provider,
                OsrmBaseUrl = routing.Osrm.BaseUrl,
                RouteFunctionBaseUrl = routing.RouteFunction.BaseUrl,
                HasRouteFunctionApiKey = !string.IsNullOrWhiteSpace(routing.RouteFunction.ApiKey),
                VroomBaseUrl = routing.Vroom.BaseUrl,
                HasAiApiKey = !string.IsNullOrWhiteSpace(ai.ApiKey),
                AiLearningModel = ai.LearningModel,
                AiExtractionModel = ai.ExtractionModel,
                GoogleClientId = mail.Google.ClientId,
                HasGoogleClientSecret = !string.IsNullOrWhiteSpace(mail.Google.ClientSecret),
                MicrosoftClientId = mail.Microsoft.ClientId,
                HasMicrosoftClientSecret = !string.IsNullOrWhiteSpace(mail.Microsoft.ClientSecret)
            }
        };
    }

    [Authorize(IntegrationsPermissions.ServiceSettings.Update)]
    public async Task UpdateAsync(UpdateServiceSettingsDto input)
    {
        EnsureHostSide();
        if (!string.IsNullOrWhiteSpace(input.RoutingProvider) && !ServiceSettingsConsts.RoutingProviders.Contains(input.RoutingProvider.Trim()))
        {
            throw new UserFriendlyException(L["Services:InvalidRoutingProvider"]);
        }

        foreach (var url in new[] { input.PdfGeneratorApiBaseUrl, input.OsrmBaseUrl, input.RouteFunctionBaseUrl, input.VroomBaseUrl })
        {
            if (!string.IsNullOrWhiteSpace(url)
                && !(Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)))
            {
                throw new UserFriendlyException(L["Services:Url.Invalid"]);
            }
        }

        await SetSecretAsync(ServiceSettingNames.PdfGeneratorApi.Key, input.PdfGeneratorApiKey, input.ClearPdfGeneratorApiKey);
        await SetSecretAsync(ServiceSettingNames.PdfGeneratorApi.Secret, input.PdfGeneratorApiSecret, input.ClearPdfGeneratorApiSecret);
        await SetSecretAsync(ServiceSettingNames.Routing.RouteFunctionApiKey, input.RouteFunctionApiKey, input.ClearRouteFunctionApiKey);
        await SetAsync(ServiceSettingNames.PdfGeneratorApi.BaseUrl, input.PdfGeneratorApiBaseUrl);
        await SetAsync(ServiceSettingNames.Routing.Provider, input.RoutingProvider);
        await SetAsync(ServiceSettingNames.Routing.OsrmBaseUrl, input.OsrmBaseUrl);
        await SetAsync(ServiceSettingNames.Routing.RouteFunctionBaseUrl, input.RouteFunctionBaseUrl);
        await SetAsync(ServiceSettingNames.Routing.VroomBaseUrl, input.VroomBaseUrl);

        /* Design 72: the Integrations tab. */
        await SetSecretAsync(ServiceSettingNames.Ai.ApiKey, input.AiApiKey, input.ClearAiApiKey);
        await SetSecretAsync(ServiceSettingNames.Google.ClientSecret, input.GoogleClientSecret, input.ClearGoogleClientSecret);
        await SetSecretAsync(ServiceSettingNames.Microsoft.ClientSecret, input.MicrosoftClientSecret, input.ClearMicrosoftClientSecret);
        await SetAsync(ServiceSettingNames.Ai.LearningModel, input.AiLearningModel);
        await SetAsync(ServiceSettingNames.Ai.ExtractionModel, input.AiExtractionModel);
        await SetAsync(ServiceSettingNames.Google.ClientId, input.GoogleClientId);
        await SetAsync(ServiceSettingNames.Microsoft.ClientId, input.MicrosoftClientId);

        /* Read back only after the commit: ABP's settings cache is unit-of-work aware. A cached probe of the old address
         * would otherwise answer for a minute. */
        var refresh = async () =>
        {
            await _overlay.RefreshAsync();
            _probes.Clear();
        };
        if (UnitOfWorkManager.Current != null)
        {
            UnitOfWorkManager.Current.OnCompleted(refresh);
        }
        else
        {
            await refresh();
        }

        Logger.LogInformation("Service settings saved by user {UserId}.", CurrentUser.Id);
    }

    [Authorize(IntegrationsPermissions.ServiceSettings.Test)]
    public async Task<List<HealthCheckEntryDto>> TestAsync(string service)
    {
        EnsureHostSide();
        return service switch
        {
            ServiceSettingsConsts.DocumentsService =>
                [await RunAsync<PdfGeneratorApiHealthCheck>(PdfGeneratorApiHealthCheck.Name)],
            ServiceSettingsConsts.RoutingService => await RoutingAsync(),
            /* Design 72: one result per section of the Integrations tab. */
            ServiceSettingsConsts.IntegrationsService =>
            [
                await RunAsync<AnthropicHealthCheck>(AnthropicHealthCheck.Name),
                await RunAsync<GoogleOAuthHealthCheck>(GoogleOAuthHealthCheck.Name),
                await RunAsync<MicrosoftOAuthHealthCheck>(MicrosoftOAuthHealthCheck.Name)
            ],
            _ => throw new UserFriendlyException(L["Services:UnknownService"])
        };
    }

    private async Task<List<HealthCheckEntryDto>> RoutingAsync()
    {
        /* A test asks again: no answer from the probe cache. */
        _probes.Clear();
        return
        [
            await RunAsync<RoutingHealthCheck>(RoutingHealthCheck.Name),
            await RunAsync<RouteFunctionHealthCheck>(RouteFunctionHealthCheck.Name),
            await RunAsync<RouteOptimizerHealthCheck>(RouteOptimizerHealthCheck.Name)
        ];
    }

    private async Task<HealthCheckEntryDto> RunAsync<TCheck>(string name) where TCheck : IHealthCheck
    {
        string[] tags = [HealthCheckFacts.ExternalTag];
        var check = ActivatorUtilities.CreateInstance<TCheck>(LazyServiceProvider.LazyGetRequiredService<IServiceProvider>());
        var context = new HealthCheckContext { Registration = new HealthCheckRegistration(name, check, HealthStatus.Unhealthy, tags) };
        var watch = Stopwatch.StartNew();
        HealthCheckResult result;
        try
        {
            result = await check.CheckHealthAsync(context);
        }
        catch (Exception e)
        {
            result = HealthCheckResult.Unhealthy(e.Message, e, HealthCheckFacts.Data(HealthCheckFacts.Reasons.Failing));
        }

        return HealthCheckEntryDto.Create(
            name,
            result.Status.ToString(),
            result.Description,
            watch.Elapsed,
            tags,
            result.Exception?.Message,
            result.Data.ToDictionary(kv => kv.Key, kv => Convert.ToString(kv.Value, CultureInfo.InvariantCulture) ?? string.Empty));
    }

    private void EnsureHostSide()
    {
        if (CurrentTenant.IsAvailable)
        {
            throw new AbpAuthorizationException("The service settings are available on the host side only.");
        }
    }

    private Task<string?> GetAsync(string name) => _settings.GetOrNullGlobalAsync(name, fallback: false);

    private Task SetAsync(string name, string? value) =>
        _settings.SetGlobalAsync(name, string.IsNullOrWhiteSpace(value) ? null : value.Trim());

    private async Task SetSecretAsync(string name, string? value, bool clear)
    {
        if (clear)
        {
            await SetAsync(name, null);
        }
        else if (!string.IsNullOrWhiteSpace(value))
        {
            await SetAsync(name, value);
        }
    }
}
