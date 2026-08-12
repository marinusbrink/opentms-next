using Volo.Abp.Application;
using Volo.Abp.Authorization;
using Volo.Abp.Modularity;

namespace OpenTms.Orders;

[DependsOn(
    typeof(OrdersDomainSharedModule),
    typeof(AbpDddApplicationContractsModule),
    typeof(AbpAuthorizationModule)
)]
public class OrdersApplicationContractsModule : AbpModule
{
}
