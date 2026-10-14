using System;
using Microsoft.EntityFrameworkCore;
using OpenTms.Platform.Tenants;
using Volo.Abp.Identity;
using Volo.Abp.ObjectExtending;
using Volo.Abp.TenantManagement;
using Volo.Abp.Threading;

namespace OpenTms.EntityFrameworkCore;

public static class OpenTmsEfCoreEntityExtensionMappings
{
    private static readonly OneTimeRunner OneTimeRunner = new OneTimeRunner();

    public static void Configure()
    {
        OpenTmsGlobalFeatureConfigurator.Configure();
        OpenTmsModuleExtensionConfigurator.Configure();

        OneTimeRunner.Run(() =>
        {
            /* The edition a tenant is on (docs/designs/tenants-and-editions.md): a real column, as ABP's Saas
             * module has it, so editions can count their tenants in one query. Platform reads and writes it as
             * the tenant's "EditionId" extra property. */
            ObjectExtensionManager.Instance
                .MapEfCoreProperty<Tenant, Guid?>(
                    TenantProperties.EditionId,
                    (entityBuilder, _) => entityBuilder.HasIndex(TenantProperties.EditionId));

                /* You can configure extra properties for the
                 * entities defined in the modules used by your application.
                 *
                 * This class can be used to map these extra properties to table fields in the database.
                 *
                 * USE THIS CLASS ONLY TO CONFIGURE EF CORE RELATED MAPPING.
                 * USE OpenTmsModuleExtensionConfigurator CLASS (in the Domain.Shared project)
                 * FOR A HIGH LEVEL API TO DEFINE EXTRA PROPERTIES TO ENTITIES OF THE USED MODULES
                 *
                 * Example: Map a property to a table field:

                     ObjectExtensionManager.Instance
                         .MapEfCoreProperty<IdentityUser, string>(
                             "MyProperty",
                             (entityBuilder, propertyBuilder) =>
                             {
                                 propertyBuilder.HasMaxLength(128);
                             }
                         );

                 * See the documentation for more:
                 * https://docs.abp.io/en/abp/latest/Customizing-Application-Modules-Extending-Entities
                 */
        });
    }
}
