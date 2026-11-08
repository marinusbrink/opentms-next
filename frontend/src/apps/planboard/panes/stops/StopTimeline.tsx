import { Fragment, useEffect, useMemo, useRef, useState, type DragEvent, type KeyboardEvent, type ReactNode } from "react";
import { ArrowDown, ArrowUp, Check, ClipboardCheck, GripVertical, MessageSquareText, Pencil, Plus, Trash2, Truck } from "lucide-react";

import { lower } from "@/apps/planboard/panes/lists/grid-helpers";
import { InlineTextCell, LatenessBadge, TripWarningsCell } from "@/apps/planboard/panes/lists/grid-support";
import {
  delayAtNextStop,
  lineState,
  markerState,
  stopBadge,
  stopHasActuals,
  stopOrderNumbers,
  stopTimes,
  stopWindow,
  timelinePosition,
  type MarkerState,
  type NowPosition,
} from "@/apps/planboard/panes/stops/model";
import { ContextMenu, ContextMenuContent, ContextMenuItem, ContextMenuSeparator, ContextMenuTrigger } from "@/components/ui/context-menu";
import type { TransportTripDto, TripStopDto } from "@/domains/planning-execution/trips";
import { useFormat } from "@/lib/format";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { cn } from "@/lib/utils";

/* The stops of a trip as a vertical timeline (design 74 §2; modelled on a rail route planner's result): a time column with
 * one tier per stop, a line whose driven part is solid and whose part to come is dashed, and per stop what the planner
 * needs to know. "Now" is the one accent: a marker on the line with the time and the delay, placed by the pure
 * `nowPosition` and moved once a minute.
 *
 * A click selects (two-way with the map), a double click or Enter opens the detail window, a right click opens the menu;
 * the rows are focusable and the arrow keys move between them. Dragging a stop's grip onto another stop moves it there. */

/** A stop dragged inside this pane (never leaves it, so it is not one of the board's payloads in dnd.ts). */
const DND_STOP = "application/x-opentms-stop";

/** The current time, moving on at each full minute. */
function useMinuteClock(): Date {
  const [now, setNow] = useState(() => new Date());
  useEffect(() => {
    let interval: ReturnType<typeof setInterval> | undefined;
    const timeout = setTimeout(() => {
      setNow(new Date());
      interval = setInterval(() => setNow(new Date()), 60_000);
    }, 60_000 - (Date.now() % 60_000));
    return () => {
      clearTimeout(timeout);
      clearInterval(interval);
    };
  }, []);
  return now;
}

const lineClass = (driven: boolean) => (driven ? "border-solid border-brand" : "border-dashed border-muted-foreground/40");

const LINE = "absolute left-1/2 -translate-x-1/2 border-l-2";

const MARKER_CLASSES: Record<MarkerState, string> = {
  done: "size-3 bg-brand",
  next: "size-4 border-2 border-brand bg-background",
  later: "size-3 border-2 border-muted-foreground/40 bg-background",
};

/** The dot of "now": on the line between two stops, above the first, or on the stop the trip is at. */
const NOW_DOT = "size-3 rounded-full bg-brand ring-4 ring-brand-soft";

const TIME_COLUMN = "w-20 shrink-0 text-right tabular-nums";
const RAIL_COLUMN = "relative w-6 shrink-0";

export interface StopTimelineProps {
  trip: TransportTripDto;
  /** In position order. */
  stops: readonly TripStopDto[];
  /** May manage the stops: the permission and the trip's state together. */
  editable: boolean;
  activityName: (stop: TripStopDto) => string;
  selectedStopId: string | null;
  onSelect: (stop: TripStopDto) => void;
  onOpen: (stop: TripStopDto) => void;
  onMove: (stop: TripStopDto, position: number) => void;
  onRecordActuals: (stop: TripStopDto) => void;
  onRemove: (stop: TripStopDto) => void;
  onAddStop: () => void;
  /** Saves the remarks; rejects after showing the failure. */
  onCommitRemarks: (stop: TripStopDto, remarks: string | null) => Promise<void>;
}

