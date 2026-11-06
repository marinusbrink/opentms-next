import { describe, expect, it } from "vitest";

import { complementOf, isValidSplit, splitQuantities, type LineMeasures } from "@/domains/planning-execution/components/split-line-model";

/* The pure part of Split line (design 74 §7): what each line would carry for an amount on the new line. The server decides
 * the stored values (4 decimals for quantity and colli, 5 for measures, new = total − remaining, so the total stays exact); this
 * model computes the same way. Risk class: High (the order chain; a quantity lost in a split is a billing and planning error). */

const TOTAL: LineMeasures = { quantity: 3, colli: 7, weightKg: 1000.01, volumeM3: 10, loadingMeters: 13.6, loadFactor: 100, epq: 33 };
const round5 = (value: number) => Math.round(value * 100_000) / 100_000;
const round4 = (value: number) => Math.round(value * 10_000) / 10_000;
const roundOf = (key: keyof LineMeasures) => (key === "quantity" || key === "colli" ? round4 : round5);

describe("splitQuantities", () => {
  it("divides pro rata", () => {
    const result = splitQuantities({ ...TOTAL, quantity: 10, weightKg: 1000 }, 4)!;
    expect(result.newLine).toMatchObject({ quantity: 4, weightKg: 400, volumeM3: 4, loadFactor: 40 });
    expect(result.remaining).toMatchObject({ quantity: 6, weightKg: 600, volumeM3: 6, loadFactor: 60 });
  });

  it("rounds quantity and colli to four decimals and measures to five, and the two parts of every measure add up exactly to the total", () => {
    for (const amount of [1, 2, 0.1, 0.3333, 2.9999, 0.33333]) {
      const result = splitQuantities(TOTAL, amount)!;
      for (const key of Object.keys(TOTAL) as (keyof LineMeasures)[]) {
        expect(round5((result.remaining[key] as number) + (result.newLine[key] as number)), `${key} at ${amount}`).toBe(TOTAL[key]);
        expect(roundOf(key)(result.newLine[key] as number), `${key} at ${amount}`).toBe(result.newLine[key]);
        expect(roundOf(key)(result.remaining[key] as number), `${key} at ${amount}`).toBe(result.remaining[key]);
      }
    }
  });

  it("quantity and colli keep four decimals where a measure keeps five, as the server stores them", () => {
    const result = splitQuantities({ ...TOTAL, colli: 10, volumeM3: 10 }, 1)!;
    expect(result.remaining.colli).toBe(6.6667);
    expect(result.newLine.colli).toBe(3.3333);
    expect(result.remaining.volumeM3).toBe(6.66667);
    expect(result.newLine.volumeM3).toBe(3.33333);
  });

  it("the amount counts at four decimals: the new line gets what the server would store", () => {
    expect(splitQuantities(TOTAL, 0.33333)!.newLine.quantity).toBe(0.3333);
    expect(splitQuantities(TOTAL, 0.33333)!.remaining.quantity).toBe(2.6667);
    expect(splitQuantities(TOTAL, 0.33335)!.newLine.quantity).toBe(0.3334);
  });

  it("new = total − remaining, so a third of 10 does not lose the last decimal", () => {
    const result = splitQuantities({ ...TOTAL, volumeM3: 10 }, 1)!;
    expect(result.remaining.volumeM3).toBe(6.66667);
    expect(result.newLine.volumeM3).toBe(3.33333);
  });

  it("is null at the boundaries: an amount of 0 and the whole total leave a line empty", () => {
    expect(splitQuantities(TOTAL, 0)).toBeNull();
    expect(splitQuantities(TOTAL, 3)).toBeNull();
  });

  it("is null beyond them and without a usable amount", () => {
    expect(splitQuantities(TOTAL, -1)).toBeNull();
    expect(splitQuantities(TOTAL, 3.5)).toBeNull();
    expect(splitQuantities(TOTAL, null)).toBeNull();
    expect(splitQuantities(TOTAL, Number.NaN)).toBeNull();
    expect(splitQuantities(TOTAL, Number.POSITIVE_INFINITY)).toBeNull();
  });

  it("is null when the amount only rounds onto a boundary", () => {
    expect(splitQuantities(TOTAL, 0.000001)).toBeNull();
    expect(splitQuantities(TOTAL, 2.999999)).toBeNull();
    expect(splitQuantities(TOTAL, 0.00001)).toBeNull();
    expect(splitQuantities(TOTAL, 0.00004)).toBeNull();
    expect(splitQuantities(TOTAL, 2.99999)).toBeNull();
    expect(splitQuantities(TOTAL, 2.99996)).toBeNull();
  });

  it("the smallest amounts that do count", () => {
    expect(splitQuantities(TOTAL, 0.0001)!.newLine.quantity).toBe(0.0001);
    expect(splitQuantities(TOTAL, 2.9999)!.remaining.quantity).toBe(0.0001);
    expect(splitQuantities(TOTAL, 0.00005)!.newLine.quantity).toBe(0.0001);
  });

  it("keeps a measure the line does not have null on both sides", () => {
    const result = splitQuantities({ quantity: 2, colli: null, weightKg: 10, volumeM3: null, loadingMeters: null, loadFactor: null, epq: null }, 1)!;
    expect(result.newLine).toEqual({ quantity: 1, colli: null, weightKg: 5, volumeM3: null, loadingMeters: null, loadFactor: null, epq: null });
    expect(result.remaining).toEqual({ quantity: 1, colli: null, weightKg: 5, volumeM3: null, loadingMeters: null, loadFactor: null, epq: null });
  });

  it("a line with a total of 0 cannot be split", () => {
    expect(splitQuantities({ ...TOTAL, quantity: 0 }, 1)).toBeNull();
  });
});

