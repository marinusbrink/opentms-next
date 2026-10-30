/// <reference types="node" />
import { readdirSync, readFileSync } from "node:fs";
import { join, relative, resolve } from "node:path";

import { describe, expect, it } from "vitest";

/* Static guard for UI-LOOK-004 (PO decision 2026-10-16, docs/conventions/ui/ui-rules/lookup.md): every record picker
 * in a form offers "Add new …". It scans src/apps/**, src/domains/** and src/components/record-editor/** for <ReferenceCombobox, <MultiSelect, <TreeSelect and
 * <EntityLookup and fails on a usage that passes neither `onCreateNew=` nor an `offer(…)` spread
 * (useCreateInPlace) and carries no `DEVIATION(UI-LOOK-004)` marker in the lines above it. Excluded: the list-toolbar
 * filters (the rule's "Not covered"), and the pickers over fixed lists in ALLOWED, each with its reason. A stale
 * allow-list entry fails too, so the list cannot rot.
 *
 * Two more kinds of record picker are covered. A `<Select` in a form must be over a fixed list (the base Select
 * has no "Add new …" row, so a Select over records is a UI-LOOK-001 / UI-LOOK-004 violation): its `items` source is
 * read heuristically, as records when it comes from a lookup or query (`…Lookup`, `useQuery`, `.data`, `fetch…`),
 * as fixed when it maps an UPPER_CASE constant, uses Object.values/keys/entries or is an array literal; a source the
 * heuristic cannot read needs a SELECT_ALLOWED entry with its reason. Grid record columns: a
 * `referenceCellEditorColDef(…)` must pass `onCreateNew`, and an `agRichSelectCellEditor` (no "Add new …" row) is
 * allowed only over a fixed list in RICH_SELECT_ALLOWED. Risk class: Low (a convention check), kept because a picker
 * without the row is a reviewer blocker that no component test would notice. */

const SRC = resolve((import.meta as unknown as { dirname: string }).dirname, "../..");
/* Forms live in the apps and, when two apps share them, in the domains (the record editors the Master data and Transport
 * apps both open, the Administration dialogs); the editor kit's generic fields live in components/record-editor. */
