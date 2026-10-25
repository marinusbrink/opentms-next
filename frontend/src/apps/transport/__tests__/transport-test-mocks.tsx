import { createContext, useContext, useEffect, useState, type ReactNode } from "react";
import type { ColDef, ValueFormatterParams, ValueGetterParams } from "ag-grid-community";
import { vi } from "vitest";

import type { DatePickerProps } from "@/components/ui/date-picker";
import type { EntityLookupProps } from "@/components/ui/entity-lookup";
import type { LinesGridProps } from "@/components/ui/lines-grid";
import type { MultiSelectProps } from "@/components/ui/multi-select";
import type { GridRequest, OpenTmsGridProps } from "@/components/ui/opentms-grid";
import type { ReferenceComboboxProps } from "@/components/ui/reference-combobox";
import type { TreeSelectProps } from "@/components/ui/tree-select";

/* Native stand-ins for the Base UI pickers and the AG Grids the Transport screens compose (design 61). The pickers
 * and grids have their own component tests; these mocks keep the props contract through plain controls so the
 * screens' own logic (filters, commands, DTO mapping, section saves, status ribbons) is what the tests exercise.
 * Not a test file: vitest's include pattern matches *.test.* only. */

type Row = Record<string, unknown>;
const EMPTY = "";

/* ── OpenTmsGrid → fetches once per fetcher identity, renders filters, columns, rows (cell values) and row actions ── */

export const gridRows: { current: unknown[] } = { current: [] };
export const GRID_REQUEST: GridRequest = { startRow: 0, endRow: 50, sortModels: [], columnFilters: {}, wildcardSearch: null, rowGroupCols: [], groupKeys: [] };

function cellValue<TRow>(col: ColDef<TRow>, row: TRow): unknown {
  if (col.field) return (row as Row)[col.field];
  if (typeof col.valueGetter === "function") return col.valueGetter({ data: row } as ValueGetterParams<TRow>);
  return undefined;
}

function cellText<TRow>(col: ColDef<TRow>, row: TRow, raw: unknown): ReactNode {
  if (typeof col.cellRenderer === "function") {
    const Renderer = col.cellRenderer as (p: { value: unknown; data: TRow }) => ReactNode;
    return <Renderer value={raw} data={row} />;
  }
  if (typeof col.valueFormatter === "function") return String(col.valueFormatter({ value: raw, data: row } as ValueFormatterParams<TRow>) ?? "");
  return raw == null ? "" : String(raw);
}

export function MockOpenTmsGrid<TRow>({ gridId, columnDefs, fetchRows, rowActions, filters, onSelectionChange, onOpen }: OpenTmsGridProps<TRow>) {
  /* The rows the last fetch returned, as the real grid shows; a test may also preset gridRows. */
  const [fetched, setFetched] = useState<TRow[] | null>(null);
  useEffect(() => {
    let cancelled = false;
    void fetchRows(GRID_REQUEST)
      .then((page) => {
        if (!cancelled) setFetched(page.rows);
      })
      .catch(() => undefined);
    return () => {
      cancelled = true;
    };
  }, [fetchRows]);
  const rows = fetched && fetched.length > 0 ? fetched : (gridRows.current as TRow[]);
  return (
    <div data-testid="mock-grid" data-grid-id={gridId}>
      {filters}
      <div data-slot="mock-grid-columns">
        {columnDefs.map((col, index) => (
          <span key={index} data-column={col.field ?? col.colId} data-hidden={col.hide ? "true" : undefined} data-sortable={col.sortable === false ? "false" : "true"}>
            {col.headerName}
          </span>
        ))}
      </div>
      {onSelectionChange && (
        <>
          <button type="button" onClick={() => onSelectionChange({ mode: "Explicit", explicitIds: rows.map((row) => (row as Row).id as string), filterRequest: null, excludedIds: [] })}>
            select-all
          </button>
          <button type="button" onClick={() => onSelectionChange({ mode: "Explicit", explicitIds: rows.slice(0, 1).map((row) => (row as Row).id as string), filterRequest: null, excludedIds: [] })}>
            select-first
          </button>
        </>
      )}
      {rows.map((row, index) => (
        <div key={index} data-slot="mock-grid-row" data-id={String((row as Row).id ?? "")}>
          {columnDefs.map((col, cIndex) => (
            <span key={cIndex} data-cell={col.field ?? col.colId}>
              {cellText(col, row, cellValue(col, row))}
            </span>
          ))}
          {onOpen && (
            <button type="button" onClick={() => onOpen(row)}>
              open-row
            </button>
          )}
          {rowActions?.([row]).map((action) => (
            <button key={action.id} type="button" disabled={action.disabled === true} title={action.disabled === true ? action.disabledReasonKey : undefined} onClick={action.onClick} data-row-action={action.id}>
              {action.labelKey}
            </button>
          ))}
        </div>
      ))}
    </div>
  );
}

/* ── LinesGrid → one block per line with a control per editable column ─────────────────────────────────────── */

