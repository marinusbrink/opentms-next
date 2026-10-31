/* Drag and drop between Planboard panes (design 70, "Drag and drop"): a typed payload per MIME type. Legs travel from
 * Legs to schedule / Legs onto a trips-pane row, a Scheduler row or bar, or the map's selected trip; a driver from
 * Resources onto a trip row or a bar; a trip from the trips pane or a bar. The payload carries ids only.
 *
 * During `dragover` the browser hides the data and shows only the types, so a drop target decides with
 * `planboardDragKind(dataTransfer.types)` and reads the payload with `readDragPayload` in `drop`. Parsing is safe: a
 * foreign or malformed payload yields null, never an exception. */

export const DND_LEGS = "application/x-opentms-legs";
export const DND_DRIVER = "application/x-opentms-driver";
export const DND_TRIP = "application/x-opentms-trip";

/** The most legs one drag may carry (a multi-selection in Legs to schedule); more is treated as malformed. */
export const MAX_DRAGGED_LEGS = 500;

export interface LegsDragPayload {
  kind: "legs";
  legIds: string[];
  /** The trip the legs come from (Legs pane), so a drop on another trip means "move", not "assign". */
  fromTripId?: string | null;
}

export interface DriverDragPayload {
  kind: "driver";
  driverId: string;
}

export interface TripDragPayload {
  kind: "trip";
  tripId: string;
}

export type PlanboardDragPayload = LegsDragPayload | DriverDragPayload | TripDragPayload;
export type PlanboardDragKind = PlanboardDragPayload["kind"];

const MIME_BY_KIND: Record<PlanboardDragKind, string> = { legs: DND_LEGS, driver: DND_DRIVER, trip: DND_TRIP };
const KIND_BY_MIME: Record<string, PlanboardDragKind> = { [DND_LEGS]: "legs", [DND_DRIVER]: "driver", [DND_TRIP]: "trip" };

export function mimeTypeOf(kind: PlanboardDragKind): string {
  return MIME_BY_KIND[kind];
}

const isId = (value: unknown): value is string => typeof value === "string" && value.trim().length > 0 && value.length <= 64;

/** The payload as the string stored under its MIME type. */
export function encodeDragPayload(payload: PlanboardDragPayload): string {
  switch (payload.kind) {
    case "legs":
      return JSON.stringify({ legIds: payload.legIds, fromTripId: payload.fromTripId ?? null });
    case "driver":
      return JSON.stringify({ driverId: payload.driverId });
    case "trip":
      return JSON.stringify({ tripId: payload.tripId });
  }
}

/** Parses the string stored under `mimeType`; null when the type is not ours or the content is malformed. */
export function decodeDragPayload(mimeType: string, raw: string | null | undefined): PlanboardDragPayload | null {
  const kind = KIND_BY_MIME[mimeType];
  if (!kind || !raw) return null;
  let value: unknown;
  try {
    value = JSON.parse(raw);
  } catch {
    return null;
  }
  if (value == null || typeof value !== "object" || Array.isArray(value)) return null;
  const data = value as Record<string, unknown>;
  switch (kind) {
    case "legs": {
      const legIds = data.legIds;
      if (!Array.isArray(legIds) || legIds.length === 0 || legIds.length > MAX_DRAGGED_LEGS || !legIds.every(isId)) return null;
      const fromTripId = isId(data.fromTripId) ? data.fromTripId : null;
      return { kind, legIds: [...new Set(legIds)], fromTripId };
    }
    case "driver":
      return isId(data.driverId) ? { kind, driverId: data.driverId } : null;
    case "trip":
      return isId(data.tripId) ? { kind, tripId: data.tripId } : null;
  }
}

/** Puts the payload on a drag that starts (`dragstart`); the effect is "move" for legs and trips, "link" for a driver. */
export function setDragPayload(dataTransfer: DataTransfer, payload: PlanboardDragPayload): void {
  dataTransfer.setData(mimeTypeOf(payload.kind), encodeDragPayload(payload));
  dataTransfer.effectAllowed = payload.kind === "driver" ? "link" : "move";
}

/** The kind of Planboard payload a drag carries, from its types alone (usable in `dragover`); null for foreign drags. */
export function planboardDragKind(types: readonly string[] | DOMStringList | null | undefined): PlanboardDragKind | null {
  if (!types) return null;
  for (const type of Array.from(types as ArrayLike<string>)) {
    const kind = KIND_BY_MIME[type];
    if (kind) return kind;
  }
  return null;
}

/** Reads the payload in `drop`; null for foreign or malformed drags. `accept` limits the kinds the target takes. */
export function readDragPayload(
  dataTransfer: Pick<DataTransfer, "types" | "getData"> | null | undefined,
  accept?: readonly PlanboardDragKind[],
): PlanboardDragPayload | null {
  if (!dataTransfer) return null;
  const kind = planboardDragKind(dataTransfer.types);
  if (!kind || (accept && !accept.includes(kind))) return null;
  const mime = mimeTypeOf(kind);
  let raw: string;
  try {
    raw = dataTransfer.getData(mime);
  } catch {
    return null;
  }
  return decodeDragPayload(mime, raw);
}
