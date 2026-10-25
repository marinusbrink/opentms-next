using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using OpenTms.Integrations.CarbonFootprint;
using OpenTms.Integrations.Permissions;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Modularity;
using Xunit;

namespace OpenTms.Integrations.Routing;

/// <summary>
/// The wiring of design 60 in IntegrationsApplicationModule: the options bound from <c>Integrations:Routing</c> and
/// <c>Integrations:CarbonFootprint</c> with their defaults, both providers as typed clients (the OSRM base URL with a
/// trailing slash, the timeout from the options), the providers resolvable by name, the function behind
/// <see cref="IRouteFunction"/>, the <c>routing</c> health check with the <c>external</c> tag only, and the status service's
/// permission. Risk class: High (configuration — a wrong default would route every tenant to nothing or to a literal host).
/// </summary>
public class RoutingRegistrationTests
{
    private static ServiceProvider Configure(Dictionary<string, string?>? settings = null)
    {
        var services = new ServiceCollection();
        services.ReplaceConfiguration(new ConfigurationBuilder().AddInMemoryCollection(settings ?? new Dictionary<string, string?>()).Build());
        services.AddOptions();
        var module = new IntegrationsApplicationModule();
        var context = new ServiceConfigurationContext(services);
        typeof(AbpModule).GetProperty("ServiceConfigurationContext", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!
            .SetValue(module, context);
        module.ConfigureServices(context);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void The_defaults_are_osrm_thirty_cache_days_ten_seconds_and_a_cpi_of_62_with_no_host_anywhere()
    {
        using var provider = Configure();

        var routing = provider.GetRequiredService<IOptions<RoutingIntegrationOptions>>().Value;
        routing.Provider.ShouldBe("Osrm");
        routing.CacheDays.ShouldBe(30);
        routing.TimeoutSeconds.ShouldBe(10);
        routing.Osrm.BaseUrl.ShouldBeNull("no literal host: unset means not configured");
        routing.Osrm.IsConfigured.ShouldBeFalse();
        routing.RouteFunction.BaseUrl.ShouldBeNull();
        routing.RouteFunction.ApiKey.ShouldBeNull();
        routing.RouteFunction.ApiKeyHeader.ShouldBe("X-API-Key");
        routing.RouteFunction.IsConfigured.ShouldBeFalse();
        routing.RouteFunction.Host.ShouldBeNull();

        provider.GetRequiredService<IOptions<CarbonFootprintOptions>>().Value.CpiGramPerTonneKm.ShouldBe(62m);
    }

    [Fact]
    public void The_options_bind_from_the_Integrations_sections()
    {
        using var provider = Configure(new Dictionary<string, string?>
        {
            ["Integrations:Routing:Provider"] = "RouteFunction",
            ["Integrations:Routing:Osrm:BaseUrl"] = "https://osrm.example.invalid",
            ["Integrations:Routing:RouteFunction:BaseUrl"] = "https://fn.example.invalid/route",
            ["Integrations:Routing:RouteFunction:ApiKey"] = "qq",
            ["Integrations:Routing:CacheDays"] = "7",
            ["Integrations:Routing:TimeoutSeconds"] = "4",
            ["Integrations:CarbonFootprint:CpiGramPerTonneKm"] = "71.5"
        });

        var routing = provider.GetRequiredService<IOptions<RoutingIntegrationOptions>>().Value;
        routing.Provider.ShouldBe("RouteFunction");
        routing.Osrm.IsConfigured.ShouldBeTrue();
        routing.RouteFunction.Host.ShouldBe("fn.example.invalid");
        routing.CacheDays.ShouldBe(7);
        routing.CacheLifetime.ShouldBe(TimeSpan.FromDays(7));
        routing.Timeout.ShouldBe(TimeSpan.FromSeconds(4));
        provider.GetRequiredService<IOptions<CarbonFootprintOptions>>().Value.CpiGramPerTonneKm.ShouldBe(71.5m);
    }

    [Theory]
    [InlineData("https://osrm.example.invalid")]
    [InlineData("https://osrm.example.invalid/")]
    public void The_osrm_typed_client_gets_the_base_url_with_a_trailing_slash_and_the_timeout(string baseUrl)
    {
        using var provider = Configure(new Dictionary<string, string?>
        {
            ["Integrations:Routing:Osrm:BaseUrl"] = baseUrl,
            ["Integrations:Routing:TimeoutSeconds"] = "3"
        });

        var client = ClientOf<OsrmRoutingProvider>(provider);
        client.BaseAddress.ShouldBe(new Uri("https://osrm.example.invalid/"));
        client.Timeout.ShouldBe(TimeSpan.FromSeconds(3));

        ClientOf<ValhallaFunctionProvider>(provider).Timeout.ShouldBe(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void An_unconfigured_osrm_client_has_no_base_address()
    {
        using var provider = Configure();

        ClientOf<OsrmRoutingProvider>(provider).BaseAddress.ShouldBeNull();
    }

    [Fact]
    public void Both_providers_are_registered_by_name_and_the_function_behind_its_own_interface()
    {
        var services = new ServiceCollection();
        services.ReplaceConfiguration(new ConfigurationBuilder().Build());
        var module = new IntegrationsApplicationModule();
        var context = new ServiceConfigurationContext(services);
        typeof(AbpModule).GetProperty("ServiceConfigurationContext", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!
            .SetValue(module, context);
        module.ConfigureServices(context);

        services.Count(d => d.ServiceType == typeof(IRouteProvider)).ShouldBe(2);
        services.Count(d => d.ServiceType == typeof(IRouteFunction)).ShouldBe(1);
        services.ShouldContain(d => d.ServiceType == typeof(OsrmRoutingProvider));
        services.ShouldContain(d => d.ServiceType == typeof(ValhallaFunctionProvider));

        using var provider = services.BuildServiceProvider();
        provider.GetServices<IRouteProvider>().Select(p => p.Name).ShouldBe(["Osrm", "RouteFunction"]);
        provider.GetRequiredService<IRouteFunction>().ShouldBeOfType<ValhallaFunctionProvider>();
    }

    [Fact]
    public void The_routing_check_is_registered_with_only_the_external_tag_next_to_the_pdfgeneratorapi_one()
    {
        using var provider = Configure();

        var registrations = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;
        var routing = registrations.Single(r => r.Name == RoutingHealthCheck.Name);
        routing.Tags.ShouldBe(["external"], ignoreOrder: false, "never 'ready': an outage at the engine must not take the API out of rotation");
        registrations.Select(r => r.Name).ShouldBe(["pdfgeneratorapi", "routing"], ignoreOrder: true);
    }

    [Fact]
    public void The_status_service_is_the_routing_controller_and_demands_the_Routing_permission()
    {
        var method = typeof(RoutingAppService).GetMethod(nameof(RoutingAppService.GetStatusAsync))!;
        method.GetCustomAttribute<AuthorizeAttribute>()!.Policy.ShouldBe(IntegrationsPermissions.Routing);
        IntegrationsPermissions.Routing.ShouldBe("Integrations.Routing");
        /* Conventional routing: "RoutingAppService" → /api/integrations/routing, "GetStatusAsync" → GET …/status. */
        typeof(RoutingAppService).Name.ShouldBe("RoutingAppService");
        typeof(IRoutingAppService).IsAssignableFrom(typeof(RoutingAppService)).ShouldBeTrue();
    }

    [Fact]
    public void The_in_process_services_implement_the_published_contracts()
    {
        typeof(IRoutingService).IsAssignableFrom(typeof(RoutingService)).ShouldBeTrue();
        typeof(ICarbonFootprintCalculator).IsAssignableFrom(typeof(CarbonFootprintCalculator)).ShouldBeTrue();
        typeof(RoutingService).GetCustomAttribute<RemoteServiceAttribute>().ShouldBeNull("not an application service: nothing to expose");
    }

    private static HttpClient ClientOf<T>(ServiceProvider provider)
    {
        var factoryOptions = provider.GetRequiredService<IOptionsMonitor<HttpClientFactoryOptions>>().Get(typeof(T).Name);
        factoryOptions.HttpClientActions.ShouldNotBeEmpty();
        var client = new HttpClient();
        foreach (var action in factoryOptions.HttpClientActions)
        {
            action(client);
        }

        return client;
    }
}