const ROOTS = [join(SRC, "apps"), join(SRC, "domains"), join(SRC, "components", "record-editor")];
const formFiles = (ext?: ".tsx" | ".ts") => ROOTS.flatMap((root) => sourceFiles(root, ext));
const PICKER = /<(ReferenceCombobox|MultiSelect|TreeSelect|EntityLookup)\b/g;
const OFFERS = /\bonCreateNew\s*=|\boffer\s*(<[^>]*>)?\s*\(/;
const MARKER = /DEVIATION\(UI-LOOK-004\)/;
const MARKER_REACH = 8;

/** List toolbars: their pickers are filters, which the rule excludes. Every picker in these files must be a filter (id ending in "-filter"). */
const TOOLBAR_FILTER_VIEWS: Record<string, string> = {
  "apps/master-data/views/DriversView.tsx": "employer filter in the list toolbar",
  "apps/master-data/views/GeographicalRegionsView.tsx": "country filter in the list toolbar",
  "apps/master-data/views/RelationsView.tsx": "category filter in the list toolbar",
  "apps/master-data/views/VehiclesView.tsx": "owner filter in the list toolbar",
  "apps/transport/views/OrdersView.tsx": "status, customer and organisation unit filters in the list toolbar (design 61)",
  "apps/transport/views/TripsView.tsx": "status, carrier and organisation unit filters in the list toolbar (design 61)",
};

/** Pickers in forms over lists with nothing to create, keyed by file and the id expression the usage passes. */
const ALLOWED: { file: string; id: string; reason: string }[] = [
  { file: "domains/master-data/components/AddressFields.tsx", id: 'id("timeZoneId")', reason: "IANA time zones: a fixed list from the runtime, no record" },
  { file: "domains/master-data/components/CountryEditor.tsx", id: 'form.id("primaryTimeZoneId")', reason: "IANA time zones: a fixed list from the runtime, no record" },
  { file: "domains/master-data/components/DriverEditor.tsx", id: 'form.id("preferredCulture")', reason: "the tenant's ABP languages; adding a language is a PO decision, not a record" },
  { file: "domains/master-data/components/RelationEditor.tsx", id: 'form.id("preferredCulture")', reason: "the tenant's ABP languages; adding a language is a PO decision, not a record" },
  { file: "domains/master-data/components/RelationEditor.tsx", id: 'form.id("roles")', reason: "the RelationRoles flags enum: fixed values" },
];

/** Form Selects whose `items` the heuristic cannot read, keyed by file and the items expression, with why they are fixed. */
const SELECT_ALLOWED: { file: string; items: string; reason: string }[] = [
  { file: "domains/master-data/components/VehicleEditor.tsx", items: "items", reason: "enumSelect helper: called only with fuelItems / euroItems (FUEL_TYPES, EURO_CLASSES enums)" },
  { file: "components/record-editor/fields.tsx", items: "options", reason: "EnumSelectField helper: its items are the caller's localized enum table (sides, bases, scopes, targets, box sources), never records" },
  { file: "apps/tenants/EditionFormDialog.tsx", items: "items", reason: "an ABP selection feature's item source: fixed values of the feature definition, no record" },
];

/** agRichSelectCellEditor columns over fixed lists (it has no "Add new …" row), keyed by file and the `values:` expression. */
const RICH_SELECT_ALLOWED: { file: string; values: string; reason: string }[] = [
  { file: "domains/master-data/components/DriverEditor.tsx", values: "kindValues", reason: "the DriverQualificationKind enum (QUALIFICATION_KINDS): fixed values" },
  { file: "apps/master-data/editors/DocumentTypeEditor.tsx", values: "kindValues", reason: "the DocumentTransformationKind enum (DOCUMENT_TRANSFORMATION_KINDS): fixed values" },
  { file: "domains/financial/components/DiscountOrSurchargeEditor.tsx", values: "appliesAtValues", reason: "the RegionAppliesAt enum (REGION_APPLIES_AT): fixed values" },
];

interface Usage {
  file: string;
  line: number;
  component: string;
  id: string | null;
  element: string;
  offers: boolean;
  marked: boolean;
}

function sourceFiles(dir: string, extension: ".tsx" | ".ts" = ".tsx"): string[] {
  return readdirSync(dir, { withFileTypes: true }).flatMap((entry) => {
    if (entry.isDirectory()) return entry.name === "__tests__" ? [] : sourceFiles(join(dir, entry.name), extension);
    return entry.name.endsWith(extension) && !/\.test\.tsx?$/.test(entry.name) ? [join(dir, entry.name)] : [];
  });
}

/** The element from `<Tag` to its self-closing `/>` at brace depth 0 (pickers take no children). */
function elementAt(src: string, start: number, tagLength: number): string | null {
  let i = start + tagLength;
  if (src[i] === "<") i = src.indexOf(">", i) + 1; // a generic argument: <EntityLookup<RelationOption>
  let depth = 0;
  for (; i < src.length - 1; i++) {
    const c = src[i];
    if (c === "{") depth++;
    else if (c === "}") depth--;
    else if (depth === 0 && c === "/" && src[i + 1] === ">") return src.slice(start, i + 2);
    else if (depth === 0 && c === ">" && src[i - 1] !== "=") return null; // an opening tag with children
  }
  return null;
}

function idOf(element: string): string | null {
  const match = /\sid=(?:"([^"]*)"|\{([^}]*)\})/.exec(element);
  return match ? (match[1] !== undefined ? `"${match[1]}"` : match[2].trim()) : null;
}

function scan(): Usage[] {
  const usages: Usage[] = [];
  for (const path of formFiles()) {
    const file = relative(SRC, path).split("\\").join("/");
    const src = readFileSync(path, "utf-8");
    const lines = src.split("\n");
    for (const match of src.matchAll(PICKER)) {
      const line = src.slice(0, match.index).split("\n").length;
      const before = lines[line - 1].slice(0, match.index! - src.lastIndexOf("\n", match.index! - 1) - 1);
      if (/^\s*(\*|\/\/|\/\*)/.test(before)) continue; // inside a comment (usage examples in doc comments)
      const element = elementAt(src, match.index!, match[0].length);
      if (element === null) throw new Error(`${file}:${line}: <${match[1]}> is not self-closing; the UI-LOOK-004 guard cannot read it`);
      usages.push({
        file,
        line,
        component: match[1],
        id: idOf(element),
        element,
        offers: OFFERS.test(element),
        marked: lines.slice(Math.max(0, line - 1 - MARKER_REACH), line).some((l) => MARKER.test(l)),
      });
    }
  }
  return usages;
}

