/* The pure part of the resource scheduler adapter (resource-scheduler.tsx; design 66 D13, design 70 "Scheduler"): the
 * public types and the mapping onto the timeline's event objects, with no FullCalendar import, so feature code and tests
 * use it without loading the component. Dates: `range` is calendar dates (both ends inclusive); event times are ISO
 * date-times or Dates in the browser's time zone. */

export type SchedulerZoom = "hour" | "day";

/** The colour family of a bar (the status token of its business state, mapped by the caller). */
export type SchedulerTone = "neutral" | "info" | "success" | "warning" | "danger" | "brand";

export interface SchedulerResource {
  id: string;
  title: string;
  /** Rows with the same group are shown under one group header. */
  group?: string | null;
}

export interface SchedulerEvent<TData = unknown> {
  id: string;
  resourceId: string;
  start: string | Date;
  end: string | Date;
  title?: string;
  tone?: SchedulerTone;
  /** Overlap on the same row or over a block: drawn with the conflict outline. */
  conflict?: boolean;
  /** The caller's own data for `eventContent` and the callbacks. */
  data?: TData;
}

export type SchedulerBackgroundKind = "unavailable" | "outside-working-time";

export interface SchedulerBackgroundEvent {
  id: string;
  resourceId: string;
  start: string | Date;
  end: string | Date;
  kind: SchedulerBackgroundKind;
  title?: string;
}

export interface SchedulerRange {
  /** First day, `YYYY-MM-DD`. */
  from: string;
  /** Last day (inclusive), `YYYY-MM-DD`. */
  till: string;
}

/** The id prefix of background blocks, so they never collide with bars. */
export const BACKGROUND_ID_PREFIX = "bg:";
export const SNAP_MINUTES = 15;

export interface TimelineEventInput {
  id: string;
  resourceId: string;
  start: string | Date;
  end: string | Date;
  title?: string;
  display?: "background";
  editable?: boolean;
  startEditable?: boolean;
  durationEditable?: boolean;
  resourceEditable?: boolean;
  classNames: string[];
  extendedProps: { source: SchedulerEvent | SchedulerBackgroundEvent; background: boolean };
}

function nextDay(isoDate: string): string {
  const [y, m, d] = isoDate.split("-").map(Number);
  const date = new Date(Date.UTC(y, m - 1, d + 1));
  return date.toISOString().slice(0, 10);
}

/** The timeline's visible range: from the first day 00:00 to the day after the last (exclusive end). */
export function toVisibleRange(range: SchedulerRange): { start: string; end: string } {
  return { start: range.from, end: nextDay(range.till) };
}

export function slotDurationMs(zoom: SchedulerZoom): number {
  return zoom === "hour" ? 3_600_000 : 86_400_000;
}

/** Maps bars and blocks onto the timeline's events; read-only bars cannot be dragged or resized. */
export function toTimelineEvents<TData>(
  events: readonly SchedulerEvent<TData>[],
  backgroundEvents: readonly SchedulerBackgroundEvent[] = [],
  readOnly?: (event: SchedulerEvent<TData>) => boolean,
  selectedIds?: ReadonlySet<string>,
): TimelineEventInput[] {
  const bars = events.map((event): TimelineEventInput => {
    const editable = !(readOnly?.(event) ?? false);
    const classNames = ["opentms-sched-event", `opentms-sched-event--tone-${event.tone ?? "info"}`];
    if (event.conflict) classNames.push("opentms-sched-event--conflict");
    if (selectedIds?.has(event.id)) classNames.push("opentms-sched-event--selected");
    if (!editable) classNames.push("opentms-sched-event--readonly");
    return {
      id: event.id,
      resourceId: event.resourceId,
      start: event.start,
      end: event.end,
      title: event.title,
      editable,
      startEditable: editable,
      durationEditable: editable,
      resourceEditable: editable,
      classNames,
      extendedProps: { source: event as SchedulerEvent, background: false },
    };
  });
  const blocks = backgroundEvents.map(
    (block): TimelineEventInput => ({
      id: `${BACKGROUND_ID_PREFIX}${block.id}`,
      resourceId: block.resourceId,
      start: block.start,
      end: block.end,
      title: block.title,
      display: "background",
      classNames: ["opentms-sched-bg", `opentms-sched-bg--${block.kind}`],
      extendedProps: { source: block, background: true },
    }),
  );
  return [...bars, ...blocks];
}

/** A slot of the timeline's body as measured on screen. */
export interface MeasuredSlot {
  start: Date;
  left: number;
  width: number;
}

/** The slot's `data-date` attribute as a local Date (a bare date is local midnight, not UTC). */
export function parseSlotDate(value: string | null | undefined): Date | null {
  if (!value) return null;
  const date = new Date(value.length === 10 ? `${value}T00:00:00` : value);
  return Number.isNaN(date.getTime()) ? null : date;
}

/** A row of the timeline's body as measured on screen. */
export interface MeasuredRow {
  resourceId: string;
  top: number;
  bottom: number;
}

/** The row under a vertical screen position; null above, below or between the rows. */
export function rowAtY(rows: readonly MeasuredRow[], y: number): string | null {
  return rows.find((row) => y >= row.top && y < row.bottom)?.resourceId ?? null;
}

/** The time under a horizontal screen position, snapped to `snapMinutes`; null outside every slot. */
export function timeAtX(slots: readonly MeasuredSlot[], x: number, slotMs: number, snapMinutes = SNAP_MINUTES): Date | null {
  const slot = slots.find((s) => x >= s.left && x < s.left + s.width);
  if (!slot || slot.width <= 0) return null;
  const offset = ((x - slot.left) / slot.width) * slotMs;
  const snap = snapMinutes * 60_000;
  return new Date(slot.start.getTime() + Math.floor(offset / snap) * snap);
}
