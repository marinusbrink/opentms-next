using Volo.Abp.Application;
using Volo.Abp.Authorization;
using Volo.Abp.Modularity;

namespace OpenTms.PlanningExecution;

[DependsOn(
    typeof(PlanningExecutionDomainSharedModule),
    typeof(AbpDddApplicationContractsModule),
    typeof(AbpAuthorizationModule)
)]
public class PlanningExecutionApplicationContractsModule : AbpModule
{
}
