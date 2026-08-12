using Microsoft.EntityFrameworkCore;
using Volo.Abp.Data;
using Volo.Abp.EntityFrameworkCore;

namespace OpenTms.PlanningExecution.EntityFrameworkCore;

[ConnectionStringName(PlanningExecutionDbProperties.ConnectionStringName)]
public class PlanningExecutionDbContext : AbpDbContext<PlanningExecutionDbContext>
{
    /* Add DbSet properties for the PlanningExecution module's aggregate roots / entities here. */

    public PlanningExecutionDbContext(DbContextOptions<PlanningExecutionDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        /* Every table of this module lives in its own schema ("planning").
         * Never map an entity of another module into this context. */
        builder.HasDefaultSchema(PlanningExecutionDbProperties.DbSchema);
    }
}
