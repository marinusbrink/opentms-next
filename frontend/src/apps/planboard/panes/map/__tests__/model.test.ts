import polyline from "@mapbox/polyline";
import { describe, expect, it } from "vitest";

import {
  allTripsFeatures,
  AT_RISK_MINUTES,
  decodePolyline,
  fitPoints,
  nearLegPoint,
  orderedStops,
  routeFeatures,
  stopPoint,
  stopState,
  tollSharePercent,
} from "@/apps/planboard/panes/map/model";
import type { PlanboardMapStopDto, PlanboardMapTripDto, PlanboardNearLegDto, PlanboardTripMapDto } from "@/domains/planning-execution/planboard-map";

/* The pure part of the Map pane (design 70 "Map"): stop colouring (done, on time, at risk, late), the focused trip's
 * route (toll vs toll-free segments, straight lines where no geometry is stored), polyline decoding, the all-trips
 * layer and the fit points. Risk class: Medium. */

const stop = (extra: Partial<PlanboardMapStopDto>): PlanboardMapStopDto => ({ ...extra }) as PlanboardMapStopDto;
const iso = (h: number, m = 0) => new Date(Date.UTC(2026, 10, 2, h, m)).toISOString();

describe("stopState", () => {
  it("is done once the stop has an actual arrival or departure, whatever else", () => {
    expect(stopState(stop({ actualArrival: iso(8), latenessMinutes: 40 }))).toBe("done");
    expect(stopState(stop({ actualDeparture: iso(9) }))).toBe("done");
  });

  it("is late with positive lateness or an estimate after the window's end", () => {
    expect(stopState(stop({ latenessMinutes: 5 }))).toBe("late");
    expect(stopState(stop({ estimatedArrival: iso(10, 1), windowTill: iso(10) }))).toBe("late");
  });

  it("is at risk within 30 minutes of the window's end (inclusive), on time before", () => {
    expect(AT_RISK_MINUTES).toBe(30);
    expect(stopState(stop({ estimatedArrival: iso(10), windowTill: iso(10) }))).toBe("at-risk");
    expect(stopState(stop({ estimatedArrival: iso(9, 30), windowTill: iso(10) }))).toBe("at-risk");
    expect(stopState(stop({ estimatedArrival: iso(9, 29), windowTill: iso(10) }))).toBe("on-time");
    expect(stopState(stop({ estimatedArrival: iso(9, 50), windowTill: iso(10) }), 5)).toBe("on-time");
  });

  it("is on time without an estimate, without a window, with zero lateness or with unparsable times", () => {
    expect(stopState(stop({}))).toBe("on-time");
    expect(stopState(stop({ estimatedArrival: iso(9), latenessMinutes: 0 }))).toBe("on-time");
    expect(stopState(stop({ estimatedArrival: "x", windowTill: iso(10) }))).toBe("on-time");
  });
});

describe("coordinates", () => {
  it("rejects missing, out-of-range and 0,0 coordinates", () => {
    expect(stopPoint(stop({ latitude: 52.1, longitude: 5.1 }))).toEqual({ latitude: 52.1, longitude: 5.1 });
    expect(stopPoint(stop({ latitude: 0, longitude: 0 }))).toBeNull();
    expect(stopPoint(stop({ latitude: 91, longitude: 5 }))).toBeNull();
    expect(stopPoint(stop({ latitude: 52, longitude: null }))).toBeNull();
  });

  it("places an unplanned leg at its origin, else its destination", () => {
    expect(nearLegPoint({ originLatitude: 51, originLongitude: 4, destinationLatitude: 52, destinationLongitude: 5 } as PlanboardNearLegDto)).toEqual({ latitude: 51, longitude: 4 });
    expect(nearLegPoint({ destinationLatitude: 52, destinationLongitude: 5 } as PlanboardNearLegDto)).toEqual({ latitude: 52, longitude: 5 });
    expect(nearLegPoint({} as PlanboardNearLegDto)).toBeNull();
  });
});

describe("decodePolyline", () => {
  it("decodes to [lon, lat] pairs", () => {
    const encoded = polyline.encode([
      [52.1, 5.1],
      [52.2, 5.3],
    ]);
    expect(decodePolyline(encoded)).toEqual([
      [5.1, 52.1],
      [5.3, 52.2],
    ]);
  });

  it("yields [] for empty input and never throws on garbage", () => {
    expect(decodePolyline(null)).toEqual([]);
    expect(decodePolyline("")).toEqual([]);
    expect(() => decodePolyline("\u0000\u0001not a polyline")).not.toThrow();
  });
});