const usages = scan();
const isAllowed = (u: Usage) => ALLOWED.some((a) => a.file === u.file && a.id === u.id);

/* ── Selects, reference cell editors, rich-select cell editors ────────────────────────────────────────────── */

type Source = "fixed" | "records" | "unknown";

interface SelectUsage {
  file: string;
  line: number;
  items: string | null;
  source: Source;
  marked: boolean;
}

interface ColumnUsage {
  file: string;
  line: number;
  kind: "referenceCellEditorColDef" | "agRichSelectCellEditor";
  /** referenceCellEditorColDef: its argument; agRichSelectCellEditor: the `values:` expression. */
  detail: string;
  offers: boolean;
  marked: boolean;
}

const RECORDS = /Lookup\b|\blookup\b|\buseQuery\b|\.data\b|\bfetch[A-Z]\w*\(|\buse(Detail|List|All)\w*\(/;
const FIXED = /\b[A-Z][A-Z0-9_]+\.map\(|\bObject\.(values|keys|entries)\(|^\s*(\(\)\s*=>\s*)?\[/;

/** The text from `start` to the matching close of the bracket at `open` (or null). */
function balanced(src: string, open: number): string | null {
  const pairs: Record<string, string> = { "(": ")", "{": "}", "[": "]" };
  const close = pairs[src[open]];
  let depth = 0;
  for (let i = open; i < src.length; i++) {
    if (src[i] === src[open]) depth++;
    else if (src[i] === close && --depth === 0) return src.slice(open + 1, i);
  }
  return null;
}

/** Where an identifier's value comes from: its `const`/`let` initialiser (a useMemo's factory included). */
function definitionOf(src: string, name: string): string | null {
  const match = new RegExp(`\\b(?:const|let)\\s+${name}\\b[^=]*=\\s*`).exec(src);
  if (!match) return null;
  const start = match.index + match[0].length;
  const memo = /^useMemo\s*\(/.exec(src.slice(start));
  if (memo) return balanced(src, start + memo[0].length - 1);
  const end = src.indexOf(";\n", start);
  return src.slice(start, end === -1 ? undefined : end);
}

function classify(src: string, expression: string | null): Source {
  if (expression == null) return "unknown";
  const definition = /^[A-Za-z_$][\w$]*$/.test(expression) ? definitionOf(src, expression) : expression;
  if (definition == null) return "unknown";
  if (RECORDS.test(definition)) return "records";
  return FIXED.test(definition) ? "fixed" : "unknown";
}

const markedAt = (lines: string[], line: number) => lines.slice(Math.max(0, line - 1 - MARKER_REACH), line).some((l) => MARKER.test(l));
const inComment = (src: string, index: number) => /^\s*(\*|\/\/|\/\*)/.test(src.slice(src.lastIndexOf("\n", index - 1) + 1, index));

function scanSelects(): SelectUsage[] {
  const found: SelectUsage[] = [];
  for (const path of formFiles()) {
    const file = relative(SRC, path).split("\\").join("/");
    const src = readFileSync(path, "utf-8");
    const lines = src.split("\n");
    for (const match of src.matchAll(/<Select\b/g)) {
      if (inComment(src, match.index!)) continue;
      const line = src.slice(0, match.index).split("\n").length;
      const head = src.slice(match.index!, match.index! + 600);
      const itemsAt = head.search(/\sitems=\{/);
      const items = itemsAt === -1 ? null : (balanced(head, head.indexOf("{", itemsAt))?.trim() ?? null);
      found.push({ file, line, items, source: classify(src, items), marked: markedAt(lines, line) });
    }
  }
  return found;
}

function scanColumns(): ColumnUsage[] {
  const found: ColumnUsage[] = [];
  for (const path of formFiles().concat(formFiles(".ts"))) {
    const file = relative(SRC, path).split("\\").join("/");
    const src = readFileSync(path, "utf-8");
    const lines = src.split("\n");
    for (const match of src.matchAll(/\breferenceCellEditorColDef\s*(<[^>]*>)?\s*\(/g)) {
      if (inComment(src, match.index!)) continue;
      const line = src.slice(0, match.index).split("\n").length;
      const args = balanced(src, match.index! + match[0].length - 1) ?? "";
      found.push({ file, line, kind: "referenceCellEditorColDef", detail: args.trim(), offers: /\bonCreateNew\b/.test(args), marked: markedAt(lines, line) });
    }
    for (const match of src.matchAll(/["']agRichSelectCellEditor["']/g)) {
      if (inComment(src, match.index!)) continue;
      const line = src.slice(0, match.index).split("\n").length;
      const values = /\bvalues:\s*([^,\n}]+)/.exec(src.slice(match.index!, match.index! + 400));
      found.push({ file, line, kind: "agRichSelectCellEditor", detail: values ? values[1].trim() : "?", offers: false, marked: markedAt(lines, line) });
    }
  }
  return found;
}

const selects = scanSelects();
const columns = scanColumns();
const formSelects = selects.filter((u) => !(u.file in TOOLBAR_FILTER_VIEWS));
const isSelectAllowed = (u: SelectUsage) => SELECT_ALLOWED.some((a) => a.file === u.file && a.items === u.items);
const isRichSelectAllowed = (u: ColumnUsage) => RICH_SELECT_ALLOWED.some((a) => a.file === u.file && a.values === u.detail);

describe("UI-LOOK-004 guard: every record picker in a form offers Add new", () => {
  it("finds the pickers (the scan itself works)", () => {
    expect(usages.length).toBeGreaterThan(25);
    expect(usages.find((u) => u.file === "domains/master-data/components/VehicleEditor.tsx" && u.id === 'form.id("vehicleTypeId")')?.offers).toBe(true);
    expect(usages.find((u) => u.file === "domains/master-data/components/DriverEditor.tsx" && u.id === 'form.id("user")')?.offers).toBe(true);
    expect(usages.find((u) => u.file === "domains/platform/components/OrganizationUnitFormDialog.tsx" && u.component === "TreeSelect")?.offers).toBe(true);
    expect(usages.find((u) => u.file === "apps/tenants/TenantFormDialog.tsx" && u.component === "ReferenceCombobox")?.offers).toBe(true);
  });

  it("every picker in a form passes onCreateNew / offer(…) or carries a DEVIATION(UI-LOOK-004) marker", () => {
    const missing = usages
      .filter((u) => !(u.file in TOOLBAR_FILTER_VIEWS))
      .filter((u) => !u.offers && !u.marked && !isAllowed(u))
      .map((u) => `${u.file}:${u.line} <${u.component} id=${u.id ?? "?"}>`);
    expect(missing, "pickers without Add new (UI-LOOK-004): add onCreateNew via useCreateInPlace().offer, or a DEVIATION marker").toEqual([]);
  });

  it("the excluded toolbar views hold only filters", () => {
    const notFilters = usages.filter((u) => u.file in TOOLBAR_FILTER_VIEWS && !(u.id ?? "").endsWith('-filter"')).map((u) => `${u.file}:${u.line} id=${u.id}`);
    expect(notFilters, "a picker in a toolbar-filter view that is not a filter is a form picker: give it Add new").toEqual([]);
    for (const view of Object.keys(TOOLBAR_FILTER_VIEWS)) expect(usages.some((u) => u.file === view), `${view} has no picker any more: drop it from TOOLBAR_FILTER_VIEWS`).toBe(true);
  });

  it("every allow-list entry still matches a picker without Add new (no stale entries)", () => {
    for (const entry of ALLOWED) {
      const matches = usages.filter((u) => u.file === entry.file && u.id === entry.id);
      expect(matches.length, `${entry.file} ${entry.id}: no such picker any more (${entry.reason})`).toBeGreaterThan(0);
      expect(
        matches.every((u) => !u.offers),
        `${entry.file} ${entry.id} now offers Add new: drop the allow-list entry`,
      ).toBe(true);
      expect(entry.reason.trim().length).toBeGreaterThan(0);
    }
  });
});

describe("UI-LOOK-004 guard: a Select in a form is over a fixed list", () => {
  it("finds the Selects and reads their sources (the heuristic itself works)", () => {
    expect(formSelects.length).toBeGreaterThan(5);
    expect(formSelects.find((u) => u.file === "domains/master-data/components/VehicleTypeEditor.tsx" && u.items === "kindItems")?.source).toBe("fixed");
    expect(formSelects.find((u) => u.file === "domains/master-data/components/RelationEditor.tsx" && u.items === "blockReasonOptions")?.source).toBe("fixed");
    expect(classify("const opts = useMemo(() => (lookup.data ?? []).map((c) => c), [lookup.data]);", "opts")).toBe("records");
    expect(classify("const opts = useMemo(() => CURRENCIES.map((c) => c), []);\nconst x = countries.useLookup(open);", "opts")).toBe("fixed");
    expect(classify("const opts = vehicleTypes.useLookup(open).data ?? [];\n", "opts")).toBe("records");
    expect(classify("const opts = Object.values(Kinds).map((k) => k);\n", "opts")).toBe("fixed");
    expect(classify("const opts = [{ value: 1 }];\n", "opts")).toBe("fixed");
    expect(classify("", "param")).toBe("unknown");
  });

  it("no form Select over records (use EntityLookup / ReferenceCombobox with Add new), and every other source is readable or allow-listed", () => {
    const records = formSelects.filter((u) => u.source === "records" && !u.marked).map((u) => `${u.file}:${u.line} <Select items={${u.items}}>`);
    expect(records, "a Select over records has no Add new row (UI-LOOK-001, UI-LOOK-004): use a record picker with useCreateInPlace().offer").toEqual([]);
    const unread = formSelects
      .filter((u) => u.source === "unknown" && !u.marked && !isSelectAllowed(u))
      .map((u) => `${u.file}:${u.line} <Select items={${u.items ?? "?"}}>`);
    expect(unread, "the guard cannot tell where these Selects' items come from: add a SELECT_ALLOWED entry with the reason the list is fixed").toEqual([]);
  });

  it("every SELECT_ALLOWED entry still matches a Select the heuristic cannot read (no stale entries)", () => {
    for (const entry of SELECT_ALLOWED) {
      const matches = formSelects.filter((u) => u.file === entry.file && u.items === entry.items);
      expect(matches.length, `${entry.file} items={${entry.items}}: no such Select any more (${entry.reason})`).toBeGreaterThan(0);
      expect(matches.every((u) => u.source === "unknown"), `${entry.file} items={${entry.items}}: the heuristic reads it now; drop the entry`).toBe(true);
      expect(entry.reason.trim().length).toBeGreaterThan(0);
    }
  });
});

describe("UI-LOOK-004 guard: grid record columns offer Add new", () => {
  it("finds the grid record columns (the scan itself works)", () => {
    expect(columns.some((c) => c.kind === "referenceCellEditorColDef" && c.file === "domains/master-data/components/TransportUnitEditor.tsx" && c.offers)).toBe(true);
    expect(columns.some((c) => c.kind === "agRichSelectCellEditor" && c.file === "domains/master-data/components/DriverEditor.tsx")).toBe(true);
  });

  it("every referenceCellEditorColDef passes onCreateNew or carries a DEVIATION(UI-LOOK-004) marker", () => {
    const missing = columns.filter((c) => c.kind === "referenceCellEditorColDef" && !c.offers && !c.marked).map((c) => `${c.file}:${c.line}`);
    expect(missing, "a reference cell editor without onCreateNew: pass it via useCreateInPlace().offer").toEqual([]);
  });

  it("no agRichSelectCellEditor over records (it has no Add new row): only allow-listed fixed lists", () => {
    const records = columns
      .filter((c) => c.kind === "agRichSelectCellEditor" && !c.marked && !isRichSelectAllowed(c))
      .map((c) => `${c.file}:${c.line} values: ${c.detail}`);
    expect(records, "use referenceCellEditorColDef with onCreateNew for a record column; a fixed list goes into RICH_SELECT_ALLOWED with its reason").toEqual([]);
  });

  it("every RICH_SELECT_ALLOWED entry still matches a column over a fixed list (no stale entries)", () => {
    for (const entry of RICH_SELECT_ALLOWED) {
      const matches = columns.filter((c) => c.kind === "agRichSelectCellEditor" && c.file === entry.file && c.detail === entry.values);
      expect(matches.length, `${entry.file} values: ${entry.values}: no such column any more (${entry.reason})`).toBeGreaterThan(0);
      const src = readFileSync(join(SRC, entry.file), "utf-8");
      expect(classify(src, entry.values), `${entry.file} values: ${entry.values} is not a fixed list (${entry.reason})`).toBe("fixed");
    }
  });
});
