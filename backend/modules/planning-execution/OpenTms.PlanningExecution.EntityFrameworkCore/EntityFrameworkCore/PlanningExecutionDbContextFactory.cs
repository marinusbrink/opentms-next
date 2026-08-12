using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OpenTms.PlanningExecution.EntityFrameworkCore;

/* Used by EF Core tooling (dotnet ef migrations add) at design time only. It never connects
 * to a database: migrations are applied exclusively by OpenTms.DbMigrator. */
public class PlanningExecutionDbContextFactory : IDesignTimeDbContextFactory<PlanningExecutionDbContext>
{
    public PlanningExecutionDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<PlanningExecutionDbContext>()
            .UseNpgsql(
                "Host=localhost;Port=5432;Database=OpenTms_Design;Username=postgres;Password=postgres",
                b => b.MigrationsHistoryTable("__EFMigrationsHistory", PlanningExecutionDbProperties.DbSchema));

        return new PlanningExecutionDbContext(builder.Options);
    }
}