describe("isValidSplit", () => {
  it("wants both lines above zero", () => {
    expect(isValidSplit(10, 1)).toBe(true);
    expect(isValidSplit(10, 9.9999)).toBe(true);
    expect(isValidSplit(10, 0)).toBe(false);
    expect(isValidSplit(10, 10)).toBe(false);
    expect(isValidSplit(10, 11)).toBe(false);
    expect(isValidSplit(10, -1)).toBe(false);
    expect(isValidSplit(10, Number.NaN)).toBe(false);
  });
});

describe("isValidSplit: the same rounding as the server (backend PlanboardReviewSplitLineTests)", () => {
  it.each([0.00001, 0.00004, 0.000049])("refuses %s: below 0.00005 it rounds to nothing", (amount) => {
    expect(isValidSplit(10, amount)).toBe(false);
  });

  it.each([9.99999, 9.99996, 9.99995])("refuses %s of 10: within 0.00005 of the total it rounds to the total", (amount) => {
    expect(isValidSplit(10, amount)).toBe(false);
  });

  it.each([0.00005, 0.0001, 9.9999, 9.99994])("accepts %s of 10", (amount) => {
    expect(isValidSplit(10, amount)).toBe(true);
  });

  /* An exact decimal midpoint: the server rounds decimals away from zero, so 1.24995 is 1.25, the whole total, and refused. */
  it.each([
    [1.25, 1.24995],
    [1.01, 1.00995],
    [0.08, 0.07995],
  ])("refuses, as the server does, an amount exactly 0.00005 below the total of %s", (total, amount) => {
    expect(isValidSplit(total, amount)).toBe(false);
  });
});

describe("complementOf", () => {
  it("is what the other column holds", () => {
    expect(complementOf(10, 3)).toBe(7);
    expect(complementOf(10.1, 0.1)).toBe(10);
    expect(complementOf(0.3, 0.1)).toBe(0.2);
    expect(complementOf(10, 0)).toBe(10);
  });

  it("holds four decimals, like the quantity", () => {
    expect(complementOf(10, 3.3333)).toBe(6.6667);
    expect(complementOf(10, 9.99996)).toBe(0);
    expect(complementOf(10, 0.00004)).toBe(10);
  });

  it("is null when the typed text is no number", () => {
    expect(complementOf(10, null)).toBeNull();
    expect(complementOf(10, Number.NaN)).toBeNull();
  });
});
