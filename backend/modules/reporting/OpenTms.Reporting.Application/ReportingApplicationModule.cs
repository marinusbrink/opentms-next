using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenTms.Orders.Permissions;
using OpenTms.PlanningExecution.Permissions;
using OpenTms.Platform.DerivedValues;
using OpenTms.Reporting.Facts;
using Volo.Abp;
using Volo.Abp.Application;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Modularity;
using Volo.Abp.SettingManagement;

namespace OpenTms.Reporting;

[DependsOn(
    typeof(ReportingDomainModule),
    typeof(ReportingApplicationContractsModule),
    typeof(AbpDddApplicationModule),
    typeof(AbpBackgroundWorkersModule),
    typeof(AbpSettingManagementDomainModule)
)]
public class ReportingApplicationModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        /* Design 75 (flow 2): the facts are derived values; no screen watches them yet. */
        Configure<DerivedValueOptions>(options =>
        {
            options.AddCalculator<OrderFactCalculator>(ReportingFactConsts.OrderFactTargetType, OrdersPermissions.TransportOrders.Default);
            options.AddCalculator<TripFactCalculator>(ReportingFactConsts.TripFactTargetType, PlanningExecutionPermissions.TransportTrips.Default);
        });

        context.Services.Configure<ReportingMetricsOptions>(context.Services.GetConfiguration().GetSection(ReportingMetricsOptions.SectionName));
    }

    public override async Task OnApplicationInitializationAsync(ApplicationInitializationContext context)
    {
        /* The DbMigrator does not load this module. */
        if (context.ServiceProvider.GetRequiredService<IOptions<ReportingMetricsOptions>>().Value.WorkersEnabled)
        {
            await context.AddBackgroundWorkerAsync<ReportingFactBackfillWorker>();
            await context.AddBackgroundWorkerAsync<ReportingFactRepairWorker>();
        }
    }
}
