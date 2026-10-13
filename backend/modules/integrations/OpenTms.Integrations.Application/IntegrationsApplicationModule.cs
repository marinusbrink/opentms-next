using Microsoft.Extensions.DependencyInjection;
using OpenTms.Integrations.AddressLookup;
using Volo.Abp.Application;
using Volo.Abp.Caching;
using Volo.Abp.Modularity;

namespace OpenTms.Integrations;

[DependsOn(
    typeof(IntegrationsDomainModule),
    typeof(IntegrationsApplicationContractsModule),
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
    }
}
