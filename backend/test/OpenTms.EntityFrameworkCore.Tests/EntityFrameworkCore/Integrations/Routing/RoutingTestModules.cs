using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTms.Integrations.DocumentRendering.PdfGeneratorApi;
using OpenTms.Integrations.EntityFrameworkCore;
using OpenTms.Integrations.Routing;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Modularity;

namespace OpenTms.EntityFrameworkCore.Integrations.Routing;

/// <summary>
/// The SQLite test application of design 60 with both engines on the fake transport of
/// <see cref="RoutingHttpHandler"/> (base URLs in the reserved ".invalid" domain: nothing can leave the process), the
/// Integrations tables created in the shared in-memory database, a manual clock for the probe and cache lifetimes, and
/// the warning log captured. Everything else — the repository, the tenant filter, the options binding, the permission
/// definitions — is the production wiring.
/// </summary>
[DependsOn(typeof(OpenTmsEntityFrameworkCoreTestModule))]
public class RoutingTestModule : AbpModule
{
    public static readonly DateTimeOffset Start = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        ConfigureRoutingTestServices(context, configured: true);
    }

    internal static void ConfigureRoutingTestServices(ServiceConfigurationContext context, bool configured)
    {
        var handler = new RoutingHttpHandler();
        handler.AnswerByPath();
        context.Services.AddSingleton(handler);
        context.Services.AddHttpClient<OsrmRoutingProvider>().ConfigurePrimaryHttpMessageHandler(() => handler);
        context.Services.AddHttpClient<ValhallaFunctionProvider>().ConfigurePrimaryHttpMessageHandler(() => handler);
        context.Services.Configure<RoutingIntegrationOptions>(options =>
        {
            options.Osrm.BaseUrl = configured ? RoutingHarness.OsrmBaseUrl : null;
            options.RouteFunction.BaseUrl = configured ? RoutingHarness.FunctionUrl : null;
            options.TimeoutSeconds = 2;
        });

        var time = new ManualTimeProvider(Start);
        context.Services.AddSingleton(time);
        context.Services.Replace(ServiceDescriptor.Singleton<TimeProvider>(time));

        var log = new CapturingLog();
        context.Services.AddSingleton(log);
        context.Services.AddLogging(logging => logging.AddProvider(log));
    }
}

/// <summary>The same application without any base URL: the "not configured" behaviour of design 60's rollout plan.</summary>
[DependsOn(typeof(OpenTmsEntityFrameworkCoreTestModule))]
public class RoutingUnconfiguredTestModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        RoutingTestModule.ConfigureRoutingTestServices(context, configured: false);
    }
}
