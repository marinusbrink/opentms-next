using OpenTms.Integrations.Permissions;
using OpenTms.Platform;
using OpenTms.Platform.PredefinedRoles;
using Volo.Abp.Application;
using Volo.Abp.Authorization;
using Volo.Abp.Modularity;

namespace OpenTms.Integrations;

[DependsOn(
    typeof(IntegrationsDomainSharedModule),
    typeof(PlatformApplicationContractsModule),
    typeof(AbpDddApplicationContractsModule),
    typeof(AbpAuthorizationModule)
)]
public class IntegrationsApplicationContractsModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        /* Its permissions in the predefined roles (design 71); here, so the DbMigrator seeds them too. */
        Configure<PredefinedRoleOptions>(IntegrationsPredefinedRoles.Configure);
    }
}
