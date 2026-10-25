using Microsoft.Extensions.DependencyInjection;
using OpenTms.Platform.DocumentRendering;
using Volo.Abp.Application;
using Volo.Abp.AuditLogging;
using OpenTms.Platform.Editions;
using OpenTms.Platform.Permissions;
using Volo.Abp.BlobStoring;
using Volo.Abp.Emailing;
using Volo.Abp.FeatureManagement;
using Volo.Abp.Identity;
using Volo.Abp.Modularity;
using Volo.Abp.PermissionManagement;
using Volo.Abp.SettingManagement;
using Volo.Abp.UI.Navigation;

namespace OpenTms.Platform;

[DependsOn(
    typeof(PlatformDomainModule),
    typeof(PlatformApplicationContractsModule),
    typeof(AbpDddApplicationModule),
    typeof(AbpBlobStoringModule),
    typeof(AbpSettingManagementDomainModule),
    typeof(AbpIdentityDomainModule),
    typeof(AbpPermissionManagementDomainModule),
    typeof(AbpAuditLoggingDomainModule),
    typeof(AbpFeatureManagementDomainModule),
    typeof(AbpEmailingModule),
    typeof(AbpUiNavigationModule)
)]
public class PlatformApplicationModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        /* Edition feature values go through ABP's own feature-management endpoint; reading and writing them under
         * provider "E" needs this permission (docs/designs/tenants-and-editions.md). */
        Configure<FeatureManagementOptions>(options =>
        {
            options.ProviderPolicies[EditionConsts.FeatureProviderName] = PlatformPermissions.Editions.ManageFeatures;
        });

        /* Design 55: the interactive command budget of the report definition service ("Platform:DocumentRendering"). */
        context.Services.Configure<DocumentRenderingOptions>(context.Services.GetConfiguration().GetSection(DocumentRenderingOptions.SectionName));
    }
}
