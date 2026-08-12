using Microsoft.EntityFrameworkCore;
using Volo.Abp.Data;
using Volo.Abp.EntityFrameworkCore;

namespace OpenTms.Financial.EntityFrameworkCore;

[ConnectionStringName(FinancialDbProperties.ConnectionStringName)]
public class FinancialDbContext : AbpDbContext<FinancialDbContext>
{
    /* Add DbSet properties for the Financial module's aggregate roots / entities here. */

    public FinancialDbContext(DbContextOptions<FinancialDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        /* Every table of this module lives in its own schema ("financial").
         * Never map an entity of another module into this context. */
        builder.HasDefaultSchema(FinancialDbProperties.DbSchema);
    }
}
