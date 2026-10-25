using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace OpenTms.Orders.EntityFrameworkCore;

/// <summary>
/// The jsonb columns of the Orders module (design 56 "Attributes", AD-06): the attribute dictionary of orders, lines and
/// templates and the small lists (ids, enum sets, measurements) that are document-shaped parts of their owner. Each has
/// a value comparer, so EF Core notices an in-place change. On SQLite the column stores the same JSON text.
/// </summary>
public static class OrdersJsonConversions
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.General);

    public static PropertyBuilder<Dictionary<string, string>> HasJsonbDictionary(this PropertyBuilder<Dictionary<string, string>> property)
        => property
            .HasConversion(
                new ValueConverter<Dictionary<string, string>, string>(
                    v => JsonSerializer.Serialize(v.OrderBy(p => p.Key, StringComparer.Ordinal).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal), Options),
                    v => JsonSerializer.Deserialize<Dictionary<string, string>>(v, Options) ?? new Dictionary<string, string>()),
                new ValueComparer<Dictionary<string, string>>(
                    (a, b) => SameEntries(a, b),
                    v => v.OrderBy(p => p.Key, StringComparer.Ordinal).Aggregate(17, (hash, p) => HashCode.Combine(hash, p.Key, p.Value)),
                    v => new Dictionary<string, string>(v, StringComparer.Ordinal)))
            .HasColumnType("jsonb")
            .IsRequired();

    private static bool SameEntries(Dictionary<string, string>? a, Dictionary<string, string>? b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a == null || b == null || a.Count != b.Count)
        {
            return false;
        }

        foreach (var (key, value) in a)
        {
            if (!b.TryGetValue(key, out var other) || other != value)
            {
                return false;
            }
        }

        return true;
    }

    public static PropertyBuilder<List<T>> HasJsonbList<T>(this PropertyBuilder<List<T>> property)
        => property
            .HasConversion(
                new ValueConverter<List<T>, string>(
                    v => JsonSerializer.Serialize(v, Options),
                    v => JsonSerializer.Deserialize<List<T>>(v, Options) ?? new List<T>()),
                new ValueComparer<List<T>>(
                    (a, b) => ReferenceEquals(a, b) || (a != null && b != null && a.SequenceEqual(b)),
                    v => v.Aggregate(17, (hash, item) => HashCode.Combine(hash, item)),
                    v => v.ToList()))
            .HasColumnType("jsonb")
            .IsRequired();

    /// <summary>Instants are stored in UTC (timestamptz); the client shows them in the stop's zone.</summary>
    public static PropertyBuilder<DateTimeOffset> HasUtcInstant(this PropertyBuilder<DateTimeOffset> property)
        => property.HasConversion(new ValueConverter<DateTimeOffset, DateTimeOffset>(v => v.ToUniversalTime(), v => v));

    public static PropertyBuilder<DateTimeOffset?> HasUtcInstant(this PropertyBuilder<DateTimeOffset?> property)
        => property.HasConversion(new ValueConverter<DateTimeOffset?, DateTimeOffset?>(v => v == null ? null : v.Value.ToUniversalTime(), v => v));
}
