import { describe, expect, it } from "vitest";

import {
  decodeDragPayload,
  DND_DRIVER,
  DND_LEGS,
  DND_TRIP,
  encodeDragPayload,
  MAX_DRAGGED_LEGS,
  mimeTypeOf,
  planboardDragKind,
  readDragPayload,
  setDragPayload,
  type PlanboardDragPayload,
} from "@/apps/planboard/dnd";

/* Drag and drop payloads between Planboard panes (design 70, "Drag and drop"): a round trip per kind, foreign and
 * malformed payloads yield null (never an exception), the 500-leg cap, the drop target's accept list, and the kind read
 * from the types alone during dragover. Risk class: Medium (the payload decides which command a drop runs). */

function fakeTransfer(data: Record<string, string>, opts: { throwOnGet?: boolean } = {}) {
  const store = { ...data };
  return {
    types: Object.keys(store),
    effectAllowed: "uninitialized" as DataTransfer["effectAllowed"],
    getData: (type: string) => {
      if (opts.throwOnGet) throw new Error("protected mode");
      return store[type] ?? "";
    },
    setData: (type: string, value: string) => {
      store[type] = value;
    },
    get stored() {
      return store;
    },
  };
}

describe("encode / decode", () => {
  it.each<PlanboardDragPayload>([
    { kind: "legs", legIds: ["l1", "l2"], fromTripId: "t1" },
    { kind: "legs", legIds: ["l1"], fromTripId: null },
    { kind: "driver", driverId: "d1" },
    { kind: "trip", tripId: "t1" },
  ])("round-trips %o", (payload) => {
    expect(decodeDragPayload(mimeTypeOf(payload.kind), encodeDragPayload(payload))).toEqual(payload);
  });

  it("dedupes leg ids and drops an invalid fromTripId", () => {
    expect(decodeDragPayload(DND_LEGS, JSON.stringify({ legIds: ["l1", "l1", "l2"], fromTripId: 42 }))).toEqual({ kind: "legs", legIds: ["l1", "l2"], fromTripId: null });
  });

  it("caps a drag at 500 legs", () => {
    const ids = (n: number) => Array.from({ length: n }, (_, i) => `leg-${i}`);
    expect(decodeDragPayload(DND_LEGS, JSON.stringify({ legIds: ids(MAX_DRAGGED_LEGS) }))).toMatchObject({ kind: "legs" });
    expect(decodeDragPayload(DND_LEGS, JSON.stringify({ legIds: ids(MAX_DRAGGED_LEGS + 1) }))).toBeNull();
  });

  it.each([
    ["not JSON", DND_LEGS, "{legIds:"],
    ["empty", DND_LEGS, ""],
    ["null JSON", DND_LEGS, "null"],
    ["an array", DND_LEGS, '["l1"]'],
    ["no legs", DND_LEGS, JSON.stringify({ legIds: [] })],
    ["legIds not an array", DND_LEGS, JSON.stringify({ legIds: "l1" })],
    ["a blank leg id", DND_LEGS, JSON.stringify({ legIds: ["l1", "  "] })],
    ["a numeric leg id", DND_LEGS, JSON.stringify({ legIds: [1] })],
    ["an id over 64 chars", DND_LEGS, JSON.stringify({ legIds: ["x".repeat(65)] })],
    ["a driver without id", DND_DRIVER, JSON.stringify({ tripId: "t1" })],
    ["a trip with an object id", DND_TRIP, JSON.stringify({ tripId: { id: "t1" } })],
    ["a foreign MIME type", "text/plain", JSON.stringify({ tripId: "t1" })],
    ["another app's type", "application/x-other-legs", JSON.stringify({ legIds: ["l1"] })],
  ])("yields null for %s", (_label, mime, raw) => {
    expect(decodeDragPayload(mime, raw)).toBeNull();
  });

  it("yields null for a missing raw value", () => {
    expect(decodeDragPayload(DND_TRIP, null)).toBeNull();
    expect(decodeDragPayload(DND_TRIP, undefined)).toBeNull();
  });
});

describe("setDragPayload / planboardDragKind / readDragPayload", () => {
  it("stores the payload under its MIME type with move for legs and trips, link for a driver", () => {
    const legs = fakeTransfer({});
    setDragPayload(legs as unknown as DataTransfer, { kind: "legs", legIds: ["l1"] });
    expect(Object.keys(legs.stored)).toEqual([DND_LEGS]);
    expect(legs.effectAllowed).toBe("move");

    const driver = fakeTransfer({});
    setDragPayload(driver as unknown as DataTransfer, { kind: "driver", driverId: "d1" });
    expect(driver.effectAllowed).toBe("link");

    const trip = fakeTransfer({});
    setDragPayload(trip as unknown as DataTransfer, { kind: "trip", tripId: "t1" });
    expect(trip.effectAllowed).toBe("move");
  });

  it("reads the kind from the types alone (dragover), null for foreign drags", () => {
    expect(planboardDragKind(["text/plain", DND_DRIVER])).toBe("driver");
    expect(planboardDragKind(["Files"])).toBeNull();
    expect(planboardDragKind(null)).toBeNull();
    expect(planboardDragKind([])).toBeNull();
  });

  it("reads the payload on drop, limited to the kinds the target accepts", () => {
    const transfer = fakeTransfer({ [DND_LEGS]: encodeDragPayload({ kind: "legs", legIds: ["l1"], fromTripId: "t9" }) });
    expect(readDragPayload(transfer)).toEqual({ kind: "legs", legIds: ["l1"], fromTripId: "t9" });
    expect(readDragPayload(transfer, ["legs", "trip"])).toMatchObject({ kind: "legs" });
    expect(readDragPayload(transfer, ["driver"])).toBeNull();
  });

  it("yields null for a foreign drag, a missing transfer, a malformed payload or a getData that throws", () => {
    expect(readDragPayload(fakeTransfer({ "text/plain": "hello" }))).toBeNull();
    expect(readDragPayload(null)).toBeNull();
    expect(readDragPayload(fakeTransfer({ [DND_TRIP]: "<html>" }))).toBeNull();
    expect(readDragPayload(fakeTransfer({ [DND_TRIP]: '{"tripId":"t1"}' }, { throwOnGet: true }))).toBeNull();
  });
});
