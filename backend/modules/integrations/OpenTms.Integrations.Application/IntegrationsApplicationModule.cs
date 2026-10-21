using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using OpenTms.Integrations.AddressLookup;
using OpenTms.Integrations.DocumentRendering.PdfGeneratorApi;
using OpenTms.Platform;
using OpenTms.Platform.DocumentRendering;
using Volo.Abp.Application;
using Volo.Abp.Caching;
using Volo.Abp.Modularity;

namespace OpenTms.Integrations;

[DependsOn(
    typeof(IntegrationsDomainModule),
    typeof(IntegrationsApplicationContractsModule),
    typeof(PlatformApplicationContractsModule),
    typeof(AbpDddApplicationModule),
    typeof(AbpCachingModule)
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
    }

    /* Design 50: the pdfgeneratorapi.com adapter behind Platform's IReportRenderingProvider. Key and secret come from
     * the deployment's secret store into "Integrations:PdfGeneratorApi"; the typed client carries the base URL and the
     * 30-second timeout. The health check is tagged "external": the host keeps it out of its probes. */
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
}
