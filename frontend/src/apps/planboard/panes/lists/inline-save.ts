import { isConcurrencyConflict } from "@/lib/api/errors";

/* Saving a cell of a list pane (design 74 §6). Background jobs move a record's stamp too, so a 409 does not yet mean someone
 * else changed what the user edited: the row is refetched, and when the edited field still has the value the user saw the
 * save runs once more on the fresh row. Otherwise, and when the refetch fails, the 409 stands: the caller shows the usual
 * stale message. Any other failure goes straight to the caller. */

export interface StaleRetryArgs<TRow, TResult> {
  row: TRow;
  save: (row: TRow) => Promise<TResult>;
  refetch: (row: TRow) => Promise<TRow | null | undefined>;
  /** Whether the edited field of the fresh row still has the value the user saw in `row`. */
  unchanged: (fresh: TRow, seen: TRow) => boolean;
}

export async function saveWithStaleRetry<TRow, TResult>({ row, save, refetch, unchanged }: StaleRetryArgs<TRow, TResult>): Promise<TResult> {
  try {
    return await save(row);
  } catch (error) {
    if (!isConcurrencyConflict(error)) throw error;
    let fresh: TRow | null | undefined;
    try {
      fresh = await refetch(row);
    } catch {
      throw error;
    }
    if (fresh == null || !unchanged(fresh, row)) throw error;
    return save(fresh);
  }
}

/** Two values of a field as the user sees them: an empty text and no value are the same. */
export function sameValue(a: unknown, b: unknown): boolean {
  return (a ?? "") === (b ?? "");
}
