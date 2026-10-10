using Microsoft.Extensions.DependencyInjection;
using OpenTms.Platform.Events.Outbox;
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
    }
}
