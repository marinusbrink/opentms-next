using Volo.Abp.Domain;
using Volo.Abp.Modularity;

namespace OpenTms.PlanningExecution;

[DependsOn(
    typeof(AbpDddDomainModule),
    typeof(PlanningExecutionDomainSharedModule)
)]
public class PlanningExecutionDomainModule : AbpModule
{
}
