using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenTms.MasterData.Drivers;
using OpenTms.MasterData.Fleet;
using OpenTms.MasterData.Relations;
using OpenTms.MasterData.Regions;
using OpenTms.MasterData.Transport;
using OpenTms.Platform.EntityFrameworkCore.Localization;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.PostgreSql;
using Volo.Abp.Modularity;

namespace OpenTms.MasterData.EntityFrameworkCore;

[DependsOn(
    typeof(MasterDataDomainModule),
    typeof(AbpEntityFrameworkCorePostgreSqlModule)
)]
public class MasterDataEntityFrameworkCoreModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddAbpDbContext<MasterDataDbContext>(options =>
        {
            options.AddDefaultRepositories(includeAllEntities: true);

            /* includeDetails: the aggregates with children load them with GetAsync(id, includeDetails: true). */
            options.Entity<VehicleType>(e => e.DefaultWithDetailsFunc = q => q.Include(v => v.Characteristics));
            options.Entity<TransportUnit>(e => e.DefaultWithDetailsFunc = q => q.Include(u => u.LoadFactors));
            options.Entity<GeographicalRegion>(e => e.DefaultWithDetailsFunc = q => q.Include(r => r.PostalCodeRanges));
            options.Entity<Relation>(e => e.DefaultWithDetailsFunc = q => q
                .Include(r => r.Addresses)
                .Include(r => r.ContactPersons)
                .Include(r => r.BankAccounts)
                .Include(r => r.Categories)
                .Include(r => r.GeographicalRegions)
                .Include(r => r.OrganizationUnits));
            options.Entity<Driver>(e => e.DefaultWithDetailsFunc = q => q.Include(d => d.Qualifications));
        });

        Configure<AbpDbContextOptions>(options =>
        {
            options.Configure<MasterDataDbContext>(c =>
            {
                c.UseNpgsql(b =>
                {
                    /* Each module context keeps its own migrations history table inside its own schema. */
                    b.MigrationsHistoryTable("__EFMigrationsHistory", MasterDataDbProperties.DbSchema);
                });

                /* LocalizedNameQuery.Text → jsonb_extract_path_text (Platform, design 41). */
                c.DbContextOptions.UseLocalizedNameTranslation();
            });
        });
    }
}
