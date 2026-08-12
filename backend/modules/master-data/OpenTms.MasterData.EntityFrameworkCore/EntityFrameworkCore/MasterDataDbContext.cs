using Microsoft.EntityFrameworkCore;
using Volo.Abp.Data;
using Volo.Abp.EntityFrameworkCore;

namespace OpenTms.MasterData.EntityFrameworkCore;

[ConnectionStringName(MasterDataDbProperties.ConnectionStringName)]
public class MasterDataDbContext : AbpDbContext<MasterDataDbContext>
{
    /* Add DbSet properties for the MasterData module's aggregate roots / entities here. */

    public MasterDataDbContext(DbContextOptions<MasterDataDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        /* Every table of this module lives in its own schema ("masterdata").
         * Never map an entity of another module into this context. */
        builder.HasDefaultSchema(MasterDataDbProperties.DbSchema);
    }
}
