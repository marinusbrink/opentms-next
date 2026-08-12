using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.PostgreSql;
using Volo.Abp.Modularity;

namespace OpenTms.Orders.EntityFrameworkCore;

[DependsOn(
    typeof(OrdersDomainModule),
    typeof(AbpEntityFrameworkCorePostgreSqlModule)
)]
public class OrdersEntityFrameworkCoreModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddAbpDbContext<OrdersDbContext>(options =>
        {
            options.AddDefaultRepositories(includeAllEntities: true);
        });

        Configure<AbpDbContextOptions>(options =>
        {
            options.Configure<OrdersDbContext>(c =>
            {
                c.UseNpgsql(b =>
                {
                    /* Each module context keeps its own migrations history table inside its own schema. */
                    b.MigrationsHistoryTable("__EFMigrationsHistory", OrdersDbProperties.DbSchema);
                });
            });
        });
    }
}
