using Microsoft.EntityFrameworkCore;
using Volo.Abp.Data;
using Volo.Abp.EntityFrameworkCore;

namespace OpenTms.Platform.EntityFrameworkCore;

[ConnectionStringName(PlatformDbProperties.ConnectionStringName)]
public class PlatformDbContext : AbpDbContext<PlatformDbContext>
{
    /* Add DbSet properties for the Platform module's aggregate roots / entities here. */

    public PlatformDbContext(DbContextOptions<PlatformDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        /* Every table of this module lives in its own schema ("platform").
         * Never map an entity of another module into this context. */
        builder.HasDefaultSchema(PlatformDbProperties.DbSchema);
    }
}
