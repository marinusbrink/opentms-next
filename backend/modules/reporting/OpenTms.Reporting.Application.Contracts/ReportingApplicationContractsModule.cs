using OpenTms.Platform;
using Volo.Abp.Application;
using Volo.Abp.Authorization;
using Volo.Abp.Modularity;

namespace OpenTms.Reporting;

[DependsOn(
    typeof(ReportingDomainSharedModule),
    typeof(PlatformApplicationContractsModule),
    typeof(AbpDddApplicationContractsModule),
    typeof(AbpAuthorizationModule)
)]
public class ReportingApplicationContractsModule : AbpModule
{
}
