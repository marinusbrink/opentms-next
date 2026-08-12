using OpenTms.Financial;
using OpenTms.Integrations;
using OpenTms.Localization;
using OpenTms.MasterData;
using OpenTms.Orders;
using OpenTms.PlanningExecution;
using OpenTms.Platform;
using OpenTms.Reporting;
using Volo.Abp.AuditLogging;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.FeatureManagement;
using Volo.Abp.Identity;
using Volo.Abp.Localization;
using Volo.Abp.Localization.ExceptionHandling;
using Volo.Abp.Validation.Localization;
using Volo.Abp.Modularity;
using Volo.Abp.PermissionManagement;
using Volo.Abp.SettingManagement;
using Volo.Abp.VirtualFileSystem;
using Volo.Abp.OpenIddict;
using Volo.Abp.BlobStoring.Database;
using Volo.Abp.TenantManagement;

namespace OpenTms;

[DependsOn(
    typeof(AbpAuditLoggingDomainSharedModule),
    typeof(AbpBackgroundJobsDomainSharedModule),
    typeof(AbpFeatureManagementDomainSharedModule),
    typeof(AbpPermissionManagementDomainSharedModule),
    typeof(AbpSettingManagementDomainSharedModule),
    typeof(AbpIdentityDomainSharedModule),
    typeof(AbpOpenIddictDomainSharedModule),
    typeof(AbpTenantManagementDomainSharedModule),
    typeof(BlobStoringDatabaseDomainSharedModule),
    typeof(PlatformDomainSharedModule),
    typeof(OrdersDomainSharedModule),
    typeof(PlanningExecutionDomainSharedModule),
    typeof(FinancialDomainSharedModule),
    typeof(MasterDataDomainSharedModule),
    typeof(IntegrationsDomainSharedModule),
    typeof(ReportingDomainSharedModule)
    )]
public class OpenTmsDomainSharedModule : AbpModule
{
    public override void PreConfigureServices(ServiceConfigurationContext context)
    {
        OpenTmsGlobalFeatureConfigurator.Configure();
        OpenTmsModuleExtensionConfigurator.Configure();
    }

    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        Configure<AbpVirtualFileSystemOptions>(options =>
        {
            options.FileSets.AddEmbedded<OpenTmsDomainSharedModule>();
        });

        Configure<AbpLocalizationOptions>(options =>
        {
            options.Resources
                .Add<OpenTmsResource>("en")
                .AddBaseTypes(typeof(AbpValidationResource))
                .AddVirtualJson("/Localization/OpenTms");

            options.DefaultResourceType = typeof(OpenTmsResource);

            /* Launch languages for the Dutch logistics market: Dutch and English.
             * Adding a language is a PO decision (each one is translation maintenance). */
            options.Languages.Add(new LanguageInfo("nl", "nl", "Nederlands"));
            options.Languages.Add(new LanguageInfo("en", "en", "English"));
        });
        
        Configure<AbpExceptionLocalizationOptions>(options =>
        {
            options.MapCodeNamespace("OpenTms", typeof(OpenTmsResource));
        });
    }
}
