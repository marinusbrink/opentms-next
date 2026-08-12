using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OpenTms.Integrations.EntityFrameworkCore;

/* Used by EF Core tooling (dotnet ef migrations add) at design time only. It never connects
 * to a database: migrations are applied exclusively by OpenTms.DbMigrator. */
public class IntegrationsDbContextFactory : IDesignTimeDbContextFactory<IntegrationsDbContext>
{
    public IntegrationsDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<IntegrationsDbContext>()
            .UseNpgsql(
                "Host=localhost;Port=5432;Database=OpenTms_Design;Username=postgres;Password=postgres",
                b => b.MigrationsHistoryTable("__EFMigrationsHistory", IntegrationsDbProperties.DbSchema));

        return new IntegrationsDbContext(builder.Options);
    }
}
