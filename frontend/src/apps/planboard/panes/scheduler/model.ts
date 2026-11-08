import { addDays, isValidRange, type PlanboardRange } from "@/apps/planboard/range";
import type { SchedulerBackgroundEvent, SchedulerEvent, SchedulerResource, SchedulerTone, SchedulerZoom } from "@/components/ui/resource-scheduler-model";
import {
  isPlanboardResourceMode,
  MAX_SCHEDULER_DAYS,
  type PlanboardBarDto,
  type PlanboardBlockDto,
  type PlanboardResourceMode,
  type PlanboardResourceRowDto,
  type PlanboardSchedulerDto,
} from "@/domains/planning-execution/planboard-scheduler";
import { DerivedValueStatus, type RecordUpdateMessage } from "@/lib/realtime/record-updates";

/* The pure part of the Scheduler pane (design 70 "Scheduler"): the pane's settings and their repair, the mapping of a
 * Scheduler read onto the adapter's rows, bars and blocks, conflict detection (overlap on a row, or over a block),
 * the optimistic patch of a moved bar and the merge of refetched bars. No React, so it is testable on its own. */

/** The fields a bar may show (design 70: the bar-fields whitelist). */
export const BAR_FIELDS = ["number", "name", "status", "carrier", "drivers", "trailer", "cities", "loadFactor", "warnings"] as const;
export type BarField = (typeof BAR_FIELDS)[number];
export const DEFAULT_BAR_FIELDS: readonly BarField[] = ["number", "cities", "drivers", "warnings"];

/** Range presets of the pane header: day, 3 days, week, 2 weeks. */
export const RANGE_PRESETS = [1, 3, 7, 14] as const;
export const DEFAULT_SCHEDULER_PAGE_SIZE = 50;

export interface SchedulerPaneSettings {
  mode: PlanboardResourceMode;
  zoom: SchedulerZoom;
  search: string;
  barFields: BarField[];
  pageSize: number;
}

export const DEFAULT_SCHEDULER_SETTINGS: SchedulerPaneSettings = { mode: "tractor", zoom: "hour", search: "", barFields: [...DEFAULT_BAR_FIELDS], pageSize: DEFAULT_SCHEDULER_PAGE_SIZE };

const PAGE_SIZES = [25, 50, 100, 200];

/** Repairs stored settings of an older or corrupt shape (the filter part is repaired by usePaneFilters). */
export function sanitizeSchedulerSettings(stored: Record<string, unknown>): Partial<SchedulerPaneSettings> {
  const result: Partial<SchedulerPaneSettings> = {};
  if (isPlanboardResourceMode(stored.mode)) result.mode = stored.mode;
  if (stored.zoom === "hour" || stored.zoom === "day") result.zoom = stored.zoom;
  if (typeof stored.search === "string") result.search = stored.search.slice(0, 128);
  if (Array.isArray(stored.barFields)) result.barFields = BAR_FIELDS.filter((f) => (stored.barFields as unknown[]).includes(f));
  if (typeof stored.pageSize === "number" && PAGE_SIZES.includes(stored.pageSize)) result.pageSize = stored.pageSize;
  return result;
}

export const SCHEDULER_PAGE_SIZES: readonly number[] = PAGE_SIZES;

/** The range of a preset starting at `from`. */
export function presetRange(from: string, days: number): PlanboardRange {
  return { from, till: addDays(from, Math.min(Math.max(1, days), MAX_SCHEDULER_DAYS) - 1) };
}

export { isValidRange };

/* ── Bars ──────────────────────────────────────────────────────────────────────────────────────────────────────── */

export const TRIP_STATUS = { Planned: 1, InProgress: 2, Completed: 3, Cancelled: 4 } as const;

/** The status tone of a bar: exactly the tone the trip's StatusBadge gives the status (status-badge.tsx,
 * `TransportTrip`): Planned neutral, In progress info, Completed success, Cancelled danger; unknown neutral. */
export function barTone(status: number | null | undefined): SchedulerTone {
  switch (status) {
    case TRIP_STATUS.InProgress:
      return "info";
    case TRIP_STATUS.Completed:
      return "success";
    case TRIP_STATUS.Cancelled:
      return "danger";
    default:
      return "neutral";
  }
}

