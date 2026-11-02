using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenTms.PlanningExecution.TransportTrips;

/// <summary>
/// One stored segment of a trip's route (design 68 §4, PO decision D3): the drive between two consecutive route points.
/// A point is a stop, or the trip's departure or arrival address when the first/last stop rule does not make it a stop
/// (then <see cref="FromStopId"/> or <see cref="ToStopId"/> is null). Written by <c>TripCalculator</c> only, as one jsonb
/// list on the trip. Distances and durations as the routing provider answered them (no speed correction).
/// </summary>
public sealed class TripRouteSegment : IEquatable<TripRouteSegment>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public Guid? FromStopId { get; set; }

    public Guid? ToStopId { get; set; }

    /// <summary>The geometry in Google's encoded polyline format (precision 5); null when the provider gave none.</summary>
    public string? EncodedPolyline { get; set; }

    public int DistanceM { get; set; }

    public int DurationS { get; set; }

    public int? TollDistanceM { get; set; }

    public decimal? TollAmount { get; set; }

    public static string ToJson(IReadOnlyList<TripRouteSegment> segments) => JsonSerializer.Serialize(segments, JsonOptions);

    public static List<TripRouteSegment> FromJson(string? json)
        => string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<List<TripRouteSegment>>(json, JsonOptions) ?? [];

    public bool Equals(TripRouteSegment? other)
        => other != null && FromStopId == other.FromStopId && ToStopId == other.ToStopId
           && string.Equals(EncodedPolyline, other.EncodedPolyline, StringComparison.Ordinal)
           && DistanceM == other.DistanceM && DurationS == other.DurationS && TollDistanceM == other.TollDistanceM && TollAmount == other.TollAmount;

    public override bool Equals(object? obj) => obj is TripRouteSegment other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(FromStopId, ToStopId, EncodedPolyline, DistanceM, DurationS, TollDistanceM, TollAmount);
}
