using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenTms.Integrations.DocumentRendering.PdfGeneratorApi;
using OpenTms.Integrations.Routing;
using Volo.Abp.Modularity;

namespace OpenTms.PostgreSql.Integrations;

/// <summary>
/// The PostgreSQL test application of design 60: the production EF Core stack of <see cref="OpenTmsPostgreSqlTestModule"/>
/// (database per tenant, real transactions, the migrated RouteCache table) with both engines on the fake transport and a
/// manual clock. Base URLs end in the reserved ".invalid" domain: no request can leave the process.
/// </summary>
[DependsOn(typeof(OpenTmsPostgreSqlTestModule))]
public class RoutingPostgreSqlTestModule : AbpModule
{
    public static readonly DateTimeOffset Start = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var handler = new RoutingHttpHandler();
        handler.AnswerByPath();
        context.Services.AddSingleton(handler);
        context.Services.AddHttpClient<OsrmRoutingProvider>().ConfigurePrimaryHttpMessageHandler(() => handler);
        context.Services.AddHttpClient<ValhallaFunctionProvider>().ConfigurePrimaryHttpMessageHandler(() => handler);
        context.Services.Configure<RoutingIntegrationOptions>(options =>
        {
            options.Osrm.BaseUrl = RoutingHarness.OsrmBaseUrl;
            options.RouteFunction.BaseUrl = RoutingHarness.FunctionUrl;
            options.TimeoutSeconds = 2;
        });

        var time = new ManualTimeProvider(Start);
        context.Services.AddSingleton(time);
        context.Services.Replace(ServiceDescriptor.Singleton<TimeProvider>(time));
    }
}
