using OpenTms.TestDoubles;
using Volo.Abp.Modularity;

namespace OpenTms.EntityFrameworkCore.PlanningExecution;

/// <summary>
/// The SQLite test application of the Planning &amp; Execution tests (design 59): the base module plus the controllable
/// routing and CO₂ doubles of <see cref="TestRoutingWorld"/> in place of Integrations' adapters (design 60 has its own
/// tests on the real adapters with a fake HTTP transport). Scoped to this module: the other modules' tests keep the
/// production registrations.
/// </summary>
[DependsOn(typeof(OpenTmsEntityFrameworkCoreTestModule))]
public class PlanningTestModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        TestRoutingWorld.Register(context.Services);
    }
}
