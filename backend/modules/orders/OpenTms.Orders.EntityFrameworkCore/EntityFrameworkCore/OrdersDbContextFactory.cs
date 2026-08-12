using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OpenTms.Orders.EntityFrameworkCore;

/* Used by EF Core tooling (dotnet ef migrations add) at design time only. It never connects
 * to a database: migrations are applied exclusively by OpenTms.DbMigrator. */
public class OrdersDbContextFactory : IDesignTimeDbContextFactory<OrdersDbContext>
{
    public OrdersDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<OrdersDbContext>()
            .UseNpgsql(
                "Host=localhost;Port=5432;Database=OpenTms_Design;Username=postgres;Password=postgres",
                b => b.MigrationsHistoryTable("__EFMigrationsHistory", OrdersDbProperties.DbSchema));

        return new OrdersDbContext(builder.Options);
    }
}
