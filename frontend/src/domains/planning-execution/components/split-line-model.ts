/* The pure part of Split line (design 74 §7): what each of the two lines would carry for an amount on the new line. The
 * server decides the stored values (5 decimals, new = total − remaining, so the total stays exact); this model computes the
 * same way so the dialog can recalculate live, and the dialog shows two decimals. */

export interface LineMeasures {
  quantity: number;
  colli: number | null;
  weightKg: number | null;
  volumeM3: number | null;
  loadingMeters: number | null;
  loadFactor: number | null;
  epq: number | null;
}

export interface SplitResult {
  remaining: LineMeasures;
  newLine: LineMeasures;
}

const DECIMALS = 100_000;
const round5 = (value: number) => Math.round(value * DECIMALS) / DECIMALS;

function split(value: number | null, remainingShare: number): { remaining: number | null; newPart: number | null } {
  if (value == null) return { remaining: null, newPart: null };
  const remaining = round5(value * remainingShare);
  return { remaining, newPart: round5(value - remaining) };
}

/** The two lines for `newAmount` on the new line; null when the amount does not leave both lines above zero. */
export function splitQuantities(total: LineMeasures, newAmount: number | null): SplitResult | null {
  if (newAmount == null || !isValidSplit(total.quantity, newAmount)) return null;
  const amount = round5(newAmount);
  const share = (total.quantity - amount) / total.quantity;
  const of = (key: Exclude<keyof LineMeasures, "quantity">) => split(total[key], share);
  const [colli, weightKg, volumeM3, loadingMeters, loadFactor, epq] = [of("colli"), of("weightKg"), of("volumeM3"), of("loadingMeters"), of("loadFactor"), of("epq")];
  return {
    remaining: { quantity: round5(total.quantity - amount), colli: colli.remaining, weightKg: weightKg.remaining, volumeM3: volumeM3.remaining, loadingMeters: loadingMeters.remaining, loadFactor: loadFactor.remaining, epq: epq.remaining },
    newLine: { quantity: amount, colli: colli.newPart, weightKg: weightKg.newPart, volumeM3: volumeM3.newPart, loadingMeters: loadingMeters.newPart, loadFactor: loadFactor.newPart, epq: epq.newPart },
  };
}

/** Both lines keep more than zero (the rule the server applies after rounding to 5 decimals). */
export function isValidSplit(total: number, newAmount: number): boolean {
  if (!Number.isFinite(newAmount)) return false;
  const amount = round5(newAmount);
  return amount > 0 && amount < total;
}

/** The other column's amount when one is edited; null when the typed text is no number. */
export function complementOf(total: number, amount: number | null): number | null {
  return amount == null || Number.isNaN(amount) ? null : round5(total - amount);
}