export function isReadOnlyStatus(status: number | null | undefined): boolean {
  return status === TRIP_STATUS.Completed || status === TRIP_STATUS.Cancelled;
}

/** A bar without an arrival is drawn one hour long, so it stays visible and draggable. */
export const DEFAULT_BAR_MS = 3_600_000;

export interface BarData {
  bar: PlanboardBarDto;
  tripId: string;
  rowId: string;
}

const EVENT_ID_SEPARATOR = "::";
/** One event per trip and row (a trip with two trailers sits in two trailer rows). */
export const barEventId = (tripId: string, rowId: string) => `${tripId}${EVENT_ID_SEPARATOR}${rowId}`;
export function parseBarEventId(eventId: string): { tripId: string; rowId: string } | null {
  const index = eventId.indexOf(EVENT_ID_SEPARATOR);
  if (index <= 0) return null;
  return { tripId: eventId.slice(0, index), rowId: eventId.slice(index + EVENT_ID_SEPARATOR.length) };
}

/** The row ids of a bar in the mode: its drivers in driver mode, its vehicles otherwise. */
export function barResourceIds(bar: PlanboardBarDto, mode: PlanboardResourceMode): string[] {
  return (mode === "driver" ? bar.driverIds : bar.vehicleIds) ?? [];
}

export function barInterval(bar: PlanboardBarDto): { start: number; end: number } | null {
  if (!bar.plannedDeparture) return null;
  const start = new Date(bar.plannedDeparture).getTime();
  if (Number.isNaN(start)) return null;
  const arrival = bar.plannedArrival ? new Date(bar.plannedArrival).getTime() : Number.NaN;
  const end = Number.isNaN(arrival) || arrival <= start ? start + DEFAULT_BAR_MS : arrival;
  return { start, end };
}

const sameId = (a: string | null | undefined, b: string | null | undefined) => !!a && !!b && a.toLowerCase() === b.toLowerCase();

export function rowTitle(row: PlanboardResourceRowDto): string {
  return row.name || row.code || "";
}

export interface SchedulerViewData {
  resources: SchedulerResource[];
  events: SchedulerEvent<BarData>[];
  backgroundEvents: SchedulerBackgroundEvent[];
}

/** Maps a Scheduler read onto the adapter: one row per resource, one bar per trip and row, the blocks as background. */
export function toSchedulerView(dto: PlanboardSchedulerDto | undefined, mode: PlanboardResourceMode, blockTitle: (block: PlanboardBlockDto) => string): SchedulerViewData {
  const rows = (dto?.rows ?? []).filter((r): r is PlanboardResourceRowDto & { id: string } => !!r.id);
  const rowIds = new Map(rows.map((r) => [r.id.toLowerCase(), r.id]));
  const resources = rows.map((r): SchedulerResource => ({ id: r.id, title: rowTitle(r) }));
  const events: SchedulerEvent<BarData>[] = [];
  for (const bar of dto?.bars ?? []) {
    if (!bar.id) continue;
    const interval = barInterval(bar);
    if (!interval) continue;
    const seen = new Set<string>();
    for (const resourceId of barResourceIds(bar, mode)) {
      const rowId = rowIds.get(resourceId.toLowerCase());
      if (!rowId || seen.has(rowId)) continue;
      seen.add(rowId);
      events.push({
        id: barEventId(bar.id, rowId),
        resourceId: rowId,
        start: new Date(interval.start),
        end: new Date(interval.end),
        title: bar.number ?? "",
        tone: barTone(bar.status),
        data: { bar, tripId: bar.id, rowId },
      });
    }
  }
  const backgroundEvents = (dto?.blocks ?? [])
    .filter((b) => b.resourceId && b.from && b.till && rowIds.has(b.resourceId.toLowerCase()))
    .map(
      (b, index): SchedulerBackgroundEvent => ({
        id: `${b.resourceId}:${b.unavailabilityId ?? "owt"}:${b.from}:${index}`,
        resourceId: rowIds.get(b.resourceId!.toLowerCase())!,
        start: b.from!,
        end: b.till!,
        kind: b.unavailabilityId ? "unavailable" : "outside-working-time",
        title: blockTitle(b),
      }),
    );
  const conflicts = detectConflicts(events, backgroundEvents);
  for (const event of events) if (conflicts.has(event.id)) event.conflict = true;
  return { resources, events, backgroundEvents };
}

