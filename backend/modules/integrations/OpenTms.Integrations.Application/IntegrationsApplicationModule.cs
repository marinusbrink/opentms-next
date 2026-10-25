using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using OpenTms.Integrations.AddressLookup;
using OpenTms.Integrations.DocumentRendering.PdfGeneratorApi;
using OpenTms.Integrations.Routing;
using OpenTms.Platform;
using OpenTms.Platform.DocumentRendering;
using Volo.Abp;
using Volo.Abp.Application;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Caching;
using Volo.Abp.Modularity;

namespace OpenTms.Integrations;

[DependsOn(
    typeof(IntegrationsDomainModule),
    typeof(IntegrationsApplicationContractsModule),
    typeof(PlatformApplicationContractsModule),
    typeof(AbpDddApplicationModule),
    typeof(AbpCachingModule),
    typeof(AbpBackgroundWorkersModule)
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
    }

    public override async Task OnApplicationInitializationAsync(ApplicationInitializationContext context)
    {
        /* Design 60: the daily purge of expired route-cache rows. The DbMigrator does not load this module. */
        await context.AddBackgroundWorkerAsync<RouteCachePurgeWorker>();
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
        context.Services.AddTransient<IRouteProvider>(services => services.GetRequiredService<OsrmRoutingProvider>());
        context.Services.AddTransient<IRouteProvider>(services => services.GetRequiredService<ValhallaFunctionProvider>());
        context.Services.AddTransient<IRouteFunction>(services => services.GetRequiredService<ValhallaFunctionProvider>());
        context.Services.AddHealthChecks()
            .AddCheck<RoutingHealthCheck>(RoutingHealthCheck.Name, tags: [RoutingHealthCheck.Tag]);
    }
}
