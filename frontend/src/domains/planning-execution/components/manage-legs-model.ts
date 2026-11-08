import type { RelationOption } from "@/domains/master-data/entities";
import { joinDateTime } from "@/domains/planning-execution/components/new-trip-form";
import { parseTimeOfDay } from "@/domains/orders/reference-data";
import type { LegChainDto, LegChainEndInputDto, LegChainUpdateDto, LegEndSnapshotDto } from "@/domains/planning-execution/legs";

/* The pure part of Manage legs (design 74 §4): the line's legs as rows, and the rules legacy applied in the screen made
 * correct. The server remains the validator of the chain (contiguous, dates forward, required values): these functions keep
 * the form consistent as the planner edits it, they do not decide what may be saved.
 *
 * - The first row's origin and the last row's destination (activity, relation, address) are read-only.
 * - A leg on a trip or with actuals is read-only as a whole; adding and removing around it are refused when they would change
 *   its ends, and a neighbour's end that must equal one of its ends is read-only too.
 * - Add appends: the new row ends where the old last row ended and starts with the successor of that activity, without a
 *   relation; the old last row's destination is cleared until a relation is chosen.
 * - Remove keeps at least one row: the first or a middle row's next row takes its origin, the last row's previous row takes
 *   its destination.
 * - A destination relation sets the next row's origin (relation, address, date, successor activity); an origin relation sets
 *   the previous row's destination. */

export type ChainSide = "origin" | "destination";

export interface ChainEnd {
  waypointTypeId: string | null;
  relation: RelationOption | null;
  relationAddressId: string | null;
  plannedDate: string | null;
  windowFromDate: string | null;
  windowFromTime: string;
  windowTillDate: string | null;
  windowTillTime: string;
  reference: string;
}

export interface ChainRow {
  /** Stable for React: the leg's id, or a generated key for a new row. */
  key: string;
  /** Null: a new leg. */
  id: string | null;
  readOnly: boolean;
  tripNumber: string | null;
  hasActuals: boolean;
  remarks: string;
  origin: ChainEnd;
  destination: ChainEnd;
  /** Calculated by routing; shown read-only. */
  distanceM: number | null;
  durationS: number | null;
}

/** The activity after an activity (design 51); null when it has none, and the activity itself then follows. */
export type SuccessorOf = (waypointTypeId: string | null | undefined) => string | null;

/** An ISO instant as its local date and time; the end's windows are edited in the browser's time zone, as elsewhere. */
export function splitLocal(value: string | null | undefined): { date: string | null; time: string } {
  if (!value) return { date: null, time: "" };
  const d = new Date(value);
  if (Number.isNaN(d.getTime())) return { date: null, time: "" };
  const pad = (n: number) => String(n).padStart(2, "0");
  return { date: `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`, time: `${pad(d.getHours())}:${pad(d.getMinutes())}` };
}

function endOf(end: LegEndSnapshotDto | undefined): ChainEnd {
  const from = splitLocal(end?.windowFrom);
  const till = splitLocal(end?.windowTill);
  return {
    waypointTypeId: end?.waypointTypeId ?? null,
    relation: end?.relationId ? { id: end.relationId, code: "", name: end.name ?? "", city: end.city ?? null, countryCode: end.countryCode ?? null, roles: 0 } : null,
    relationAddressId: end?.relationAddressId ?? null,
    plannedDate: end?.plannedDate ?? null,
    windowFromDate: from.date,
    windowFromTime: from.time,
    windowTillDate: till.date,
    windowTillTime: till.time,
    reference: end?.reference ?? "",
  };
}

/** The line's legs as rows, in order. */
export function chainRowsOf(chain: LegChainDto): ChainRow[] {
  return (chain.rows ?? []).map((row, index) => ({
    key: row.leg?.id ?? `row-${index}`,
    id: row.leg?.id ?? null,
    readOnly: row.isReadOnly === true,
    tripNumber: row.leg?.tripNumber ?? null,
    hasActuals: row.hasActuals === true,
    remarks: row.leg?.remarks ?? "",
    origin: endOf(row.leg?.origin),
    destination: endOf(row.leg?.destination),
    distanceM: row.leg?.distanceM ?? null,
    durationS: row.leg?.durationS ?? null,
  }));
}

const withRow = (rows: readonly ChainRow[], index: number, patch: Partial<ChainRow>) => rows.map((row, i) => (i === index ? { ...row, ...patch } : row));
const place = (end: ChainEnd) => ({ relation: end.relation, relationAddressId: end.relationAddressId });

/** Whether an end's place (activity, relation, address) may change: see the file comment. */
export function isPlaceLocked(rows: readonly ChainRow[], index: number, side: ChainSide): boolean {
  if (rows[index]?.readOnly) return true;
  if (side === "origin") return index === 0 || rows[index - 1]?.readOnly === true;
  return index === rows.length - 1 || rows[index + 1]?.readOnly === true;
}

/** Add changes the last row's destination, so a read-only last row refuses it. */
export function canAddLeg(rows: readonly ChainRow[]): boolean {
  return rows.length > 0 && !rows[rows.length - 1].readOnly;
}

export function addLeg(rows: readonly ChainRow[], successorOf: SuccessorOf, newKey: string): ChainRow[] {
  if (!canAddLeg(rows)) return [...rows];
  const lastIndex = rows.length - 1;
  const last = rows[lastIndex];
  const end = last.destination;
  const added: ChainRow = {
    key: newKey,
    id: null,
    readOnly: false,
    tripNumber: null,
    hasActuals: false,
    remarks: "",
    origin: {
      waypointTypeId: successorOf(end.waypointTypeId) ?? end.waypointTypeId,
      relation: null,
      relationAddressId: null,
      plannedDate: end.plannedDate,
      windowFromDate: null,
      windowFromTime: "",
      windowTillDate: null,
      windowTillTime: "",
      reference: "",
    },
    destination: { ...end },
    distanceM: null,
    durationS: null,
  };
  const cleared: ChainEnd = { ...end, relation: null, relationAddressId: null, windowFromDate: null, windowFromTime: "", windowTillDate: null, windowTillTime: "", reference: "" };
  return [...withRow(rows, lastIndex, { destination: cleared }), added];
}

