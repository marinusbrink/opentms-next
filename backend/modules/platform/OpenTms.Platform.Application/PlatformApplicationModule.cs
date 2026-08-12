using Volo.Abp.Application;
using Volo.Abp.Modularity;

namespace OpenTms.Platform;

[DependsOn(
    typeof(PlatformDomainModule),
    typeof(PlatformApplicationContractsModule),
    typeof(AbpDddApplicationModule)
)]
public class PlatformApplicationModule : AbpModule
{
}
