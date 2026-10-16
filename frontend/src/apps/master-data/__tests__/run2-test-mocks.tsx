import { createContext, useContext, useEffect, type ReactNode } from "react";
import type { ColDef, ValueFormatterParams, ValueGetterParams } from "ag-grid-community";

import type { LinesGridProps } from "@/components/ui/lines-grid";
import type { MultiSelectProps } from "@/components/ui/multi-select";
import type { GridRequest, OpenTmsGridProps } from "@/components/ui/opentms-grid";
import type { ReferenceComboboxProps } from "@/components/ui/reference-combobox";

/* Native stand-ins for the Base UI pickers and the AG Grids the run-2 editors and lists compose
 * (design 43). The pickers and grids have their own component tests; these mocks expose the same
 * props through plain form controls so the editors' own logic (validation, DTO mapping, line
 * staging, confirmations) is what the tests exercise. Not a test file: vitest's include pattern
 * matches *.test.* only. */

/* ── Select (Base UI) → a native <select> ─────────────────────────────────────────────────────── */

interface SelectItem {
  value: unknown;
  label: string;
}

interface SelectContext {
  items: SelectItem[];
  value: unknown;
  onValueChange: (value: unknown) => void;
  disabled?: boolean;
}

const Ctx = createContext<SelectContext | null>(null);

export function MockSelect(props: SelectContext & { children?: ReactNode }) {
  return <Ctx.Provider value={props}>{props.children}</Ctx.Provider>;
}

const EMPTY = "";
const asOptionValue = (value: unknown) => (value == null ? EMPTY : String(value));

/** The trigger renders the native select, so the FormField label (htmlFor = trigger id) names it. */
export function MockSelectTrigger(props: { id?: string; "aria-label"?: string; "aria-invalid"?: boolean; children?: ReactNode; className?: string }) {
  const ctx = useContext(Ctx);
  if (!ctx) return null;
  const hasNullItem = ctx.items.some((item) => item.value == null);
  return (
    <select
      id={props.id}
      aria-label={props["aria-label"]}
      aria-invalid={props["aria-invalid"]}
      disabled={ctx.disabled}
      value={asOptionValue(ctx.value)}
      onChange={(e) => {
        const item = ctx.items.find((candidate) => asOptionValue(candidate.value) === e.target.value);
        ctx.onValueChange(item ? item.value : null);
      }}
    >
      {!hasNullItem && <option value={EMPTY}>—</option>}
      {ctx.items.map((item) => (
        <option key={asOptionValue(item.value)} value={asOptionValue(item.value)}>
          {item.label}
        </option>
      ))}
    </select>
  );
}

export function Nothing() {
  return null;
}

/* ── ReferenceCombobox → a native <select> ─────────────────────────────────────────────────────── */

export function MockReferenceCombobox(props: ReferenceComboboxProps) {
  return (
    <select
      id={props.id}
      disabled={props.disabled}
      aria-invalid={props["aria-invalid"]}
      aria-describedby={props["aria-describedby"]}
      aria-required={props["aria-required"]}
      value={props.value ?? EMPTY}
      onChange={(e) => props.onChange(e.target.value || null)}
    >
      <option value={EMPTY}>{props.placeholder ?? "—"}</option>
      {props.options.map((option) => (
        <option key={option.value} value={option.value}>
          {option.label}
        </option>
      ))}
    </select>
  );
}

/* ── MultiSelect → one native checkbox per option ──────────────────────────────────────────────── */

export function MockMultiSelect(props: MultiSelectProps) {
  return (
    <div id={props.id} role="group" data-slot="mock-multi-select">
      {props.options.map((option) => (
        <label key={option.id}>
          <input
            type="checkbox"
            aria-label={option.label}
            disabled={props.disabled}
            checked={props.value.includes(option.id)}
            onChange={(e) => props.onChange(e.target.checked ? [...props.value, option.id] : props.value.filter((id) => id !== option.id))}
          />
          {option.label}
        </label>
      ))}
    </div>
  );
}

/* ── LinesGrid → one block per line with a control per editable column ────────────────────────── */

type Row = Record<string, unknown>;

function cellValue<TRow>(col: ColDef<TRow>, row: TRow): unknown {
  if (col.field) return (row as Row)[col.field];
  if (typeof col.valueGetter === "function") return col.valueGetter({ data: row } as ValueGetterParams<TRow>);
  return undefined;
}

