using Volo.Abp.Domain;
using Volo.Abp.Modularity;

namespace OpenTms.MasterData;

[DependsOn(
    typeof(AbpDddDomainModule),
    typeof(MasterDataDomainSharedModule)
)]
public class MasterDataDomainModule : AbpModule
{
}
