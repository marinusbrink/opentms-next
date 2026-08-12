using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.PostgreSql;
using Volo.Abp.Modularity;

namespace OpenTms.Financial.EntityFrameworkCore;

[DependsOn(
    typeof(FinancialDomainModule),
    typeof(AbpEntityFrameworkCorePostgreSqlModule)
)]
public class FinancialEntityFrameworkCoreModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddAbpDbContext<FinancialDbContext>(options =>
        {
            options.AddDefaultRepositories(includeAllEntities: true);
        });

        Configure<AbpDbContextOptions>(options =>
        {
            options.Configure<FinancialDbContext>(c =>
            {
                c.UseNpgsql(b =>
                {
                    /* Each module context keeps its own migrations history table inside its own schema. */
                    b.MigrationsHistoryTable("__EFMigrationsHistory", FinancialDbProperties.DbSchema);
                });
            });
        });
    }
}
