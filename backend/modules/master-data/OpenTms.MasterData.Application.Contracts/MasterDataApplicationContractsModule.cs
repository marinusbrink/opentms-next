using Volo.Abp.Application;
using Volo.Abp.Authorization;
using Volo.Abp.Modularity;

namespace OpenTms.MasterData;

[DependsOn(
    typeof(MasterDataDomainSharedModule),
    typeof(AbpDddApplicationContractsModule),
    typeof(AbpAuthorizationModule)
)]
public class MasterDataApplicationContractsModule : AbpModule
{
}
