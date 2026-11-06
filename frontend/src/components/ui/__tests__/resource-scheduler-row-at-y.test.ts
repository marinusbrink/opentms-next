import { describe, expect, it } from "vitest";

import { rowAtY, type MeasuredRow } from "@/components/ui/resource-scheduler-model";

/* The row under a drop (design 74 §10: "a drop that cannot proceed says why", here: not over a row). Risk class: Medium. */

const ROWS: MeasuredRow[] = [
  { resourceId: "r1", top: 100, bottom: 140 },
  { resourceId: "r2", top: 140, bottom: 180 },
  { resourceId: "r3", top: 200, bottom: 240 },
];

describe("rowAtY", () => {
  it("finds the row under the position", () => {
    expect(rowAtY(ROWS, 120)).toBe("r1");
    expect(rowAtY(ROWS, 220)).toBe("r3");
  });

  it("a row's top belongs to it, its bottom to the next", () => {
    expect(rowAtY(ROWS, 100)).toBe("r1");
    expect(rowAtY(ROWS, 139.9)).toBe("r1");
    expect(rowAtY(ROWS, 140)).toBe("r2");
  });

  it("is null above, below and between the rows, and without rows", () => {
    expect(rowAtY(ROWS, 99)).toBeNull();
    expect(rowAtY(ROWS, 190)).toBeNull();
    expect(rowAtY(ROWS, 240)).toBeNull();
    expect(rowAtY([], 120)).toBeNull();
  });
});
