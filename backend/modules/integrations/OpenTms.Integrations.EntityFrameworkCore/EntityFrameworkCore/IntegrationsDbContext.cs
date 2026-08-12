using Microsoft.EntityFrameworkCore;
using Volo.Abp.Data;
using Volo.Abp.EntityFrameworkCore;

namespace OpenTms.Integrations.EntityFrameworkCore;

[ConnectionStringName(IntegrationsDbProperties.ConnectionStringName)]
public class IntegrationsDbContext : AbpDbContext<IntegrationsDbContext>
{
    /* Add DbSet properties for the Integrations module's aggregate roots / entities here. */

    public IntegrationsDbContext(DbContextOptions<IntegrationsDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        /* Every table of this module lives in its own schema ("integrations").
         * Never map an entity of another module into this context. */
        builder.HasDefaultSchema(IntegrationsDbProperties.DbSchema);
    }
}
