using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OpenTms.Platform.EntityFrameworkCore;

/* Used by EF Core tooling (dotnet ef migrations add) at design time only. It never connects
 * to a database: migrations are applied exclusively by OpenTms.DbMigrator. */
public class PlatformDbContextFactory : IDesignTimeDbContextFactory<PlatformDbContext>
{
    public PlatformDbContext CreateDbContext(string[] args)
    {
        /* Same Npgsql timestamp mode as the runtime (OpenTmsEntityFrameworkCoreModule) and the main
         * design-time factory, so generated column types match the rest of the schema. */
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

        var builder = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseNpgsql(
                "Host=localhost;Port=5432;Database=OpenTms_Design;Username=postgres;Password=postgres",
                b => b.MigrationsHistoryTable("__EFMigrationsHistory", PlatformDbProperties.DbSchema));

        return new PlatformDbContext(builder.Options);
    }
}
