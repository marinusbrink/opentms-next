import { useCallback, useMemo, useState } from "react";
import type { ColDef, ValueFormatterParams } from "ag-grid-community";
import { TriangleAlert } from "lucide-react";

import { Button } from "@/components/ui/button";
import { CalculatingIndicator, calculatingValueClass } from "@/components/ui/calculating-indicator";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import { LazyLinesGrid } from "@/components/ui/lines-grid-lazy";
import { referenceCellEditorColDef } from "@/components/ui/reference-cell-editor";
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui/tooltip";
import { toOptions } from "@/apps/transport/components/fields";
import { TotalsBlock } from "@/apps/transport/components/TotalsBlock";
import { FreightItemDialog } from "@/apps/transport/orders/FreightItemDialog";
import { FreightItemEntry } from "@/apps/transport/orders/FreightItemEntry";
import { REQUIRED_FLAG, entryWaypointsOf, hasFlag, lineProblemText, lineProblemsOf, type EditorMode, type LineValues, type OrderValues } from "@/apps/transport/orders/order-form";
import { PasteItemsDialog } from "@/apps/transport/orders/PasteItemsDialog";
import type { PricePreviewState } from "@/apps/transport/orders/use-price-preview";
import type { RateLineSetDto } from "@/domains/financial/rate-lines";
import { useCreateInPlace } from "@/domains/master-data/components/create-in-place";
import { transportUnits } from "@/domains/master-data/entities";
import { useWaypointTypeActivities, type TransportOrderTypeDto } from "@/domains/orders/reference-data";
import type { LegDto } from "@/domains/planning-execution/legs";
import type { FormContext } from "@/lib/forms/use-form-state";
import { useFormat } from "@/lib/format";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { useOpenRecord } from "@/lib/use-open-record";

/* The Freight items tab of General (design 61 "Order page, legacy tab structure" 3), top to bottom: the entry of a new
 * item (the stop strip, the freight fields, Add and Paste from Excel), then the freight items grid (LinesGrid):
 * position, from → to (first and last stop: relation, city), date (template: day offset), transport unit, quantity,
 * positions, weight, loading metres, reference (the first stop's), price (the item's stored rate-line total with the
 * CalculatingIndicator) and trip (a link when the item's legs are on a trip); unit, quantities and reference are edited
 * in the cell; the footer gives the freight items total. Double-click, Enter or the pencil opens Edit freight item
 * (FreightItemDialog); Delete asks "Delete freight item {n}?". An item with a problem is marked in the grid; once the
 * items were touched or submitted, the first problem shows above the grid, a stop's naming the stop. */

const MAX_LINES = 200;

type Row = LineValues & { position: number; route: string; when: string; reference: string; price: number | null; trips: { id: string; number: string }[]; problem: string | null };

export interface FreightItemsProps {
  form: FormContext<OrderValues>;
  mode: EditorMode;
  type: TransportOrderTypeDto | undefined;
  orderId: string | undefined;
  legsCreated: boolean;
  legs: LegDto[] | undefined;
  rateLines: RateLineSetDto | undefined;
  preview: PricePreviewState;
  canEditRateLines: boolean;
  /** The item open in Edit freight item (by form key), owned by the page so a save keeps it open. */
  editingKey: string | null;
  onEdit: (key: string | null) => void;
}

