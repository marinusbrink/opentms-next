using Xunit;

namespace OpenTms.PostgreSql;

/// <summary>Serializes the PostgreSQL-backed test classes: they share real databases.</summary>
[CollectionDefinition(Name)]
public class PostgreSqlTestCollection
{
    public const string Name = "OpenTms PostgreSQL collection";
}
