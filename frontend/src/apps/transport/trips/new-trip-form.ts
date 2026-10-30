import { joinDateTime } from "@/apps/transport/orders/order-form";
import { parseTimeOfDay } from "@/domains/orders/reference-data";
import type { TripPresetFields } from "@/domains/planning-execution/trip-type-presets";
import type { FirstLastStopRule, StopCompositionMode, TransportTripCreateDto, TripExecution } from "@/domains/planning-execution/trips";
import type { FieldErrors } from "@/lib/forms/use-form-state";

/* The form model of the New trip dialog (design 61 "Trip type defaults" 4): trip type, trip date, planned departure
 * (date and time), name, execution, carrier and contact, combination or vehicles, drivers, departure and arrival
 * (relation and address), stop composition mode and first/last stop rule. The type's defaults preset the fields
 * (`switchTripTypePreset`, point 5); an empty field is left to the server, which fills it from the type (point 2). */

export interface NewTripValues extends TripPresetFields {
  transportTripTypeId: string | null;
  /** `YYYY-MM-DD`. */
  tripDate: string | null;
  /** The planned departure's date (`YYYY-MM-DD`); `departureTime` is its time (`HH:mm`, as typed). */
  departureDate: string | null;
  name: string;
  execution: TripExecution | null;
  stopCompositionMode: StopCompositionMode | null;
  firstLastStopRule: FirstLastStopRule | null;
}

/** A new trip on `today`: the planned departure on the trip date, everything else empty until the type presets it. */
export function newTripValues(today: string): NewTripValues {
  return {
    transportTripTypeId: null,
    tripDate: today,
    departureDate: today,
    departureTime: "",
    name: "",
    execution: null,
    carrierRelation: null,
    carrierContactPersonId: null,
    vehicleCombinationId: null,
    vehicleIds: [],
    driverIds: [],
    departureRelation: null,
    departureAddressId: null,
    arrivalRelation: null,
    arrivalAddressId: null,
    stopCompositionMode: null,
    firstLastStopRule: null,
  };
}

/** Another trip date: the planned departure's date follows while it is empty or still the old trip date (the type's
 * departure time is combined with the trip date, point 5); a date the user chose stays. */
export function withTripDate(values: NewTripValues, tripDate: string | null): NewTripValues {
  const follows = !values.departureDate || values.departureDate === values.tripDate;
  return { ...values, tripDate, departureDate: follows ? tripDate : values.departureDate };
}

export function validateNewTrip(v: NewTripValues): FieldErrors<NewTripValues> {
  const errors: FieldErrors<NewTripValues> = {};
  if (!v.transportTripTypeId) errors.transportTripTypeId = "MasterData:Required";
  if (!v.tripDate) errors.tripDate = "MasterData:Required";
  if (!v.departureDate) errors.departureDate = "MasterData:Required";
  if (!v.departureTime.trim()) errors.departureTime = "MasterData:Required";
  else if (!parseTimeOfDay(v.departureTime)) errors.departureTime = "Orders:InvalidTime";
  return errors;
}

/** The create input; an empty field is sent empty so the server fills it from the type (a contact without its carrier,
 * an address without its relation and vehicles beside a combination are never sent). */
export function newTripCreateDtoOf(v: NewTripValues): TransportTripCreateDto {
  return {
    transportTripTypeId: v.transportTripTypeId ?? "",
    tripDate: v.tripDate,
    plannedDeparture: joinDateTime(v.departureDate, v.departureTime) ?? "",
    name: v.name.trim() || null,
    execution: v.execution ?? undefined,
    carrierRelationId: v.carrierRelation?.id ?? null,
    carrierContactPersonId: v.carrierRelation ? v.carrierContactPersonId : null,
    vehicleCombinationId: v.vehicleCombinationId,
    /* The server needs the lists; an empty one is filled from the type. */
    vehicleIds: v.vehicleCombinationId ? [] : v.vehicleIds,
    driverIds: v.driverIds,
    departureRelationId: v.departureRelation?.id ?? null,
    departureAddressId: v.departureRelation ? v.departureAddressId : null,
    arrivalRelationId: v.arrivalRelation?.id ?? null,
    arrivalAddressId: v.arrivalRelation ? v.arrivalAddressId : null,
    stopCompositionMode: v.stopCompositionMode ?? undefined,
    firstLastStopRule: v.firstLastStopRule ?? undefined,
  };
}
