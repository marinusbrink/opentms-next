using Microsoft.EntityFrameworkCore;
using Volo.Abp.Data;
using Volo.Abp.EntityFrameworkCore;

namespace OpenTms.Orders.EntityFrameworkCore;

[ConnectionStringName(OrdersDbProperties.ConnectionStringName)]
public class OrdersDbContext : AbpDbContext<OrdersDbContext>
{
    /* Add DbSet properties for the Orders module's aggregate roots / entities here. */

    public OrdersDbContext(DbContextOptions<OrdersDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        /* Every table of this module lives in its own schema ("orders").
         * Never map an entity of another module into this context. */
        builder.HasDefaultSchema(OrdersDbProperties.DbSchema);
    }
}
