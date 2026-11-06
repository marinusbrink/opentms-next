using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using OpenTms.Integrations.AddressLookup;
using OpenTms.Integrations.DocumentRendering.PdfGeneratorApi;
using OpenTms.Integrations.Emailing.Postmark;
using OpenTms.Integrations.OrderIntake.Ai;
using OpenTms.Integrations.OrderIntake.Channels;
using OpenTms.Integrations.OrderIntake;
using OpenTms.Integrations.Routing;
using OpenTms.Integrations.ServiceSettings;
using OpenTms.Platform;
using OpenTms.Platform.DocumentRendering;
using OpenTms.Platform.Emailing;
using Volo.Abp;
using Volo.Abp.Application;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.BlobStoring;
using Volo.Abp.Caching;
using Volo.Abp.Identity;
using Volo.Abp.Modularity;
using Volo.Abp.OpenIddict;
using Volo.Abp.PermissionManagement;
using Volo.Abp.SettingManagement;

namespace OpenTms.Integrations;

[DependsOn(
    typeof(IntegrationsDomainModule),
    typeof(IntegrationsApplicationContractsModule),
    typeof(PlatformApplicationContractsModule),
    typeof(AbpDddApplicationModule),
    typeof(AbpCachingModule),
    typeof(AbpBackgroundWorkersModule),
    typeof(AbpSettingManagementDomainModule),
    /* Design 72: API clients are OpenIddict applications with a client permission grant in the host database. */
    typeof(AbpOpenIddictDomainModule),
    typeof(AbpPermissionManagementDomainModule),
    typeof(AbpBlobStoringModule),
    typeof(AbpBackgroundJobsAbstractionsModule),
    /* Design 74: ABP's user events (art. 17) and user names. */
    typeof(AbpIdentityDomainModule)
)]
public class IntegrationsApplicationModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        /* Design 44: the address lookup's providers are HttpClient users; their settings come from "AddressLookup". */
        context.Services.AddHttpClient();
        Configure<AddressLookupOptions>(context.Services.GetConfiguration().GetSection("AddressLookup"));
        /* Registered here, in this order, on purpose: the first provider that supports the country answers, and
         * the national source must come before the worldwide fallback (conventional registration would be alphabetical). */
        context.Services.AddTransient<IAddressLookupProvider, PdokAddressLookupProvider>();
        context.Services.AddTransient<IAddressLookupProvider, NominatimAddressLookupProvider>();
        /* The dropdown asks on every keystroke: only sources whose terms allow that (PDOK; not Nominatim). */
        context.Services.AddTransient<IAddressSuggestionProvider, PdokAddressLookupProvider>();

        ConfigurePdfGeneratorApi(context);
        ConfigureRouting(context);
        ConfigurePostmark(context);
        ConfigureOrderIntakeChannels(context);
        ConfigureServiceSettings(context);
        ConfigureOrderIntakePipeline(context);
    }

    public override async Task OnApplicationInitializationAsync(ApplicationInitializationContext context)
    {
        /* Design 60: the daily purge of expired route-cache rows. The DbMigrator does not load this module. */
        await context.AddBackgroundWorkerAsync<RouteCachePurgeWorker>();
        /* Administration → Services: the stored settings, read at start and every minute. */
        await context.AddBackgroundWorkerAsync<ServiceSettingsRefreshWorker>();

        /* Design 72: the mailbox sync, per instance (Integrations:OrderIntake:SyncEnabled). */
        if (context.ServiceProvider.GetRequiredService<IOptions<MailboxSyncOptions>>().Value.SyncEnabled)
        {
            await context.AddBackgroundWorkerAsync<MailboxSyncWorker>();
        }
    }

    /* Design 72 "Channels": the Gmail and Microsoft Graph clients are plain REST over named HttpClients (no provider SDKs),
     * registered by provider here; their OAuth clients come from "Integrations:OrderIntake:Mail" overlaid by the host's
     * Integrations tab. The sync's switches come from "Integrations:OrderIntake". The Anthropic SDK of the tab's test
     * uses its own named client. */
    private static void ConfigureOrderIntakeChannels(ServiceConfigurationContext context)
    {
        var configuration = context.Services.GetConfiguration();
        context.Services.Configure<MailOAuthOptions>(configuration.GetSection(MailOAuthOptions.SectionName));
        context.Services.Configure<OrderIntakeAiOptions>(configuration.GetSection(OrderIntakeAiOptions.SectionName));
        context.Services.Configure<MailboxSyncOptions>(configuration.GetSection(MailboxSyncOptions.SectionName));
        context.Services.AddHttpClient(GmailClient.Name, client => client.Timeout = MailProviderClientBase.Timeout);
        context.Services.AddHttpClient(MicrosoftGraphMailClient.Name, client => client.Timeout = MailProviderClientBase.Timeout);
        context.Services.AddHttpClient(AnthropicHealthCheck.HttpClientName, client => client.Timeout = MailProviderClientBase.Timeout);
        context.Services.AddTransient<IMailProviderClient, GmailClient>();
        context.Services.AddTransient<IMailProviderClient, MicrosoftGraphMailClient>();
    }

    /* Administration → Services (docs/designs/maintenance-view.md, "Services"): the host's stored settings lie over the
     * configuration of pdfgeneratorapi and routing. The adapters keep taking IOptions<T>, here resolved per use from the
     * options monitor, which recomputes whenever the overlay's values change, so a save applies without a redeploy. The
     * typed clients read their base address through the same IOptions<T> each time one is created. */
    private static void ConfigureServiceSettings(ServiceConfigurationContext context)
    {
        context.Services.AddSingleton<ServiceSettingsOverlay>();
        context.Services.AddSingleton<IPostConfigureOptions<PdfGeneratorApiOptions>, ServiceSettingsPostConfigure>();
        context.Services.AddSingleton<IPostConfigureOptions<RoutingIntegrationOptions>, ServiceSettingsPostConfigure>();
        context.Services.AddSingleton<IPostConfigureOptions<OrderIntakeAiOptions>, ServiceSettingsPostConfigure>();
        context.Services.AddSingleton<IPostConfigureOptions<MailOAuthOptions>, ServiceSettingsPostConfigure>();
        context.Services.AddSingleton<IOptionsChangeTokenSource<PdfGeneratorApiOptions>, ServiceSettingsChangeTokenSource<PdfGeneratorApiOptions>>();
        context.Services.AddSingleton<IOptionsChangeTokenSource<RoutingIntegrationOptions>, ServiceSettingsChangeTokenSource<RoutingIntegrationOptions>>();
        context.Services.AddSingleton<IOptionsChangeTokenSource<OrderIntakeAiOptions>, ServiceSettingsChangeTokenSource<OrderIntakeAiOptions>>();
        context.Services.AddSingleton<IOptionsChangeTokenSource<MailOAuthOptions>, ServiceSettingsChangeTokenSource<MailOAuthOptions>>();
        context.Services.Replace(ServiceDescriptor.Transient<IOptions<PdfGeneratorApiOptions>>(
            services => Options.Create(services.GetRequiredService<IOptionsMonitor<PdfGeneratorApiOptions>>().CurrentValue)));
        context.Services.Replace(ServiceDescriptor.Transient<IOptions<RoutingIntegrationOptions>>(
            services => Options.Create(services.GetRequiredService<IOptionsMonitor<RoutingIntegrationOptions>>().CurrentValue)));
        context.Services.Replace(ServiceDescriptor.Transient<IOptions<OrderIntakeAiOptions>>(
            services => Options.Create(services.GetRequiredService<IOptionsMonitor<OrderIntakeAiOptions>>().CurrentValue)));
        context.Services.Replace(ServiceDescriptor.Transient<IOptions<MailOAuthOptions>>(
            services => Options.Create(services.GetRequiredService<IOptionsMonitor<MailOAuthOptions>>().CurrentValue)));
    }

    /* Design 55: the pdfgeneratorapi.com adapter behind Platform's IReportRenderingProvider. Key and secret come from
     * the deployment's secret store into "Integrations:PdfGeneratorApi"; the typed client carries the base URL and the
     * 30-second timeout, the ceiling of one attempt (the client times each call per request: 15 s interactive, 30 s
     * background). The health check is tagged "external": the host keeps it out of its probes. */
    private static void ConfigurePdfGeneratorApi(ServiceConfigurationContext context)
    {
        context.Services.Configure<PdfGeneratorApiOptions>(context.Services.GetConfiguration().GetSection(PdfGeneratorApiOptions.SectionName));
        context.Services.TryAddSingleton(TimeProvider.System);
        context.Services.TryAddSingleton<IRetryDelay, TaskRetryDelay>();
        context.Services.AddHttpClient<PdfGeneratorApiClient>((services, client) =>
        {
            var baseUrl = services.GetRequiredService<IOptions<PdfGeneratorApiOptions>>().Value.BaseUrl;
            client.BaseAddress = new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/");
            client.Timeout = PdfGeneratorApiClient.Timeout;
        });
        context.Services.AddTransient<IReportRenderingProvider, PdfGeneratorApiRenderingProvider>();
        context.Services.AddHealthChecks()
            .AddCheck<PdfGeneratorApiHealthCheck>(PdfGeneratorApiHealthCheck.Name, tags: [PdfGeneratorApiHealthCheck.Tag]);
    }

    /* Design 60: the routing and carbon-footprint adapters. Both engines are typed HttpClients whose hosts come from
     * "Integrations:Routing" only (an unset base URL means "not configured": every routing call answers null); the
     * providers are registered by name here and chosen by Integrations:Routing:Provider — no factories, no reflection.
     * The function's key comes from the secret store. The health check is tagged "external" like the pdfgeneratorapi one. */
    private static void ConfigureRouting(ServiceConfigurationContext context)
    {
        var configuration = context.Services.GetConfiguration();
        context.Services.Configure<RoutingIntegrationOptions>(configuration.GetSection(RoutingIntegrationOptions.SectionName));
        context.Services.Configure<CarbonFootprintOptions>(configuration.GetSection(CarbonFootprintOptions.SectionName));
        context.Services.AddHttpClient<OsrmRoutingProvider>((services, client) =>
        {
            var options = services.GetRequiredService<IOptions<RoutingIntegrationOptions>>().Value;
            if (options.Osrm.IsConfigured)
            {
                var baseUrl = options.Osrm.BaseUrl!;
                client.BaseAddress = new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/");
            }

            client.Timeout = options.Timeout;
        });
        context.Services.AddHttpClient<ValhallaFunctionProvider>((services, client) =>
        {
            client.Timeout = services.GetRequiredService<IOptions<RoutingIntegrationOptions>>().Value.Timeout;
        });
        /* Design 68 §5: the VROOM adapter of the stop optimiser, used only when Integrations:Routing:Vroom:BaseUrl is set. */
        context.Services.AddHttpClient<VroomRouteOptimizer>((services, client) =>
        {
            var options = services.GetRequiredService<IOptions<RoutingIntegrationOptions>>().Value;
            if (options.Vroom.IsConfigured)
            {
                var baseUrl = options.Vroom.BaseUrl!;
                client.BaseAddress = new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/");
            }

            client.Timeout = options.Timeout;
        });
        context.Services.AddTransient<IRouteProvider>(services => services.GetRequiredService<OsrmRoutingProvider>());
        context.Services.AddTransient<IRouteProvider>(services => services.GetRequiredService<ValhallaFunctionProvider>());
        context.Services.AddTransient<IRouteFunction>(services => services.GetRequiredService<ValhallaFunctionProvider>());
        context.Services.AddHealthChecks()
            .AddCheck<RoutingHealthCheck>(RoutingHealthCheck.Name, tags: [RoutingHealthCheck.Tag])
            .AddCheck<RouteFunctionHealthCheck>(RouteFunctionHealthCheck.Name, tags: [RoutingHealthCheck.Tag])
            .AddCheck<RouteOptimizerHealthCheck>(RouteOptimizerHealthCheck.Name, tags: [RoutingHealthCheck.Tag]);
    }

    /* Design 72: the import pipeline of the order intake. The pipeline's own services (recorder, mail parsing, extractors,
     * matcher, executor, learner, processor, jobs, event handlers) register by convention; the blob container
     * InboundDocumentContainer uses the default (database) provider like every container (AD-09). Here: the AI settings
     * (deployment defaults from "Integrations:OrderIntake:Ai", overlaid by the host's saved settings; resolved per use from
     * the options monitor so a save applies at once), the price table of the metering, and the vendor adapter behind
     * ILanguageModelClient (AD-21: a second vendor is this registration and an adapter). The named HttpClient outlives
     * the SDK's own ten-minute ceiling: a streamed learning answer may take minutes. */
    private static void ConfigureOrderIntakePipeline(ServiceConfigurationContext context)
    {
        var configuration = context.Services.GetConfiguration();
        context.Services.Configure<OrderIntakeAiOptions>(configuration.GetSection(OrderIntakeAiOptions.SectionName));
        context.Services.Configure<OrderIntakeAiPriceOptions>(options => Microsoft.Extensions.Configuration.ConfigurationBinder.Bind(configuration.GetSection(OrderIntakeAiPriceOptions.SectionName), options.Models));
        context.Services.Replace(ServiceDescriptor.Transient<IOptions<OrderIntakeAiOptions>>(
            services => Options.Create(services.GetRequiredService<IOptionsMonitor<OrderIntakeAiOptions>>().CurrentValue)));
        context.Services.AddHttpClient(nameof(AnthropicLanguageModelClient), client => client.Timeout = AnthropicLanguageModelClient.Timeout + TimeSpan.FromMinutes(1));
        context.Services.AddTransient<ILanguageModelClient, AnthropicLanguageModelClient>();
    }

    /* Design 64: Postmark behind Platform's IEmailDeliveryProvider. The token and the webhook password come from the
     * secret store into "Integrations:Postmark"; the typed client carries the base URL and the 30-second timeout of one
     * attempt (the send job retries). Replaces Platform's "not configured" provider. The health check is "external". */
    private static void ConfigurePostmark(ServiceConfigurationContext context)
    {
        context.Services.Configure<PostmarkOptions>(context.Services.GetConfiguration().GetSection(PostmarkOptions.SectionName));
        context.Services.AddHttpClient<PostmarkEmailDeliveryProvider>((services, client) =>
        {
            var baseUrl = services.GetRequiredService<IOptions<PostmarkOptions>>().Value.BaseUrl;
            client.BaseAddress = new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/");
            client.Timeout = PostmarkEmailDeliveryProvider.Timeout;
        });
        context.Services.Replace(ServiceDescriptor.Transient<IEmailDeliveryProvider>(services => services.GetRequiredService<PostmarkEmailDeliveryProvider>()));
        context.Services.AddHealthChecks()
            .AddCheck<PostmarkHealthCheck>(PostmarkHealthCheck.Name, tags: [PostmarkHealthCheck.Tag]);
    }
}
