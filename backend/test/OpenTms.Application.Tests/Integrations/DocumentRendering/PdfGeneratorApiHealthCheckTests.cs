using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using OpenTms.Platform.DocumentRendering;
using Shouldly;
using Volo.Abp.Modularity;
using Xunit;

namespace OpenTms.Integrations.DocumentRendering.PdfGeneratorApi;

/// <summary>
/// The pdfgeneratorapi connection test (docs/designs/50-report-rendering.md, "Health check": <c>GET templates?per_page=1</c>
/// in the default workspace, tag <c>external</c>, not part of <c>/health/ready</c>) and the adapter's registration in
/// IntegrationsApplicationModule (typed client: base URL, 30-second timeout; the provider behind Platform's abstraction).
/// The host's endpoint predicates are exercised in OpenTms.HttpApi.Host.Tests (HealthCheckEndpointTests). Risk class:
/// High (external integration). Fake transport only.
/// </summary>
public class PdfGeneratorApiHealthCheckTests
{
    private static HealthCheckContext Context(IHealthCheck check) => new()
    {
        Registration = new HealthCheckRegistration(PdfGeneratorApiHealthCheck.Name, check, HealthStatus.Unhealthy, [PdfGeneratorApiHealthCheck.Tag])
    };

    [Fact]
    public async Task Not_configured_is_Degraded_without_a_call()
    {
        var harness = new PdfHarness(o => o.Secret = null);
        var check = harness.HealthCheck();

        var result = await check.CheckHealthAsync(Context(check));

        result.Status.ShouldBe(HealthStatus.Degraded);
        result.Description!.ShouldContain("not configured");
        harness.Handler.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Reachable_is_Healthy_after_one_list_call_in_the_default_workspace_even_inside_a_tenant()
    {
        var harness = new PdfHarness();
        harness.Handler.ThenJson("""{"response":[{"id":1,"name":"X"}],"meta":{"total":1}}""");
        var check = harness.HealthCheck();

        HealthCheckResult result;
        using (harness.CurrentTenant.Change(Guid.NewGuid()))
        {
            result = await check.CheckHealthAsync(Context(check));
        }

        result.Status.ShouldBe(HealthStatus.Healthy);
        var request = harness.Handler.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Get);
        request.Relative.ShouldBe("templates?per_page=1");
        PdfHarness.Decode(request.Bearer!).Payload.GetProperty("sub").GetString().ShouldBe("default-workspace@opentms.nl");
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task A_failing_answer_is_Unhealthy_after_exactly_one_attempt(HttpStatusCode status)
    {
        var harness = new PdfHarness();
        harness.Handler.Always(status, """{"message":"Invalid credentials"}""");
        var check = harness.HealthCheck();

        var result = await check.CheckHealthAsync(Context(check));

        result.Status.ShouldBe(HealthStatus.Unhealthy);
        result.Description!.ShouldContain($"HTTP {(int)status}");
        harness.Handler.Requests.Count.ShouldBe(1, "the connection test does not retry");
        harness.Delay.Delays.ShouldBeEmpty();
        result.Description.ShouldNotContain(PdfHarness.Key);
        result.Description.ShouldNotContain(PdfHarness.Secret);
    }

    [Fact]
    public async Task An_unreachable_host_is_Unhealthy()
    {
        var harness = new PdfHarness();
        harness.Handler.Fallback = (_, _) => throw new HttpRequestException(HttpRequestError.ConnectionError, "refused");
        var check = harness.HealthCheck();

        var result = await check.CheckHealthAsync(Context(check));

        result.Status.ShouldBe(HealthStatus.Unhealthy);
        result.Description!.ShouldContain("could not be reached");
    }

    // ── Registration in IntegrationsApplicationModule ──────────────────────────

    private static ServiceProvider ConfigureIntegrationsModule(string baseUrl)
    {
        var services = new ServiceCollection();
        services.ReplaceConfiguration(new ConfigurationBuilder()
            .AddInMemoryCollection(new System.Collections.Generic.Dictionary<string, string?>
            {
                ["Integrations:PdfGeneratorApi:BaseUrl"] = baseUrl
            })
            .Build());
        services.AddOptions();
        var module = new IntegrationsApplicationModule();
        var context = new ServiceConfigurationContext(services);
        typeof(AbpModule).GetProperty("ServiceConfigurationContext", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!
            .SetValue(module, context);
        module.ConfigureServices(context);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void The_check_is_registered_as_pdfgeneratorapi_with_only_the_external_tag()
    {
        using var provider = ConfigureIntegrationsModule("https://us1.pdfgeneratorapi.com/api/v4/");

        var registration = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations
            .ShouldHaveSingleItem();
        registration.Name.ShouldBe("pdfgeneratorapi");
        registration.Tags.ShouldBe(["external"], ignoreOrder: false, "never 'ready': an outage at the provider must not take the API out of rotation");
    }

    [Theory]
    [InlineData("https://us1.pdfgeneratorapi.com/api/v4/")]
    [InlineData("https://us1.pdfgeneratorapi.com/api/v4")]
    public void The_typed_client_gets_the_base_url_with_a_trailing_slash_and_a_30_second_timeout(string baseUrl)
    {
        using var provider = ConfigureIntegrationsModule(baseUrl);

        var factoryOptions = provider.GetRequiredService<IOptionsMonitor<HttpClientFactoryOptions>>().Get(nameof(PdfGeneratorApiClient));
        factoryOptions.HttpClientActions.ShouldNotBeEmpty();
        using var client = new HttpClient();
        foreach (var action in factoryOptions.HttpClientActions)
        {
            action(client);
        }

        client.BaseAddress.ShouldBe(new Uri("https://us1.pdfgeneratorapi.com/api/v4/"));
        client.Timeout.ShouldBe(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void The_adapter_is_registered_behind_Platforms_rendering_abstraction()
    {
        var services = new ServiceCollection();
        services.ReplaceConfiguration(new ConfigurationBuilder().Build());
        var module = new IntegrationsApplicationModule();
        var context = new ServiceConfigurationContext(services);
        typeof(AbpModule).GetProperty("ServiceConfigurationContext", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!
            .SetValue(module, context);
        module.ConfigureServices(context);

        services.Where(d => d.ServiceType == typeof(IReportRenderingProvider)).Select(d => d.ImplementationType)
            .ShouldBe([typeof(PdfGeneratorApiRenderingProvider)]);
        services.ShouldContain(d => d.ServiceType == typeof(IRetryDelay) && d.ImplementationType == typeof(TaskRetryDelay));
    }
}
