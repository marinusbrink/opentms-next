/// <reference types="node" />
import { readFileSync } from "node:fs";
import { join, resolve } from "node:path";

import { describe, expect, it } from "vitest";

/* Static guard (design 70, "Selection bus"; UI-GRID-006 "single click on a row selects it"): the Planboard list panes
 * whose grid selection drives the other panes select a row on click. Found in the browser check on 2026-11-01: a click
 * on a trip row only focused it, so Map, Trip stops and Legs kept showing the previous trip until the checkbox was
 * ticked. The grid itself is AG Grid (not rendered in jsdom), so the prop is read from the sources.
 * Risk class: Low (a wiring check). */

const PANES = resolve((import.meta as unknown as { dirname: string }).dirname, "../panes");
const LIST_PANES = ["TripsPane.tsx", "LegsToSchedulePane.tsx", "LegsPane.tsx", "OrdersPane.tsx"];

describe("Planboard list panes select on row click", () => {
  it.each(LIST_PANES)("%s passes clickSelects next to onSelectionChange", (file) => {
    const source = readFileSync(join(PANES, file), "utf8");
    const grid = source.slice(source.indexOf("<LazyOpenTmsGrid"), source.indexOf("/>", source.indexOf("<LazyOpenTmsGrid")));
    expect(grid, `${file} renders a LazyOpenTmsGrid`).toContain("onSelectionChange=");
    expect(grid, `${file} must select on row click (design 70 selection bus)`).toMatch(/\bclickSelects\b/);
  });
});
