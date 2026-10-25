using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenTms.Orders.Permissions;
using OpenTms.Orders.TransportOrders;
using OpenTms.Platform.DerivedValues;
using Volo.Abp;
using Volo.Abp.Application;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.BlobStoring;
using Volo.Abp.Modularity;

namespace OpenTms.Orders;

[DependsOn(
    typeof(OrdersDomainModule),
    typeof(OrdersApplicationContractsModule),
    typeof(AbpDddApplicationModule),
    typeof(AbpBlobStoringModule),
    typeof(AbpBackgroundWorkersModule)
)]
public class OrdersApplicationModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        /* Design 56 (flow 2): the order's stored derived values; watched with the orders permission. */
        Configure<DerivedValueOptions>(options =>
        {
            options.AddCalculator<OrderSummaryCalculator>(OrdersConsts.OrderSummaryTargetType, OrdersPermissions.TransportOrders.Default);
        });

        context.Services.Configure<TemplateMaterialisationOptions>(context.Services.GetConfiguration().GetSection(TemplateMaterialisationOptions.SectionName));
    }

    public override async Task OnApplicationInitializationAsync(ApplicationInitializationContext context)
    {
        /* Design 58: the hourly template materialisation, as the host registers its own workers (Outbox:DispatchEnabled). */
        var options = context.ServiceProvider.GetRequiredService<IOptions<TemplateMaterialisationOptions>>().Value;
        if (options.Enabled)
        {
            await context.AddBackgroundWorkerAsync<TemplateMaterialisationWorker>();
        }
    }
}
