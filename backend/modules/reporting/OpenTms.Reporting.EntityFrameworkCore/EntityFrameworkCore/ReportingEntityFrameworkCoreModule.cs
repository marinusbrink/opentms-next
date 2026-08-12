using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.PostgreSql;
using Volo.Abp.Modularity;

namespace OpenTms.Reporting.EntityFrameworkCore;

[DependsOn(
    typeof(ReportingDomainModule),
    typeof(AbpEntityFrameworkCorePostgreSqlModule)
)]
public class ReportingEntityFrameworkCoreModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddAbpDbContext<ReportingDbContext>(options =>
        {
            options.AddDefaultRepositories(includeAllEntities: true);
        });

        Configure<AbpDbContextOptions>(options =>
        {
            options.Configure<ReportingDbContext>(c =>
            {
                c.UseNpgsql(b =>
                {
                    /* Each module context keeps its own migrations history table inside its own schema. */
                    b.MigrationsHistoryTable("__EFMigrationsHistory", ReportingDbProperties.DbSchema);
                });
            });
        });
    }
}
