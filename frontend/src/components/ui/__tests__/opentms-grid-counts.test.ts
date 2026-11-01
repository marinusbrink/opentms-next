import type { GridApi, ServerSideTransaction, ServerSideTransactionResult } from "ag-grid-community";
import { describe, expect, it, vi } from "vitest";

import { followTransactionCounts, shiftCount } from "@/components/ui/opentms-grid-counts";

/* The footer counts follow rows a caller patches in place (found in the Planboard browser check, 2026-11-01: a leg
 * assigned from Legs to schedule left the list live, but "Filtered: 1,335 of 8,008" stayed until the next fetch).
 * Risk class: Low (display of a count; the next fetch replaces it). */

function fakeApi(result: ServerSideTransactionResult | null) {
  const original = vi.fn((_tx: ServerSideTransaction) => result);
  const api = { applyServerSideTransaction: original } as unknown as GridApi<unknown>;
  return { api, original };
}

const node = {} as never;

describe("followTransactionCounts", () => {
  it("reports removed top-level rows as a negative delta and still applies the transaction", () => {
    const { api, original } = fakeApi({ status: "Applied" as never, remove: [node, node] });
    const onDelta = vi.fn();
    followTransactionCounts(api, onDelta);

    const tx = { remove: [{ id: "a" }, { id: "b" }] };
    const result = api.applyServerSideTransaction(tx);

    expect(original).toHaveBeenCalledWith(tx);
    expect(result?.status).toBe("Applied");
    expect(onDelta).toHaveBeenCalledWith(-2);
  });

  it("reports added rows as a positive delta, net of removed ones", () => {
    const { api } = fakeApi({ status: "Applied" as never, add: [node, node, node], remove: [node] });
    const onDelta = vi.fn();
    followTransactionCounts(api, onDelta);
    api.applyServerSideTransaction({ add: [{}, {}, {}], remove: [{}] });
    expect(onDelta).toHaveBeenCalledWith(2);
  });

  it("leaves the counts alone for an update-only transaction", () => {
    const { api } = fakeApi({ status: "Applied" as never, update: [node] });
    const onDelta = vi.fn();
    followTransactionCounts(api, onDelta);
    api.applyServerSideTransaction({ update: [{}] });
    expect(onDelta).not.toHaveBeenCalled();
  });

  it("leaves the counts alone when the grid did not apply the transaction", () => {
    const { api } = fakeApi({ status: "StoreNotFound" as never, remove: [node] });
    const onDelta = vi.fn();
    followTransactionCounts(api, onDelta);
    api.applyServerSideTransaction({ remove: [{}] });
    expect(onDelta).not.toHaveBeenCalled();
  });

  it("leaves the counts alone for a transaction on a child route (tree data)", () => {
    const { api } = fakeApi({ status: "Applied" as never, remove: [node] });
    const onDelta = vi.fn();
    followTransactionCounts(api, onDelta);
    api.applyServerSideTransaction({ route: ["group-1"], remove: [{}] });
    expect(onDelta).not.toHaveBeenCalled();
  });
});

describe("shiftCount", () => {
  it("shifts a known count and never goes below zero", () => {
    expect(shiftCount(10, -3)).toBe(7);
    expect(shiftCount(1, -5)).toBe(0);
    expect(shiftCount(4, 2)).toBe(6);
  });

  it("keeps an unknown count unknown", () => {
    expect(shiftCount(null, -1)).toBeNull();
  });
});
