using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenTms.PlanningExecution.TransportTrips;

/// <summary>
/// One stored trip warning (design 59, the <c>Warnings</c> jsonb list: <c>{ code, legId?, stopId?, data }</c>). Codes
/// and ids only; the screen localises. <c>Data</c> carries the id or pair the code is about (a characteristic id,
/// "a;b" for an incompatible pair), never a text.
/// </summary>
public sealed class TripWarning : IEquatable<TripWarning>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public TripWarningCode Code { get; set; }

    public Guid? LegId { get; set; }

    public Guid? StopId { get; set; }

    public string? Data { get; set; }

    public TripWarning()
    {
    }

    public TripWarning(TripWarningCode code, Guid? legId = null, Guid? stopId = null, string? data = null)
    {
        Code = code;
        LegId = legId;
        StopId = stopId;
        Data = data;
    }

    public static string ToJson(IReadOnlyList<TripWarning> warnings) => JsonSerializer.Serialize(warnings, JsonOptions);

    public static List<TripWarning> FromJson(string? json)
        => string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<List<TripWarning>>(json, JsonOptions) ?? [];

    public bool Equals(TripWarning? other)
        => other != null && Code == other.Code && LegId == other.LegId && StopId == other.StopId && string.Equals(Data, other.Data, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is TripWarning other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Code, LegId, StopId, Data);
}

/// <summary>The attribute bag of a trip (OpenTMS custom properties, design 56 "Attributes"): bounded, string to string.</summary>
public static class TripAttributes
{
    private static readonly JsonSerializerOptions JsonOptions = new();

    public static string ToJson(IReadOnlyDictionary<string, string> attributes) => JsonSerializer.Serialize(attributes, JsonOptions);

    public static Dictionary<string, string> FromJson(string? json)
        => string.IsNullOrWhiteSpace(json)
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions) ?? new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Null when valid, otherwise the reason ("count", "key", "value").</summary>
    public static string? Validate(IReadOnlyDictionary<string, string>? attributes)
    {
        if (attributes == null)
        {
            return null;
        }

        if (attributes.Count > PlanningExecutionConsts.MaxAttributes)
        {
            return "count";
        }

        foreach (var (key, value) in attributes)
        {
            if (string.IsNullOrWhiteSpace(key) || key.Length > PlanningExecutionConsts.MaxAttributeKeyLength)
            {
                return "key";
            }

            if (value != null && value.Length > PlanningExecutionConsts.MaxAttributeValueLength)
            {
                return "value";
            }
        }

        return null;
    }
}
