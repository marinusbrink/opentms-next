using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenTms.Financial.Invoicing;
using OpenTms.Financial.Tariffs;
using OpenTms.Platform.EntityFrameworkCore.Localization;
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

            /* includeDetails: the aggregates with children load them with GetAsync(id, includeDetails: true). */
            options.Entity<VatPercentage>(e => e.DefaultWithDetailsFunc = q => q.Include(v => v.Rates));
            /* Two child collections: split queries (200 values × 100 regions would otherwise multiply). */
            options.Entity<DiscountOrSurcharge>(e => e.DefaultWithDetailsFunc = q => q
                .AsSplitQuery()
                .Include(d => d.Values)
                .Include(d => d.Regions));
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

                /* LocalizedNameQuery.Text → jsonb_extract_path_text (Platform, design 41). */
                c.DbContextOptions.UseLocalizedNameTranslation();
            });
        });
    }
}
