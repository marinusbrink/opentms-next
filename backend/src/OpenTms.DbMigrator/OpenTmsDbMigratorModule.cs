using OpenTms.EntityFrameworkCore;
using Volo.Abp.Autofac;
using Volo.Abp.Modularity;

namespace OpenTms.DbMigrator;

[DependsOn(
    typeof(AbpAutofacModule),
    typeof(OpenTmsEntityFrameworkCoreModule),
    typeof(OpenTmsApplicationContractsModule)
)]
public class OpenTmsDbMigratorModule : AbpModule
{
}
