using Volo.Abp.Application;
using Volo.Abp.Modularity;
using Volo.Abp.SettingManagement;

namespace OpenTms.Platform;

[DependsOn(
    typeof(PlatformDomainModule),
    typeof(PlatformApplicationContractsModule),
    typeof(AbpDddApplicationModule),
    typeof(AbpSettingManagementDomainModule)
)]
public class PlatformApplicationModule : AbpModule
{
}
