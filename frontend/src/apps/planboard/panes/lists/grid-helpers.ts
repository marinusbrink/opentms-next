import { useCallback, useMemo, useRef, type DragEvent } from "react";
import type { GridApi } from "ag-grid-community";

import { planboardDragKind, readDragPayload, type PlanboardDragKind, type PlanboardDragPayload } from "@/apps/planboard/dnd";
import { parseTimeOfDay } from "@/domains/orders/reference-data";
import { tripWarningKey, type TripWarningDto } from "@/domains/planning-execution/trips";

/* The non-visual part of the list panes' grid support (grid-support.tsx): the grid api capture, rows as drop targets,
 * the drag column, warning keys, lateness and date-time helpers.
 *
 * OpenTmsGrid exposes no grid api; the panes take it from a column's valueGetter (every AG Grid callback receives the
 * api), which is available as soon as a row renders. Without it (no rows yet) a pane falls back to a full refresh. */

export const lower = (id: string | null | undefined) => (id ?? "").toLowerCase();

/** Keeps the grid api a column callback hands over (see the file comment). */
export function useGridApiCapture<TRow>() {
  const apiRef = useRef<GridApi<TRow> | null>(null);
  const capture = useCallback((api: GridApi<TRow> | null | undefined) => {
    if (api && apiRef.current !== api && !api.isDestroyed()) apiRef.current = api;
  }, []);
  const current = useCallback(() => {
    const api = apiRef.current;
    return api && !api.isDestroyed() ? api : null;
  }, []);
  return useMemo(() => ({ capture, current }), [capture, current]);
}

/** Marks the AG Grid rows of one id inside `container` (both pinned and centre parts). */
function markRow(container: HTMLElement, rowId: string | null) {
  container.querySelectorAll<HTMLElement>(".ag-row[data-drop-target]").forEach((row) => {
    if (row.getAttribute("row-id") !== rowId) row.removeAttribute("data-drop-target");
  });
  if (!rowId) return;
  container.querySelectorAll<HTMLElement>(".ag-row").forEach((row) => {
    if (row.getAttribute("row-id") === rowId) row.setAttribute("data-drop-target", "");
  });
}

function rowIdOf(target: EventTarget | null): string | null {
  const element = target instanceof Element ? target.closest(".ag-row[row-id]") : null;
  return element?.getAttribute("row-id") ?? null;
}

/** The class a drop-target container adds: the row under the drag is highlighted with the accent token. */
export const ROW_DROP_CLASS = "[&_.ag-row[data-drop-target]]:bg-accent! [&_.ag-row[data-drop-target]]:outline-2 [&_.ag-row[data-drop-target]]:-outline-offset-2 [&_.ag-row[data-drop-target]]:outline-primary";

/**
 * Rows of a grid as drop targets for Planboard payloads (dnd.ts): spread the returned handlers on the element around the
 * grid. A drag of a kind in `accept` over a row highlights it; the drop hands the row's id and the payload over. Foreign
 * and malformed drags are ignored.
 */
export function useRowDropTarget({ accept, onDrop, canDrop }: { accept: readonly PlanboardDragKind[]; onDrop: (rowId: string, payload: PlanboardDragPayload) => void; canDrop?: (rowId: string, kind: PlanboardDragKind) => boolean }) {
  const latest = useRef({ accept, onDrop, canDrop });
  latest.current = { accept, onDrop, canDrop };

  const onDragOver = useCallback((event: DragEvent<HTMLElement>) => {
    const { accept: kinds, canDrop: allowed } = latest.current;
    const kind = planboardDragKind(event.dataTransfer.types);
    const rowId = rowIdOf(event.target);
    if (!kind || !kinds.includes(kind) || !rowId || (allowed && !allowed(rowId, kind))) {
      markRow(event.currentTarget, null);
      return;
    }
    event.preventDefault();
    event.dataTransfer.dropEffect = kind === "driver" ? "link" : "move";
    markRow(event.currentTarget, rowId);
  }, []);

  const onDragLeave = useCallback((event: DragEvent<HTMLElement>) => {
    if (event.relatedTarget instanceof Node && event.currentTarget.contains(event.relatedTarget)) return;
    markRow(event.currentTarget, null);
  }, []);

  const onDropEvent = useCallback((event: DragEvent<HTMLElement>) => {
    markRow(event.currentTarget, null);
    const { accept: kinds, onDrop: handle, canDrop: allowed } = latest.current;
    const rowId = rowIdOf(event.target);
    const payload = readDragPayload(event.dataTransfer, kinds);
    if (!rowId || !payload || (allowed && !allowed(rowId, payload.kind))) return;
    event.preventDefault();
    handle(rowId, payload);
  }, []);

  return { onDragOver, onDragLeave, onDrop: onDropEvent };
}

/** A utility column for the drag handle (fixed width, UI-GRID-010's exception for utility columns). */
export const DRAG_COLUMN = { colId: "__drag__", headerName: "", width: 36, minWidth: 36, maxWidth: 36, pinned: "left" as const, sortable: false, resizable: false, suppressMovable: true, context: { export: false } };

/** The warning codes design 68 added (the Transport app's map stops at 11). */
const PLANBOARD_WARNING_CODES: Record<number, string> = { 20: "ResourceDoubleBooked", 21: "ResourceUnavailable", 22: "OutsideWorkingTime" };

export const warningKey = (code: TripWarningDto["code"]) =>
  code != null && PLANBOARD_WARNING_CODES[code] ? `PlanningExecution:TripWarningCode.${PLANBOARD_WARNING_CODES[code]}` : tripWarningKey(code);

/** Minutes the estimated arrival lies after the window's end; null when on time or unknown (exported for tests). */
export function latenessMinutes(estimatedArrival: string | null | undefined, windowTill: string | null | undefined): number | null {
  if (!estimatedArrival || !windowTill) return null;
  const eta = new Date(estimatedArrival).getTime();
  const till = new Date(windowTill).getTime();
  if (Number.isNaN(eta) || Number.isNaN(till) || eta <= till) return null;
  return Math.round((eta - till) / 60_000);
}

/** An ISO instant as its local date (`YYYY-MM-DD`) and time (`HH:mm`). */
export function splitDateTime(value: string | null | undefined): { date: string | null; time: string } {
  if (!value) return { date: null, time: "" };
  const d = new Date(value);
  if (Number.isNaN(d.getTime())) return { date: null, time: "" };
  const pad = (n: number) => String(n).padStart(2, "0");
  return { date: `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`, time: `${pad(d.getHours())}:${pad(d.getMinutes())}` };
}

/** A local date and a typed time (`HH:mm`, empty = midnight) as an ISO instant; null without a date or a valid time. */
export function joinDateTime(date: string | null, time: string): string | null {
  if (!date) return null;
  const parsed = parseTimeOfDay(time.trim() || "00:00");
  if (!parsed) return null;
  const [y, m, d] = date.split("-").map(Number);
  const [hh, mm] = parsed.split(":").map(Number);
  return new Date(y, m - 1, d, hh, mm).toISOString();
}