function MockCell<TRow>({ col, row, readOnly, onChange }: { col: ColDef<TRow>; row: TRow; readOnly: boolean; onChange: (next: TRow) => void }) {
  const raw = cellValue(col, row);
  const label = col.headerName ?? col.field ?? "";
  const editable = (typeof col.editable === "function" ? col.editable({ data: row } as never) : col.editable === true) && !readOnly && col.field != null;
  const field = col.field as string;
  const update = (value: unknown) => onChange({ ...(row as Row), [field]: value } as TRow);
  if (!editable) {
    return (
      <span data-field={col.field ?? col.colId} data-cell>
        {cellText(col, row, raw)}
      </span>
    );
  }
  if (typeof col.cellEditor === "function" && (col.cellEditorParams as { options?: { value: string; label: string }[] } | undefined)?.options) {
    const options = (col.cellEditorParams as { options: { value: string; label: string }[] }).options;
    return (
      <select aria-label={label} value={(raw as string | null) ?? EMPTY} onChange={(e) => update(e.target.value || null)}>
        <option value={EMPTY}>—</option>
        {options.map((o) => (
          <option key={o.value} value={o.value}>
            {o.label}
          </option>
        ))}
      </select>
    );
  }
  return <input type="text" aria-label={label} value={raw == null ? "" : String(raw)} onChange={(e) => update(typeof col.cellEditor === "function" ? e.target.value || null : e.target.value)} />;
}

export function MockLinesGrid<TRow>({ rows, onRowsChange, getRowId, columnDefs, toolbar, readOnly = false, onOpen, canRemoveRow }: LinesGridProps<TRow>) {
  return (
    <div data-slot="mock-lines-grid" data-readonly={readOnly ? "true" : undefined}>
      {!readOnly && toolbar}
      <div data-slot="mock-lines-grid-columns">
        {columnDefs.map((col, index) => (
          <span key={index} data-column={col.field ?? col.colId} data-editable={col.editable === true ? "true" : undefined}>
            {col.headerName}
          </span>
        ))}
      </div>
      {rows.map((row) => {
        const key = getRowId(row);
        return (
          <div key={key} data-slot="mock-line" data-key={key}>
            {columnDefs.map((col, index) => (
              <MockCell key={index} col={col} row={row} readOnly={readOnly} onChange={(next) => onRowsChange(rows.map((r) => (r === row ? next : r)))} />
            ))}
            {!readOnly && onOpen && (
              <button type="button" onClick={() => onOpen(row)}>
                Edit
              </button>
            )}
            {!readOnly && (!canRemoveRow || canRemoveRow(row)) && (
              <button type="button" onClick={() => onRowsChange(rows.filter((r) => r !== row))}>
                Remove
              </button>
            )}
          </div>
        );
      })}
    </div>
  );
}

/* ── Pickers → native controls ────────────────────────────────────────────────────────────────────────────── */

export function MockReferenceCombobox(props: ReferenceComboboxProps) {
  return (
    <select id={props.id} disabled={props.disabled} aria-invalid={props["aria-invalid"]} aria-required={props["aria-required"]} value={props.value ?? EMPTY} onChange={(e) => props.onChange(e.target.value || null)}>
      <option value={EMPTY}>{props.placeholder ?? "—"}</option>
      {props.options.map((option) => (
        <option key={option.value} value={option.value}>
          {option.label}
        </option>
      ))}
    </select>
  );
}

export function MockMultiSelect(props: MultiSelectProps) {
  return (
    <div id={props.id} role="group" data-slot="mock-multi-select">
      {props.options.map((option) => (
        <label key={option.id}>
          <input type="checkbox" aria-label={option.label} disabled={props.disabled} checked={props.value.includes(option.id)} onChange={(e) => props.onChange(e.target.checked ? [...props.value, option.id] : props.value.filter((id) => id !== option.id))} />
          {option.label}
        </label>
      ))}
    </div>
  );
}

export function MockTreeSelect(props: TreeSelectProps) {
  const flat = (nodes: TreeSelectProps["nodes"]): { id: string; label: string }[] => nodes.flatMap((n) => [{ id: n.id, label: n.label }, ...flat(n.children ?? [])]);
  return (
    <select id={props.id} disabled={props.disabled} value={props.value ?? EMPTY} onChange={(e) => props.onChange(e.target.value || null)}>
      <option value={EMPTY}>{props.noneLabel ?? "—"}</option>
      {flat(props.nodes).map((o) => (
        <option key={o.id} value={o.id}>
          {o.label}
        </option>
      ))}
    </select>
  );
}

/** The lookup items a test offers; the mock picks by id through a native select (the server search is the real component's). */
export const lookupItems: { current: Record<string, { id: string; name?: string; label?: string }[]> } = { current: {} };