describe("routeFeatures", () => {
  const stops = [
    stop({ id: "S2", position: 2, latitude: 52.2, longitude: 5.2 }),
    stop({ id: "S1", position: 1, latitude: 52.1, longitude: 5.1 }),
    stop({ id: "S3", position: 3, latitude: 52.3, longitude: 5.3 }),
    stop({ id: "S4", position: 4 }),
  ];
  const line = (a: [number, number], b: [number, number]) => polyline.encode([a, b]);

  it("draws stored segments as toll or route by their toll distance, and straight lines where none is stored", () => {
    const map = {
      stops,
      segments: [
        { fromStopId: "s1", toStopId: "s2", encodedPolyline: line([52.1, 5.1], [52.2, 5.2]), tollDistanceM: 1200 },
        { fromStopId: "S2", toStopId: "S3", encodedPolyline: line([52.2, 5.2], [52.3, 5.3]), tollDistanceM: 0 },
      ],
    } as unknown as PlanboardTripMapDto;
    expect(routeFeatures(map).features.map((f) => f.properties.kind)).toEqual(["toll", "route"]);

    const noGeometry = { stops, segments: [{ fromStopId: "S1", toStopId: "S2", encodedPolyline: "", tollDistanceM: 5 }] } as unknown as PlanboardTripMapDto;
    const features = routeFeatures(noGeometry).features;
    expect(features.map((f) => f.properties.kind)).toEqual(["straight", "straight"]);
    expect(features[0].geometry.coordinates).toEqual([
      [5.1, 52.1],
      [5.2, 52.2],
    ]);
  });

  it("treats a segment without toll distance as toll-free and skips a stop pair without coordinates", () => {
    const map = { stops, segments: [{ fromStopId: "S3", toStopId: "S4", encodedPolyline: line([52.3, 5.3], [52.4, 5.4]) }] } as unknown as PlanboardTripMapDto;
    const kinds = routeFeatures(map).features.map((f) => f.properties.kind);
    expect(kinds).toEqual(["route", "straight", "straight"]);
  });

  it("is an empty collection without a map", () => {
    expect(routeFeatures(undefined)).toEqual({ type: "FeatureCollection", features: [] });
  });

  it("orders stops by position", () => {
    expect(orderedStops({ stops } as unknown as PlanboardTripMapDto).map((s) => s.id)).toEqual(["S1", "S2", "S3", "S4"]);
  });
});

describe("all trips, fit points, toll share", () => {
  const trips = [
    { id: "T1", number: "R1", firstLatitude: 52, firstLongitude: 5, lastLatitude: 51, lastLongitude: 4 },
    { id: "T2", number: "R2", firstLatitude: 52, firstLongitude: 5, lastLatitude: null, lastLongitude: null },
  ] as PlanboardMapTripDto[];

  it("draws a first→last line per trip with both ends, marking the selected ones", () => {
    const features = allTripsFeatures(trips, new Set(["t1"])).features;
    expect(features).toHaveLength(1);
    expect(features[0].properties).toEqual({ id: "T1", number: "R1", selected: true });
  });

  it("fits the focused trip's points, else every trip's ends", () => {
    const map = { stops: [stop({ id: "S1", latitude: 52.1, longitude: 5.1 })], departurePoint: { latitude: 52, longitude: 5 }, arrivalPoint: null } as unknown as PlanboardTripMapDto;
    expect(fitPoints(map, trips)).toHaveLength(2);
    expect(fitPoints({ stops: [] } as unknown as PlanboardTripMapDto, trips)).toHaveLength(3);
    expect(fitPoints(undefined, [])).toEqual([]);
  });

  it("computes the toll share in percent, null when unknown", () => {
    expect(tollSharePercent({ tollDistanceM: 25_000, plannedDistanceM: 100_000 } as PlanboardTripMapDto)).toBe(25);
    expect(tollSharePercent({ tollDistanceM: null, plannedDistanceM: 100_000 } as PlanboardTripMapDto)).toBeNull();
    expect(tollSharePercent({ tollDistanceM: 5, plannedDistanceM: 0 } as PlanboardTripMapDto)).toBeNull();
  });
});
