import type { DriverOption, RelationOption, VehicleOption } from "@/domains/master-data/entities";
import type { TransportTripTypeDto } from "@/domains/planning-execution/reference-data";
import type { FirstLastStopRule, StopCompositionMode, TripExecution } from "@/domains/planning-execution/trips";

/* The trip type's defaults as a screen presets them (design 61 "Trip type defaults" 5): choosing a type presets every
 * field the type defines; switching to another type changes the fields that still hold the previous type's preset and
 * keeps the fields the user changed (each field compared with the previous type's preset). The New trip dialog and the
 * Tevens trip of an order or a template use these pure functions; the server fills whatever stays empty (point 2).
 *
 * Groups follow the server's rules: a contact belongs to its carrier and an address to its relation, so a carrier or
 * relation the switch replaces takes the new type's contact or address with it; vehicles and combination are one
 * source (either a combination or vehicles), switched together. */

/** MaxVehiclesPerTrip and MaxDriversPerTrip (design 59). */
export const MAX_TRIP_VEHICLES = 3;
export const MAX_TRIP_DRIVERS = 3;

/** The fields a type presets, under the names the forms use. Absent enum fields (the Tevens trip has none) are skipped. */
export interface TripPresetFields {
  carrierRelation: RelationOption | null;
  carrierContactPersonId: string | null;
  vehicleCombinationId: string | null;
  vehicleIds: string[];
  driverIds: string[];
  departureRelation: RelationOption | null;
  departureAddressId: string | null;
  /** `HH:mm`, "" when none. */
  departureTime: string;
  arrivalRelation: RelationOption | null;
  arrivalAddressId: string | null;
  execution?: TripExecution | null;
  stopCompositionMode?: StopCompositionMode | null;
  firstLastStopRule?: FirstLastStopRule | null;
}

/** A type's preset: the fields plus the names the pickers show for the preset records (no extra lookups). */
export interface TripTypePreset extends TripPresetFields {
  execution: TripExecution | null;
  stopCompositionMode: StopCompositionMode | null;
  firstLastStopRule: FirstLastStopRule | null;
  vehicleCombinationName: string | null;
  vehicles: VehicleOption[];
  drivers: DriverOption[];
}

/** No type (or one that defines nothing): every field empty. A first choice compares with this. */
export const NO_TRIP_TYPE_PRESET: TripTypePreset = {
  carrierRelation: null,
  carrierContactPersonId: null,
  vehicleCombinationId: null,
  vehicleIds: [],
  driverIds: [],
  departureRelation: null,
  departureAddressId: null,
  departureTime: "",
  arrivalRelation: null,
  arrivalAddressId: null,
  execution: null,
  stopCompositionMode: null,
  firstLastStopRule: null,
  vehicleCombinationName: null,
  vehicles: [],
  drivers: [],
};

const relationOf = (id: string | null | undefined, name: string | null | undefined): RelationOption | null =>
  id ? { id, code: "", name: name ?? "", city: null, countryCode: null, roles: 0 } : null;

/** The type's detail (with the names) as a preset; a missing detail is no preset. */
export function tripTypePresetOf(dto: TransportTripTypeDto | null | undefined): TripTypePreset {
  if (!dto) return NO_TRIP_TYPE_PRESET;
  const vehicles: VehicleOption[] = (dto.defaultVehicles ?? [])
    .filter((v) => v.id)
    .map((v) => ({ id: v.id!, name: v.name ?? "", licencePlate: v.licencePlate ?? null, kind: v.kind ?? 1, vehicleTypeName: v.vehicleTypeName ?? null }));
  const drivers: DriverOption[] = (dto.defaultDrivers ?? []).filter((d) => d.id).map((d) => ({ id: d.id!, name: d.name ?? "", code: "" }));
  const carrierRelation = relationOf(dto.defaultCarrierRelationId, dto.defaultCarrierRelationName);
  const departureRelation = relationOf(dto.defaultDepartureRelationId, dto.defaultDepartureRelationName);
  const arrivalRelation = relationOf(dto.defaultArrivalRelationId, dto.defaultArrivalRelationName);
  const vehicleCombinationId = dto.defaultVehicleCombinationId ?? null;
  return {
    carrierRelation,
    carrierContactPersonId: carrierRelation ? (dto.defaultCarrierContactPersonId ?? null) : null,
    vehicleCombinationId,
    vehicleIds: vehicleCombinationId ? [] : (dto.defaultVehicleIds ?? vehicles.map((v) => v.id)).slice(0, MAX_TRIP_VEHICLES),
    driverIds: (dto.defaultDriverIds ?? drivers.map((d) => d.id)).slice(0, MAX_TRIP_DRIVERS),
    departureRelation,
    departureAddressId: departureRelation ? (dto.defaultDepartureAddressId ?? null) : null,
    departureTime: timeText(dto.defaultDepartureTime),
    arrivalRelation,
    arrivalAddressId: arrivalRelation ? (dto.defaultArrivalAddressId ?? null) : null,
    /* A carrier from the type subcontracts the trip, as the server's fill does (design 61 point 2, PO 2026-10-29). */
    execution: carrierRelation ? 2 : (dto.defaultExecution ?? null),
    stopCompositionMode: dto.defaultStopCompositionMode ?? null,
    firstLastStopRule: dto.defaultFirstLastStopRule ?? null,
    vehicleCombinationName: dto.defaultVehicleCombinationName ?? null,
    vehicles,
    drivers,
  };
}

