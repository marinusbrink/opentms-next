using Volo.Abp.Application;
using Volo.Abp.Modularity;

namespace OpenTms.PlanningExecution;

[DependsOn(
    typeof(PlanningExecutionDomainModule),
    typeof(PlanningExecutionApplicationContractsModule),
    typeof(AbpDddApplicationModule)
)]
public class PlanningExecutionApplicationModule : AbpModule
{
}
