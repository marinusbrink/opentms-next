using OpenTms.Financial;
using OpenTms.Integrations;
using OpenTms.MasterData;
using OpenTms.Orders;
using OpenTms.PlanningExecution;
using OpenTms.Platform;
using OpenTms.Reporting;
using Volo.Abp.PermissionManagement;
using Volo.Abp.SettingManagement;
using Volo.Abp.Account;
using Volo.Abp.Identity;
using Volo.Abp.Mapperly;
using Volo.Abp.FeatureManagement;
using Volo.Abp.Modularity;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.TenantManagement;

namespace OpenTms;

[DependsOn(
    typeof(OpenTmsDomainModule),
    typeof(OpenTmsApplicationContractsModule),
    typeof(AbpPermissionManagementApplicationModule),
    typeof(AbpFeatureManagementApplicationModule),
    typeof(AbpIdentityApplicationModule),
    typeof(AbpAccountApplicationModule),
    typeof(AbpTenantManagementApplicationModule),
    typeof(AbpSettingManagementApplicationModule),
    typeof(PlatformApplicationModule),
    typeof(OrdersApplicationModule),
    typeof(PlanningExecutionApplicationModule),
    typeof(FinancialApplicationModule),
    typeof(MasterDataApplicationModule),
    typeof(IntegrationsApplicationModule),
    typeof(ReportingApplicationModule)
    )]
public class OpenTmsApplicationModule : AbpModule
{

}
