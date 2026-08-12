using OpenTms.Financial;
using OpenTms.Integrations;
using OpenTms.MasterData;
using OpenTms.Orders;
using OpenTms.PlanningExecution;
using OpenTms.Platform;
using OpenTms.Reporting;
using Volo.Abp.Account;
using Volo.Abp.Modularity;
using Volo.Abp.PermissionManagement;
using Volo.Abp.SettingManagement;
using Volo.Abp.FeatureManagement;
using Volo.Abp.Identity;
using Volo.Abp.TenantManagement;

namespace OpenTms;

[DependsOn(
    typeof(OpenTmsDomainSharedModule),
    typeof(AbpFeatureManagementApplicationContractsModule),
    typeof(AbpSettingManagementApplicationContractsModule),
    typeof(AbpIdentityApplicationContractsModule),
    typeof(AbpAccountApplicationContractsModule),
    typeof(AbpTenantManagementApplicationContractsModule),
    typeof(AbpPermissionManagementApplicationContractsModule),
    typeof(PlatformApplicationContractsModule),
    typeof(OrdersApplicationContractsModule),
    typeof(PlanningExecutionApplicationContractsModule),
    typeof(FinancialApplicationContractsModule),
    typeof(MasterDataApplicationContractsModule),
    typeof(IntegrationsApplicationContractsModule),
    typeof(ReportingApplicationContractsModule)
)]
public class OpenTmsApplicationContractsModule : AbpModule
{
    public override void PreConfigureServices(ServiceConfigurationContext context)
    {
        OpenTmsDtoExtensions.Configure();
    }
}
