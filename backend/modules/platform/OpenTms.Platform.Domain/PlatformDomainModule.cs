using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenTms.Platform.Auditing;
using OpenTms.Platform.Events.Outbox;
using OpenTms.Platform.Maintenance.Caching;
using OpenTms.Platform.Maintenance.Instances;
using OpenTms.Platform.Tenants;
using Volo.Abp;
using Volo.Abp.Auditing;
using Volo.Abp.Domain;
using Volo.Abp.Features;
using Volo.Abp.Modularity;
using Volo.Abp.TenantManagement;

namespace OpenTms.Platform;

[DependsOn(
    typeof(AbpDddDomainModule),
    typeof(AbpFeaturesModule),
    typeof(AbpTenantManagementDomainModule),
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

        /* The instance registry (docs/designs/maintenance-view.md): the worker is started by the host. */
        Configure<InstanceRegistryOptions>(configuration.GetSection("InstanceRegistry"));

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

        /* Tenants and editions (docs/designs/tenants-and-editions.md). */
        Configure<TenantDatabaseOptions>(configuration.GetSection(TenantDatabaseOptions.SectionName));

        /* The edition of a tenant is resolved from the tenant, not from a login claim, so features
         * evaluate the same in requests, jobs and event handlers. Same name and position as ABP's. */
        Configure<AbpFeatureOptions>(options =>
        {
            var index = options.ValueProviders.IndexOf(typeof(EditionFeatureValueProvider));
            if (index >= 0)
            {
                options.ValueProviders[index] = typeof(TenantEditionFeatureValueProvider);
            }
            else
            {
                options.ValueProviders.Add<TenantEditionFeatureValueProvider>();
            }
        });
    }
}
