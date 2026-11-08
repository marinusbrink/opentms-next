import { describe, expect, it, vi } from "vitest";

import { decodeDragPayload, DND_LEGS, DND_ORDER, encodeDragPayload, mimeTypeOf, planboardDragKind, readDragPayload, setDragPayload } from "@/apps/planboard/dnd";

/* The order payload (design 74 §8): `application/x-opentms-order`, ids only. Risk class: Medium. */

describe("order drag payload", () => {
  it("has its own MIME type", () => {
    expect(DND_ORDER).toBe("application/x-opentms-order");
    expect(mimeTypeOf("order")).toBe(DND_ORDER);
    expect(planboardDragKind([DND_ORDER])).toBe("order");
  });

  it("round-trips, and carries the id only", () => {
    const raw = encodeDragPayload({ kind: "order", orderId: "o1" });
    expect(JSON.parse(raw)).toEqual({ orderId: "o1" });
    expect(decodeDragPayload(DND_ORDER, raw)).toEqual({ kind: "order", orderId: "o1" });
  });

  it("refuses a foreign or malformed payload without an exception", () => {
    for (const raw of ["not json", "", null, undefined, "null", "[]", "{}", JSON.stringify({ orderId: "" }), JSON.stringify({ orderId: "   " }), JSON.stringify({ orderId: 5 }), JSON.stringify({ orderId: "x".repeat(65) })]) {
      expect(decodeDragPayload(DND_ORDER, raw), String(raw)).toBeNull();
    }
  });

  it("an order's content under another type is not an order", () => {
    expect(decodeDragPayload(DND_LEGS, JSON.stringify({ orderId: "o1" }))).toBeNull();
  });

  it("is put on a drag with the effect 'move'", () => {
    const transfer = { setData: vi.fn(), effectAllowed: "none" } as unknown as DataTransfer;
    setDragPayload(transfer, { kind: "order", orderId: "o1" });
    expect(transfer.setData).toHaveBeenCalledWith(DND_ORDER, JSON.stringify({ orderId: "o1" }));
    expect(transfer.effectAllowed).toBe("move");
  });

  it("is read in `drop` when the target accepts orders, and not otherwise", () => {
    const transfer = { types: [DND_ORDER], getData: (type: string) => (type === DND_ORDER ? JSON.stringify({ orderId: "o1" }) : "") };
    expect(readDragPayload(transfer, ["legs", "driver", "order"])).toEqual({ kind: "order", orderId: "o1" });
    expect(readDragPayload(transfer, ["legs", "driver"])).toBeNull();
    expect(readDragPayload(transfer)).toEqual({ kind: "order", orderId: "o1" });
  });
});
