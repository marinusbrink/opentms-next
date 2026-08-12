using System.Threading.Tasks;

namespace OpenTms.Platform.Data;

/* Every module's EntityFrameworkCore project implements this interface for its own DbContext.
 * OpenTms.DbMigrator collects all implementations and runs them against the host database and
 * against every tenant database that has its own connection string (database-per-tenant). */
public interface IDbSchemaMigrator
{
    string ModuleName { get; }

    Task MigrateAsync();
}