const time = (value: string | Date) => (value instanceof Date ? value.getTime() : new Date(value).getTime());
const overlaps = (aStart: number, aEnd: number, bStart: number, bEnd: number) => aStart < bEnd && bStart < aEnd;

/**
 * The bars in conflict (design 70; design 68 D10 warns, never blocks): a bar overlapping another live bar on the same
 * row, or overlapping a block (unavailability or outside working time) of its row. Cancelled bars take no part.
 */
export function detectConflicts(events: readonly SchedulerEvent<BarData>[], blocks: readonly SchedulerBackgroundEvent[]): Set<string> {
  const result = new Set<string>();
  const byRow = new Map<string, SchedulerEvent<BarData>[]>();
  for (const event of events) {
    if (event.data?.bar.status === TRIP_STATUS.Cancelled) continue;
    const list = byRow.get(event.resourceId) ?? [];
    list.push(event);
    byRow.set(event.resourceId, list);
  }
  for (const list of byRow.values()) {
    const sorted = [...list].sort((a, b) => time(a.start) - time(b.start));
    for (let i = 0; i < sorted.length; i += 1) {
      const aEnd = time(sorted[i].end);
      for (let j = i + 1; j < sorted.length && time(sorted[j].start) < aEnd; j += 1) {
        result.add(sorted[i].id);
        result.add(sorted[j].id);
      }
    }
  }
  for (const block of blocks) {
    const bStart = time(block.start);
    const bEnd = time(block.end);
    for (const event of byRow.get(block.resourceId) ?? []) {
      if (overlaps(time(event.start), time(event.end), bStart, bEnd)) result.add(event.id);
    }
  }
  return result;
}

export interface MoveConflicts {
  /** The numbers of the other trips the moved bar would overlap on its target row. */
  trips: string[];
  /** The blocks of the target row the moved bar would overlap. */
  blocks: SchedulerBackgroundEvent[];
}

/** What a proposed move would collide with on its target row (the bar itself excluded). */
export function moveConflicts(
  tripId: string,
  rowId: string,
  start: Date,
  end: Date,
  events: readonly SchedulerEvent<BarData>[],
  blocks: readonly SchedulerBackgroundEvent[],
): MoveConflicts {
  const s = start.getTime();
  const e = Math.max(end.getTime(), s + 1);
  const trips = events
    .filter((ev) => ev.resourceId === rowId && !sameId(ev.data?.tripId, tripId) && ev.data?.bar.status !== TRIP_STATUS.Cancelled)
    .filter((ev) => overlaps(s, e, time(ev.start), time(ev.end)))
    .map((ev) => ev.data?.bar.number ?? "");
  const hit = blocks.filter((b) => b.resourceId === rowId && overlaps(s, e, time(b.start), time(b.end)));
  return { trips: [...new Set(trips)], blocks: hit };
}

export const hasConflicts = (c: MoveConflicts) => c.trips.length > 0 || c.blocks.length > 0;

/* ── Moves and patches ─────────────────────────────────────────────────────────────────────────────────────────── */

export interface PlannedMove {
  tripId: string;
  fromRowId: string;
  toRowId: string;
  start: Date;
  end: Date;
  /** The time changed (departure or arrival). */
  timeChanged: boolean;
  /** The row changed. */
  rowChanged: boolean;
}

/** What a drag of the bar did, compared with its current bar. */
export function planMove(bar: PlanboardBarDto, fromRowId: string, toRowId: string, start: Date, end: Date): PlannedMove | null {
  if (!bar.id) return null;
  const interval = barInterval(bar);
  if (!interval) return null;
  const timeChanged = interval.start !== start.getTime() || interval.end !== end.getTime();
  const rowChanged = fromRowId !== toRowId;
  if (!timeChanged && !rowChanged) return null;
  return { tripId: bar.id, fromRowId, toRowId, start, end, timeChanged, rowChanged };
}

