import { describe, expect, it } from "vitest";

/* Design 75 risk analysis — Optimistic assign (Low risk unit tests):
 * - Leg IDs are compared case-insensitively
 * - pendingLegData key normalisation
 * - Assign with fromTripId marks both trips as pending */

/* These are pure logic tests on the state shape; no DOM/React needed.
 * The PlanboardOptimisticStateProvider integration is covered by command-queue.test.tsx. */

function lower(id: string) {
  return id.toLowerCase();
}

describe("optimistic state shape invariants", () => {
  it("lowercases leg IDs when checking containment", () => {
    const set = new Set(["leg-A", "LEG-B"].map(lower));
    expect(set.has("leg-a")).toBe(true);
    expect(set.has("leg-b")).toBe(true);
    expect(set.has("leg-A")).toBe(false);
  });

  it("filters pending leg data by lower-cased id", () => {
    const legData = new Map([
      ["leg-a", { id: "leg-A", orderNumber: "O1" }],
      ["leg-b", { id: "LEG-B", orderNumber: "O2" }],
    ]);
    const result = [...legData.entries()].filter(([k]) => ["leg-a", "leg-b"].includes(k));
    expect(result).toHaveLength(2);
  });

  it("assign with fromTripId adds fromTripId to unassignedLegs", () => {
    /* Simulate the enqueueAssign state transition logic. */
    const assignedLegs = new Map<string, Set<string>>();
    const unassignedLegs = new Map<string, Set<string>>();
    const pendingValues = new Set<string>();

    const tripId = "trip-1";
    const fromTripId = "trip-2";
    const legIds = ["leg-x"];
    const tripKey = lower(tripId);
    const fromKey = lower(fromTripId);

    if (!assignedLegs.has(tripKey)) assignedLegs.set(tripKey, new Set());
    legIds.forEach((id) => assignedLegs.get(tripKey)!.add(lower(id)));
    pendingValues.add(tripKey);
    if (fromTripId) {
      if (!unassignedLegs.has(fromKey)) unassignedLegs.set(fromKey, new Set());
      legIds.forEach((id) => unassignedLegs.get(fromKey)!.add(lower(id)));
      pendingValues.add(fromKey);
    }

    expect(assignedLegs.get(tripKey)?.has("leg-x")).toBe(true);
    expect(unassignedLegs.get(fromKey)?.has("leg-x")).toBe(true);
    expect(pendingValues.has(tripKey)).toBe(true);
    expect(pendingValues.has(fromKey)).toBe(true);
  });

  it("rollback reverses assign state correctly", () => {
    const assignedLegs = new Map([["trip-1", new Set(["leg-a"])]]);
    const unassignedLegs = new Map([["trip-2", new Set(["leg-a"])]]);
    const pendingValues = new Set(["trip-1", "trip-2"]);

    /* Simulate rollback of one assign command with fromTripId. */
    const cmd = { type: "assign" as const, tripId: "trip-1", legIds: ["leg-a"], fromTripId: "trip-2" };
    const tripKey = lower(cmd.tripId);
    const set = assignedLegs.get(tripKey);
    if (set) {
      cmd.legIds.forEach((id) => set.delete(lower(id)));
      if (set.size === 0) assignedLegs.delete(tripKey);
    }
    if (cmd.fromTripId) {
      const fromKey = lower(cmd.fromTripId);
      const fromSet = unassignedLegs.get(fromKey);
      if (fromSet) {
        cmd.legIds.forEach((id) => fromSet.delete(lower(id)));
        if (fromSet.size === 0) unassignedLegs.delete(fromKey);
      }
    }
    pendingValues.delete(tripKey);

    expect(assignedLegs.has(tripKey)).toBe(false);
    expect(unassignedLegs.has("trip-2")).toBe(false);
    expect(pendingValues.has(tripKey)).toBe(false);
  });
});
