using Microsoft.Extensions.DependencyInjection;
using OpenTms.Platform.Auditing;
using OpenTms.Platform.Events.Outbox;
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
    }
}
