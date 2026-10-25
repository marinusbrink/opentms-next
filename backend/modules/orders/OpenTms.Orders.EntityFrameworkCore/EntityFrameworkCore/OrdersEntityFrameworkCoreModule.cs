using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenTms.Orders.ConsignmentNotes;
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

            /* Design 58: the order and the template with every child collection; split queries, as Master Data's relation. */
            options.Entity<TransportOrder>(e => e.DefaultWithDetailsFunc = q => q
                .AsSplitQuery()
                .Include(o => o.Lines).ThenInclude(l => l.Stops)
                .Include(o => o.Lines).ThenInclude(l => l.Measurements)
                .Include(o => o.Lines).ThenInclude(l => l.RequiredTransportCharacteristics)
                .Include(o => o.DefaultStops)
                .Include(o => o.References)
                .Include(o => o.PriceLists)
                .Include(o => o.DiscountsOrSurcharges)
                .Include(o => o.Remarks));
            options.Entity<TransportOrderTemplate>(e => e.DefaultWithDetailsFunc = q => q
                .AsSplitQuery()
                .Include(t => t.Lines).ThenInclude(l => l.Stops)
                .Include(t => t.DefaultStops)
                .Include(t => t.References)
                .Include(t => t.PriceLists)
                .Include(t => t.DiscountsOrSurcharges));
            options.Entity<ConsignmentNoteDocument>(e => e.DefaultWithDetailsFunc = q => q.Include(d => d.Lines));
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
