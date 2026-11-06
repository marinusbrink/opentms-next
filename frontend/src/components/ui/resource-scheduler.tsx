import { useCallback, useMemo, useRef, type DragEvent, type ReactNode } from "react";
import FullCalendar from "@fullcalendar/react";
import interactionPlugin from "@fullcalendar/interaction";
import resourceTimelinePlugin from "@fullcalendar/resource-timeline";
import enGbLocale from "@fullcalendar/core/locales/en-gb";
import nlLocale from "@fullcalendar/core/locales/nl";
import type { DateSelectArg, EventClickArg, EventContentArg, EventDropArg, EventMountArg } from "@fullcalendar/core";
import type { EventResizeDoneArg } from "@fullcalendar/interaction";
import "@/styles/resource-scheduler.css";

import {
  parseSlotDate,
  rowAtY,
  slotDurationMs,
  timeAtX,
  toTimelineEvents,
  toVisibleRange,
  type MeasuredRow,
  type MeasuredSlot,
  type SchedulerBackgroundEvent,
  type SchedulerEvent,
  type SchedulerRange,
  type SchedulerResource,
  type SchedulerZoom,
} from "@/components/ui/resource-scheduler-model";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { cn } from "@/lib/utils";

export type {
  SchedulerBackgroundEvent,
  SchedulerBackgroundKind,
  SchedulerEvent,
  SchedulerRange,
  SchedulerResource,
  SchedulerTone,
  SchedulerZoom,
} from "@/components/ui/resource-scheduler-model";

/* The resource scheduler (design 66 D13, design 70 "Scheduler"): rows of resources against a time axis with bars, over
 * FullCalendar Scheduler's resource timeline. This file is the ONLY place that imports FullCalendar (the lint rule
 * no-restricted-imports guards it), so another component (Bryntum, DHTMLX) can replace it here after the PO's trial.
 * No licence key is set: FullCalendar runs in evaluation mode and shows its licence notice (design 66: the PO decides
 * on the component to buy before go-live).
 *
 * Controlled: the bars always come from `events`. A drag or resize calls `onEventMove` and the bar returns to where
 * `events` puts it; the caller moves it (optimistically, or after a confirmation) by changing `events`. Load it through
 * resource-scheduler-lazy.tsx. */

export interface ResourceSchedulerProps<TData = unknown> {
  resources: readonly SchedulerResource[];
  events: readonly SchedulerEvent<TData>[];
  /** Unavailability and outside-working-time blocks. */
  backgroundEvents?: readonly SchedulerBackgroundEvent[];
  range: SchedulerRange;
  zoom: SchedulerZoom;
  /** A bar was dragged in time and/or to another row, or resized (then the resource is unchanged). */
  onEventMove?: (eventId: string, newStart: Date, newEnd: Date, fromResourceId: string, toResourceId: string) => void;
  onEventClick?: (eventId: string, jsEvent: MouseEvent) => void;
  onEventDoubleClick?: (eventId: string) => void;
  /**
   * Something from outside (another pane) was dropped on a row: the row's resource, the time under the pointer, the
   * drag's data (read it with the feature's payload parser) and the bar it landed on, if any.
   */
  onExternalDrop?: (resourceId: string, start: Date, payload: DataTransfer, targetEventId: string | null) => void;
  /** A drag that may drop here was let go outside the rows (the row labels, the header, below the last row). */
  onExternalDropMiss?: () => void;
  /** Whether a drag from outside may drop here, judged from its types during dragover; default: any drag. */
  acceptExternalDrag?: (types: readonly string[]) => boolean;
  /** An empty stretch of a row was selected (e.g. to create a trip there). */
  onSelectEmpty?: (resourceId: string, start: Date, end: Date) => void;
  /** The bar's content; default its title. */
  eventContent?: (event: SchedulerEvent<TData>) => ReactNode;
  /** A read-only bar cannot be dragged or resized (completed or cancelled trips). */
  readOnly?: (event: SchedulerEvent<TData>) => boolean;
  /** The row header's content; default the resource's title. */
  resourceLabel?: (resource: SchedulerResource) => ReactNode;
  /** The header above the rows (already translated). */
  resourceAreaHeader?: string;
  /** Bars drawn as selected. */
  selectedEventIds?: readonly string[];
  className?: string;
  "aria-label"?: string;
}

