import type { ColDef } from "ag-grid-community";
import type { ComponentType, ReactNode } from "react";

import { featureChain } from "@/app/app-features";
import type { EntityEditorProps } from "@/apps/master-data/components/editor-props";
import type { EntityLookupProps } from "@/components/ui/entity-lookup";
import type { LinesGridProps } from "@/components/ui/lines-grid";
import type { MultiSelectProps } from "@/components/ui/multi-select";
import type { ReferenceComboboxProps } from "@/components/ui/reference-combobox";
import type { TreeSelectProps } from "@/components/ui/tree-select";

/* Stand-ins for the UI-LOOK-004 tests ("Add new …" in every record picker, PO decision 2026-10-16). The pickers keep
 * their value contract through native controls and expose the "Add new …" row as a button `data-create-for=<id>`
 * that hands over TYPED_TERM; the nested editors are stubs that show the initialName they got and save a fixed DTO.
 * The library pickers have their own component tests (create-new-row.test.tsx, reference-cell-editor.test.tsx).
 * Not a test file: vitest's include pattern matches *.test.* only. */

/** The feature of each creatable entity's navigation node (apps.config.ts), as the server's create endpoint requires it. */
export const CREATE_NODE_FEATURES = {
  Country: "Apps.MasterData.Reference.Countries",
  Currency: "Apps.MasterData.Reference.Currencies",
  Driver: "Apps.MasterData.Fleet.Drivers",
  GeographicalRegion: "Apps.MasterData.Reference.GeographicalRegions",
  Product: "Apps.MasterData.Transport.Products",
  Relation: "Apps.MasterData.Relations.Relations",
  RelationCategory: "Apps.MasterData.Relations.RelationCategories",
  Temperature: "Apps.MasterData.Transport.Temperatures",
  TransportCharacteristics: "Apps.MasterData.Fleet.TransportCharacteristics",
  TransportUnit: "Apps.MasterData.Transport.TransportUnits",
  Vehicle: "Apps.MasterData.Fleet.Vehicles",
  VehicleType: "Apps.MasterData.Fleet.VehicleTypes",
  OrganizationUnit: "Apps.Administration.OrganizationUnits",
  User: "Apps.Administration.Users",
} as const;

/** The feature values with each given node and all its ancestors on. */
export function featuresOn(...nodes: string[]): Record<string, string> {
  return Object.fromEntries(nodes.flatMap(featureChain).map((f) => [f, "true"]));
}

/** Every creatable entity's node chain on, and nothing else. */
export const ALL_CREATE_FEATURES = featuresOn(...Object.values(CREATE_NODE_FEATURES));

/** What the user "typed" before choosing Add new. */
export const TYPED_TERM = "Typed name";

function CreateRow({ id, onCreateNew, createLabel, term = TYPED_TERM }: { id: string; onCreateNew?: (term: string) => void; createLabel?: string; term?: string }) {
  if (!onCreateNew) return null;
  return (
    <button type="button" data-create-for={id} onClick={() => onCreateNew(term)}>
      {createLabel ?? "AddNew"}
    </button>
  );
}

export function PickerReferenceCombobox(props: ReferenceComboboxProps) {
  return (
    <span data-picker={props.id}>
      <select id={props.id} disabled={props.disabled} value={props.value ?? ""} onChange={(e) => props.onChange(e.target.value || null)}>
        <option value="">—</option>
        {props.options.map((o) => (
          <option key={o.value} value={o.value}>
            {o.label}
          </option>
        ))}
      </select>
      <CreateRow id={props.id} onCreateNew={props.onCreateNew} createLabel={props.createLabel} />
    </span>
  );
}

export function PickerMultiSelect(props: MultiSelectProps) {
  return (
    <span data-picker={props.id}>
      <span id={props.id} role="group">
        {props.options.map((o) => (
          <label key={o.id}>
            <input
              type="checkbox"
              data-option={o.id}
              aria-label={o.label}
              disabled={props.disabled}
              checked={props.value.includes(o.id)}
              onChange={(e) => props.onChange(e.target.checked ? [...props.value, o.id] : props.value.filter((v) => v !== o.id))}
            />
            {o.label}
          </label>
        ))}
      </span>
      <output data-value-for={props.id}>{props.value.join(",")}</output>
      <CreateRow id={props.id} onCreateNew={props.onCreateNew} createLabel={props.createLabel} />
    </span>
  );
}

export function PickerTreeSelect(props: TreeSelectProps) {
  return (
    <span data-picker={props.id}>
      <output id={props.id} data-value-for={props.id}>
        {props.value ?? ""}
      </output>
      <CreateRow id={props.id} onCreateNew={props.onCreateNew} createLabel={props.createLabel} term="" />
    </span>
  );
}

export function PickerEntityLookup<T extends { id: string }>(props: EntityLookupProps<T>) {
  const value = props.value as T | null;
  return (
    <span data-picker={props.id}>
      <output id={props.id} data-value-for={props.id} data-id={value?.id ?? ""}>
        {value ? props.getLabel(value) : ""}
      </output>
      <CreateRow id={props.id} onCreateNew={props.onCreateNew} createLabel={props.createLabel} />
    </span>
  );
}

