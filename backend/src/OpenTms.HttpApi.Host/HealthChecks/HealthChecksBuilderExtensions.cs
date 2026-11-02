using System;
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using OpenTms.Platform.Maintenance;

namespace OpenTms.HealthChecks;

public static class HealthChecksBuilderExtensions
{
    public static void AddOpenTmsHealthChecks(this IServiceCollection services)
    {
        /* The components of the system itself (docs/designs/maintenance-view.md, "Components"); the Integrations module
         * registers its external services. Only the database takes part in readiness: the others report a part of the
         * system that is down, which must not take the whole API out of rotation. */
        var healthChecksBuilder = services.AddHealthChecks();
        healthChecksBuilder.AddCheck<OpenTmsDatabaseCheck>(OpenTmsDatabaseCheck.Name, tags: [HealthCheckFacts.CoreTag, "database", "ready"]);
        healthChecksBuilder.AddCheck<TenantDatabasesHealthCheck>(TenantDatabasesHealthCheck.Name, tags: [HealthCheckFacts.CoreTag, "database"]);
        healthChecksBuilder.AddCheck<BackgroundJobsHealthCheck>(BackgroundJobsHealthCheck.Name, tags: [HealthCheckFacts.CoreTag]);
        healthChecksBuilder.AddCheck<OutboxHealthCheck>(OutboxHealthCheck.Name, tags: [HealthCheckFacts.CoreTag]);
        healthChecksBuilder.AddCheck<CacheHealthCheck>(CacheHealthCheck.Name, tags: [HealthCheckFacts.CoreTag]);
        healthChecksBuilder.AddCheck<LiveUpdatesHealthCheck>(LiveUpdatesHealthCheck.Name, tags: [HealthCheckFacts.CoreTag]);

        services.ConfigureHealthCheckEndpoint("/health-status");

        /* Liveness: the process answers requests — no dependencies checked.
         * Readiness: dependencies (database) are reachable; Cloud Run/K8s probes point here. Checks of external
         * services (tag "external") never take part: an outage there must not take the API out of rotation. */
        services.Configure<AbpEndpointRouterOptions>(options =>
        {
            options.EndpointConfigureActions.Add(endpointContext =>
            {
                endpointContext.Endpoints.MapHealthChecks("/health/live", new HealthCheckOptions
                {
                    Predicate = _ => false,
                    AllowCachingResponses = false
                });
                endpointContext.Endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
                {
                    Predicate = registration => registration.Tags.Contains("ready"),
                    AllowCachingResponses = false
                });
            });
        });

        var configuration = services.GetConfiguration();
        var healthCheckUrl = configuration["App:HealthCheckUrl"];

        if (string.IsNullOrEmpty(healthCheckUrl))
        {
            healthCheckUrl = "/health-status";
        }

        var healthChecksUiBuilder = services.AddHealthChecksUI(settings =>
        {
            settings.AddHealthCheckEndpoint("OpenTms Health Status", configuration["App:HealthUiCheckUrl"] ?? healthCheckUrl);
        });

        // Set your HealthCheck UI Storage here
        healthChecksUiBuilder.AddInMemoryStorage();

        services.MapHealthChecksUiEndpoints(options =>
        {
            options.UIPath = "/health-ui";
            options.ApiPath = "/health-api";
        });
    }

    private static IServiceCollection ConfigureHealthCheckEndpoint(this IServiceCollection services, string path)
    {
        services.Configure<AbpEndpointRouterOptions>(options =>
        {
            options.EndpointConfigureActions.Add(endpointContext =>
            {
                endpointContext.Endpoints.MapHealthChecks(
                    new PathString(path.EnsureStartsWith('/')),
                    new HealthCheckOptions
                    {
                        /* The health UI polls this endpoint: only the readiness checks. The others (tenant databases,
                         * outbox, external services) run in the heartbeat and on demand in Maintenance → Health. */
                        Predicate = registration => registration.Tags.Contains("ready"),
                        ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse,
                        AllowCachingResponses = false,
                    });
            });
        });

        return services;
    }

    private static IServiceCollection MapHealthChecksUiEndpoints(this IServiceCollection services, Action<global::HealthChecks.UI.Configuration.Options>? setupOption = null)
    {
        services.Configure<AbpEndpointRouterOptions>(routerOptions =>
        {
            routerOptions.EndpointConfigureActions.Add(endpointContext =>
            {
                endpointContext.Endpoints.MapHealthChecksUI(setupOption);
            });
        });

        return services;
    }
}