const SLOT_LABELS = {
  hour: [{ weekday: "short", day: "numeric", month: "short" }, { hour: "2-digit", minute: "2-digit", hour12: false }],
  day: [{ weekday: "short", day: "numeric", month: "short" }],
} as const;

/* Pixel widths of one slot per zoom, so a day of hours or two weeks of days stays readable (scrolls when wider). */
const SLOT_MIN_WIDTH: Record<SchedulerZoom, number> = { hour: 36, day: 96 };

const dblClickHandlers = new WeakMap<HTMLElement, () => void>();

export function ResourceScheduler<TData = unknown>(props: ResourceSchedulerProps<TData>) {
  const { resources, events, backgroundEvents, range, zoom, eventContent, readOnly, resourceLabel, resourceAreaHeader, selectedEventIds, className } = props;
  const { culture } = useL();
  const calendarRef = useRef<FullCalendar>(null);
  const containerRef = useRef<HTMLDivElement>(null);
  /* The callbacks change every render; FullCalendar keeps the first ones it saw in some places, so they go via a ref. */
  const handlers = useRef(props);
  handlers.current = props;

  const selected = useMemo(() => new Set(selectedEventIds ?? []), [selectedEventIds]);
  const timelineEvents = useMemo(() => toTimelineEvents(events, backgroundEvents, readOnly, selected), [events, backgroundEvents, readOnly, selected]);
  const timelineResources = useMemo(() => resources.map((r, sortIndex) => ({ id: r.id, title: r.title, sortIndex, ...(r.group ? { group: r.group } : {}) })), [resources]);
  const grouped = resources.some((r) => r.group);
  const visibleRange = useMemo(() => toVisibleRange(range), [range]);

  const sourceOf = (event: { extendedProps: Record<string, unknown> }) => event.extendedProps.source as SchedulerEvent<TData>;

  const handleDrop = (info: EventDropArg) => {
    const event = info.event;
    const fromResourceId = info.oldResource?.id ?? event.getResources()[0]?.id ?? sourceOf(event).resourceId;
    const toResourceId = info.newResource?.id ?? fromResourceId;
    const start = event.start;
    const end = event.end ?? event.start;
    info.revert();
    if (start && end) handlers.current.onEventMove?.(event.id, start, end, fromResourceId, toResourceId);
  };

  const handleResize = (info: EventResizeDoneArg) => {
    const event = info.event;
    const resourceId = event.getResources()[0]?.id ?? sourceOf(event).resourceId;
    const start = event.start;
    const end = event.end ?? event.start;
    info.revert();
    if (start && end) handlers.current.onEventMove?.(event.id, start, end, resourceId, resourceId);
  };

  const handleSelect = (info: DateSelectArg) => {
    calendarRef.current?.getApi().unselect();
    if (info.resource) handlers.current.onSelectEmpty?.(info.resource.id, info.start, info.end);
  };

  const handleMount = (info: EventMountArg) => {
    if (info.event.display === "background") return;
    info.el.dataset.eventId = info.event.id;
    const onDblClick = () => handlers.current.onEventDoubleClick?.(info.event.id);
    dblClickHandlers.set(info.el, onDblClick);
    info.el.addEventListener("dblclick", onDblClick);
  };

  const handleUnmount = (info: EventMountArg) => {
    const onDblClick = dblClickHandlers.get(info.el);
    if (onDblClick) info.el.removeEventListener("dblclick", onDblClick);
    dblClickHandlers.delete(info.el);
  };

  const renderContent = (arg: EventContentArg) => {
    if (arg.event.display === "background") return arg.event.title ? <span className="opentms-sched-bg-title">{arg.event.title}</span> : null;
    const source = sourceOf(arg.event);
    return <div className="opentms-sched-event-content">{eventContent ? eventContent(source) : (source.title ?? arg.event.title)}</div>;
  };

  /* Drops from other panes (HTML5 drag and drop): FullCalendar handles only its own drags. The row and time come from
   * the pointer's position, not from the drop target: on an empty stretch the element under the pointer is the slot
   * table, which lies over the lanes and belongs to no row (only bars and blocks sit inside a lane's element). Only the
   * timeline's body counts: the row labels carry a resource id too, but with the timeline scrolled sideways a slot lies
   * behind them and would give a wrong time. Measured at the drop, so scrolling either way and both zooms need no care. */
  const measureSlots = useCallback((): MeasuredSlot[] => {
    const cells = containerRef.current?.querySelectorAll<HTMLElement>("td.fc-timeline-slot-lane[data-date]") ?? [];
    const slots: MeasuredSlot[] = [];
    cells.forEach((cell) => {
      const start = parseSlotDate(cell.dataset.date);
      if (!start) return;
      const rect = cell.getBoundingClientRect();
      slots.push({ start, left: rect.left, width: rect.width });
    });
    return slots;
  }, []);

  const measureRows = useCallback((): MeasuredRow[] => {
    const cells = containerRef.current?.querySelectorAll<HTMLElement>("td.fc-timeline-lane[data-resource-id]") ?? [];
    const rows: MeasuredRow[] = [];
    cells.forEach((cell) => {
      const rect = cell.getBoundingClientRect();
      rows.push({ resourceId: cell.dataset.resourceId ?? "", top: rect.top, bottom: rect.bottom });
    });
    return rows;
  }, []);

  const accepts = (e: DragEvent) => {
    if (!handlers.current.onExternalDrop) return false;
    const types = Array.from(e.dataTransfer.types);
    return handlers.current.acceptExternalDrag ? handlers.current.acceptExternalDrag(types) : true;
  };

  /* Everywhere in the scheduler, so a drop beside the rows reaches `onDrop` and can say why nothing happens. */
  const onDragOver = (e: DragEvent<HTMLDivElement>) => {
    if (accepts(e)) e.preventDefault();
  };

  const onDrop = (e: DragEvent<HTMLDivElement>) => {
    if (!accepts(e)) return;
    e.preventDefault();
    const target = e.target as Element | null;
    const resourceId = target?.closest(".fc-timeline-body") ? rowAtY(measureRows(), e.clientY) : null;
    const start = resourceId ? timeAtX(measureSlots(), e.clientX, slotDurationMs(zoom)) : null;
    if (!resourceId || !start) {
      handlers.current.onExternalDropMiss?.();
      return;
    }
    const targetEventId = target?.closest<HTMLElement>("[data-event-id]")?.dataset.eventId ?? null;
    handlers.current.onExternalDrop?.(resourceId, start, e.dataTransfer, targetEventId);
  };

  return (
    <div
      ref={containerRef}
      className={cn("opentms-scheduler h-full min-h-0", className)}
      aria-label={props["aria-label"]}
      onDragOver={onDragOver}
      onDrop={onDrop}
    >
      <FullCalendar
        ref={calendarRef}
        plugins={[resourceTimelinePlugin, interactionPlugin]}
        initialView="resourceTimeline"
        visibleRange={visibleRange}
        headerToolbar={false}
        height="100%"
        locale={culture.toLowerCase().startsWith("nl") ? nlLocale : enGbLocale}
        slotDuration={zoom === "hour" ? { hours: 1 } : { days: 1 }}
        slotLabelFormat={SLOT_LABELS[zoom] as never}
        slotMinWidth={SLOT_MIN_WIDTH[zoom]}
        snapDuration={{ minutes: 15 }}
        nowIndicator
        resources={timelineResources}
        resourceOrder="sortIndex"
        {...(grouped ? { resourceGroupField: "group" } : {})}
        resourceAreaWidth="14rem"
        resourceAreaHeaderContent={resourceAreaHeader ?? ""}
        resourceLabelContent={(arg) => {
          const resource = resources.find((r) => r.id === arg.resource.id);
          return resource && resourceLabel ? resourceLabel(resource) : arg.resource.title;
        }}
        events={timelineEvents}
        editable
        eventResourceEditable
        selectable={!!props.onSelectEmpty}
        selectMirror={false}
        eventDrop={handleDrop}
        eventResize={handleResize}
        eventClick={(info: EventClickArg) => {
          if (info.event.display === "background") return;
          handlers.current.onEventClick?.(info.event.id, info.jsEvent);
        }}
        select={handleSelect}
        eventContent={renderContent}
        eventDidMount={handleMount}
        eventWillUnmount={handleUnmount}
      />
    </div>
  );
}
