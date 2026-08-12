using Volo.Abp.Application;
using Volo.Abp.Authorization;
using Volo.Abp.Modularity;

namespace OpenTms.Integrations;

[DependsOn(
    typeof(IntegrationsDomainSharedModule),
    typeof(AbpDddApplicationContractsModule),
    typeof(AbpAuthorizationModule)
)]
public class IntegrationsApplicationContractsModule : AbpModule
{
}
