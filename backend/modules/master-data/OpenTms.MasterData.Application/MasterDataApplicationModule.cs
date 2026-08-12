using Volo.Abp.Application;
using Volo.Abp.Modularity;

namespace OpenTms.MasterData;

[DependsOn(
    typeof(MasterDataDomainModule),
    typeof(MasterDataApplicationContractsModule),
    typeof(AbpDddApplicationModule)
)]
public class MasterDataApplicationModule : AbpModule
{
}
