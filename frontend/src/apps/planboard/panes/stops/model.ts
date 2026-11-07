import { latenessMinutes } from "@/apps/planboard/panes/lists/grid-helpers";
import { stopState, type StopState } from "@/apps/planboard/panes/map/model";
import type { TripStopDto } from "@/domains/planning-execution/trips";

/* The pure part of the Stops timeline (design 74 §2): where the trip is now, which times a stop shows (one tier per stop),
 * its marker and line, its window and its state badge. No React, so all of it is testable with plain data. */

/** The times a position needs; a stop's other fields do not matter here. */
export type TimelineTimes = Pick<TripStopDto, "actualArrival" | "actualDeparture" | "estimatedArrival" | "estimatedDeparture" | "plannedArrival" | "plannedDeparture">;

/** Where the trip is, as indexes into the stops in position order. */
export type NowPosition =
  /** Above the first stop: not started. */
  | { kind: "before" }
  /** At this stop. */
  | { kind: "at"; index: number }
  /** On the way from `from` to `to`; `fraction` (0..1) is how far, from the expected times. */
  | { kind: "between"; from: number; to: number; fraction: number }
  /** Past the end: no marker. */
  | { kind: "after" };

const ms = (value: string | null | undefined): number | null => {
  if (!value) return null;
  const time = new Date(value).getTime();
  return Number.isNaN(time) ? null : time;
};

const expectedArrival = (stop: TimelineTimes) => ms(stop.actualArrival ?? stop.estimatedArrival ?? stop.plannedArrival);
const expectedDeparture = (stop: TimelineTimes) => ms(stop.actualDeparture ?? stop.estimatedDeparture ?? stop.plannedDeparture);

/** Whether `time` lies in the stop's expected stay. A stop without an expected departure has no stay. */
function withinStay(stop: TimelineTimes, time: number): boolean {
  const arrival = expectedArrival(stop);
  const departure = expectedDeparture(stop);
  return arrival != null && departure != null && arrival <= time && time <= departure;
}

function between(stops: readonly TimelineTimes[], from: number, time: number): NowPosition {
  const start = expectedDeparture(stops[from]) ?? expectedArrival(stops[from]);
  const end = expectedArrival(stops[from + 1]);
  let fraction = 0;
  if (start != null && end != null) {
    fraction = end > start ? Math.min(1, Math.max(0, (time - start) / (end - start))) : time >= end ? 1 : 0;
  }
  return { kind: "between", from, to: from + 1, fraction };
}

/**
 * Where the trip is (design 74 §2). Actual times decide when there are any: the last stop with an actual time is the
 * trip's real progress, even when the expected times have passed since. Without actuals the expected times do.
 * `stops` are in position order.
 */
export function nowPosition(stops: readonly TimelineTimes[], now: Date): NowPosition {
  if (stops.length === 0) return { kind: "before" };
  const time = now.getTime();

  const lastActual = stops.findLastIndex((stop) => stop.actualArrival != null || stop.actualDeparture != null);
  if (lastActual >= 0) {
    if (stops[lastActual].actualDeparture == null) return { kind: "at", index: lastActual };
    if (lastActual === stops.length - 1) return { kind: "after" };
    if (withinStay(stops[lastActual + 1], time)) return { kind: "at", index: lastActual + 1 };
    return between(stops, lastActual, time);
  }

  const firstArrival = expectedArrival(stops[0]);
  if (firstArrival != null && time < firstArrival) return { kind: "before" };

  const staying = stops.findLastIndex((stop) => withinStay(stop, time));
  if (staying >= 0) return { kind: "at", index: staying };

  const passed = stops.findLastIndex((stop) => {
    const left = expectedDeparture(stop) ?? expectedArrival(stop);
    return left != null && left < time;
  });
  if (passed < 0) return { kind: "before" };
  /* Every expected time has passed and nothing was reported: the trip is not finished, so the marker stays at its last stop. */
  if (passed === stops.length - 1) return { kind: "at", index: passed };
  return between(stops, passed, time);
}

const COMPLETED = 3;
const CANCELLED = 4;

/**
 * The position a trip's timeline draws, and whether it shows the now marker. A completed trip is past its end whatever
 * its stops say. A cancelled trip goes nowhere any more: only its actuals count (the expected times never pass) and it
 * has no marker.
 */
export function timelinePosition(stops: readonly TimelineTimes[], now: Date, status: number | null | undefined): { position: NowPosition; showNow: boolean } {
  if (status === COMPLETED) return { position: { kind: "after" }, showNow: false };
  if (status === CANCELLED) return { position: nowPosition(stops, new Date(0)), showNow: false };
  const position = nowPosition(stops, now);
  return { position, showNow: position.kind !== "after" };
}