/* ── LinesGrid: one block per row; a column with a component cell editor renders that editor live ────────────── */

type Row = Record<string, unknown>;

export function GridWithCellEditors<TRow>({ rows, onRowsChange, getRowId, columnDefs, readOnly = false }: LinesGridProps<TRow>) {
  return (
    <div data-slot="mock-lines-grid" data-readonly={readOnly ? "true" : undefined}>
      {rows.map((row) => {
        const key = getRowId(row);
        return (
          <div key={key} data-slot="mock-line" data-key={key}>
            {columnDefs.map((col: ColDef<TRow>, index) => {
              const field = col.field as string | undefined;
              const raw = field ? (row as Row)[field] : undefined;
              const update = (value: unknown) => onRowsChange(rows.map((r) => (r === row ? ({ ...(r as Row), [field!]: value } as TRow) : r)));
              if (typeof col.cellEditor === "function" && col.editable === true && !readOnly) {
                const Editor = col.cellEditor as ComponentType<Record<string, unknown>>;
                return (
                  <div key={index} data-cell-editor-for={field}>
                    {/* Keyed by the value: a changed cell is a fresh edit, as after AG Grid ended the previous one. */}
                    <Editor key={String(raw)} {...(col.cellEditorParams as Row)} value={raw ?? null} onValueChange={update} stopEditing={() => undefined} api={{ stopEditing: () => undefined }} data={row} eventKey={null} />
                  </div>
                );
              }
              return (
                <span key={index} data-field={field}>
                  {raw == null ? "" : String(raw)}
                </span>
              );
            })}
          </div>
        );
      })}
    </div>
  );
}

/* ── The nested editors ───────────────────────────────────────────────────────────────────────────────────── */

/** The DTO each stub saves; a test may replace one. */
export const savedDtos: Record<string, Record<string, unknown> | undefined> = {};

export function resetSavedDtos() {
  for (const key of Object.keys(savedDtos)) delete savedDtos[key];
  Object.assign(savedDtos, {
    Country: { id: "new-country", isoAlpha2: "XK", names: { en: "Kosovo", nl: "Kosovo (nl)" } },
    Currency: { id: "new-currency", isoCode: "CHF", names: { en: "Swiss franc", nl: "Zwitserse frank" } },
    Driver: { id: "new-driver", code: "D9", firstName: "Jan", lastName: "Smit", fullName: "Jan Smit" },
    GeographicalRegion: { id: "new-region", code: null, name: "Randstad" },
    Product: { id: "new-product", code: "P1", name: "Pallets" },
    Relation: { id: "new-relation", code: "NEW", name: "New Relation" },
    RelationCategory: { id: "new-category", code: null, name: "Category N" },
    Temperature: { id: "new-temperature", code: null, name: "Frozen" },
    TransportCharacteristics: { id: "new-characteristic", code: "TL", name: "Tail lift" },
    TransportUnit: { id: "new-unit", code: null, name: "Box pallet" },
    Vehicle: { id: "new-vehicle", code: null, name: "Truck 42", kind: 2, licencePlate: "AB-12-CD", vehicleTypeName: "Trailer" },
    VehicleType: { id: "new-type", code: null, name: "Low loader" },
    OrganizationUnit: { id: "new-ou", code: "00009", displayName: "Depot Venlo", parentId: null },
    User: { id: "new-user", userName: "jdoe", name: "Jane", surname: "Doe", email: "jane@example.test" },
    Edition: { id: "new-edition", displayName: "Platinum" },
  });
}
resetSavedDtos();

function StubShell({ name, initialName, mode, onSave, onClose, children }: { name: string; initialName?: string; mode: string; onSave: () => void; onClose: () => void; children?: ReactNode }) {
  return (
    <div data-stub-editor={name} data-mode={mode} data-initial-name={initialName ?? ""}>
      <button type="button" onClick={onSave}>
        {`stub-save-${name}`}
      </button>
      <button type="button" onClick={onClose}>
        {`stub-cancel-${name}`}
      </button>
      {children}
    </div>
  );
}

/** A master-data editor stub: on save it reports the DTO through onSaved and closes, as MasterDataEditor does. */
export function stubEditor(name: string) {
  return function StubEditor({ mode, initialName, onSaved, onClose }: EntityEditorProps) {
    return (
      <StubShell
        name={name}
        mode={mode}
        initialName={initialName}
        onClose={onClose}
        onSave={() => {
          onSaved(savedDtos[name]);
          onClose();
        }}
      />
    );
  };
}

/** An Administration / Tenants dialog stub (onSuccess(saved) then onClose, as the real dialogs do). */
export function stubDialog(name: string) {
  return function StubDialog({ mode, initialName, onSuccess, onClose }: { mode: string; initialName?: string; onSuccess: (saved?: unknown) => void; onClose: () => void }) {
    return (
      <StubShell
        name={name}
        mode={mode}
        initialName={initialName}
        onClose={onClose}
        onSave={() => {
          onSuccess(savedDtos[name]);
          onClose();
        }}
      />
    );
  };
}
