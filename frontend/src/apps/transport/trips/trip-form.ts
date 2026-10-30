import { relationOptionOf } from "@/domains/master-data/components/RelationFields";
import { splitDateTime } from "@/apps/transport/orders/order-form";
import type { RelationOption } from "@/domains/master-data/entities";
import type { FirstLastStopRule, StopCompositionMode, TransportTripDto, TripExecution } from "@/domains/planning-execution/trips";
import { parseTimeOfDay } from "@/domains/orders/reference-data";
import { numberError, numberToText, type FieldErrors } from "@/lib/forms/use-form-state";

/* The form model of the trip's header (design 59 `UpdateHeader`; design 61 General tab). Carrier, vehicles and
 * drivers are commands of their own and live outside this form. */

export interface TripValues {
  tripDate: string | null;
  name: string;
  execution: TripExecution;
  stopCompositionMode: StopCompositionMode;
  firstLastStopRule: FirstLastStopRule;
  departureRelation: RelationOption | null;
  departureAddressId: string | null;
  arrivalRelation: RelationOption | null;
  arrivalAddressId: string | null;
  plannedDepartureDate: string | null;
  plannedDepartureTime: string;
  plannedArrivalDate: string | null;
  plannedArrivalTime: string;
  actualDistanceKm: string;
  organizationUnitId: string | null;
  remarks: string;
  driverInstructions: string;
  attributes: Record<string, string>;
}

export function tripValuesOf(trip: TransportTripDto): TripValues {
  const departure = splitDateTime(trip.plannedDeparture);
  const arrival = splitDateTime(trip.plannedArrival);
  return {
    tripDate: trip.tripDate ?? null,
    name: trip.name ?? "",
    execution: trip.execution ?? 1,
    stopCompositionMode: trip.stopCompositionMode ?? 1,
    firstLastStopRule: trip.firstLastStopRule ?? 0,
    departureRelation: relationOptionOf(trip.departureRelationId, trip.departureRelationName),
    departureAddressId: trip.departureAddressId ?? null,
    arrivalRelation: relationOptionOf(trip.arrivalRelationId, trip.arrivalRelationName),
    arrivalAddressId: trip.arrivalAddressId ?? null,
    plannedDepartureDate: departure.date,
    plannedDepartureTime: departure.time,
    plannedArrivalDate: arrival.date,
    plannedArrivalTime: arrival.time,
    actualDistanceKm: trip.actualDistanceM == null ? "" : numberToText(Math.round(trip.actualDistanceM / 1000)),
    organizationUnitId: trip.organizationUnitId ?? null,
    remarks: trip.remarks ?? "",
    driverInstructions: trip.driverInstructions ?? "",
    attributes: trip.attributes ?? {},
  };
}

export function validateTrip(v: TripValues): FieldErrors<TripValues> {
  const errors: FieldErrors<TripValues> = {};
  if (!v.tripDate) errors.tripDate = "MasterData:Required";
  if (!v.plannedDepartureDate) errors.plannedDepartureDate = "MasterData:Required";
  if (v.plannedDepartureTime.trim() && !parseTimeOfDay(v.plannedDepartureTime)) errors.plannedDepartureTime = "Orders:InvalidTime";
  if (v.plannedArrivalTime.trim() && !parseTimeOfDay(v.plannedArrivalTime)) errors.plannedArrivalTime = "Orders:InvalidTime";
  if (numberError(v.actualDistanceKm, { integer: true, min: 0 })) errors.actualDistanceKm = "MasterData:InvalidNumber";
  return errors;
}
