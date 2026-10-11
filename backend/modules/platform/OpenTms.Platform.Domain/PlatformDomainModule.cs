using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenTms.Platform.Auditing;
using OpenTms.Platform.Events.Outbox;
using OpenTms.Platform.Maintenance.Caching;
using Volo.Abp;
using Volo.Abp.Auditing;
using Volo.Abp.Domain;
using Volo.Abp.Modularity;

namespace OpenTms.Platform;

[DependsOn(
    typeof(AbpDddDomainModule),
    typeof(PlatformDomainSharedModule)
)]
public class PlatformDomainModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var configuration = context.Services.GetConfiguration();

        /* Transactional outbox (docs/designs/outbox-per-tenant.md): the store and the selector are
         * configured by the EntityFrameworkCore project; the dispatcher is started by the host. */
        Configure<OutboxOptions>(configuration.GetSection("Outbox"));

        /* The functional logbook (entity history) is on by default: PO decision 2026-10-10.
         * ABP records entity changes only for types a selector matches and ships with none. */
        Configure<AbpAuditingOptions>(options =>
        {
            options.EntityHistorySelectors.Add(
                new NamedTypeSelector(FunctionalLogbookEntitySelector.Name, FunctionalLogbookEntitySelector.IsSelected));
        });

        /* The in-memory distributed cache of this instance, replaced by one the Maintenance view can
         * count and clear (docs/designs/maintenance-view.md). A distributed cache (proposal AP-13)
         * registers its own IDistributedCache after this module and wins. */
        context.Services.Replace(ServiceDescriptor.Singleton<IDistributedCache, OpenTmsMemoryDistributedCache>());
    }
}