export function FreightItems({ form, mode, type, orderId, legsCreated, legs, rateLines, preview, canEditRateLines, editingKey, onEdit }: FreightItemsProps) {
  const { t } = useL();
  const f = useFormat();
  const openRecord = useOpenRecord();
  const flags = type?.requiredFields as number | undefined;
  const unitLookup = transportUnits.useLookup();
  const unitOptions = useMemo(() => toOptions(unitLookup.data), [unitLookup.data]);
  /* A pasted row has a From and a To: the order type's first and last default waypoint, else Load and Unload. */
  const activities = useWaypointTypeActivities(!form.readOnly && (type?.defaultWaypoints ?? []).length < 2);
  const pasteWaypoints = useMemo(() => entryWaypointsOf(type?.defaultWaypoints, activities.data), [type?.defaultWaypoints, activities.data]);
  const [paste, setPaste] = useState<string | null>(null);
  const [deleting, setDeleting] = useState<Row | null>(null);

  /* "Add new …" from the unit cell: the record lands in that item of the latest values. */
  const inPlace = useCreateInPlace();
  const { offer } = inPlace;
  const { readOnly, update: updateValues } = form;
  const unitCreate = useMemo(() => {
    const props = offer("TransportUnit", readOnly, () => undefined);
    return {
      createLabel: props.createLabel,
      onCreateNew: props.onCreateNew
        ? (term: string, row: Row) =>
            offer("TransportUnit", readOnly, (record) =>
              updateValues((values) => ({ ...values, lines: values.lines.map((line) => (line.key === row.key ? { ...line, transportUnitId: record.id, transportUnitName: record.name } : line)) })),
            ).onCreateNew?.(term)
        : undefined,
    };
  }, [offer, readOnly, updateValues]);

  const priceOf = useMemo(() => {
    const byAnchor = new Map((rateLines?.totals?.byAnchor ?? []).map((a) => [a.anchorId?.toLowerCase() ?? "", a.amount ?? 0]));
    const sums = new Map<string, number>();
    for (const line of rateLines?.lines ?? []) {
      const id = line.orderLineId?.toLowerCase();
      if (id) sums.set(id, (sums.get(id) ?? 0) + (line.amount ?? 0));
    }
    return (id: string | null) => (id ? (byAnchor.get(id.toLowerCase()) ?? sums.get(id.toLowerCase()) ?? null) : null);
  }, [rateLines]);
  const tripsOf = useMemo(() => {
    const byLine = new Map<string, { id: string; number: string }[]>();
    for (const leg of legs ?? []) {
      const id = leg.orderLineId?.toLowerCase();
      if (!id || !leg.tripId) continue;
      const trips = byLine.get(id) ?? [];
      if (!trips.some((trip) => trip.id === leg.tripId)) trips.push({ id: leg.tripId, number: leg.tripNumber ?? "" });
      byLine.set(id, trips);
    }
    return (id: string | null) => (id ? (byLine.get(id.toLowerCase()) ?? []) : []);
  }, [legs]);

  const problems = useMemo(() => lineProblemsOf(form.values.lines, mode), [form.values.lines, mode]);
  const rows = useMemo<Row[]>(
    () =>
      form.values.lines.map((line, index) => {
        const first = line.stops[0];
        const last = line.stops.length > 1 ? line.stops.at(-1) : undefined;
        const end = (stop: typeof first | undefined) => (stop ? [stop.relationName, stop.city].filter(Boolean).join(", ") : "");
        const when = first ? (mode === "order" ? [f.date(first.date), first.windowFrom].filter(Boolean).join(" ") : `+${first.dayOffset || "0"} ${first.windowFrom}`.trim()) : "";
        const problem = problems.get(line.key);
        return {
          ...line,
          position: index + 1,
          route: [end(first), end(last)].filter(Boolean).join(" → "),
          when,
          reference: first?.reference ?? "",
          price: priceOf(line.id),
          trips: tripsOf(line.id),
          problem: problem ? lineProblemText(t, line, problem) : null,
        };
      }),
    [form.values.lines, mode, f, t, priceOf, tripsOf, problems],
  );

  const setLines = useCallback(
    (lines: LineValues[]) => {
      form.set("lines", lines);
      form.blur("lines");
    },
    [form],
  );
  const labelOf = (value: string | null) => unitOptions.find((o) => o.value === value)?.label ?? "";
  const onRowsChange = (next: Row[]) =>
    setLines(
      next.map(({ position: _position, route: _route, when: _when, reference, price: _price, trips: _trips, problem: _problem, ...line }) => ({
        ...line,
        transportUnitName: labelOf(line.transportUnitId) || line.transportUnitName,
        stops: line.stops.map((stop, index) => (index === 0 && stop.reference !== reference ? { ...stop, reference } : stop)),
      })),
    );

  const state = rateLines?.state;
  const mark = (label: string, flag: number) => (hasFlag(flags, flag) ? `${label} *` : label);
  const columns = useMemo<ColDef<Row>[]>(
    () => [
      {
        colId: "problem",
        headerName: "",
        width: 40,
        minWidth: 40,
        maxWidth: 40,
        sortable: false,
        valueGetter: (p) => p.data?.problem,
        cellRenderer: (p: { value: string | null }) =>
          p.value ? (
            <Tooltip>
              <TooltipTrigger render={<span className="inline-flex text-destructive" data-slot="item-problem" />}>
                <TriangleAlert aria-label={p.value} className="size-4" />
              </TooltipTrigger>
              <TooltipContent>{p.value}</TooltipContent>
            </Tooltip>
          ) : null,
      },
      { field: "position", headerName: t("Orders:Position"), minWidth: 60, maxWidth: 80, type: "rightAligned", cellClass: "tabular-nums" },
      { field: "route", headerName: t("Orders:Route"), flex: 3, minWidth: 220 },
      { field: "when", headerName: mode === "order" ? t("Orders:DateTime") : t("Orders:DayOffset"), flex: 1, minWidth: 120, cellClass: "tabular-nums" },
      {
        field: "transportUnitId",
        headerName: `${t("Orders:TransportUnit")} *`,
        flex: 1,
        minWidth: 130,
        editable: !form.readOnly,
        ...referenceCellEditorColDef<Row>({ options: unitOptions, onCreateNew: unitCreate.onCreateNew, createLabel: unitCreate.createLabel }),
        valueFormatter: (p: ValueFormatterParams<Row, string | null>) => labelOf(p.value ?? null) || p.data?.transportUnitName || "",
      },
      { field: "quantity", headerName: mark(t("Orders:Quantity"), REQUIRED_FLAG.QuantityPerLine), flex: 1, minWidth: 90, editable: !form.readOnly, type: "rightAligned", cellClass: "tabular-nums" },
      { field: "positions", headerName: t("Orders:Positions"), flex: 1, minWidth: 90, editable: !form.readOnly, type: "rightAligned", cellClass: "tabular-nums" },
      { field: "weightKg", headerName: mark(t("Orders:WeightKg"), REQUIRED_FLAG.WeightPerLine), flex: 1, minWidth: 100, editable: !form.readOnly, type: "rightAligned", cellClass: "tabular-nums" },
      { field: "loadingMeters", headerName: mark(t("Orders:LoadingMeters"), REQUIRED_FLAG.LoadingMetersPerLine), flex: 1, minWidth: 100, editable: !form.readOnly, type: "rightAligned", cellClass: "tabular-nums" },
      { field: "reference", headerName: mark(t("Orders:Reference"), REQUIRED_FLAG.LoadingReference), flex: 1, minWidth: 120, editable: !form.readOnly },
      ...(mode === "order"
        ? [
            {
              field: "price",
              headerName: `${t("Financial:Amount")} (${rateLines?.currencyCode ?? form.values.terms.currencyCode})`,
              flex: 1,
              minWidth: 120,
              type: "rightAligned",
              cellRenderer: (p: { value: number | null }) => (
                <span className="inline-flex items-center justify-end gap-1">
                  <CalculatingIndicator state={state} showLabel={false} />
                  <span className={`tabular-nums ${calculatingValueClass(state) ?? ""}`}>{p.value == null ? "" : f.amount(p.value)}</span>
                </span>
              ),
            } as ColDef<Row>,
            {
              field: "trips",
              headerName: t("PlanningExecution:Trip"),
              flex: 1,
              minWidth: 110,
              sortable: false,
              valueFormatter: (p: ValueFormatterParams<Row, { number: string }[]>) => (p.value ?? []).map((trip) => trip.number).join(", "),
              cellRenderer: (p: { value: { id: string; number: string }[] | undefined }) => (
                <span className="inline-flex gap-2">
                  {(p.value ?? []).map((trip) => (
                    <Button key={trip.id} type="button" variant="link" size="sm" className="h-auto p-0" onClick={() => openRecord("TransportTrip", trip.id)}>
                      {trip.number}
                    </Button>
                  ))}
                </span>
              ),
            } as ColDef<Row>,
          ]
        : []),
    ],
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [t, f, mode, form.readOnly, unitOptions, flags, state, rateLines?.currencyCode, form.values.terms.currencyCode, unitCreate.onCreateNew != null, openRecord],
  );

  const room = MAX_LINES - form.values.lines.length;
  const addItems = (items: LineValues[]) => {
    const taken = items.slice(0, Math.max(room, 0));
    if (taken.length === 0) return;
    form.update((values) => ({ ...values, lines: [...values.lines, ...taken] }));
    form.blur("lines");
  };

  /* The first problem, named (the stop's "Stop 2 (Unload): …"), once the items were touched or submitted. */
  const firstProblem = form.error("lines") != null ? rows.find((row) => row.problem) : undefined;
  const itemsTotal = rows.reduce((sum, row) => sum + (row.price ?? 0), 0);

  return (
    <div className="flex flex-col gap-4" data-slot="freight-items">
      {!form.readOnly && room > 0 && (
        <FreightItemEntry idPrefix={form.id("items")} mode={mode} orderDate={form.values.orderDate} type={type} disabled={form.readOnly} onAdd={(item) => addItems([item])} onPasteRows={setPaste} />
      )}
      {form.error("lines") && (
        <p className="text-label text-destructive" data-slot="items-message">
          {firstProblem ? t("Orders:ItemProblem").replace("{0}", String(firstProblem.position)).replace("{1}", firstProblem.problem!) : form.error("lines")}
        </p>
      )}
      <div className="h-72" data-slot="freight-items-grid">
        <LazyLinesGrid<Row>
          rows={rows}
          onRowsChange={onRowsChange}
          getRowId={(row) => row.key}
          columnDefs={columns}
          readOnly={form.readOnly}
          onOpen={(row) => onEdit(row.key)}
          onRemoveRequest={(targets) => setDeleting(targets[0] ?? null)}
          emptyText={t("Orders:NoLines")}
        />
      </div>
      {mode === "order" && rows.length > 0 && (
        <TotalsBlock lines={[{ label: t("Orders:FreightItemsTotal"), value: itemsTotal, grand: true }]} currencyCode={rateLines?.currencyCode ?? form.values.terms.currencyCode} state={state} className="self-end" />
      )}
      <FreightItemDialog
        form={form}
        mode={mode}
        lineKey={editingKey}
        flags={flags}
        onClose={() => onEdit(null)}
        orderId={orderId}
        legsCreated={legsCreated}
        legs={legs}
        rateLines={rateLines}
        preview={preview}
        canEditRateLines={canEditRateLines}
      />
      <ConfirmDialog
        open={deleting != null}
        onOpenChange={(next) => !next && setDeleting(null)}
        title={t("Orders:DeleteFreightItem")}
        description={t("Orders:DeleteFreightItemQuestion").replace("{0}", String(deleting?.position ?? ""))}
        confirmLabel={t("Delete")}
        destructive
        onConfirm={() => {
          const key = deleting?.key;
          setDeleting(null);
          if (!key) return;
          setLines(form.values.lines.filter((line) => line.key !== key));
          if (editingKey === key) onEdit(null);
        }}
      />
      {inPlace.dialog}
      <PasteItemsDialog open={paste != null} onOpenChange={(next) => !next && setPaste(null)} initialText={paste ?? ""} mode={mode} orderDate={form.values.orderDate} waypoints={pasteWaypoints} room={room} onAdd={addItems} />
    </div>
  );
}