export function StopTimeline({ trip, stops, editable, activityName, selectedStopId, onSelect, onOpen, onMove, onRecordActuals, onRemove, onAddStop, onCommitRemarks }: StopTimelineProps) {
  const { t } = useL();
  const f = useFormat();
  const now = useMinuteClock();
  const listRef = useRef<HTMLDivElement>(null);
  const dragged = useRef<string | null>(null);
  const [dropTargetId, setDropTargetId] = useState<string | null>(null);

  const { position, showNow } = useMemo(() => timelinePosition(stops, now, trip.status), [stops, now, trip.status]);
  const delay = useMemo(() => delayAtNextStop(stops, position), [stops, position]);

  const rowOf = (stopId: string | null | undefined) => (stopId ? listRef.current?.querySelector<HTMLElement>(`[data-stop-id="${lower(stopId)}"]`) : null);

  /* The stop selected here or on the map comes into view, also when its trip arrives after the selection. */
  useEffect(() => {
    rowOf(selectedStopId)?.scrollIntoView?.({ block: "nearest" });
  }, [selectedStopId, stops.length]);

  const onRowKeyDown = (event: KeyboardEvent<HTMLElement>, index: number) => {
    /* The row's own keys only: an inline editor or a button inside it keeps its keys. */
    if (event.target !== event.currentTarget) return;
    const stop = stops[index];
    if (event.key === "Enter") {
      event.preventDefault();
      onOpen(stop);
    } else if (event.key === "ArrowDown" || event.key === "ArrowUp") {
      event.preventDefault();
      const next = stops[index + (event.key === "ArrowDown" ? 1 : -1)];
      if (!next) return;
      onSelect(next);
      rowOf(next.id)?.focus();
    }
  };

  const dropHandlers = (stop: TripStopDto) => ({
    onDragOver: (event: DragEvent<HTMLElement>) => {
      if (!editable || !Array.from(event.dataTransfer.types).includes(DND_STOP)) return;
      event.preventDefault();
      event.dataTransfer.dropEffect = "move";
      setDropTargetId(stop.id ?? null);
    },
    onDragLeave: (event: DragEvent<HTMLElement>) => {
      if (!event.currentTarget.contains(event.relatedTarget as Node | null)) setDropTargetId(null);
    },
    onDrop: (event: DragEvent<HTMLElement>) => {
      setDropTargetId(null);
      const sourceId = event.dataTransfer.getData(DND_STOP) || dragged.current;
      dragged.current = null;
      if (!editable || !sourceId || sourceId === stop.id) return;
      event.preventDefault();
      const source = stops.find((s) => s.id === sourceId);
      if (source && stop.position != null) onMove(source, stop.position);
    },
  });

  /* Roving focus: one row is in the tab order, the selected stop or else the first. */
  const hasSelectedRow = stops.some((stop) => lower(stop.id) === lower(selectedStopId));

  const nowLabel = position.kind === "before" ? t("Planboard:Stops.NotStarted") : t("Planboard:Stops.Now");
  const nowDelay = delay == null ? null : <LatenessBadge minutes={delay > 0 ? delay : null} known />;

  return (
    <div ref={listRef} role="list" aria-label={t("PlanningExecution:Stops")} className="min-h-0 flex-1 overflow-y-auto px-2 py-3" data-slot="planboard-stop-timeline">
      {showNow && position.kind === "before" && (
        <div role="listitem" className="flex min-h-10 items-stretch" data-slot="planboard-stop-now">
          <span className={cn(TIME_COLUMN, "self-center text-sm font-semibold text-brand")}>{f.time(now)}</span>
          <span className={RAIL_COLUMN} aria-hidden="true">
            <span className={cn(LINE, "top-1/2 bottom-0", lineClass(false))} />
            <span className={cn(NOW_DOT, "absolute top-1/2 left-1/2 -translate-x-1/2 -translate-y-1/2")} />
          </span>
          <NowCaption label={nowLabel} delay={nowDelay} />
        </div>
      )}
      {stops.map((stop, index) => {
        const showDate = index === 0 || dayOf(stops[index - 1], f) !== dayOf(stop, f);
        const line = lineState(index, position);
        const first = index === 0;
        const last = index === stops.length - 1;
        return (
          <Fragment key={stop.id}>
            {showDate && dayOf(stop, f) && (
              <div className="flex" aria-hidden="true">
                <span className={cn(TIME_COLUMN, "pt-2 pb-1 text-label font-medium text-muted-foreground")}>{dayOf(stop, f)}</span>
                <span className={RAIL_COLUMN}>{!first && <span className={cn(LINE, "inset-y-0", lineClass(line.upper))} />}</span>
              </div>
            )}
            <StopRow
              stop={stop}
              index={index}
              stops={stops}
              first={first}
              last={last}
              line={line}
              marker={markerState(index, position)}
              isNow={showNow && position.kind === "at" && position.index === index}
              nowText={f.time(now)}
              nowLabel={nowLabel}
              nowDelay={nowDelay}
              editable={editable}
              selected={lower(stop.id) === lower(selectedStopId)}
              inTabOrder={hasSelectedRow ? lower(stop.id) === lower(selectedStopId) : first}
              isDropTarget={dropTargetId != null && dropTargetId === stop.id}
              activity={activityName(stop)}
              warnings={(trip.warnings ?? []).filter((warning) => warning.stopId != null && warning.stopId === stop.id)}
              onSelect={onSelect}
              onOpen={onOpen}
              onMove={onMove}
              onRecordActuals={onRecordActuals}
              onRemove={onRemove}
              onAddStop={onAddStop}
              onCommitRemarks={onCommitRemarks}
              onKeyDown={(event) => onRowKeyDown(event, index)}
              onDragStart={(event) => {
                dragged.current = stop.id ?? null;
                event.dataTransfer.setData(DND_STOP, stop.id ?? "");
                event.dataTransfer.effectAllowed = "move";
              }}
              dropHandlers={dropHandlers(stop)}
            />
            {position.kind === "between" && position.from === index && <BetweenStops position={position} showNow={showNow} time={f.time(now)} label={nowLabel} delay={nowDelay} />}
          </Fragment>
        );
      })}
    </div>
  );
}

