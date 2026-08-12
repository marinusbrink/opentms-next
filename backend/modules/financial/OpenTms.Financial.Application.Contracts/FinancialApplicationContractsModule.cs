using Volo.Abp.Application;
using Volo.Abp.Authorization;
using Volo.Abp.Modularity;

namespace OpenTms.Financial;

[DependsOn(
    typeof(FinancialDomainSharedModule),
    typeof(AbpDddApplicationContractsModule),
    typeof(AbpAuthorizationModule)
)]
public class FinancialApplicationContractsModule : AbpModule
{
}
