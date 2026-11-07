using Xunit;

namespace OpenTms.PostgreSql;

/// <summary>Serializes the PostgreSQL-backed test classes: they share real databases.</summary>
/* Not next to the SQLite collection: the volume tests here time real calls against a budget, and on a CI runner those 1,474 tests take the same cores. */
[CollectionDefinition(Name, DisableParallelization = true)]
public class PostgreSqlTestCollection
{
    public const string Name = "OpenTms PostgreSQL collection";
}
