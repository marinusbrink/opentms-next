using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenTms.EntityFrameworkCore.Platform.DocumentRendering;
using OpenTms.Platform.DocumentRendering;
using OpenTms.TestDoubles;
using Volo.Abp.Modularity;

namespace OpenTms.PostgreSql.TransportRun;

/// <summary>
/// The PostgreSQL test application of the Planning calculators and the end-to-end chain of the transport run (design 56):
/// the production stack of <see cref="OpenTmsPostgreSqlTestModule"/> with Integrations' routing engine and CO₂ calculator
/// replaced by the controllable doubles of <see cref="TestRoutingWorld"/>, and the report rendering provider by design 55's
/// scriptable fake (the CMR print of the chain test). Scoped to this module.
/// </summary>
[DependsOn(typeof(OpenTmsPostgreSqlTestModule))]
public class TransportRunPostgreSqlTestModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        TestRoutingWorld.Register(context.Services);

        context.Services.RemoveAll<IReportRenderingProvider>();
        var provider = new FakeReportRenderingProvider();
        context.Services.AddSingleton(provider);
        context.Services.AddSingleton<IReportRenderingProvider>(provider);
    }
}
