import polyline from "@mapbox/polyline";

import type { MapBounds, MapPoint } from "@/components/ui/map-view";
import type { PlanboardMapStopDto, PlanboardMapTripDto, PlanboardNearLegDto, PlanboardTripMapDto } from "@/domains/planning-execution/planboard-map";

/* The pure part of the Map pane (design 70 "Map"): stop states for the marker colours, the route of the focused trip as
 * GeoJSON (decoded segment polylines, toll segments marked, straight lines where a segment has no geometry), the "all
 * trips" lines and the bounds to fit. No MapLibre import, so it is testable without a map. */

/** The marker colour family of a stop (design 70: done, on time, at risk, late — estimated arrival against the window). */
export type StopState = "done" | "on-time" | "at-risk" | "late";

/** An estimated arrival closer than this to the window's end is "at risk". */
export const AT_RISK_MINUTES = 30;

export function stopState(stop: PlanboardMapStopDto, atRiskMinutes = AT_RISK_MINUTES): StopState {
  if (stop.actualArrival || stop.actualDeparture) return "done";
  if ((stop.latenessMinutes ?? 0) > 0) return "late";
  if (stop.estimatedArrival && stop.windowTill) {
    const eta = new Date(stop.estimatedArrival).getTime();
    const till = new Date(stop.windowTill).getTime();
    if (!Number.isNaN(eta) && !Number.isNaN(till)) {
      if (eta > till) return "late";
      if (till - eta <= atRiskMinutes * 60_000) return "at-risk";
    }
  }
  return "on-time";
}

export const STOP_STATES: readonly StopState[] = ["done", "on-time", "at-risk", "late"];

const isCoordinate = (lat: number | null | undefined, lon: number | null | undefined): boolean =>
  lat != null && lon != null && Number.isFinite(lat) && Number.isFinite(lon) && Math.abs(lat) <= 90 && Math.abs(lon) <= 180 && !(lat === 0 && lon === 0);

export function stopPoint(stop: PlanboardMapStopDto): MapPoint | null {
  return isCoordinate(stop.latitude, stop.longitude) ? { latitude: stop.latitude!, longitude: stop.longitude! } : null;
}

export function pointOf(p: { latitude?: number | null; longitude?: number | null } | null | undefined): MapPoint | null {
  return p && isCoordinate(p.latitude, p.longitude) ? { latitude: p.latitude!, longitude: p.longitude! } : null;
}

/** The stops in position order. */
export function orderedStops(map: PlanboardTripMapDto | undefined): PlanboardMapStopDto[] {
  return [...(map?.stops ?? [])].sort((a, b) => (a.position ?? 0) - (b.position ?? 0));
}

/** Decodes an encoded polyline (precision 5) to [lon, lat] pairs; an empty or broken string yields []. */
export function decodePolyline(encoded: string | null | undefined): [number, number][] {
  if (!encoded) return [];
  try {
    return polyline
      .decode(encoded)
      .filter(([lat, lon]) => isCoordinate(lat, lon))
      .map(([lat, lon]) => [lon, lat] as [number, number]);
  } catch {
    return [];
  }
}

export type RouteKind = "route" | "toll" | "straight";

/**
 * The focused trip's route: one line per stored segment (`toll` when the segment has toll kilometres, else `route`),
 * and a straight (dashed) line between two consecutive stops wherever no geometry is stored.
 */
