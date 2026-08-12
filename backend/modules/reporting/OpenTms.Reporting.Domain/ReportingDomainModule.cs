using Volo.Abp.Domain;
using Volo.Abp.Modularity;

namespace OpenTms.Reporting;

[DependsOn(
    typeof(AbpDddDomainModule),
    typeof(ReportingDomainSharedModule)
)]
public class ReportingDomainModule : AbpModule
{
}