/** The stop the trip goes to next; null at the last stop and past the end. */
export function nextStopIndex(position: NowPosition, stopCount: number): number | null {
  switch (position.kind) {
    case "before":
      return stopCount > 0 ? 0 : null;
    case "at":
      return position.index + 1 < stopCount ? position.index + 1 : null;
    case "between":
      return position.to;
    case "after":
      return null;
  }
}

/** The now marker's delay: expected against planned arrival at the next stop, in minutes (negative = early); null when unknown. */
export function delayAtNextStop(stops: readonly TimelineTimes[], position: NowPosition): number | null {
  const next = nextStopIndex(position, stops.length);
  if (next == null) return null;
  const expected = ms(stops[next].estimatedArrival);
  const planned = ms(stops[next].plannedArrival);
  return expected == null || planned == null ? null : Math.round((expected - planned) / 60_000);
}

/** Which times a stop shows: one tier per stop, as legacy. */
export type StopTier = "actual" | "estimated" | "planned";

export function stopTimes(stop: TimelineTimes): { tier: StopTier; arrival: string | null; departure: string | null } {
  if (stop.actualArrival != null || stop.actualDeparture != null) return { tier: "actual", arrival: stop.actualArrival ?? null, departure: stop.actualDeparture ?? null };
  if (stop.estimatedArrival != null || stop.estimatedDeparture != null) return { tier: "estimated", arrival: stop.estimatedArrival ?? null, departure: stop.estimatedDeparture ?? null };
  return { tier: "planned", arrival: stop.plannedArrival ?? null, departure: stop.plannedDeparture ?? null };
}

export type MarkerState = "done" | "next" | "later";

/** The marker of the stop at `index`: the stop the trip is at or going to is next. */
export function markerState(index: number, position: NowPosition): MarkerState {
  switch (position.kind) {
    case "before":
      return index === 0 ? "next" : "later";
    case "at":
      return index < position.index ? "done" : index === position.index ? "next" : "later";
    case "between":
      return index <= position.from ? "done" : index === position.to ? "next" : "later";
    case "after":
      return "done";
  }
}

/**
 * Whether the line above and below the stop's marker is already driven (solid) or still to come (dashed). Between two
 * stops, the part in between is drawn by the now marker itself, split at `fraction`.
 */
export function lineState(index: number, position: NowPosition): { upper: boolean; lower: boolean } {
  switch (position.kind) {
    case "before":
      return { upper: false, lower: false };
    case "at":
      return { upper: index <= position.index, lower: index < position.index };
    case "between":
      return { upper: index <= position.from, lower: index <= position.from };
    case "after":
      return { upper: true, lower: true };
  }
}

/** The window of a stop: the earliest start and the earliest end of its legs' windows (the end the stop must meet). */
export function stopWindow(stop: Pick<TripStopDto, "legs">): { from: string | null; till: string | null } {
  const legs = stop.legs ?? [];
  const froms = legs.map((leg) => leg.windowFrom).filter((value): value is string => !!value).sort();
  const tills = legs.map((leg) => leg.windowTill).filter((value): value is string => !!value).sort();
  return { from: froms[0] ?? null, till: tills[0] ?? null };
}

/** The state badge of a stop still to come (the map's states); null once it is done or without an estimate and a window. */
export function stopBadge(stop: Pick<TripStopDto, "legs" | "actualArrival" | "actualDeparture" | "estimatedArrival">): { state: Exclude<StopState, "done">; delayMinutes: number | null } | null {
  const { till } = stopWindow(stop);
  if (!stop.estimatedArrival || !till) return null;
  const delayMinutes = latenessMinutes(stop.estimatedArrival, till);
  const state = stopState({ actualArrival: stop.actualArrival, actualDeparture: stop.actualDeparture, estimatedArrival: stop.estimatedArrival, windowTill: till, latenessMinutes: delayMinutes });
  return state === "done" ? null : { state, delayMinutes };
}

/** The order numbers on a stop, each once, in the legs' order. */
export function stopOrderNumbers(stop: Pick<TripStopDto, "legs">): string[] {
  return [...new Set((stop.legs ?? []).map((leg) => leg.orderNumber).filter((value): value is string => !!value))];
}

export const stopHasActuals = (stop: Pick<TripStopDto, "actualArrival" | "actualDeparture"> | undefined) => stop?.actualArrival != null || stop?.actualDeparture != null;
