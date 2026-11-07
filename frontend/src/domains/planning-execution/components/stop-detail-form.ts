import { joinDateTime } from "@/domains/planning-execution/components/new-trip-form";
import { parseTimeOfDay } from "@/domains/orders/reference-data";
import type { StopActualsDto, TripStopDto, UpdateStopDto } from "@/domains/planning-execution/trips";
import { numberError, numberToText, parseInteger } from "@/lib/forms/use-form-state";

/* The pure part of the stop detail window (design 74 §3): the form's values from a stop, the dirty compare, the validation
 * and the two requests a save may need. The server's rules shape it: `record-stop-actuals` and `UpdateStopDto` treat null
 * as "keep", so a stored actual or planned duration cannot be emptied here, and only what changed is sent. */

export interface StopDetailValues {
  arrivalDate: string | null;
  arrivalTime: string;
  departureDate: string | null;
  departureTime: string;
  remarks: string;
  plannedDurationMin: string;
  relationId: string | null;
  relationName: string | null;
  relationAddressId: string | null;
}

export type StopDetailField = "arrivalDate" | "arrivalTime" | "departureDate" | "departureTime" | "plannedDurationMin";
/** Localization keys per field. */
export type StopDetailErrors = Partial<Record<StopDetailField, string>>;

const CANNOT_BE_EMPTIED = "PlanningExecution:StopValueCannotBeEmptied";

/** An ISO instant as its local date (`YYYY-MM-DD`) and time (`HH:mm`). */
function splitDateTime(value: string | null | undefined): { date: string | null; time: string } {
  if (!value) return { date: null, time: "" };
  const d = new Date(value);
  if (Number.isNaN(d.getTime())) return { date: null, time: "" };
  const pad = (n: number) => String(n).padStart(2, "0");
  return { date: `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`, time: `${pad(d.getHours())}:${pad(d.getMinutes())}` };
}

export function stopDetailValues(stop: TripStopDto): StopDetailValues {
  const arrival = splitDateTime(stop.actualArrival);
  const departure = splitDateTime(stop.actualDeparture);
  return {
    arrivalDate: arrival.date,
    arrivalTime: arrival.time,
    departureDate: departure.date,
    departureTime: departure.time,
    remarks: stop.remarks ?? "",
    plannedDurationMin: numberToText(stop.plannedDurationMin),
    relationId: stop.relationId ?? null,
    relationName: stop.relationName ?? stop.name ?? null,
    relationAddressId: stop.relationAddressId ?? null,
  };
}

const sameActual = (a: StopDetailValues, b: StopDetailValues, kind: "arrival" | "departure") =>
  kind === "arrival" ? a.arrivalDate === b.arrivalDate && a.arrivalTime.trim() === b.arrivalTime.trim() : a.departureDate === b.departureDate && a.departureTime.trim() === b.departureTime.trim();

/** UI-FORM-009: the values against the ones the form opened with (empty text and null are the same; the relation's name follows its id). */
export function isStopDetailDirty(initial: StopDetailValues, values: StopDetailValues): boolean {
  return (
    !sameActual(initial, values, "arrival") ||
    !sameActual(initial, values, "departure") ||
    initial.remarks.trim() !== values.remarks.trim() ||
    initial.plannedDurationMin.trim() !== values.plannedDurationMin.trim() ||
    initial.relationId !== values.relationId ||
    initial.relationAddressId !== values.relationAddressId
  );
}

/** The errors of the form; empty when it may be saved. */
export function validateStopDetail(initial: StopDetailValues, values: StopDetailValues): StopDetailErrors {
  const errors: StopDetailErrors = {};
  const time = (date: string | null, text: string, dateField: StopDetailField, timeField: StopDetailField, stored: string | null) => {
    if (text.trim() && !parseTimeOfDay(text)) errors[timeField] = "Orders:InvalidTime";
    if (text.trim() && !date) errors[dateField] = "MasterData:Required";
    /* A date without a time would silently record midnight. */
    if (date && !text.trim()) errors[timeField] = "MasterData:Required";
    if (!date && !text.trim() && stored) errors[dateField] = CANNOT_BE_EMPTIED;
  };
  time(values.arrivalDate, values.arrivalTime, "arrivalDate", "arrivalTime", initial.arrivalDate);
  time(values.departureDate, values.departureTime, "departureDate", "departureTime", initial.departureDate);

  const duration = numberError(values.plannedDurationMin, { integer: true, min: 0 });
  if (duration) errors.plannedDurationMin = duration;
  else if (!values.plannedDurationMin.trim() && initial.plannedDurationMin.trim()) errors.plannedDurationMin = CANNOT_BE_EMPTIED;
  return errors;
}

/** The stop's update (position stays), or null when nothing it carries changed. A relation or address goes along only when it changed: the server refuses them on a stop with legs. */
export function stopUpdateDto(initial: StopDetailValues, values: StopDetailValues): UpdateStopDto | null {
  const dto: UpdateStopDto = {};
  if (initial.remarks.trim() !== values.remarks.trim()) dto.remarks = values.remarks.trim();
  if (initial.plannedDurationMin.trim() !== values.plannedDurationMin.trim()) dto.plannedDurationMin = parseInteger(values.plannedDurationMin);
  if (initial.relationId !== values.relationId) dto.relationId = values.relationId;
  if (initial.relationAddressId !== values.relationAddressId || initial.relationId !== values.relationId) dto.relationAddressId = values.relationAddressId;
  return Object.keys(dto).length > 0 ? dto : null;
}

/** The actual times to record, or null when neither changed. Null keeps a time on the server, so an unchanged one keeps its seconds. */
export function stopActualsDto(initial: StopDetailValues, values: StopDetailValues): StopActualsDto | null {
  const arrivalChanged = !sameActual(initial, values, "arrival");
  const departureChanged = !sameActual(initial, values, "departure");
  if (!arrivalChanged && !departureChanged) return null;
  return {
    actualArrival: arrivalChanged ? joinDateTime(values.arrivalDate, values.arrivalTime) : null,
    actualDeparture: departureChanged ? joinDateTime(values.departureDate, values.departureTime) : null,
    cumulativeDistanceM: null,
  };
}

/** The stop's map position, or null without usable coordinates. */
export function stopCoordinates(stop: Pick<TripStopDto, "latitude" | "longitude">): { latitude: number; longitude: number } | null {
  const { latitude, longitude } = stop;
  if (latitude == null || longitude == null || !Number.isFinite(latitude) || !Number.isFinite(longitude)) return null;
  if (Math.abs(latitude) > 90 || Math.abs(longitude) > 180 || (latitude === 0 && longitude === 0)) return null;
  return { latitude, longitude };
}
