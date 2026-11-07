import { describe, expect, it } from "vitest";

import { orderLegRoute, planOrderDrop, previewOfOrderLegs, pulledTripIds } from "@/apps/planboard/order-drop-model";
import type { LegDto } from "@/domains/planning-execution/legs";
import { PLANBOARD_STUCK_REASON, type PlanboardAssignOrderPreviewDto } from "@/domains/planning-execution/planboard-lists";

/* The pure part of dropping an order on a trip (design 74 §8). Risk class: High (planning: the plan decides whether legs leave
 * other trips). The server-side preview and assign are in the backend's PlanboardReviewAssignOrderTests. */

const preview = (patch: Partial<PlanboardAssignOrderPreviewDto>) => patch as PlanboardAssignOrderPreviewDto;
const leg = (id: string, patch: Record<string, unknown> = {}) => ({ id, ...patch }) as unknown as LegDto;

describe("planOrderDrop", () => {
  it("nothing movable because everything is stuck: blocked, naming them", () => {
    const stuck = [{ id: "l1", stuckReason: PLANBOARD_STUCK_REASON.HasActuals }];
    expect(planOrderDrop(preview({ movableCount: 0, stuck, legs: stuck }))).toEqual({ kind: "blocked", reasonKey: "Planboard:OrderDrop.AllLegsStuck", stuck });
  });

  it("nothing movable, nothing stuck, but legs: they already are on this trip", () => {
    expect(planOrderDrop(preview({ movableCount: 0, stuck: [], legs: [{ id: "l1" }] }))).toEqual({ kind: "blocked", reasonKey: "Planboard:OrderDrop.AlreadyOnTrip", stuck: [] });
  });

  it("no legs at all", () => {
    expect(planOrderDrop(preview({ movableCount: 0, stuck: [], legs: [] }))).toEqual({ kind: "blocked", reasonKey: "Planboard:OrderDrop.NoLegs", stuck: [] });
    expect(planOrderDrop(preview({}))).toEqual({ kind: "blocked", reasonKey: "Planboard:OrderDrop.NoLegs", stuck: [] });
  });

  it("legs would leave other trips: one confirmation first", () => {
    const pulledFrom = [{ tripId: "t2", number: "TR-2", legCount: 2 }];
    expect(planOrderDrop(preview({ movableCount: 2, pulledFrom, legs: [{ id: "l1" }, { id: "l2" }] }))).toEqual({ kind: "confirm", pulledFrom });
  });

  it("movable and nothing pulled: go, without a dialog, also with some stuck", () => {
    expect(planOrderDrop(preview({ movableCount: 1, pulledFrom: [] }))).toEqual({ kind: "go" });
    expect(planOrderDrop(preview({ movableCount: 1, stuck: [{ id: "l9" }] }))).toEqual({ kind: "go" });
    expect(planOrderDrop(preview({ movableCount: 1, pulledFrom: null }))).toEqual({ kind: "go" });
  });
});

describe("previewOfOrderLegs", () => {
  it("unplanned legs all move and pull from nothing", () => {
    const result = previewOfOrderLegs([leg("l1"), leg("l2")]);
    expect(result.movableCount).toBe(2);
    expect(result.pulledFrom).toEqual([]);
    expect(result.stuck).toEqual([]);
    expect(result.legs!.map((l) => l.id)).toEqual(["l1", "l2"]);
    expect(planOrderDrop(result)).toEqual({ kind: "go" });
  });

  it("legs on running trips are pulled, counted per trip", () => {
    const result = previewOfOrderLegs([leg("l1", { tripId: "t1", tripNumber: "TR-1", tripStatus: 1 }), leg("l2", { tripId: "t1", tripNumber: "TR-1", tripStatus: 1 }), leg("l3", { tripId: "t2", tripStatus: 2 })]);
    expect(result.pulledFrom).toEqual([{ tripId: "t1", number: "TR-1", legCount: 2 }, { tripId: "t2", number: "", legCount: 1 }]);
    expect(result.movableCount).toBe(3);
    expect(planOrderDrop(result)).toMatchObject({ kind: "confirm" });
  });

  it("legs on completed or cancelled trips are stuck with their reason and do not count as pulled", () => {
    const result = previewOfOrderLegs([leg("l1", { tripId: "t1", tripNumber: "TR-1", tripStatus: 3 }), leg("l2", { tripId: "t2", tripNumber: "TR-2", tripStatus: 4 }), leg("l3")]);
    expect(result.pulledFrom).toEqual([]);
    expect(result.movableCount).toBe(1);
    expect(result.stuck!.map((s) => [s.id, s.stuckReason])).toEqual([["l1", PLANBOARD_STUCK_REASON.TripFinal], ["l2", PLANBOARD_STUCK_REASON.TripFinal]]);
  });

  it("all legs on final trips block the drop, naming them", () => {
    const plan = planOrderDrop(previewOfOrderLegs([leg("l1", { tripId: "t1", tripStatus: 3 })]));
    expect(plan).toMatchObject({ kind: "blocked", reasonKey: "Planboard:OrderDrop.AllLegsStuck" });
  });

  it("an order without legs says so", () => {
    expect(planOrderDrop(previewOfOrderLegs([]))).toMatchObject({ kind: "blocked", reasonKey: "Planboard:OrderDrop.NoLegs" });
  });

  it("maps a leg's ends for the texts", () => {
    const result = previewOfOrderLegs([leg("l1", { orderLineId: "ol", position: 2, origin: { name: "A", city: "Aa" }, destination: { name: "B", city: "Bb" } })]);
    expect(result.legs![0]).toMatchObject({ orderLineId: "ol", position: 2, originName: "A", originCity: "Aa", destinationName: "B", destinationCity: "Bb" });
  });
});

describe("orderLegRoute and pulledTripIds", () => {
  it("the route prefers the name and falls back on the city", () => {
    expect(orderLegRoute({ originName: "A", originCity: "Aa", destinationCity: "Bb" })).toBe("A → Bb");
    expect(orderLegRoute({})).toBe(" → ");
  });

  it("the ids of the trips legs left", () => {
    expect(pulledTripIds({ pulledFrom: [{ tripId: "t1" }, {}, { tripId: "t2" }] })).toEqual(["t1", "t2"]);
    expect(pulledTripIds({})).toEqual([]);
    expect(pulledTripIds({ pulledFrom: null })).toEqual([]);
  });
});
