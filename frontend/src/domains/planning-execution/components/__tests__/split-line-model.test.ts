import { describe, expect, it } from "vitest";

import { complementOf, isValidSplit, splitQuantities, type LineMeasures } from "@/domains/planning-execution/components/split-line-model";

/* The pure part of Split line (design 74 §7): what each line would carry for an amount on the new line. The server decides
 * the stored values (5 decimals, new = total − remaining, so the total stays exact); this model computes the same way. Risk
 * class: High (the order chain; a quantity lost in a split is a billing and planning error). */

const TOTAL: LineMeasures = { quantity: 3, colli: 7, weightKg: 1000.01, volumeM3: 10, loadingMeters: 13.6, loadFactor: 100, epq: 33 };
const round5 = (value: number) => Math.round(value * 100_000) / 100_000;

describe("splitQuantities", () => {
  it("divides pro rata", () => {
    const result = splitQuantities({ ...TOTAL, quantity: 10, weightKg: 1000 }, 4)!;
    expect(result.newLine).toMatchObject({ quantity: 4, weightKg: 400, volumeM3: 4, loadFactor: 40 });
    expect(result.remaining).toMatchObject({ quantity: 6, weightKg: 600, volumeM3: 6, loadFactor: 60 });
  });

  it("rounds to five decimals, and the two parts of every measure add up exactly to the total", () => {
    for (const amount of [1, 2, 0.1, 0.33333, 2.99999]) {
      const result = splitQuantities(TOTAL, amount)!;
      for (const key of Object.keys(TOTAL) as (keyof LineMeasures)[]) {
        expect(round5((result.remaining[key] as number) + (result.newLine[key] as number)), `${key} at ${amount}`).toBe(TOTAL[key]);
        expect(round5(result.newLine[key] as number), `${key} at ${amount}`).toBe(result.newLine[key]);
      }
    }
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
  });

  it("the smallest amounts that do count", () => {
    expect(splitQuantities(TOTAL, 0.00001)!.newLine.quantity).toBe(0.00001);
    expect(splitQuantities(TOTAL, 2.99999)!.remaining.quantity).toBe(0.00001);
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
    expect(isValidSplit(10, 9.99999)).toBe(true);
    expect(isValidSplit(10, 0)).toBe(false);
    expect(isValidSplit(10, 10)).toBe(false);
    expect(isValidSplit(10, 11)).toBe(false);
    expect(isValidSplit(10, -1)).toBe(false);
    expect(isValidSplit(10, Number.NaN)).toBe(false);
  });
});

describe("complementOf", () => {
  it("is what the other column holds", () => {
    expect(complementOf(10, 3)).toBe(7);
    expect(complementOf(10.1, 0.1)).toBe(10);
    expect(complementOf(0.3, 0.1)).toBe(0.2);
    expect(complementOf(10, 0)).toBe(10);
  });

  it("is null when the typed text is no number", () => {
    expect(complementOf(10, null)).toBeNull();
    expect(complementOf(10, Number.NaN)).toBeNull();
  });
});
