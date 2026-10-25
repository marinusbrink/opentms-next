/// <reference types="node" />
import { readdirSync, readFileSync } from "node:fs";
import { join, relative, resolve } from "node:path";

import { describe, expect, it } from "vitest";

/* Static guard for UI-DLG-006 (PO decision 2026-10-21, docs/conventions/ui/ui-rules/dialog.md): the dialog never
 * scrolls; a tab panel or a content section scrolls. Two things are read from the sources, not from a render:
 *
 * 1. `components/ui/entity-dialog.tsx` itself: no `overflow-auto` (or `overflow-x-*`) anywhere; the popup
 *    (`<DialogContent`) and the body (`data-slot="entity-dialog-body"`) are `overflow-hidden`; every `overflow-y-auto`
 *    belongs to the tab panel or the content section element, next to `min-h-0`.
 * 2. Feature code (src/apps/**, src/domains/**): EntityDialog and MasterDataEditor take no `className` (the popup and
 *    the body cannot be restyled from a feature; the type says so, this keeps a reviewer from adding the prop
 *    quietly), and every scroll token (`overflow-auto`, `overflow-y-auto`, `overflow-x-auto`, `*-scroll`, with any
 *    variant prefix) is a documented region in ALLOWED: bounded in height (`min-h-0` in a sized column, or a `max-h-*`)
 *    so it scrolls inside its panel and never re-creates the panel's own scrollbar. A new region fails until it is
 *    listed with its reason; a stale entry fails too, so the list cannot rot. The scan covers every feature file,
 *    dialogs or not: a scroll region in feature code is a decision, and listing it costs one line.
 *
 * Risk class: Low (a convention check), kept because a re-introduced window-wide scrollbar is a reviewer blocker that
 * no component test of a single feature would notice. */

const SRC = resolve((import.meta as unknown as { dirname: string }).dirname, "../..");
const ENTITY_DIALOG = "components/ui/entity-dialog.tsx";
const ROOTS = [join(SRC, "apps"), join(SRC, "domains")];

/** Every scroll utility, with its variant prefixes (`lg:overflow-y-auto`). */
const SCROLL_TOKEN = /(?<![\w-])((?:[a-z0-9-]+:)*overflow-(?:x-|y-)?(?:auto|scroll))(?![\w-])/g;
const BOUNDED = /(?<![\w-])(?:[a-z0-9-]+:)*(?:min-h-0|max-h-[\w[\]().%-]+)(?![\w-])/;

/** Scroll regions in feature code, keyed by file and the exact token, each with why it is a region inside a panel. */
const ALLOWED: { file: string; token: string; reason: string }[] = [
  {
    file: "apps/admin/components/TechnicalLogbookEntryDialog.tsx",
    token: "overflow-auto",
    reason: "the exception text and an action's parameters: <pre> blocks capped at max-h-64 / max-h-48, self-contained inside the section",
  },
  {
    file: "apps/tenants/EditionFormDialog.tsx",
    token: "overflow-y-auto",
    reason: "the feature list: a min-h-0 flex-1 region in the full-height column of the Features panel (panelScroll)",
  },
  {
    file: "apps/master-data/editors/EmailTemplateEditor.tsx",
    token: "lg:overflow-y-auto",
    reason: "the form column: a lg:min-h-0 region beside the Tags panel in the full-height General panel (panelScroll); stacked, the panel scrolls",
  },
  {
    file: "apps/master-data/components/EmailTemplateTagsPanel.tsx",
    token: "overflow-y-auto",
    reason: "the tags list: a min-h-0 flex-1 region under the fixed caption, hint and filter of the Tags panel",
  },
  {
    file: "apps/transport/orders/LinesTab.tsx",
    token: "overflow-y-auto",
    reason: "the selected line's side panel: a min-h-0 flex-1 region under the fixed lines grid of the Lines tab (design 61: no nested modal)",
  },
];

