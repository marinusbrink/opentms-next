import type { LegDto } from "@/domains/planning-execution/legs";
import { PLANBOARD_STUCK_REASON, type PlanboardAssignOrderPreviewDto, type PlanboardOrderLegDto, type PlanboardPulledFromDto } from "@/domains/planning-execution/planboard-lists";

/* The pure part of dropping an order on a trip (design 74 §8): what a preview means for the flow, and the texts' parts. */

export type OrderDropPlan =
  /** Nothing can move; `reasonKey` says why and `stuck` names the legs when that is the reason. */
  | { kind: "blocked"; reasonKey: string; stuck: PlanboardOrderLegDto[] }
  /** Legs would leave these trips: one confirmation first. */
  | { kind: "confirm"; pulledFrom: PlanboardPulledFromDto[] }
  | { kind: "go" };

export function planOrderDrop(preview: PlanboardAssignOrderPreviewDto): OrderDropPlan {
  const stuck = preview.stuck ?? [];
  if ((preview.movableCount ?? 0) <= 0) {
    if (stuck.length > 0) return { kind: "blocked", reasonKey: "Planboard:OrderDrop.AllLegsStuck", stuck };
    return { kind: "blocked", reasonKey: (preview.legs ?? []).length > 0 ? "Planboard:OrderDrop.AlreadyOnTrip" : "Planboard:OrderDrop.NoLegs", stuck: [] };
  }
  const pulledFrom = preview.pulledFrom ?? [];
  return pulledFrom.length > 0 ? { kind: "confirm", pulledFrom } : { kind: "go" };
}

const TRIP_COMPLETED = 3;
const TRIP_CANCELLED = 4;

/**
 * The preview of a drop on an empty cell, from the order's legs: the preview endpoint needs an existing trip. A leg with
 * actuals on a running trip cannot be told from here, so it counts as movable; the server keeps it and the result names it.
 */
export function previewOfOrderLegs(legs: readonly LegDto[]): PlanboardAssignOrderPreviewDto {
  const toOrderLeg = (leg: LegDto, stuckReason?: PlanboardOrderLegDto["stuckReason"]): PlanboardOrderLegDto => ({
    id: leg.id,
    orderLineId: leg.orderLineId,
    position: leg.position,
    originName: leg.origin?.name,
    originCity: leg.origin?.city,
    destinationName: leg.destination?.name,
    destinationCity: leg.destination?.city,
    tripId: leg.tripId,
    tripNumber: leg.tripNumber,
    stuckReason,
  });
  const isFinal = (leg: LegDto) => leg.tripStatus === TRIP_COMPLETED || leg.tripStatus === TRIP_CANCELLED;
  const pulled = new Map<string, PlanboardPulledFromDto>();
  for (const leg of legs) {
    if (!leg.tripId || isFinal(leg)) continue;
    const trip = pulled.get(leg.tripId);
    if (trip) trip.legCount = (trip.legCount ?? 0) + 1;
    else pulled.set(leg.tripId, { tripId: leg.tripId, number: leg.tripNumber ?? "", legCount: 1 });
  }
  const stuck = legs.filter(isFinal);
  return { legs: legs.map((leg) => toOrderLeg(leg)), stuck: stuck.map((leg) => toOrderLeg(leg, PLANBOARD_STUCK_REASON.TripFinal)), pulledFrom: [...pulled.values()], movableCount: legs.length - stuck.length };
}

/** "Origin → Destination" of a leg of the order. */
export function orderLegRoute(leg: PlanboardOrderLegDto): string {
  return `${leg.originName ?? leg.originCity ?? ""} → ${leg.destinationName ?? leg.destinationCity ?? ""}`;
}

/** The ids of every trip a result touched besides the target. */
export function pulledTripIds(result: { pulledFrom?: PlanboardPulledFromDto[] | null }): string[] {
  return (result.pulledFrom ?? []).map((trip) => trip.tripId).filter((id): id is string => !!id);
}
