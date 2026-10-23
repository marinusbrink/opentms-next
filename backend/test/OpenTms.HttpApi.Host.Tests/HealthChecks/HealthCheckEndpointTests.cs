using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace OpenTms.HealthChecks;

/// <summary>
/// The host's health endpoints as AddOpenTmsHealthChecks maps them (docs/designs/50-report-rendering.md, "Health check":
/// the pdfgeneratorapi check is tagged "external" and is "not part of /health/ready"; the host also keeps it out of the
/// polled /health-status). The real endpoint configuration actions run on a bare WebApplication and each endpoint's
/// request delegate is invoked; probe checks record which of them ran. The registration of the pdfgeneratorapi check with
/// exactly the "external" tag is pinned in PdfGeneratorApiHealthCheckTests. Risk class: High (an outage at an external
/// provider must not take the API out of rotation).
/// </summary>
public class HealthCheckEndpointTests
{
    private sealed class Probe(string name, ConcurrentBag<string> ran) : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            ran.Add(name);
            return Task.FromResult(HealthCheckResult.Healthy());
        }
    }

    private static (WebApplication App, ConcurrentBag<string> Ran) Host()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddOpenTmsHealthChecks();

        var ran = new ConcurrentBag<string>();
        builder.Services.PostConfigure<HealthCheckServiceOptions>(options =>
        {
            /* The database check needs the ABP application; the predicates are the subject, so probes stand in. */
            foreach (var registration in options.Registrations.ToList())
            {
                options.Registrations.Remove(registration);
            }

            options.Registrations.Add(new HealthCheckRegistration("ready-probe", new Probe("ready-probe", ran), null, ["database", "ready"]));
            options.Registrations.Add(new HealthCheckRegistration("pdfgeneratorapi", new Probe("pdfgeneratorapi", ran), null, ["external"]));
            options.Registrations.Add(new HealthCheckRegistration("untagged-probe", new Probe("untagged-probe", ran), null, []));
        });

        var app = builder.Build();
        var routerOptions = app.Services.GetRequiredService<IOptions<AbpEndpointRouterOptions>>().Value;
        var context = new EndpointRouteBuilderContext(app, app.Services);
        foreach (var configure in routerOptions.EndpointConfigureActions)
        {
            configure(context);
        }

        return (app, ran);
    }

    private static async Task<(int Status, string[] Ran)> CallAsync(string path)
    {
        var (app, ran) = Host();
        await using (app)
        {
            var endpoint = ((IEndpointRouteBuilder)app).DataSources
                .SelectMany(source => source.Endpoints)
                .OfType<RouteEndpoint>()
                .SingleOrDefault(e => e.RoutePattern.RawText == path);
            endpoint.ShouldNotBeNull($"{path} is mapped");

            var http = new DefaultHttpContext { RequestServices = app.Services };
            http.Request.Path = path;
            http.Response.Body = new MemoryStream();
            await endpoint.RequestDelegate!(http);
            return (http.Response.StatusCode, ran.ToArray());
        }
    }

    [Fact]
    public async Task Readiness_runs_only_the_ready_checks_never_the_external_one()
    {
        var (status, ran) = await CallAsync("/health/ready");

        status.ShouldBe(StatusCodes.Status200OK);
        ran.ShouldBe(["ready-probe"]);
    }

    [Fact]
    public async Task The_polled_health_status_runs_everything_except_the_external_checks()
    {
        var (status, ran) = await CallAsync("/health-status");

        status.ShouldBe(StatusCodes.Status200OK);
        ran.ShouldBe(["ready-probe", "untagged-probe"], ignoreOrder: true);
        ran.ShouldNotContain("pdfgeneratorapi");
    }

    [Fact]
    public async Task Liveness_runs_no_check_at_all()
    {
        var (status, ran) = await CallAsync("/health/live");

        status.ShouldBe(StatusCodes.Status200OK);
        ran.ShouldBeEmpty();
    }
}