interface Occurrence {
  file: string;
  line: number;
  token: string;
  /** The string literal the token sits in (the className piece). */
  literal: string;
}

function sourceFiles(dir: string): string[] {
  return readdirSync(dir, { withFileTypes: true }).flatMap((entry) => {
    if (entry.isDirectory()) return entry.name === "__tests__" ? [] : sourceFiles(join(dir, entry.name));
    return /\.tsx?$/.test(entry.name) && !/\.test\.tsx?$/.test(entry.name) ? [join(dir, entry.name)] : [];
  });
}

/** The source without comments (block comments, JSX comments and whole-line `//` comments), line structure kept. */
function stripComments(src: string): string {
  return src.replace(/\/\*[\s\S]*?\*\//g, (m) => m.replace(/[^\n]/g, " ")).replace(/^(\s*)\/\/.*$/gm, "$1");
}

const lineOf = (src: string, index: number) => src.slice(0, index).split("\n").length;

/** The quoted string literal around `index` (the className piece the token is part of). */
function literalAround(src: string, index: number): string {
  const before = src.slice(0, index);
  const start = Math.max(before.lastIndexOf('"'), before.lastIndexOf("'"), before.lastIndexOf("`"));
  const quote = src[start];
  const end = src.indexOf(quote, index);
  return start === -1 || end === -1 ? "" : src.slice(start + 1, end);
}

/** The JSX opening tag starting at `start`, up to its `>` at brace depth 0. */
function openingTag(src: string, start: number): string {
  let depth = 0;
  for (let i = start + 1; i < src.length; i++) {
    const c = src[i];
    if (c === "{") depth++;
    else if (c === "}") depth--;
    else if (depth === 0 && c === ">" && src[i - 1] !== "=") return src.slice(start, i + 1);
  }
  throw new Error(`unterminated tag at ${start}`);
}

/** The opening tag that encloses `index` (the last `<div` / `<Tag` before it). */
function enclosingTag(src: string, index: number): string {
  const before = src.slice(0, index);
  const start = before.search(/<[A-Za-z][\w.]*(?![\s\S]*<[A-Za-z])/);
  return openingTag(src, start);
}

function scan(path: string): Occurrence[] {
  const file = relative(SRC, path).split("\\").join("/");
  const src = stripComments(readFileSync(path, "utf-8"));
  return Array.from(src.matchAll(SCROLL_TOKEN)).map((m) => ({ file, line: lineOf(src, m.index!), token: m[1], literal: literalAround(src, m.index!) }));
}

/* ── 1. entity-dialog.tsx ───────────────────────────────────────────────────────────────────────────── */

const dialogSrc = stripComments(readFileSync(join(SRC, ENTITY_DIALOG), "utf-8"));

describe("UI-DLG-006 guard: entity-dialog.tsx never scrolls the popup or the body", () => {
  it("has no overflow-auto and nothing that scrolls horizontally", () => {
    const tokens = Array.from(dialogSrc.matchAll(SCROLL_TOKEN)).map((m) => m[1]);
    expect(tokens.filter((t) => /overflow-(auto|scroll|x-)/.test(t))).toEqual([]);
    expect(tokens.length, "the panel and the section scroll, nothing else").toBe(2);
  });

  it("the popup (DialogContent) is overflow-hidden up to the viewport ceiling", () => {
    const tag = openingTag(dialogSrc, dialogSrc.indexOf("<DialogContent"));
    expect(tag).toContain("overflow-hidden");
    expect(tag).toContain("max-h-[92vh]");
    expect(tag).not.toMatch(SCROLL_TOKEN);
  });

  it("the body is overflow-hidden and carries no padding", () => {
    const tag = openingTag(dialogSrc, dialogSrc.lastIndexOf("<div", dialogSrc.indexOf('data-slot="entity-dialog-body"')));
    expect(tag).toContain("overflow-hidden");
    expect(tag).not.toMatch(SCROLL_TOKEN);
    expect(tag).not.toMatch(/(?<![\w-])p[xytrbl]?-\d/);
  });

  it("every overflow-y-auto sits on the tab panel or the content section, next to min-h-0", () => {
    const owners = Array.from(dialogSrc.matchAll(SCROLL_TOKEN)).map((m) => enclosingTag(dialogSrc, m.index!));
    expect(owners).toHaveLength(2);
    const slots = owners.map((tag) => /data-slot="([^"]+)"/.exec(tag)?.[1]).sort();
    expect(slots).toEqual(["entity-dialog-panel", "entity-dialog-section"]);
    for (const tag of owners) expect(tag, tag).toMatch(/(?<![\w-])min-h-0(?![\w-])/);
  });
});

