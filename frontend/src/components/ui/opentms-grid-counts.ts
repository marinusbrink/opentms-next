import type { GridApi } from "ag-grid-community";

/* A caller that patches rows in place (applyServerSideTransaction, no refetch) changes the number of top-level rows; the
 * footer counts follow it until the next fetch replaces them. Wraps the api's transaction method once; a transaction on
 * a child route (tree data) or one the grid did not apply leaves the counts alone. */
export function followTransactionCounts<TRow>(api: GridApi<TRow>, onDelta: (delta: number) => void): void {
  const applyTransaction = api.applyServerSideTransaction.bind(api);
  api.applyServerSideTransaction = (transaction) => {
    const result = applyTransaction(transaction);
    if (result?.status === "Applied" && !transaction.route?.length) {
      const delta = (result.add?.length ?? 0) - (result.remove?.length ?? 0);
      if (delta !== 0) onDelta(delta);
    }
    return result;
  };
}

/** A footer count after a delta: unknown stays unknown, never below zero. */
export function shiftCount(count: number | null, delta: number): number | null {
  return count == null ? count : Math.max(0, count + delta);
}
