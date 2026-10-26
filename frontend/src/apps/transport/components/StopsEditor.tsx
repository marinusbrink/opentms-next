import { useCallback, useMemo, useRef, useState } from "react";
import type { ColDef, ValueFormatterParams } from "ag-grid-community";
import { ArrowDown, ArrowUp, Plus, X } from "lucide-react";

import { Button } from "@/components/ui/button";
import { DATE_CELL_EDITOR_COL_DEF } from "@/components/ui/date-picker";
import { FormField } from "@/components/ui/form-field";
import { Input } from "@/components/ui/input";
import { LazyLinesGrid } from "@/components/ui/lines-grid-lazy";
import { referenceCellEditorColDef } from "@/components/ui/reference-cell-editor";
import type { ReferenceOption } from "@/components/ui/reference-combobox";
import { AddressField, ContactPersonField, RelationField, relationOptionOf } from "@/apps/transport/components/RelationFields";
import { newStop, type EditorMode, type StopValues } from "@/apps/transport/orders/order-form";
import { useCreateInPlace, type CreatableEntity } from "@/domains/master-data/components/create-in-place";
import { useFormat } from "@/lib/format";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The stops of a line, the order's stop pattern and a template line's stops as one LinesGrid (UI-GRID-014; design 61:
 * "Stops as a LinesGrid with add/remove/move, no nested modal", UX finding §6.3/6.4). Waypoint type and time slot are
 * picked in the cell (ReferenceCellEditor), the date in the date cell editor (order lines) or as a day offset (pattern,
 * template), window and reference typed inline; the position is editable and renumbers the list (move). The relation,
 * its explicit address (design 58: the address type of a stop is explicit) and the contact person are edited in the
 * inline detail under the grid for the stop opened with the pencil, Enter or a double click: still no modal. The type
 * and time-slot cells offer "Add new …" (UI-LOOK-004): the editor opens on top and the new record lands in the row. */

export type StopsKind = "line" | "pattern";

export interface StopsEditorProps {
  idPrefix: string;
  stops: StopValues[];
  onChange: (stops: StopValues[]) => void;
  mode: EditorMode;
  kind: StopsKind;
  readOnly: boolean;
  /** Order lines: the default date of a new stop. */
  orderDate: string | null;
  /** The order's pattern, copied onto a new line's stops; a new stop in a line starts empty. */
  waypointTypeOptions: ReferenceOption[];
  timeSlotOptions: ReferenceOption[];
  /** The columns the type requires (UI-FORM-002 marks): loading/unloading reference and window flags apply per activity on the server; the grid marks the columns. */
  requireReference?: boolean;
  requireWindow?: boolean;
  /** The maximum number of stops (design 56: ≤ 50 per line). */
  max?: number;
  heightClass?: string;
}

type Row = StopValues & { position: number };

/** A typed position moves the stop: the rows are re-sorted by it and renumbered 1..n. */
const renumber = (rows: Row[]) => [...rows].sort((a, b) => Number(a.position) - Number(b.position));

