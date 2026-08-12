using Volo.Abp.Application;
using Volo.Abp.Authorization;
using Volo.Abp.Modularity;

namespace OpenTms.Platform;

[DependsOn(
    typeof(PlatformDomainSharedModule),
    typeof(AbpDddApplicationContractsModule),
    typeof(AbpAuthorizationModule)
)]
public class PlatformApplicationContractsModule : AbpModule
{
}