/** The day a stop's times are on: shown once, above the first stop of that day. */
function dayOf(stop: TripStopDto, f: ReturnType<typeof useFormat>): string {
  const { arrival, departure } = stopTimes(stop);
  return f.date(arrival ?? departure);
}

function NowCaption({ label, delay, className }: { label: string; delay: ReactNode; className?: string }) {
  return (
    <span className={cn("flex min-w-0 flex-1 flex-wrap items-center gap-x-2 gap-y-1 self-center px-3 text-label font-medium text-brand", className)}>
      {label}
      {delay}
    </span>
  );
}

/** The stretch between two stops while the trip is on it: the line solid up to "now", dashed after it. */
function BetweenStops({ position, showNow, time, label, delay }: { position: Extract<NowPosition, { kind: "between" }>; showNow: boolean; time: string; label: string; delay: ReactNode }) {
  /* A percentage of the stretch, kept from its very ends so the marker's text stays inside it. */
  const top = `${20 + position.fraction * 60}%`;
  if (!showNow) {
    return (
      <div className="flex h-4" aria-hidden="true">
        <span className={TIME_COLUMN} />
        <span className={RAIL_COLUMN}>
          <span className={cn(LINE, "inset-y-0", lineClass(false))} />
        </span>
      </div>
    );
  }
  return (
    <div role="listitem" className="relative flex h-16" data-slot="planboard-stop-now">
      <span className={TIME_COLUMN} />
      <span className={RAIL_COLUMN} aria-hidden="true">
        <span className={cn(LINE, "inset-y-0", lineClass(false))} />
        <span className={cn(LINE, "top-0", lineClass(true))} style={{ height: top }} />
      </span>
      <div className="absolute inset-x-0 flex -translate-y-1/2 items-center" style={{ top }}>
        <span className={cn(TIME_COLUMN, "text-sm font-semibold text-brand")}>{time}</span>
        <span className="flex w-6 shrink-0 justify-center" aria-hidden="true">
          <span className={NOW_DOT} />
        </span>
        <NowCaption label={label} delay={delay} />
      </div>
    </div>
  );
}

function StateBadge({ stop }: { stop: TripStopDto }) {
  const { t } = useL();
  const badge = stopBadge(stop);
  if (!badge) return null;
  const tone = { "on-time": "bg-tone-success-bg text-tone-success", "at-risk": "bg-tone-warning-bg text-tone-warning", late: "bg-tone-danger-bg text-tone-danger" }[badge.state];
  const text = badge.state === "late" && badge.delayMinutes != null ? t("Planboard:Lists.LateMinutes").replace("{0}", String(badge.delayMinutes)) : t(`Planboard:Map.StopState.${badge.state}`);
  return <span className={cn("inline-flex items-center rounded-full px-2 py-0.5 text-label font-medium whitespace-nowrap", tone)}>{text}</span>;
}

