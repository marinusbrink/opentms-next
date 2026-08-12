using Volo.Abp.Domain;
using Volo.Abp.Modularity;

namespace OpenTms.Financial;

[DependsOn(
    typeof(AbpDddDomainModule),
    typeof(FinancialDomainSharedModule)
)]
public class FinancialDomainModule : AbpModule
{
}
