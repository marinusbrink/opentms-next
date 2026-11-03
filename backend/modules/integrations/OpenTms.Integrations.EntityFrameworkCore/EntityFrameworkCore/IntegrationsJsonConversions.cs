using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using OpenTms.Integrations.OrderIntake;
using OpenTms.Integrations.OrderIntake.Profiles;

namespace OpenTms.Integrations.EntityFrameworkCore;

/// <summary>
/// The jsonb columns of the order intake (design 72, AD-06): the document's mapping, the profile's rules and signature,
/// the step's parameters. The typed documents serialize through their own <c>ToJson</c>/<c>FromJson</c>
/// (<see cref="IntakeJson.Options"/>) and compare by their JSON, so EF Core notices an in-place change. On SQLite the
/// column stores the same JSON text.
/// </summary>
public static class IntegrationsJsonConversions
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.General);

    public static PropertyBuilder<DocumentMapping?> HasJsonbMapping(this PropertyBuilder<DocumentMapping?> property)
        => property
            .HasConversion(
                new ValueConverter<DocumentMapping?, string?>(
                    v => v == null ? null : DocumentMapping.ToJson(v),
                    v => v == null ? null : DocumentMapping.FromJson(v)),
                new ValueComparer<DocumentMapping?>(
                    (a, b) => (a == null && b == null) || (a != null && b != null && DocumentMapping.ToJson(a) == DocumentMapping.ToJson(b)),
                    v => v == null ? 0 : DocumentMapping.ToJson(v).GetHashCode(),
                    v => v == null ? null : DocumentMapping.FromJson(DocumentMapping.ToJson(v))))
            .HasColumnType("jsonb")
            .IsRequired(false);

    public static PropertyBuilder<ImportProfileRules> HasJsonbRules(this PropertyBuilder<ImportProfileRules> property)
        => property
            .HasConversion(
                new ValueConverter<ImportProfileRules, string>(
                    v => ImportProfileRules.ToJson(v),
                    v => ImportProfileRules.FromJson(v)),
                new ValueComparer<ImportProfileRules>(
                    (a, b) => (a == null && b == null) || (a != null && b != null && ImportProfileRules.ToJson(a) == ImportProfileRules.ToJson(b)),
                    v => ImportProfileRules.ToJson(v).GetHashCode(),
                    v => ImportProfileRules.FromJson(ImportProfileRules.ToJson(v))))
            .HasColumnType("jsonb")
            .IsRequired();

    public static PropertyBuilder<ImportProfileRules?> HasNullableJsonbRules(this PropertyBuilder<ImportProfileRules?> property)
        => property
            .HasConversion(
                new ValueConverter<ImportProfileRules?, string?>(
                    v => v == null ? null : ImportProfileRules.ToJson(v),
                    v => v == null ? null : ImportProfileRules.FromJson(v)),
                new ValueComparer<ImportProfileRules?>(
                    (a, b) => (a == null && b == null) || (a != null && b != null && ImportProfileRules.ToJson(a) == ImportProfileRules.ToJson(b)),
                    v => v == null ? 0 : ImportProfileRules.ToJson(v).GetHashCode(),
                    v => v == null ? null : ImportProfileRules.FromJson(ImportProfileRules.ToJson(v))))
            .HasColumnType("jsonb")
            .IsRequired(false);

    public static PropertyBuilder<List<string>> HasJsonbStringList(this PropertyBuilder<List<string>> property)
        => property
            .HasConversion(
                new ValueConverter<List<string>, string>(
                    v => JsonSerializer.Serialize(v, Options),
                    v => JsonSerializer.Deserialize<List<string>>(v, Options) ?? new List<string>()),
                new ValueComparer<List<string>>(
                    (a, b) => ReferenceEquals(a, b) || (a != null && b != null && a.SequenceEqual(b)),
                    v => v.Aggregate(17, (hash, item) => HashCode.Combine(hash, item)),
                    v => v.ToList()))
            .HasColumnType("jsonb")
            .IsRequired();

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
        => ReferenceEquals(a, b)
           || (a != null && b != null && a.Count == b.Count && a.All(p => b.TryGetValue(p.Key, out var other) && other == p.Value));
}
