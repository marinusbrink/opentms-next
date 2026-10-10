using Microsoft.Extensions.DependencyInjection;
using OpenTms.Platform.Events;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.DistributedEvents;
using Volo.Abp.EntityFrameworkCore.PostgreSql;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Modularity;

namespace OpenTms.Platform.EntityFrameworkCore;

[DependsOn(
    typeof(PlatformDomainModule),
    typeof(AbpEntityFrameworkCorePostgreSqlModule)
)]
public class PlatformEntityFrameworkCoreModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddAbpDbContext<PlatformDbContext>(options =>
        {
            options.AddDefaultRepositories(includeAllEntities: true);
        });

        Configure<AbpDbContextOptions>(options =>
        {
            options.Configure<PlatformDbContext>(c =>
            {
                c.UseNpgsql(b =>
                {
                    /* Each module context keeps its own migrations history table inside its own schema. */
                    b.MigrationsHistoryTable("__EFMigrationsHistory", PlatformDbProperties.DbSchema);
                });
            });
        });

        ConfigureOutbox();
    }

    private void ConfigureOutbox()
    {
        /* Transactional outbox (docs/designs/outbox-per-tenant.md, AD-16). */
        Configure<AbpDistributedEventBusOptions>(options =>
        {
            options.Outboxes.Configure(outbox =>
            {
                outbox.UseDbContext<PlatformDbContext>();

                /* Our store stamps the tenant and signals the dispatcher on commit. */
                outbox.ImplementationType = typeof(PlatformEventOutbox);

                /* Only OpenTms.* events use the outbox; ABP's own events keep direct delivery. */
                outbox.Selector = OutboxEvents.UsesOutbox;

                /* ABP's OutboxSender only reads the host database, so tenant events would never be
                 * sent. Never switch it back on: the OutboxDispatcher visits every database instead. */
                outbox.IsSendingEnabled = false;
            });
        });
    }
}