function TierIcon({ tier }: { tier: "actual" | "estimated" | "planned" }) {
  const { t } = useL();
  if (tier === "planned") return null;
  const label = t(`Planboard:Stops.Tier.${tier}`);
  const Icon = tier === "actual" ? Check : Truck;
  return (
    <span role="img" aria-label={label} title={label} className={tier === "actual" ? "text-tone-success" : "text-muted-foreground"}>
      <Icon className="size-3.5" aria-hidden="true" />
    </span>
  );
}

interface StopRowProps extends Pick<StopTimelineProps, "onSelect" | "onOpen" | "onMove" | "onRecordActuals" | "onRemove" | "onAddStop" | "onCommitRemarks" | "editable"> {
  stop: TripStopDto;
  index: number;
  stops: readonly TripStopDto[];
  first: boolean;
  last: boolean;
  line: { upper: boolean; lower: boolean };
  marker: MarkerState;
  isNow: boolean;
  nowText: string;
  nowLabel: string;
  nowDelay: ReactNode;
  selected: boolean;
  inTabOrder: boolean;
  isDropTarget: boolean;
  activity: string;
  warnings: NonNullable<TransportTripDto["warnings"]>;
  onKeyDown: (event: KeyboardEvent<HTMLElement>) => void;
  onDragStart: (event: DragEvent<HTMLElement>) => void;
  dropHandlers: { onDragOver: (event: DragEvent<HTMLElement>) => void; onDragLeave: (event: DragEvent<HTMLElement>) => void; onDrop: (event: DragEvent<HTMLElement>) => void };
}

