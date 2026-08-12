using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.PostgreSql;
using Volo.Abp.Modularity;

namespace OpenTms.PlanningExecution.EntityFrameworkCore;

[DependsOn(
    typeof(PlanningExecutionDomainModule),
    typeof(AbpEntityFrameworkCorePostgreSqlModule)
)]
public class PlanningExecutionEntityFrameworkCoreModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddAbpDbContext<PlanningExecutionDbContext>(options =>
        {
            options.AddDefaultRepositories(includeAllEntities: true);
        });

        Configure<AbpDbContextOptions>(options =>
        {
            options.Configure<PlanningExecutionDbContext>(c =>
            {
                c.UseNpgsql(b =>
                {
                    /* Each module context keeps its own migrations history table inside its own schema. */
                    b.MigrationsHistoryTable("__EFMigrationsHistory", PlanningExecutionDbProperties.DbSchema);
                });
            });
        });
    }
}