export function routeFeatures(map: PlanboardTripMapDto | undefined): GeoJSON.FeatureCollection<GeoJSON.LineString, { kind: RouteKind }> {
  const features: GeoJSON.Feature<GeoJSON.LineString, { kind: RouteKind }>[] = [];
  const stops = orderedStops(map);
  const byId = new Map(stops.filter((s) => s.id).map((s) => [s.id!.toLowerCase(), s]));
  const drawnPairs = new Set<string>();
  for (const segment of map?.segments ?? []) {
    const coordinates = decodePolyline(segment.encodedPolyline);
    const pair = `${segment.fromStopId?.toLowerCase() ?? ""}>${segment.toStopId?.toLowerCase() ?? ""}`;
    if (coordinates.length >= 2) {
      features.push({ type: "Feature", properties: { kind: (segment.tollDistanceM ?? 0) > 0 ? "toll" : "route" }, geometry: { type: "LineString", coordinates } });
      drawnPairs.add(pair);
    }
  }
  for (let i = 0; i + 1 < stops.length; i += 1) {
    const from = stops[i];
    const to = stops[i + 1];
    const pair = `${from.id?.toLowerCase() ?? ""}>${to.id?.toLowerCase() ?? ""}`;
    if (drawnPairs.has(pair)) continue;
    const a = stopPoint(byId.get(from.id?.toLowerCase() ?? "") ?? from);
    const b = stopPoint(to);
    if (!a || !b) continue;
    features.push({ type: "Feature", properties: { kind: "straight" }, geometry: { type: "LineString", coordinates: [[a.longitude, a.latitude], [b.longitude, b.latitude]] } });
  }
  return { type: "FeatureCollection", features };
}

/** The "all trips" layer: a first→last line per trip with coordinates (a trip with one point is skipped). */
export function allTripsFeatures(trips: readonly PlanboardMapTripDto[], selectedIds: ReadonlySet<string>): GeoJSON.FeatureCollection<GeoJSON.LineString, { id: string; number: string; selected: boolean }> {
  const features: GeoJSON.Feature<GeoJSON.LineString, { id: string; number: string; selected: boolean }>[] = [];
  for (const trip of trips) {
    if (!trip.id || !isCoordinate(trip.firstLatitude, trip.firstLongitude) || !isCoordinate(trip.lastLatitude, trip.lastLongitude)) continue;
    features.push({
      type: "Feature",
      properties: { id: trip.id, number: trip.number ?? "", selected: selectedIds.has(trip.id.toLowerCase()) },
      geometry: { type: "LineString", coordinates: [[trip.firstLongitude!, trip.firstLatitude!], [trip.lastLongitude!, trip.lastLatitude!]] },
    });
  }
  return { type: "FeatureCollection", features };
}

/** Where an unplanned leg is drawn: its origin, else its destination. */
export function nearLegPoint(leg: PlanboardNearLegDto): MapPoint | null {
  if (isCoordinate(leg.originLatitude, leg.originLongitude)) return { latitude: leg.originLatitude!, longitude: leg.originLongitude! };
  if (isCoordinate(leg.destinationLatitude, leg.destinationLongitude)) return { latitude: leg.destinationLatitude!, longitude: leg.destinationLongitude! };
  return null;
}

/** The points to fit: the focused trip's stops and departure/arrival, or else the all-trips lines. */
export function fitPoints(map: PlanboardTripMapDto | undefined, trips: readonly PlanboardMapTripDto[]): MapPoint[] {
  if (map) {
    const points = [...orderedStops(map).map(stopPoint), pointOf(map.departurePoint), pointOf(map.arrivalPoint)].filter((p): p is MapPoint => p != null);
    if (points.length > 0) return points;
  }
  const result: MapPoint[] = [];
  for (const trip of trips) {
    const a = pointOf({ latitude: trip.firstLatitude, longitude: trip.firstLongitude });
    const b = pointOf({ latitude: trip.lastLatitude, longitude: trip.lastLongitude });
    if (a) result.push(a);
    if (b) result.push(b);
  }
  return result;
}

export function boundsKey(bounds: MapBounds | null): string {
  return bounds ? bounds.flat().join(",") : "";
}

/** The share of toll kilometres in the trip's distance, in percent; null when either is unknown. */
export function tollSharePercent(map: PlanboardTripMapDto | undefined): number | null {
  const toll = map?.tollDistanceM;
  const total = map?.plannedDistanceM;
  if (toll == null || !total) return null;
  return (toll / total) * 100;
}
