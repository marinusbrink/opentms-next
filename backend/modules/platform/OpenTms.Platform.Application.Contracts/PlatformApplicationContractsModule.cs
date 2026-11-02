using OpenTms.Platform.Permissions;
using OpenTms.Platform.PredefinedRoles;
using Volo.Abp.Application;
using Volo.Abp.Authorization;
using Volo.Abp.Modularity;

namespace OpenTms.Platform;

[DependsOn(
    typeof(PlatformDomainSharedModule),
    typeof(AbpDddApplicationContractsModule),
    typeof(AbpAuthorizationModule)
)]
public class PlatformApplicationContractsModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        /* Its permissions in the predefined roles (design 71); here, so the DbMigrator seeds them too. */
        Configure<PredefinedRoleOptions>(PlatformPredefinedRoles.Configure);
    }
}