function cellText<TRow>(col: ColDef<TRow>, row: TRow, raw: unknown): string {
  if (typeof col.valueFormatter === "function") return String(col.valueFormatter({ value: raw, data: row } as ValueFormatterParams<TRow>) ?? "");
  return raw == null ? "" : String(raw);
}

function MockCell<TRow>({ col, row, readOnly, onChange }: { col: ColDef<TRow>; row: TRow; readOnly: boolean; onChange: (next: TRow) => void }) {
  const raw = cellValue(col, row);
  const text = cellText(col, row, raw);
  const label = col.headerName ?? col.field ?? "";
  const editable = col.editable === true && !readOnly && col.field != null;
  const field = col.field as string;
  const update = (value: unknown) => onChange({ ...(row as Row), [field]: value } as TRow);

  if (!editable) {
    return (
      <span data-field={col.field ?? col.colId} data-cell>
        {text}
      </span>
    );
  }
  if (col.cellRenderer === "agCheckboxCellRenderer") {
    return <input type="checkbox" aria-label={label} checked={Boolean(raw)} onChange={(e) => update(e.target.checked)} />;
  }
  if (col.cellEditor === "agRichSelectCellEditor") {
    const params = col.cellEditorParams as { values: unknown[]; formatValue?: (value: unknown) => string };
    return (
      <select aria-label={label} value={asOptionValue(raw)} onChange={(e) => update(params.values.find((v) => asOptionValue(v) === e.target.value))}>
        {params.values.map((value) => (
          <option key={asOptionValue(value)} value={asOptionValue(value)}>
            {params.formatValue ? params.formatValue(value) : asOptionValue(value)}
          </option>
        ))}
      </select>
    );
  }
  /* Text editors keep strings; a custom (date) editor hands over the ISO text or null. */
  const custom = typeof col.cellEditor === "function";
  return <input type="text" aria-label={label} value={raw == null ? "" : String(raw)} onChange={(e) => update(custom ? e.target.value || null : e.target.value)} />;
}

export function MockLinesGrid<TRow>({ rows, onRowsChange, getRowId, columnDefs, toolbar, readOnly = false, onOpen, canRemoveRow }: LinesGridProps<TRow>) {
  return (
    <div data-slot="mock-lines-grid" data-readonly={readOnly ? "true" : undefined}>
      {!readOnly && toolbar}
      <div data-slot="mock-lines-grid-columns">
        {columnDefs.map((col, index) => (
          <span
            key={index}
            data-column={col.field ?? col.colId}
            data-editable={col.editable === true ? "true" : undefined}
            data-cell-editor={typeof col.cellEditor === "function" ? col.cellEditor.name : (col.cellEditor as string | undefined)}
            data-popup={col.cellEditorPopup ? "true" : undefined}
          >
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
            {/* As the real grid (design 48): a row canRemoveRow refuses gets no Remove. */}
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

/* ── OpenTmsGrid → fetches once per fetcher identity, renders the filters, columns and row actions ─ */

/** The rows the mock grid shows; a test sets them before rendering the view. */
export const gridRows: { current: unknown[] } = { current: [] };

export const GRID_REQUEST: GridRequest = { startRow: 0, endRow: 50, sortModels: [], columnFilters: {}, wildcardSearch: null, rowGroupCols: [], groupKeys: [] };

export function MockOpenTmsGrid<TRow>({ gridId, columnDefs, fetchRows, rowActions, filters, onSelectionChange }: OpenTmsGridProps<TRow>) {
  useEffect(() => {
    void fetchRows(GRID_REQUEST).catch(() => undefined);
  }, [fetchRows]);
  const rows = gridRows.current as TRow[];
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
        <button
          type="button"
          onClick={() => onSelectionChange({ mode: "Explicit", explicitIds: rows.map((row) => (row as Row).id as string), filterRequest: null, excludedIds: [] })}
        >
          select-all
        </button>
      )}
      {rows.map((row, index) => (
        <div key={index} data-slot="mock-grid-row">
          {rowActions?.([row]).map((action) => (
            /* As the real grid: a disabled action (UI-PERM-005) does not run and carries its reason as the tooltip. */
            <button
              key={action.id}
              type="button"
              disabled={action.disabled === true}
              title={action.disabled === true ? action.disabledReasonKey : undefined}
              onClick={action.onClick}
            >
              {action.labelKey}
            </button>
          ))}
        </div>
      ))}
    </div>
  );
}