export function StopsEditor({
  idPrefix,
  stops,
  onChange,
  mode,
  kind,
  readOnly,
  orderDate,
  waypointTypeOptions,
  timeSlotOptions,
  requireReference,
  requireWindow,
  max = 50,
  heightClass = "h-56",
}: StopsEditorProps) {
  const { t } = useL();
  const f = useFormat();
  const [openKey, setOpenKey] = useState<string | null>(null);
  const useDate = mode === "order" && kind === "line";

  const rows = useMemo<Row[]>(() => stops.map((stop, index) => ({ ...stop, position: index + 1 })), [stops]);
  const open = rows.find((row) => row.key === openKey) ?? null;

  const labelOf = (options: ReferenceOption[], value: string | null) => options.find((o) => o.value === value)?.label ?? "";

  const commit = useCallback(
    (next: Row[]) => {
      const changedPosition = next.some((row, index) => row.position !== index + 1);
      const ordered = changedPosition ? renumber(next) : next;
      onChange(
        ordered.map(({ position: _position, ...stop }) => ({
          ...stop,
          waypointTypeName: labelOf(waypointTypeOptions, stop.waypointTypeId),
        })),
      );
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [onChange, waypointTypeOptions],
  );

  const update = (key: string, change: Partial<StopValues>) => onChange(stops.map((stop) => (stop.key === key ? { ...stop, ...change } : stop)));

  /* A record created from a cell lands in that row of the latest stops (the editor saves after later edits). */
  const latest = useRef({ stops, onChange });
  latest.current = { stops, onChange };
  const inPlace = useCreateInPlace();
  const { offer } = inPlace;
  const createFor = useCallback(
    (entity: CreatableEntity, field: "waypointTypeId" | "timeSlotId") => {
      const apply = (row: Row, id: string, name: string) =>
        latest.current.onChange(
          latest.current.stops.map((stop) => (stop.key === row.key ? { ...stop, [field]: id, ...(field === "waypointTypeId" ? { waypointTypeName: name } : {}) } : stop)),
        );
      const props = offer(entity, readOnly, () => undefined);
      return {
        createLabel: props.createLabel,
        onCreateNew: props.onCreateNew ? (term: string, row: Row) => offer(entity, readOnly, (r) => apply(row, r.id, r.name)).onCreateNew?.(term) : undefined,
      };
    },
    [offer, readOnly],
  );
  const waypointCreate = createFor("WaypointType", "waypointTypeId");
  const slotCreate = createFor("TimeSlot", "timeSlotId");

  const move = (key: string, delta: number) => {
    const index = stops.findIndex((stop) => stop.key === key);
    const target = index + delta;
    if (index < 0 || target < 0 || target >= stops.length) return;
    const next = [...stops];
    [next[index], next[target]] = [next[target], next[index]];
    onChange(next);
  };

  const add = () => {
    if (stops.length >= max) return;
    const stop = newStop(orderDate);
    onChange([...stops, stop]);
    setOpenKey(stop.key);
  };

  const mark = (label: string, required: boolean | undefined) => (required ? `${label} *` : label);

  const columns = useMemo<ColDef<Row>[]>(
    () => [
      { field: "position", headerName: t("Orders:Position"), width: 90, minWidth: 80, editable: !readOnly, cellEditor: "agNumberCellEditor", cellEditorParams: { min: 1, max, precision: 0 }, type: "rightAligned" },
      {
        field: "waypointTypeId",
        headerName: `${t("Orders:StopType")} *`,
        flex: 1,
        minWidth: 140,
        editable: !readOnly,
        ...referenceCellEditorColDef<Row>({ options: waypointTypeOptions, onCreateNew: waypointCreate.onCreateNew, createLabel: waypointCreate.createLabel }),
        valueFormatter: (p: ValueFormatterParams<Row, string | null>) => labelOf(waypointTypeOptions, p.value ?? null),
      },
      { field: "relationName", headerName: `${t("Orders:Relation")} *`, flex: 1.5, minWidth: 160 },
      ...(useDate
        ? [{ field: "date", headerName: t("Orders:Date"), minWidth: 120, editable: !readOnly, ...DATE_CELL_EDITOR_COL_DEF, valueFormatter: (p: ValueFormatterParams<Row, string | null>) => f.date(p.value) } as ColDef<Row>]
        : [{ field: "dayOffset", headerName: t("Orders:DayOffset"), width: 110, minWidth: 100, editable: !readOnly, type: "rightAligned" } as ColDef<Row>]),
      {
        field: "timeSlotId",
        headerName: t("Orders:TimeSlot"),
        minWidth: 130,
        editable: !readOnly,
        ...referenceCellEditorColDef<Row>({ options: timeSlotOptions, onCreateNew: slotCreate.onCreateNew, createLabel: slotCreate.createLabel }),
        valueFormatter: (p: ValueFormatterParams<Row, string | null>) => labelOf(timeSlotOptions, p.value ?? null),
      },
      { field: "windowFrom", headerName: mark(t("Orders:WindowFrom"), requireWindow), width: 100, minWidth: 90, editable: !readOnly },
      { field: "windowTill", headerName: mark(t("Orders:WindowTill"), requireWindow), width: 100, minWidth: 90, editable: !readOnly },
      { field: "reference", headerName: mark(t("Orders:Reference"), requireReference), flex: 1, minWidth: 120, editable: !readOnly },
      { field: "remarks", headerName: t("Orders:Remarks"), flex: 1, minWidth: 120, editable: !readOnly },
    ],
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [t, readOnly, max, waypointTypeOptions, timeSlotOptions, useDate, requireReference, requireWindow, f, waypointCreate.onCreateNew != null, slotCreate.onCreateNew != null],
  );

  return (
    <div className="flex flex-col gap-3" data-slot="stops-editor" data-kind={kind}>
      <div className={heightClass}>
        <LazyLinesGrid<Row>
          rows={rows}
          onRowsChange={commit}
          getRowId={(row) => row.key}
          columnDefs={columns}
          readOnly={readOnly}
          onOpen={(row) => setOpenKey(row.key)}
          emptyText={t("Orders:NoStops")}
          toolbar={
            stops.length < max ? (
              <Button type="button" size="sm" variant="outline" onClick={add}>
                <Plus /> {t("Orders:AddStop")}
              </Button>
            ) : undefined
          }
        />
      </div>
      {open && (
        <div className="rounded-lg border border-border-section bg-background p-3" data-slot="stop-detail" data-key={open.key}>
          <div className="mb-2 flex items-center gap-2">
            <span className="text-label font-semibold text-brand">
              {t("Orders:Stop")} {open.position}
            </span>
            {!readOnly && (
              <>
                <Button type="button" variant="ghost" size="icon-sm" aria-label={t("Orders:MoveUp")} title={t("Orders:MoveUp")} disabled={open.position <= 1} onClick={() => move(open.key, -1)}>
                  <ArrowUp />
                </Button>
                <Button type="button" variant="ghost" size="icon-sm" aria-label={t("Orders:MoveDown")} title={t("Orders:MoveDown")} disabled={open.position >= stops.length} onClick={() => move(open.key, 1)}>
                  <ArrowDown />
                </Button>
              </>
            )}
            <Button type="button" variant="ghost" size="icon-sm" className="ml-auto" aria-label={t("Close")} title={t("Close")} onClick={() => setOpenKey(null)}>
              <X />
            </Button>
          </div>
          <div className="grid grid-cols-4 gap-x-4 gap-y-3">
            <RelationField
              id={`${idPrefix}-${open.key}-relation`}
              label={t("Orders:Relation")}
              value={relationOptionOf(open.relationId, open.relationName)}
              onChange={(next) => update(open.key, { relationId: next?.id ?? null, relationName: next?.name ?? "", relationAddressId: null, contactPersonId: null })}
              required={kind === "line"}
              disabled={readOnly}
            />
            <AddressField
              id={`${idPrefix}-${open.key}-address`}
              label={t("Orders:Address")}
              relationId={open.relationId}
              value={open.relationAddressId}
              onChange={(next) => update(open.key, { relationAddressId: next })}
              required={kind === "line"}
              disabled={readOnly}
            />
            <ContactPersonField id={`${idPrefix}-${open.key}-contact`} labelKey="Orders:ContactPerson" relationId={open.relationId} value={open.contactPersonId} onChange={(next) => update(open.key, { contactPersonId: next })} disabled={readOnly} />
            <FormField label={t("Orders:PlannedDurationMin")} htmlFor={`${idPrefix}-${open.key}-duration`}>
              <Input id={`${idPrefix}-${open.key}-duration`} inputMode="numeric" className="tabular-nums" value={open.plannedDurationMin} disabled={readOnly} onChange={(e) => update(open.key, { plannedDurationMin: e.target.value })} />
            </FormField>
          </div>
        </div>
      )}
      {inPlace.dialog}
    </div>
  );
}
