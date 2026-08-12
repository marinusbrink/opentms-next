using Volo.Abp.Modularity;

namespace OpenTms;

public abstract class OpenTmsApplicationTestBase<TStartupModule> : OpenTmsTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{

}
