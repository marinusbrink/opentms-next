using Volo.Abp.Modularity;

namespace OpenTms;

/* Inherit from this class for your domain layer tests. */
public abstract class OpenTmsDomainTestBase<TStartupModule> : OpenTmsTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{

}
