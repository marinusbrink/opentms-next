using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenTms.Orders.TransportOrders;
using OpenTms.Platform.EntityFrameworkCore.Localization;
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

            /* includeDetails: the aggregates with children load them with GetAsync(id, includeDetails: true). */
            options.Entity<TransportOrderType>(e => e.DefaultWithDetailsFunc = q => q.Include(t => t.DefaultWaypoints));
            options.Entity<WaypointType>(e => e.DefaultWithDetailsFunc = q => q.Include(t => t.FleetManagementCodes));
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

                /* LocalizedNameQuery.Text → jsonb_extract_path_text (Platform, design 41). */
                c.DbContextOptions.UseLocalizedNameTranslation();
            });
        });
    }
}
