using OpenTms.Financial.Permissions;
using OpenTms.Platform.PredefinedRoles;
using Volo.Abp.Application;
using Volo.Abp.Authorization;
using Volo.Abp.Modularity;

namespace OpenTms.Financial;

[DependsOn(
    typeof(FinancialDomainSharedModule),
    typeof(AbpDddApplicationContractsModule),
    typeof(AbpAuthorizationModule)
)]
public class FinancialApplicationContractsModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        /* Its permissions in the predefined roles (design 71); here, so the DbMigrator seeds them too. */
        Configure<PredefinedRoleOptions>(FinancialPredefinedRoles.Configure);
    }
}
