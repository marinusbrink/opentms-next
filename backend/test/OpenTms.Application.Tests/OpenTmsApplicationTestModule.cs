using Volo.Abp.Modularity;

namespace OpenTms;

[DependsOn(
    typeof(OpenTmsApplicationModule),
    typeof(OpenTmsDomainTestModule)
)]
public class OpenTmsApplicationTestModule : AbpModule
{

}
