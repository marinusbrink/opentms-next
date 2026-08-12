using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OpenTms.Financial.EntityFrameworkCore;

/* Used by EF Core tooling (dotnet ef migrations add) at design time only. It never connects
 * to a database: migrations are applied exclusively by OpenTms.DbMigrator. */
public class FinancialDbContextFactory : IDesignTimeDbContextFactory<FinancialDbContext>
{
    public FinancialDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<FinancialDbContext>()
            .UseNpgsql(
                "Host=localhost;Port=5432;Database=OpenTms_Design;Username=postgres;Password=postgres",
                b => b.MigrationsHistoryTable("__EFMigrationsHistory", FinancialDbProperties.DbSchema));

        return new FinancialDbContext(builder.Options);
    }
}
