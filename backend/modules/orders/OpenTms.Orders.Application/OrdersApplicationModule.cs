using Volo.Abp.Application;
using Volo.Abp.Modularity;

namespace OpenTms.Orders;

[DependsOn(
    typeof(OrdersDomainModule),
    typeof(OrdersApplicationContractsModule),
    typeof(AbpDddApplicationModule)
)]
public class OrdersApplicationModule : AbpModule
{
}
