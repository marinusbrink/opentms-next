using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenTms.Integrations.AiConnector;
using OpenTms.Integrations.OrderIntake;
using OpenTms.Integrations.OrderIntake.Profiles;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.PostgreSql;
using Volo.Abp.Modularity;

namespace OpenTms.Integrations.EntityFrameworkCore;

[DependsOn(
    typeof(IntegrationsDomainModule),
    typeof(AbpEntityFrameworkCorePostgreSqlModule)
)]
public class IntegrationsEntityFrameworkCoreModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddAbpDbContext<IntegrationsDbContext>(options =>
        {
            options.AddDefaultRepositories(includeAllEntities: true);
            /* Design 72 review: throttled set-based use stamps, the dedupe-safe message insert and the ILIKE list search. */
            options.AddRepository<ApiClient, EfCoreApiClientRepository>();
            options.AddRepository<ImportProfile, EfCoreImportProfileRepository>();
            options.AddRepository<InboundMessage, EfCoreInboundMessageRepository>();
            options.AddRepository<AiConnection, EfCoreAiConnectionRepository>();
            /* Design 72: a document is read with its trace (the step log is part of the aggregate). */
            options.Entity<InboundDocument>(e => e.DefaultWithDetailsFunc = q => q.Include(d => d.Steps));
        });

        Configure<AbpDbContextOptions>(options =>
        {
            options.Configure<IntegrationsDbContext>(c =>
            {
                c.UseNpgsql(b =>
                {
                    /* Each module context keeps its own migrations history table inside its own schema. */
                    b.MigrationsHistoryTable("__EFMigrationsHistory", IntegrationsDbProperties.DbSchema);
                });
            });
        });
    }
}
