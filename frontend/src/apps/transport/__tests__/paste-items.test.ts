import { describe, expect, it } from "vitest";

import { checkRow, looksLikeRows, matchByCodeOrName, parseDateCell, parsePastedRows, problemCells } from "@/apps/transport/orders/paste-items";

/* Paste from Excel on the order page (design 61 "Order page, legacy tab structure" 3): tab-separated rows in the paste
 * column order, relations and units matched on code or name, unmatched cells marked. Risk class: Medium. */

describe("parsePastedRows / looksLikeRows", () => {
  it("splits rows and cells in the entry row's order; empty lines go, missing cells are empty, quotes are stripped", () => {
    const rows = parsePastedRows('ACME\tBeta\t26-10-2026\t08:00\tPallet\t2\t500\tREF-1\r\n\r\n"Gamma BV"\tACME\n');
    expect(rows).toHaveLength(2);
    expect(rows[0]).toEqual({ from: "ACME", to: "Beta", date: "26-10-2026", time: "08:00", unit: "Pallet", quantity: "2", weight: "500", reference: "REF-1" });
    expect(rows[1]).toMatchObject({ from: "Gamma BV", to: "ACME", date: "", unit: "" });
    expect(looksLikeRows("Acme")).toBe(false);
    expect(looksLikeRows("Acme\n")).toBe(false);
    expect(looksLikeRows("Acme\tBeta")).toBe(true);
    expect(looksLikeRows("Acme\nBeta")).toBe(true);
  });
});

describe("matchByCodeOrName", () => {
  const relations = [
    { id: "1", code: "ACME", name: "Acme Logistics" },
    { id: "2", code: "BETA", name: "Beta" },
    { id: "3", code: "BETA2", name: "Beta" },
  ];
  it("one exact code or name (case-insensitive) is a match; several are ambiguous; none is unmatched; empty is empty", () => {
    expect(matchByCodeOrName("acme", relations)).toMatchObject({ status: "matched", value: { id: "1" } });
    expect(matchByCodeOrName("ACME LOGISTICS", relations)).toMatchObject({ status: "matched", value: { id: "1" } });
    expect(matchByCodeOrName("Beta", relations)).toMatchObject({ status: "ambiguous", value: null });
    expect(matchByCodeOrName("Acm", relations)).toMatchObject({ status: "unmatched", value: null });
    expect(matchByCodeOrName(" ", relations).status).toBe("empty");
  });
});

describe("parseDateCell / checkRow / problemCells", () => {
  it("reads ISO and the locale's pattern, with an optional time; a wrong date is not ok", () => {
    expect(parseDateCell("2026-10-26", "nl-NL")).toEqual({ date: { text: "2026-10-26", value: "2026-10-26", ok: true }, time: null });
    expect(parseDateCell("26-10-2026 8:15", "nl-NL")).toMatchObject({ date: { value: "2026-10-26", ok: true }, time: "08:15" });
    expect(parseDateCell("31-02-2026", "nl-NL").date.ok).toBe(false);
    expect(parseDateCell("", "nl-NL").date).toEqual({ text: "", value: null, ok: true });
  });

  it("marks an unmatched relation or unit, a bad date, time or number; the date cell's time fills an empty Time", () => {
    const relationMatch = (text: string) => matchByCodeOrName(text, [{ id: "r1", code: "ACME", name: "Acme" }]);
    const unitMatch = (text: string) => matchByCodeOrName(text, [{ id: "u1", code: "PL", name: "Pallet" }]);
    const [row] = parsePastedRows("ACME\tNobody\t26-10-2026 07:00\t\tCrate\tx\t12,5\tR");
    const checked = checkRow(row, "order", "nl-NL", relationMatch, unitMatch);
    expect(checked.from.value).toMatchObject({ id: "r1" });
    expect(checked.time.value).toBe("07:00");
    expect(checked.weight).toMatchObject({ ok: true, value: "12,5" });
    expect(problemCells(checked)).toEqual(["to", "unit", "quantity"]);
    const [template] = parsePastedRows("ACME\tACME\t2\t25:00\tpl");
    const checkedTemplate = checkRow(template, "template", "nl-NL", relationMatch, unitMatch);
    expect(checkedTemplate.dayOffset).toMatchObject({ ok: true, value: "2" });
    expect(problemCells(checkedTemplate)).toEqual(["time"]);
  });
});
