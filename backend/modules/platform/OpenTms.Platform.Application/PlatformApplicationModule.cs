using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenTms.Platform.Emailing;
using OpenTms.Platform.DocumentRendering;
using Volo.Abp.Application;
using Volo.Abp.Authorization.Permissions;
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
    typeof(AbpPermissionManagementApplicationModule),
    typeof(AbpAuditLoggingDomainModule),
    typeof(AbpFeatureManagementDomainModule),
    typeof(AbpEmailingModule),
    typeof(AbpUiNavigationModule)
)]
public class PlatformApplicationModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        /* Design 64: ABP's mail goes through the send log; the mail service is Integrations' (it replaces the provider). */
        context.Services.TryAddTransient<IEmailDeliveryProvider, NotConfiguredEmailDeliveryProvider>();
        context.Services.Replace(ServiceDescriptor.Transient<IEmailSender, SendLogEmailSender>());

        /* Edition feature values go through ABP's own feature-management endpoint; reading and writing them under
         * provider "E" needs this permission (docs/designs/tenants-and-editions.md). */
        Configure<FeatureManagementOptions>(options =>
        {
            options.ProviderPolicies[EditionConsts.FeatureProviderName] = PlatformPermissions.Editions.ManageFeatures;
        });

        /* Design 73: ABP's permission-management API reads and writes a role's grants (provider "R") and a user's direct
         * grants (provider "U") only with these permissions. PostConfigure: the Identity module sets its own
         * AbpIdentity.*.ManagePermissions policies in ConfigureServices, and this must win regardless of module order. */
        context.Services.PostConfigure<PermissionManagementOptions>(options =>
        {
            options.ProviderPolicies[RolePermissionValueProvider.ProviderName] = PlatformPermissions.Administration.Roles.ManagePermissions;
            options.ProviderPolicies[UserPermissionValueProvider.ProviderName] = PlatformPermissions.Administration.Users.ManagePermissions;
        });

        /* Design 55: the interactive command budget of the report definition service ("Platform:DocumentRendering"). */
        context.Services.Configure<DocumentRenderingOptions>(context.Services.GetConfiguration().GetSection(DocumentRenderingOptions.SectionName));
    }
}
