using Volo.Abp.Application;
using Volo.Abp.Modularity;

namespace OpenTms.Reporting;

[DependsOn(
    typeof(ReportingDomainModule),
    typeof(ReportingApplicationContractsModule),
    typeof(AbpDddApplicationModule)
)]
public class ReportingApplicationModule : AbpModule
{
}
