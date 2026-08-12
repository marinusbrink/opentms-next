using Volo.Abp.Application;
using Volo.Abp.Modularity;

namespace OpenTms.Financial;

[DependsOn(
    typeof(FinancialDomainModule),
    typeof(FinancialApplicationContractsModule),
    typeof(AbpDddApplicationModule)
)]
public class FinancialApplicationModule : AbpModule
{
}
