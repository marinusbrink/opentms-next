using Microsoft.EntityFrameworkCore;
using Volo.Abp.Data;
using Volo.Abp.EntityFrameworkCore;

namespace OpenTms.Reporting.EntityFrameworkCore;

[ConnectionStringName(ReportingDbProperties.ConnectionStringName)]
public class ReportingDbContext : AbpDbContext<ReportingDbContext>
{
    /* Add DbSet properties for the Reporting module's aggregate roots / entities here. */

    public ReportingDbContext(DbContextOptions<ReportingDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        /* Every table of this module lives in its own schema ("reporting").
         * Never map an entity of another module into this context. */
        builder.HasDefaultSchema(ReportingDbProperties.DbSchema);
    }
}
