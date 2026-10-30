import { describe, expect, it } from "vitest";

import { changedKeys, detailKey, detailRows, detailSourceOf, hasInvalidValue, overrideOf, overridesFor, withRows, type DetailSource } from "@/apps/transport/wizards/materialise-details";

/* The wizard's per-order details (design 61 "Details", design 58 build note 19): the source's lines in position order
 * with their ids, the edits per (date, copy), and only the values that differ from the source sent as overrides
 * (an untouched order sends nothing; an emptied number keeps the source's value; an emptied reference clears it).
 * Risk class: High (the overrides decide what the created orders hold). */

const source: DetailSource = {
  customerReference: "REF-1",
  lines: [
    { id: "l1", position: 1, label: "Pallet · Beer", quantity: 10, weightKg: 500 },
    { id: "l2", position: 2, label: "Box", quantity: 2, weightKg: null },
  ],
};

describe("detailSourceOf", () => {
  it("takes the reference and the lines in position order, leaving out a line without an id", () => {
    const result = detailSourceOf({
      customerReference: null,
      lines: [
        { id: "b", position: 2, transportUnitName: "Box", quantity: 2, transportUnitId: "u" },
        { id: null, position: 3, transportUnitName: "Lost", quantity: 1, transportUnitId: "u" },
        { id: "a", position: 1, transportUnitName: "Pallet", productName: "Beer", quantity: 10, weightKg: 500, transportUnitId: "u" },
      ],
    } as never);
    expect(result).toEqual({
      customerReference: "",
      lines: [
        { id: "a", position: 1, label: "Pallet · Beer", quantity: 10, weightKg: 500 },
        { id: "b", position: 2, label: "Box", quantity: 2, weightKg: null },
      ],
    });
    expect(detailSourceOf(undefined)).toBeUndefined();
  });
});

describe("overrides", () => {
  it("prefills the rows from the source and keeps the edits per line", () => {
    expect(detailRows(source, undefined).map((r) => [r.quantity, r.weightKg])).toEqual([["10", "500"], ["2", ""]]);
    const edit = withRows(undefined, [{ ...detailRows(source, undefined)[0], quantity: "12" }]);
    expect(detailRows(source, edit).map((r) => r.quantity)).toEqual(["12", "2"]);
  });

  it("sends nothing for an untouched order or one edited back to the source's values", () => {
    expect(overrideOf("2026-10-27", 0, source, undefined)).toBeNull();
    const back = withRows({ customerReference: "REF-1" }, detailRows(source, undefined));
    expect(overrideOf("2026-10-27", 0, source, back)).toBeNull();
  });

  it("sends only the changed values: the reference, and per changed line the changed number (others null)", () => {
    const rows = detailRows(source, undefined);
    const edit = withRows({ customerReference: " REF-2 " }, [{ ...rows[0], weightKg: "450,5" }, rows[1]]);
    expect(overrideOf("2026-10-27", 1, source, edit)).toEqual({ date: "2026-10-27", copyIndex: 1, customerReference: "REF-2", lines: [{ lineId: "l1", quantity: null, weightKg: 450.5 }] });
  });

  it("an emptied reference clears it; an emptied number keeps the source's value", () => {
    const rows = detailRows(source, undefined);
    const edit = withRows({ customerReference: "" }, [{ ...rows[0], quantity: "" }, rows[1]]);
    expect(overrideOf("2026-10-27", 0, source, edit)).toEqual({ date: "2026-10-27", copyIndex: 0, customerReference: "", lines: [] });
  });

  it("collects one date's overrides over its copies and ignores edits of dates or copies not being created", () => {
    const edits = {
      [detailKey("2026-10-27", 0)]: { customerReference: "A" },
      [detailKey("2026-10-27", 2)]: { customerReference: "C" },
      [detailKey("2026-10-28", 0)]: { customerReference: "B" },
    };
    expect(overridesFor("2026-10-27", 2, source, edits)).toEqual([{ date: "2026-10-27", copyIndex: 0, customerReference: "A", lines: [] }]);
    expect(changedKeys(["2026-10-27"], 3, source, edits)).toEqual(["2026-10-27|0", "2026-10-27|2"]);
  });

  it("flags a quantity or weight that is no number of at least 0", () => {
    const rows = detailRows(source, undefined);
    expect(hasInvalidValue(withRows(undefined, [{ ...rows[0], quantity: "abc" }]))).toBe(true);
    expect(hasInvalidValue(withRows(undefined, [{ ...rows[0], weightKg: "-1" }]))).toBe(true);
    expect(hasInvalidValue(withRows(undefined, rows))).toBe(false);
    expect(hasInvalidValue(undefined)).toBe(false);
  });
});
