import { describe, expect, it, vi } from "vitest";

import { sameValue, saveWithStaleRetry } from "@/apps/planboard/panes/lists/inline-save";

/* Saving a cell of a list pane (design 74 §6): background jobs move a record's stamp too, so a 409 is retried once on the fresh
 * row when the edited field still has the value the user saw. Risk class: High (a wrong retry overwrites someone else's
 * edit; a missing one shows a false conflict on every recalculation). */

interface Row {
  stamp: string;
  value: string | null;
}

const stale = { status: 409, error: { message: "stale" } };
const seen: Row = { stamp: "s1", value: "old" };
const fresh: Row = { stamp: "s2", value: "old" };

const args = (save: (row: Row) => Promise<string>, refetch: (row: Row) => Promise<Row | null | undefined>) => ({
  row: seen,
  save,
  refetch,
  unchanged: (a: Row, b: Row) => sameValue(a.value, b.value),
});

describe("saveWithStaleRetry", () => {
  it("does not refetch when the save succeeds", async () => {
    const save = vi.fn(async () => "saved");
    const refetch = vi.fn(async () => fresh);
    await expect(saveWithStaleRetry(args(save, refetch))).resolves.toBe("saved");
    expect(save).toHaveBeenCalledTimes(1);
    expect(refetch).not.toHaveBeenCalled();
  });

  it("retries once on the fresh row when the edited field still has the value the user saw", async () => {
    const save = vi.fn(async (row: Row) => {
      if (row.stamp === "s1") throw stale;
      return `saved with ${row.stamp}`;
    });
    await expect(saveWithStaleRetry(args(save, async () => fresh))).resolves.toBe("saved with s2");
    expect(save.mock.calls.map(([row]) => row.stamp)).toEqual(["s1", "s2"]);
  });

  it("keeps the 409 when the field changed meanwhile, without a second save", async () => {
    const save = vi.fn(async (): Promise<string> => {
      throw stale;
    });
    await expect(saveWithStaleRetry(args(save, async () => ({ stamp: "s2", value: "someone else's" })))).rejects.toBe(stale);
    expect(save).toHaveBeenCalledTimes(1);
  });

  it("keeps the 409 when the refetch fails, not the refetch's error", async () => {
    const save = vi.fn(async (): Promise<string> => {
      throw stale;
    });
    await expect(
      saveWithStaleRetry(
        args(save, async () => {
          throw new Error("network");
        }),
      ),
    ).rejects.toBe(stale);
    expect(save).toHaveBeenCalledTimes(1);
  });

  it("keeps the 409 when the row is gone", async () => {
    const save = vi.fn(async (): Promise<string> => {
      throw stale;
    });
    await expect(saveWithStaleRetry(args(save, async () => null))).rejects.toBe(stale);
    await expect(saveWithStaleRetry(args(save, async () => undefined))).rejects.toBe(stale);
  });

  it("retries only once: a second 409 goes to the caller", async () => {
    const second = { status: 409, error: { message: "again" } };
    const save = vi.fn(async (row: Row): Promise<string> => {
      throw row.stamp === "s1" ? stale : second;
    });
    await expect(saveWithStaleRetry(args(save, async () => fresh))).rejects.toBe(second);
    expect(save).toHaveBeenCalledTimes(2);
  });

  it("other errors pass straight through, a domain 409 with a code among them", async () => {
    for (const error of [{ status: 400, error: { message: "bad" } }, { status: 500 }, new Error("x"), { status: 409, error: { code: "OpenTms.Orders:SomeRule" } }]) {
      const refetch = vi.fn(async () => fresh);
      await expect(
        saveWithStaleRetry(
          args(async () => {
            throw error;
          }, refetch),
        ),
      ).rejects.toBe(error);
      expect(refetch).not.toHaveBeenCalled();
    }
  });
});

describe("sameValue", () => {
  it("an empty text and no value are the same", () => {
    expect(sameValue("", null)).toBe(true);
    expect(sameValue(undefined, "")).toBe(true);
    expect(sameValue(null, undefined)).toBe(true);
    expect(sameValue("a", "a")).toBe(true);
  });

  it("anything else differs", () => {
    expect(sameValue("a", "b")).toBe(false);
    expect(sameValue("a", null)).toBe(false);
    expect(sameValue(0, "")).toBe(false);
  });
});
