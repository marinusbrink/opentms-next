import type { EditableCallbackParams, ValueFormatterParams, ValueGetterParams } from "ag-grid-community";
import { useEffect, useState } from "react";

import type { DatePickerProps } from "@/components/ui/date-picker";
import type { EntityLookupProps } from "@/components/ui/entity-lookup";
import type { LinesGridProps } from "@/components/ui/lines-grid";

/* Native stand-ins for the design-61 Master data editor tests (the Transport run's lists), next to run2-test-mocks:
 * a LinesGrid that honours editable callbacks, number editors and value getters (the bracket ladder and the column
 * list need them), an EntityLookup that picks from a fixed relation set (or, with `lookupFromFetchPage`, from the first
 * page its own fetchPage returns), and a DatePicker as a plain text input.
 * Not a test file: vitest's include pattern matches *.test.* only. */

type Row = Record<string, unknown>;

export function MockLinesGridFn<TRow extends { key: string }>({ rows, onRowsChange, columnDefs, toolbar, readOnly = false, canRemoveRow }: LinesGridProps<TRow>) {
  return (
    <div data-slot="grid" data-readonly={readOnly ? "true" : undefined}>
      {!readOnly && toolbar}
      <div data-slot="columns">
        {columnDefs.map((c, i) => (
          <span key={i} data-column={(c.field ?? c.colId) as string}>
            {c.headerName}
          </span>
        ))}
      </div>
      {rows.map((row, index) => (
        <div key={row.key} data-slot="line" data-index={index}>
          {columnDefs.map((col, ci) => {
            const field = col.field as string | undefined;
            const params = { data: row, node: { rowIndex: index } } as unknown;
            const value = field ? (row as Row)[field] : typeof col.valueGetter === "function" ? col.valueGetter(params as ValueGetterParams<TRow>) : undefined;
            const editable = typeof col.editable === "function" ? col.editable(params as EditableCallbackParams<TRow>) : col.editable === true;
            if (editable && !readOnly && field) {
              const numeric = col.cellEditor === "agNumberCellEditor";
              return (
                <input
                  key={ci}
                  aria-label={`${col.headerName} ${index}`}
                  value={value == null ? "" : String(value)}
                  onChange={(e) =>
                    onRowsChange(rows.map((r) => (r === row ? ({ ...(r as Row), [field]: numeric ? (e.target.value === "" ? null : Number(e.target.value)) : e.target.value || null } as TRow) : r)))
                  }
                />
              );
            }
            const text = typeof col.valueFormatter === "function" ? String(col.valueFormatter({ value, data: row } as ValueFormatterParams<TRow>) ?? "") : value == null ? "" : String(value);
            return (
              <span key={ci} data-cell={(field ?? col.colId) as string}>
                {text}
              </span>
            );
          })}
          {!readOnly && (!canRemoveRow || canRemoveRow(row)) && (
            <button type="button" onClick={() => onRowsChange(rows.filter((r) => r !== row))}>
              Remove {index}
            </button>
          )}
        </div>
      ))}
    </div>
  );
}

/** The relations the mocked EntityLookup offers; a test sets them before rendering. */
export const lookupRelations: { current: { id: string; code: string; name: string; city: string | null; countryCode: string | null; roles: number }[] } = {
  current: [
    { id: "rel-1", code: "ACME", name: "Acme", city: "Venlo", countryCode: "NL", roles: 1 },
    { id: "rel-2", code: "DEPOT", name: "Own depot", city: "Tilburg", countryCode: "NL", roles: 8 | 16 },
  ],
};

/** When on, the mocked EntityLookup lists the first page of its own `fetchPage` (the generated client is the test's mock). */
export const lookupFromFetchPage = { current: false };

export function MockEntityLookup<T extends { id: string }>(props: EntityLookupProps<T>) {
  const [fetched, setFetched] = useState<T[] | null>(null);
  const { fetchPage } = props;
  useEffect(() => {
    if (lookupFromFetchPage.current) void fetchPage({ term: "", skip: 0, take: 25 }).then((page) => setFetched(page.items));
  }, [fetchPage]);
  const items = fetched ?? (lookupFromFetchPage.current ? [] : (lookupRelations.current as unknown as T[]));
  const value = props.value as T | null;
  return (
    <span data-picker={props.id}>
      <select
        id={props.id}
        disabled={props.disabled}
        aria-invalid={props["aria-invalid"]}
        value={value?.id ?? ""}
        onChange={(e) => (props as { onChange: (value: T | null) => void }).onChange(items.find((i) => i.id === e.target.value) ?? null)}
      >
        <option value="">—</option>
        {items.map((item) => (
          <option key={item.id} value={item.id}>
            {props.getLabel(item)}
          </option>
        ))}
      </select>
      {props.onCreateNew && (
        <button type="button" data-create-for={props.id} onClick={() => props.onCreateNew!("Typed")}>
          {props.createLabel ?? "AddNew"}
        </button>
      )}
    </span>
  );
}

export function MockDatePicker(props: DatePickerProps) {
  return <input id={props.id} value={props.value ?? ""} disabled={props.disabled} aria-invalid={props["aria-invalid"]} onChange={(e) => props.onChange(e.target.value || null)} />;
}

export const DATE_CELL_EDITOR_COL_DEF_MOCK = { cellEditor: function MockDateCellEditor() {
  return null;
} } as const;

export const ok = (data: unknown, status = 200) => ({ data, error: undefined, response: new Response(null, { status }) });
