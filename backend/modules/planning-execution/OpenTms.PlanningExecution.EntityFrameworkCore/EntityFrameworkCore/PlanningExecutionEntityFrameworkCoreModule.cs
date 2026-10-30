using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenTms.PlanningExecution.TransportTrips;
using OpenTms.Platform.EntityFrameworkCore.Localization;
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

            /* Design 59: a trip's children load explicitly, as split queries (three child collections). */
            options.Entity<TransportTrip>(e => e.DefaultWithDetailsFunc = q => q
                .AsSplitQuery()
                .Include(t => t.Vehicles)
                .Include(t => t.Drivers)
                .Include(t => t.Stops).ThenInclude(s => s.Legs));
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

                /* LocalizedNameQuery.Text → jsonb_extract_path_text (Platform, design 41). */
                c.DbContextOptions.UseLocalizedNameTranslation();
            });
        });
    }
}
