using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OpenTms.Reporting.EntityFrameworkCore;

/* Used by EF Core tooling (dotnet ef migrations add) at design time only. It never connects
 * to a database: migrations are applied exclusively by OpenTms.DbMigrator. */
public class ReportingDbContextFactory : IDesignTimeDbContextFactory<ReportingDbContext>
{
    public ReportingDbContext CreateDbContext(string[] args)
    {
        /* Same timestamp mapping as the runtime (OpenTmsEntityFrameworkCoreModule) and the other
         * factories, so the scaffolded migration matches the model the host runs with. */
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

        var builder = new DbContextOptionsBuilder<ReportingDbContext>()
            .UseNpgsql(
                "Host=localhost;Port=5432;Database=OpenTms_Design;Username=postgres;Password=postgres",
                b => b.MigrationsHistoryTable("__EFMigrationsHistory", ReportingDbProperties.DbSchema));

        return new ReportingDbContext(builder.Options);
    }
}