/** The bar after a move: new times, and in the mode's list the source row replaced by the target row. */
export function movedBar(bar: PlanboardBarDto, move: PlannedMove, mode: PlanboardResourceMode): PlanboardBarDto {
  const next: PlanboardBarDto = { ...bar };
  if (move.timeChanged) {
    next.plannedDeparture = move.start.toISOString();
    next.plannedArrival = move.end.toISOString();
  }
  if (move.rowChanged) {
    const key = mode === "driver" ? "driverIds" : "vehicleIds";
    const list = [...(bar[key] ?? [])];
    const index = list.findIndex((id) => sameId(id, move.fromRowId));
    if (index >= 0) list[index] = move.toRowId;
    else list.push(move.toRowId);
    next[key] = [...new Set(list)];
  }
  return next;
}

/** The read with one bar replaced (optimistic moves, reconciled stamps). */
export function patchBar(dto: PlanboardSchedulerDto, tripId: string, patch: (bar: PlanboardBarDto) => PlanboardBarDto): PlanboardSchedulerDto {
  let changed = false;
  const bars = (dto.bars ?? []).map((bar) => {
    if (!sameId(bar.id, tripId)) return bar;
    changed = true;
    return patch(bar);
  });
  return changed ? { ...dto, bars } : dto;
}

/** The read after a live refetch: the requested trips are replaced by what came back (absent = gone), names merged. */
export function mergeBars(dto: PlanboardSchedulerDto, requestedIds: readonly string[], fresh: readonly PlanboardBarDto[], names: Record<string, string>): PlanboardSchedulerDto {
  const requested = new Set(requestedIds.map((id) => id.toLowerCase()));
  const kept = (dto.bars ?? []).filter((bar) => !bar.id || !requested.has(bar.id.toLowerCase()));
  return { ...dto, bars: [...kept, ...fresh.filter((b) => b.id)], names: { ...(dto.names ?? {}), ...names } };
}

/** The blocks of a row at one moment: a drop there is a new trip, after the warning (design 74 §10). */
export function blocksAt(rowId: string, moment: Date, blocks: readonly SchedulerBackgroundEvent[]): SchedulerBackgroundEvent[] {
  const at = moment.getTime();
  return blocks.filter((block) => block.resourceId === rowId && time(block.start) <= at && at < time(block.end));
}

/**
 * What a drop on an empty cell does about the trip type (design 74 §10): with more than one type, or before the list is known,
 * the type picker asks; with one it is that type; with none the server says what is missing.
 */
export function tripTypeChoice(types: readonly { id?: string }[] | undefined): { ask: true } | { ask: false; typeId: string | null } {
  if (!types || types.length > 1) return { ask: true };
  return { ask: false, typeId: types[0]?.id ?? null };
}

/**
 * Which ids of a batch of notices need the bar refetched (design 74 §9). A Pending notice does not: its values are about to
 * change. Nor does a plain "changed" notice on a trip that is calculating: the notice that ends the calculation brings it all
 * at once. A removed record always does: its bar must go.
 */
export function idsToRefetch(messages: readonly RecordUpdateMessage[], isCalculating: (lowerId: string) => boolean): string[] {
  return messages
    .filter((message) => {
      if (message.hints?.removed) return true;
      if (message.status === DerivedValueStatus.Pending) return false;
      return !(message.status == null && isCalculating(message.targetId.toLowerCase()));
    })
    .map((message) => message.targetId.toLowerCase());
}

/** The ids of every resource on the page (rows) for the live-update filter. */
export function pageResourceIds(dto: PlanboardSchedulerDto | undefined): string[] {
  return (dto?.rows ?? []).map((r) => r.id).filter((id): id is string => !!id);
}

/** The trip ids on the page. */
export function pageTripIds(dto: PlanboardSchedulerDto | undefined): string[] {
  return (dto?.bars ?? []).map((b) => b.id).filter((id): id is string => !!id);
}
