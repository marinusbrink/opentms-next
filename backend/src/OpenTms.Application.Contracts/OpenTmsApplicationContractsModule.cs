using OpenTms.Financial;
using OpenTms.Integrations;
using OpenTms.MasterData;
using OpenTms.Orders;
using OpenTms.PlanningExecution;
using OpenTms.Platform;
using OpenTms.Reporting;
using Volo.Abp.Account;
using Volo.Abp.Account.Localization;
using Volo.Abp.Localization;
using Volo.Abp.VirtualFileSystem;
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

    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        Configure<AbpVirtualFileSystemOptions>(options =>
        {
            options.FileSets.AddEmbedded<OpenTmsApplicationContractsModule>();
        });

        /* The login page's texts come from ABP's Account resource. A locked-out user is told to
         * contact local IT support instead of ABP's "wait a while" (PO decision 2026-10-10). */
        Configure<AbpLocalizationOptions>(options =>
        {
            options.Resources
                .Get<AccountResource>()
                .AddVirtualJson("/Localization/Account");
        });
    }
}