/** "07:30:00" → "07:30"; null → "". */
export function timeText(value: string | null | undefined): string {
  return value ? value.slice(0, 5) : "";
}

const idOf = (relation: RelationOption | null | undefined) => relation?.id?.toLowerCase() ?? null;
const sameRelation = (a: RelationOption | null | undefined, b: RelationOption | null | undefined) => idOf(a) === idOf(b);
const sameId = (a: string | null | undefined, b: string | null | undefined) => (a?.toLowerCase() || null) === (b?.toLowerCase() || null);
const sameIds = (a: readonly string[], b: readonly string[]) => a.length === b.length && a.every((id, i) => sameId(id, b[i]));

/** A relation and the record that belongs to it (a carrier's contact, a relation's address), switched as a pair. */
function switchPair(
  current: { relation: RelationOption | null; child: string | null },
  previous: { relation: RelationOption | null; child: string | null },
  next: { relation: RelationOption | null; child: string | null },
): { relation: RelationOption | null; child: string | null } {
  const relation = sameRelation(current.relation, previous.relation) ? next.relation : current.relation;
  /* The relation changed: what belonged to the old one goes; the new type's child comes along when the relation is its. */
  if (!sameRelation(relation, current.relation)) return { relation, child: sameRelation(relation, next.relation) ? next.child : null };
  const childHeld = sameId(current.child, previous.child);
  return { relation, child: childHeld && sameRelation(relation, next.relation) ? next.child : current.child };
}

/**
 * Switches the preset from `previous` to `next`: every field that still holds `previous`'s value takes `next`'s; a field
 * the user changed stays. A first choice passes `NO_TRIP_TYPE_PRESET` as `previous`, so only empty fields are filled.
 */
export function switchTripTypePreset<T extends TripPresetFields>(current: T, previous: TripPresetFields, next: TripPresetFields): T {
  const result: T = { ...current };
  const carrier = switchPair(
    { relation: current.carrierRelation, child: current.carrierContactPersonId },
    { relation: previous.carrierRelation, child: previous.carrierContactPersonId },
    { relation: next.carrierRelation, child: next.carrierContactPersonId },
  );
  result.carrierRelation = carrier.relation;
  result.carrierContactPersonId = carrier.child;
  if (sameId(current.vehicleCombinationId, previous.vehicleCombinationId) && sameIds(current.vehicleIds, previous.vehicleIds)) {
    result.vehicleCombinationId = next.vehicleCombinationId;
    result.vehicleIds = [...next.vehicleIds];
  }
  if (sameIds(current.driverIds, previous.driverIds)) result.driverIds = [...next.driverIds];
  const departure = switchPair(
    { relation: current.departureRelation, child: current.departureAddressId },
    { relation: previous.departureRelation, child: previous.departureAddressId },
    { relation: next.departureRelation, child: next.departureAddressId },
  );
  result.departureRelation = departure.relation;
  result.departureAddressId = departure.child;
  if (current.departureTime.trim() === previous.departureTime.trim()) result.departureTime = next.departureTime;
  const arrival = switchPair(
    { relation: current.arrivalRelation, child: current.arrivalAddressId },
    { relation: previous.arrivalRelation, child: previous.arrivalAddressId },
    { relation: next.arrivalRelation, child: next.arrivalAddressId },
  );
  result.arrivalRelation = arrival.relation;
  result.arrivalAddressId = arrival.child;
  for (const key of ["execution", "stopCompositionMode", "firstLastStopRule"] as const) {
    if (key in current && (current[key] ?? null) === (previous[key] ?? null)) (result as TripPresetFields)[key] = (next[key] ?? null) as never;
  }
  return result;
}

/** The fields only, from any form values or preset (for comparing and switching). */
export function presetFieldsOf(values: TripPresetFields): TripPresetFields {
  return {
    carrierRelation: values.carrierRelation,
    carrierContactPersonId: values.carrierContactPersonId,
    vehicleCombinationId: values.vehicleCombinationId,
    vehicleIds: values.vehicleIds,
    driverIds: values.driverIds,
    departureRelation: values.departureRelation,
    departureAddressId: values.departureAddressId,
    departureTime: values.departureTime,
    arrivalRelation: values.arrivalRelation,
    arrivalAddressId: values.arrivalAddressId,
    ...("execution" in values ? { execution: values.execution ?? null } : {}),
    ...("stopCompositionMode" in values ? { stopCompositionMode: values.stopCompositionMode ?? null } : {}),
    ...("firstLastStopRule" in values ? { firstLastStopRule: values.firstLastStopRule ?? null } : {}),
  };
}

/** Options known by id, the later ones winning (a picker's selection shown with its names). */
export function mergeOptions<T extends { id: string }>(...lists: readonly (readonly T[])[]): T[] {
  const byId = new Map<string, T>();
  for (const list of lists) for (const item of list) byId.set(item.id.toLowerCase(), item);
  return [...byId.values()];
}

/** The ids as the pickers' options, by the known names (an unknown id shows as itself until it is picked again). */
export function optionsOfIds<T extends { id: string }>(ids: readonly string[], known: readonly T[], fallback: (id: string) => T): T[] {
  return ids.map((id) => known.find((k) => sameId(k.id, id)) ?? fallback(id));
}
