using Volo.Abp.Modularity;

namespace OpenTms;

[DependsOn(
    typeof(OpenTmsDomainModule),
    typeof(OpenTmsTestBaseModule)
)]
public class OpenTmsDomainTestModule : AbpModule
{

}