export function MockEntityLookup<T extends { id: string }>(props: EntityLookupProps<T>) {
  const items = (lookupItems.current[props.id] ?? []) as unknown as T[];
  if (props.multiple) {
    return (
      <div id={props.id} role="group" data-slot="mock-entity-lookup">
        {items.map((item) => (
          <label key={item.id}>
            <input
              type="checkbox"
              aria-label={props.getLabel(item)}
              disabled={props.disabled}
              checked={props.value.some((v) => v.id === item.id)}
              onChange={(e) => props.onChange(e.target.checked ? [...props.value, item] : props.value.filter((v) => v.id !== item.id))}
            />
            {props.getLabel(item)}
          </label>
        ))}
      </div>
    );
  }
  const current = props.value;
  return (
    <select id={props.id} disabled={props.disabled} aria-invalid={props["aria-invalid"]} value={current?.id ?? EMPTY} onChange={(e) => props.onChange(items.find((i) => i.id === e.target.value) ?? null)}>
      <option value={EMPTY}>{props.placeholder ?? "—"}</option>
      {current && !items.some((i) => i.id === current.id) && <option value={current.id}>{props.getLabel(current)}</option>}
      {items.map((item) => (
        <option key={item.id} value={item.id}>
          {props.getLabel(item)}
        </option>
      ))}
    </select>
  );
}

export function MockDatePicker(props: DatePickerProps) {
  return <input id={props.id} type="text" value={props.value ?? ""} disabled={props.disabled} aria-required={props["aria-required"]} aria-invalid={props["aria-invalid"]} placeholder={props.placeholder} onChange={(e) => props.onChange(e.target.value || null)} />;
}

/* ── Select (Base UI) → a native select ───────────────────────────────────────────────────────────────────── */

export function MockSelect(props: { items: { value: unknown; label: string }[]; value: unknown; onValueChange: (value: unknown) => void; disabled?: boolean; children?: ReactNode }) {
  return <SelectCtx.Provider value={props}>{props.children}</SelectCtx.Provider>;
}

const SelectCtx = createContext<{ items: { value: unknown; label: string }[]; value: unknown; onValueChange: (value: unknown) => void; disabled?: boolean } | null>(null);
const asValue = (value: unknown) => (value == null ? EMPTY : String(value));

export function MockSelectTrigger(props: { id?: string; "aria-invalid"?: boolean; children?: ReactNode; className?: string }) {
  const ctx = useContext(SelectCtx);
  if (!ctx) return null;
  return (
    <select id={props.id} aria-invalid={props["aria-invalid"]} disabled={ctx.disabled} value={asValue(ctx.value)} onChange={(e) => ctx.onValueChange(ctx.items.find((item) => asValue(item.value) === e.target.value)?.value ?? null)}>
      {!ctx.items.some((item) => asValue(item.value) === EMPTY) && <option value={EMPTY}>—</option>}
      {ctx.items.map((item) => (
        <option key={asValue(item.value)} value={asValue(item.value)}>
          {item.label}
        </option>
      ))}
    </select>
  );
}

export function Nothing() {
  return null;
}

/* ── Shared plumbing ──────────────────────────────────────────────────────────────────────────────────────── */

export const ok = (data: unknown) => ({ data, error: undefined, response: new Response() });
export const fail = (message: string, status = 400) => ({ data: undefined, error: { error: { message } }, response: new Response(null, { status }) });

export class MockResizeObserver {
  private readonly cb: ResizeObserverCallback;
  constructor(cb: ResizeObserverCallback) {
    this.cb = cb;
  }
  observe(el: Element) {
    this.cb([{ contentRect: { width: 9999 } as DOMRectReadOnly, target: el } as ResizeObserverEntry], this as unknown as ResizeObserver);
  }
  disconnect() {}
  unobserve() {}
}

export function stubGlobals() {
  vi.stubGlobal("ResizeObserver", MockResizeObserver);
  vi.stubGlobal("requestAnimationFrame", (cb: FrameRequestCallback) => {
    cb(0);
    return 0;
  });
}

/** The ribbon buttons of a RecordPage / EntityDialog by their label text. */
export const ribbonButton = (label: string) => Array.from(document.querySelectorAll<HTMLElement>("[data-slot=ribbon-button]")).find((b) => b.textContent?.trim() === label) ?? null;
export const ribbonLabels = () => Array.from(document.querySelectorAll<HTMLElement>("[data-slot=ribbon-button]")).map((b) => b.textContent?.trim());

/** A button outside the grids and the command bar's hidden measuring copy (command-bar and ribbon commands). */
export function barButton(label: string): HTMLElement {
  const found = Array.from(document.querySelectorAll<HTMLElement>("button")).find(
    (b) => !b.closest("[data-testid=mock-grid]") && !b.closest("[aria-hidden=true]") && b.textContent?.trim() === label,
  );
  if (!found) throw new Error(`no command ${label}`);
  return found;
}

/** A tab panel of a RecordPage by its tab label. */
export function panel(label: string): HTMLElement {
  const found = document.querySelector<HTMLElement>(`[role=tabpanel][aria-label="${label}"]`);
  if (!found) throw new Error(`no tab panel ${label}`);
  return found;
}
