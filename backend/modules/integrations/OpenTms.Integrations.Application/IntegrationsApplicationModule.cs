using Volo.Abp.Application;
using Volo.Abp.Modularity;

namespace OpenTms.Integrations;

[DependsOn(
    typeof(IntegrationsDomainModule),
    typeof(IntegrationsApplicationContractsModule),
    typeof(AbpDddApplicationModule)
)]
public class IntegrationsApplicationModule : AbpModule
{
}