function StopRow({
  stop,
  stops,
  first,
  last,
  line,
  marker,
  isNow,
  nowText,
  nowLabel,
  nowDelay,
  editable,
  selected,
  inTabOrder,
  isDropTarget,
  activity,
  warnings,
  onSelect,
  onOpen,
  onMove,
  onRecordActuals,
  onRemove,
  onAddStop,
  onCommitRemarks,
  onKeyDown,
  onDragStart,
  dropHandlers,
}: StopRowProps) {
  const { t } = useL();
  const f = useFormat();
  const times = stopTimes(stop);
  const blocked = stopHasActuals(stop);
  const name = stop.relationName ?? stop.name ?? "";
  const window = stopWindow(stop);
  const orders = stopOrderNumbers(stop);
  const details = [activity, stop.city].filter(Boolean).join(" · ");

  const windowText = () => {
    if (!window.from && !window.till) return "";
    if (window.from && window.till && f.date(window.from) === f.date(window.till)) return `${f.date(window.from)} ${f.time(window.from)} – ${f.time(window.till)}`;
    return `${window.from ? f.dateTime(window.from) : ""} – ${window.till ? f.dateTime(window.till) : ""}`;
  };

  return (
    <ContextMenu>
      <ContextMenuTrigger
        render={
          <div
            role="listitem"
            tabIndex={inTabOrder ? 0 : -1}
            aria-label={name}
            aria-current={selected || undefined}
            data-stop-id={lower(stop.id)}
            className={cn("group/stop flex rounded-lg outline-none focus-visible:ring-2 focus-visible:ring-ring", isDropTarget && "ring-2 ring-brand")}
            onClick={() => onSelect(stop)}
            onDoubleClick={() => onOpen(stop)}
            onContextMenu={() => onSelect(stop)}
            onKeyDown={onKeyDown}
            {...dropHandlers}
          />
        }
      >
        <div className={cn(TIME_COLUMN, "pt-1.5 pb-3")}>
          <div className="flex items-center justify-end gap-1 text-sm font-semibold">
            <TierIcon tier={times.tier} />
            {times.arrival ? f.time(times.arrival) : null}
          </div>
          {times.departure && <div className="text-sm text-muted-foreground">{f.time(times.departure)}</div>}
        </div>

        <div className={RAIL_COLUMN} aria-hidden="true">
          {!first && <span className={cn(LINE, "top-0 h-4", lineClass(line.upper))} />}
          {!last && <span className={cn(LINE, "top-4 bottom-0", lineClass(line.lower))} />}
          <span className={cn("absolute top-4 left-1/2 -translate-x-1/2 -translate-y-1/2 rounded-full", isNow ? NOW_DOT : MARKER_CLASSES[marker])} />
        </div>

        <div className={cn("mb-3 min-w-0 flex-1 rounded-lg px-3 py-1.5 transition-colors", selected ? "bg-brand-subtle" : "group-hover/stop:bg-muted")}>
          {isNow && (
            <div className="mb-1 flex flex-wrap items-center gap-x-2 gap-y-1 text-label font-medium text-brand" data-slot="planboard-stop-now">
              <span className="font-semibold tabular-nums">{nowText}</span>
              {nowLabel}
              {nowDelay}
            </div>
          )}
          {/* In a narrow pane the badges go under the name instead of squeezing it. */}
          <div className="flex flex-wrap items-start gap-x-2">
            <span className="min-w-0 flex-1 basis-40 text-sm font-semibold wrap-break-word">{name}</span>
            <span className="flex shrink-0 items-center gap-2">
              <StateBadge stop={stop} />
              <TripWarningsCell value={warnings} />
              {editable && !blocked && (
                <span
                  draggable
                  role="button"
                  aria-label={t("Planboard:Lists.DragToReorder")}
                  title={t("Planboard:Lists.DragToReorder")}
                  className="inline-flex cursor-grab items-center text-muted-foreground opacity-0 group-hover/stop:opacity-100 active:cursor-grabbing"
                  onDragStart={onDragStart}
                >
                  <GripVertical className="size-4" aria-hidden="true" />
                </span>
              )}
            </span>
          </div>
          {details && <div className="text-sm wrap-break-word text-muted-foreground">{details}</div>}
          {orders.length > 0 && <div className="text-label wrap-break-word text-muted-foreground tabular-nums">{orders.join(", ")}</div>}
          {windowText() && (
            <div className="text-label wrap-break-word text-muted-foreground tabular-nums">
              {t("PlanningExecution:Window")}: {windowText()}
            </div>
          )}
          {(stop.remarks || editable) && (
            /* Without remarks the row shows on hover or focus only, keeping its height so nothing moves. */
            <div className={cn("mt-1 flex min-h-6 items-center gap-1 text-label text-muted-foreground", !stop.remarks && "opacity-0 group-hover/stop:opacity-100 focus-within:opacity-100")}>
              <MessageSquareText className="size-3.5 shrink-0" aria-label={t("PlanningExecution:Remarks")} />
              <div className="min-w-0 flex-1">
                <InlineTextCell<TripStopDto> value={stop.remarks} data={stop} maxLength={2500} canEdit={() => editable} onCommit={onCommitRemarks} />
              </div>
            </div>
          )}
        </div>
      </ContextMenuTrigger>

      <ContextMenuContent>
        <ContextMenuItem onClick={() => onOpen(stop)}>
          <Pencil />
          {t("PlanningExecution:OpenStop")}
        </ContextMenuItem>
        {editable && (
          <>
            <ContextMenuSeparator />
            <MoveItem icon={<ArrowUp />} labelKey="Orders:MoveUp" disabled={(stop.position ?? 1) <= 1 || blocked} blocked={blocked} onClick={() => onMove(stop, (stop.position ?? 1) - 1)} />
            <MoveItem icon={<ArrowDown />} labelKey="Orders:MoveDown" disabled={(stop.position ?? 0) >= stops.length || blocked} blocked={blocked} onClick={() => onMove(stop, (stop.position ?? 0) + 1)} />
            <ContextMenuItem onClick={() => onRecordActuals(stop)}>
              <ClipboardCheck />
              {t("PlanningExecution:RecordActuals")}
            </ContextMenuItem>
            <ContextMenuItem onClick={onAddStop}>
              <Plus />
              {t("PlanningExecution:AddStop")}
            </ContextMenuItem>
            <ContextMenuSeparator />
            <ContextMenuItem
              variant="destructive"
              disabled={stop.isManual !== true}
              title={stop.isManual !== true ? t("OpenTms.PlanningExecution:NotManualStop") : undefined}
              className="data-disabled:pointer-events-auto"
              onClick={() => onRemove(stop)}
            >
              <Trash2 />
              {t("PlanningExecution:RemoveStop")}
            </ContextMenuItem>
          </>
        )}
      </ContextMenuContent>
    </ContextMenu>
  );
}

/** Move up or down; disabled for a business reason, which its tooltip names (UI-PERM-005). */
function MoveItem({ icon, labelKey, disabled, blocked, onClick }: { icon: ReactNode; labelKey: string; disabled: boolean; blocked: boolean; onClick: () => void }) {
  const { t } = useL();
  return (
    <ContextMenuItem disabled={disabled} title={blocked ? t("OpenTms.PlanningExecution:StopHasActuals") : undefined} className="data-disabled:pointer-events-auto" onClick={onClick}>
      {icon}
      {t(labelKey)}
    </ContextMenuItem>
  );
}