/** Remove keeps at least one row, never a read-only one, and never changes a read-only neighbour's end. */
export function canRemoveLeg(rows: readonly ChainRow[], index: number): boolean {
  if (rows.length <= 1 || !rows[index] || rows[index].readOnly) return false;
  const heir = index === rows.length - 1 ? rows[index - 1] : rows[index + 1];
  return !heir.readOnly;
}

export function removeLeg(rows: readonly ChainRow[], index: number): ChainRow[] {
  if (!canRemoveLeg(rows, index)) return [...rows];
  const removed = rows[index];
  const next = index === rows.length - 1 ? withRow(rows, index - 1, { destination: { ...removed.destination } }) : withRow(rows, index + 1, { origin: { ...removed.origin } });
  return next.filter((_, i) => i !== index);
}

/** A relation chosen on an end: the address starts empty (the picker preselects the default) and the neighbour follows. */
export function setRelation(rows: readonly ChainRow[], index: number, side: ChainSide, relation: RelationOption | null, successorOf: SuccessorOf): ChainRow[] {
  const row = rows[index];
  if (!row) return [...rows];
  const changed = { ...row[side], relation, relationAddressId: null };
  const next = withRow(rows, index, { [side]: changed });
  if (side === "destination") {
    const follower = next[index + 1];
    if (!follower || follower.readOnly) return next;
    return withRow(next, index + 1, {
      origin: { ...follower.origin, ...place(changed), plannedDate: changed.plannedDate ?? follower.origin.plannedDate, waypointTypeId: successorOf(changed.waypointTypeId) ?? changed.waypointTypeId ?? follower.origin.waypointTypeId },
    });
  }
  const previous = next[index - 1];
  if (!previous || previous.readOnly) return next;
  return withRow(next, index - 1, { destination: { ...previous.destination, ...place(changed) } });
}

/** An address chosen on an end: the same place stays on both sides of a joint, or the chain would not be contiguous. */
export function setAddress(rows: readonly ChainRow[], index: number, side: ChainSide, relationAddressId: string | null): ChainRow[] {
  const row = rows[index];
  if (!row) return [...rows];
  const next = withRow(rows, index, { [side]: { ...row[side], relationAddressId } });
  const neighbourIndex = side === "destination" ? index + 1 : index - 1;
  const neighbour = next[neighbourIndex];
  if (!neighbour || neighbour.readOnly) return next;
  const neighbourSide: ChainSide = side === "destination" ? "origin" : "destination";
  return withRow(next, neighbourIndex, { [neighbourSide]: { ...neighbour[neighbourSide], relationAddressId } });
}

export function setEndField(rows: readonly ChainRow[], index: number, side: ChainSide, patch: Partial<ChainEnd>): ChainRow[] {
  const row = rows[index];
  return row ? withRow(rows, index, { [side]: { ...row[side], ...patch } }) : [...rows];
}

export function setRemarks(rows: readonly ChainRow[], index: number, remarks: string): ChainRow[] {
  return withRow(rows, index, { remarks });
}

/** A window's date and time as an instant; null without a date. */
function windowInstant(date: string | null, time: string): string | null {
  return date ? joinDateTime(date, time) : null;
}

/** The keys of what the form can tell is missing or unreadable: `<row key>.<side>.<field>`. The rest is the server's. */
export function chainErrors(rows: readonly ChainRow[]): Set<string> {
  const errors = new Set<string>();
  for (const row of rows) {
    for (const side of ["origin", "destination"] as const) {
      const end = row[side];
      const key = `${row.key}.${side}`;
      if (!end.waypointTypeId) errors.add(`${key}.waypointTypeId`);
      if (!end.relation) errors.add(`${key}.relation`);
      if (!end.plannedDate) errors.add(`${key}.plannedDate`);
      if (end.windowFromTime.trim() && !parseTimeOfDay(end.windowFromTime)) errors.add(`${key}.windowFromTime`);
      if (end.windowTillTime.trim() && !parseTimeOfDay(end.windowTillTime)) errors.add(`${key}.windowTillTime`);
    }
  }
  return errors;
}

function endInput(end: ChainEnd): LegChainEndInputDto {
  return {
    waypointTypeId: end.waypointTypeId ?? undefined,
    relationId: end.relation?.id,
    relationAddressId: end.relationAddressId,
    plannedDate: end.plannedDate ?? undefined,
    windowFrom: windowInstant(end.windowFromDate, end.windowFromTime),
    windowTill: windowInstant(end.windowTillDate, end.windowTillTime),
    reference: end.reference.trim() || null,
  };
}

/** The whole line as the server wants it, with the stamp of every leg as it was read. */
export function chainUpdateOf(rows: readonly ChainRow[], chain: LegChainDto): LegChainUpdateDto {
  return {
    rows: rows.map((row) => ({ id: row.id, remarks: row.remarks.trim() || null, origin: endInput(row.origin), destination: endInput(row.destination) })),
    stamps: (chain.rows ?? []).filter((row) => row.leg?.id).map((row) => ({ id: row.leg!.id!, concurrencyStamp: row.leg!.concurrencyStamp ?? "" })),
  };
}