/* ── 2. feature code ────────────────────────────────────────────────────────────────────────────────── */

const featureFiles = ROOTS.flatMap(sourceFiles);
const occurrences = featureFiles.flatMap(scan);
const isAllowed = (o: Occurrence) => ALLOWED.some((a) => a.file === o.file && a.token === o.token);

describe("UI-DLG-006 guard: feature code scrolls only documented regions inside a panel", () => {
  it("finds the regions (the scan itself works)", () => {
    expect(occurrences.length).toBeGreaterThanOrEqual(ALLOWED.length);
    expect(occurrences.some((o) => o.file === "apps/tenants/EditionFormDialog.tsx" && o.token === "overflow-y-auto")).toBe(true);
    expect(occurrences.some((o) => o.file === "apps/master-data/editors/EmailTemplateEditor.tsx" && o.token === "lg:overflow-y-auto")).toBe(true);
    expect(stripComments('const a = 1; /* overflow-auto */\n// overflow-y-auto\nconst b = "x";')).not.toMatch(SCROLL_TOKEN);
    expect(literalAround('cn("a b", "min-h-0 overflow-y-auto", x)', 20)).toBe("min-h-0 overflow-y-auto");
  });

  it("no feature passes className to EntityDialog or MasterDataEditor (the popup and the body are not restyled from a feature)", () => {
    const offenders: string[] = [];
    for (const path of featureFiles) {
      const file = relative(SRC, path).split("\\").join("/");
      const src = stripComments(readFileSync(path, "utf-8"));
      for (const m of src.matchAll(/<(EntityDialog|MasterDataEditor)(?![\w])/g)) {
        const tag = openingTag(src, m.index!);
        if (/\sclassName\s*=/.test(tag)) offenders.push(`${file}:${lineOf(src, m.index!)} <${m[1]} className=…>`);
      }
    }
    expect(offenders).toEqual([]);
  });

  it("every scroll token in feature code is an ALLOWED region, and every region is bounded in height (min-h-0 or max-h-*)", () => {
    const unknown = occurrences.filter((o) => !isAllowed(o)).map((o) => `${o.file}:${o.line} ${o.token} in "${o.literal}"`);
    expect(unknown, "a scroll region UI-DLG-006 does not know: the panel scrolls; a region inside it is listed in ALLOWED with its reason").toEqual([]);
    const unbounded = occurrences.filter((o) => !BOUNDED.test(o.literal)).map((o) => `${o.file}:${o.line} ${o.token} in "${o.literal}"`);
    expect(unbounded, "a scroll region without min-h-0 / max-h-* never shrinks, so its scrollbar becomes the panel's").toEqual([]);
  });

  it("every ALLOWED entry still matches a region (no stale entries) and states its reason", () => {
    for (const entry of ALLOWED) {
      const matches = occurrences.filter((o) => o.file === entry.file && o.token === entry.token);
      expect(matches.length, `${entry.file} ${entry.token}: no such region any more (${entry.reason})`).toBeGreaterThan(0);
      expect(entry.reason.trim().length).toBeGreaterThan(0);
    }
  });
});
