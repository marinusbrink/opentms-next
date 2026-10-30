using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Npgsql;

namespace OpenTms.PostgreSql;

/// <summary>
/// The rows the charged-bases migration tests (design 61 "Order page review" point 3) write the way the previous release
/// would: an INSERT that names only the columns that schema requires (NOT NULL without a default), filled with neutral
/// values per type, and optionally fixed values for named columns. Reading the required columns from the schema itself
/// keeps the test about the migration, not about the shape of the row.
/// </summary>
internal static class ChargedBasesMigrationRows
{
    public static async Task<Guid> InsertAsync(string database, string schema, string table, IReadOnlyDictionary<string, object>? values = null)
    {
        var id = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(PostgreSqlTestDatabases.ConnectionStringFor(database));
        await connection.OpenAsync();

        var required = new List<(string Name, string Type, int? Length)>();
        await using (var columns = new NpgsqlCommand(
            "SELECT column_name, data_type, character_maximum_length FROM information_schema.columns " +
            "WHERE table_schema = @schema AND table_name = @table AND is_nullable = 'NO' AND column_default IS NULL ORDER BY ordinal_position", connection))
        {
            columns.Parameters.AddWithValue("schema", schema);
            columns.Parameters.AddWithValue("table", table);
            await using var reader = await columns.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                required.Add((reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetInt32(2)));
            }
        }

        var given = values ?? new Dictionary<string, object>();
        var names = required.Select(c => c.Name).Union(given.Keys).Distinct().ToList();
        await using var insert = new NpgsqlCommand { Connection = connection };
        var parameters = new List<string>();
        for (var i = 0; i < names.Count; i++)
        {
            var name = names[i];
            var parameter = "p" + i;
            parameters.Add("@" + parameter);
            if (name == "Id")
            {
                insert.Parameters.AddWithValue(parameter, id);
                continue;
            }

            if (given.TryGetValue(name, out var value))
            {
                insert.Parameters.AddWithValue(parameter, value);
                continue;
            }

            var column = required.Single(c => c.Name == name);
            if (column.Type is "jsonb" or "json")
            {
                insert.Parameters.Add(new NpgsqlParameter(parameter, NpgsqlTypes.NpgsqlDbType.Jsonb) { Value = "{}" });
                continue;
            }

            insert.Parameters.AddWithValue(parameter, Neutral(column.Name, column.Type, column.Length));
        }

        insert.CommandText = $"INSERT INTO {schema}.\"{table}\" ({string.Join(", ", names.Select(n => "\"" + n + "\""))}) VALUES ({string.Join(", ", parameters)})";
        await insert.ExecuteNonQueryAsync();
        return id;
    }

    public static async Task<List<int>> ColumnValuesAsync(string database, string schema, string table, string column, IEnumerable<Guid> ids)
    {
        await using var connection = new NpgsqlConnection(PostgreSqlTestDatabases.ConnectionStringFor(database));
        await connection.OpenAsync();
        var result = new List<int>();
        foreach (var id in ids)
        {
            await using var command = new NpgsqlCommand($"SELECT \"{column}\" FROM {schema}.\"{table}\" WHERE \"Id\" = @id", connection);
            command.Parameters.AddWithValue("id", id);
            result.Add((int)(await command.ExecuteScalarAsync())!);
        }

        return result;
    }

    public static async Task<string?> ColumnAsync(string database, string schema, string table, string column)
    {
        await using var connection = new NpgsqlConnection(PostgreSqlTestDatabases.ConnectionStringFor(database));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT data_type || ':' || is_nullable || ':' || COALESCE(column_default, '-') FROM information_schema.columns WHERE table_schema = @schema AND table_name = @table AND column_name = @column", connection);
        command.Parameters.AddWithValue("schema", schema);
        command.Parameters.AddWithValue("table", table);
        command.Parameters.AddWithValue("column", column);
        return (string?)await command.ExecuteScalarAsync();
    }

    public static async Task<long> CountAsync(string database, string schema, string table)
    {
        await using var connection = new NpgsqlConnection(PostgreSqlTestDatabases.ConnectionStringFor(database));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"SELECT count(*) FROM {schema}.\"{table}\"", connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    public static async Task RecreateAsync(string database, bool create)
    {
        await using var connection = new NpgsqlConnection(PostgreSqlTestDatabases.ConnectionStringFor("postgres"));
        await connection.OpenAsync();
        await using (var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{database}\" WITH (FORCE)", connection))
        {
            await drop.ExecuteNonQueryAsync();
        }

        if (create)
        {
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", connection);
            await command.ExecuteNonQueryAsync();
        }

        NpgsqlConnection.ClearAllPools();
    }

    private static object Neutral(string name, string type, int? length) => type switch
    {
        "uuid" => Guid.NewGuid(),
        "character varying" or "text" or "character" => Text(name, length),
        "integer" => 1,
        "smallint" => (short)1,
        "bigint" => 1L,
        "numeric" or "double precision" or "real" => 0m,
        "boolean" => false,
        "date" => new DateOnly(2026, 10, 26),
        "timestamp with time zone" => DateTime.SpecifyKind(new DateTime(2026, 10, 26, 8, 0, 0), DateTimeKind.Utc),
        "timestamp without time zone" => new DateTime(2026, 10, 26, 8, 0, 0),
        "time without time zone" => new TimeOnly(8, 0),
        "interval" => TimeSpan.FromMinutes(30),
        _ => throw new NotSupportedException($"no neutral value for {name} ({type})")
    };

    private static string Text(string name, int? length)
    {
        /* Unique per row: names and numbers carry unique indexes. */
        var text = name.Contains("Currency", StringComparison.Ordinal) ? "EUR" : name == "ExtraProperties" ? "{}" : Guid.NewGuid().ToString("N")[..8] + name;
        return length is { } max && text.Length > max ? text[..max] : text;
    }
}
