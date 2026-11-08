import { useMemo, useRef, useState, type FormEvent } from "react";
import type { ColDef } from "ag-grid-community";
import { toast } from "sonner";

import { joinDateTime, splitDateTime } from "@/apps/planboard/panes/lists/grid-helpers";
import { InlineTextCell } from "@/apps/planboard/panes/lists/grid-support";
import { sameValue, saveWithStaleRetry } from "@/apps/planboard/panes/lists/inline-save";
import { Button } from "@/components/ui/button";
import { DatePicker } from "@/components/ui/date-picker";
import { FormField } from "@/components/ui/form-field";
import { InlineDateCell, PopupEditCell } from "@/components/ui/grid-edit-cells";
import { Input } from "@/components/ui/input";
import { ReferenceCombobox } from "@/components/ui/reference-combobox";
import { useCreateInPlace } from "@/domains/master-data/components/create-in-place";
import { products, temperatures, transportUnits } from "@/domains/master-data/entities";
import { parseTimeOfDay } from "@/domains/orders/reference-data";
import { PLANBOARD_ORDER_EDIT_FEATURES, PLANBOARD_ORDER_FIELD, PLANBOARD_ORDER_LINE_FIELD, usePlanboardOrderField, usePlanboardOrderLineField, type PlanboardOrderLineField, type PlanboardOrderLineFieldResultDto } from "@/domains/orders/transport-orders";
import { toOptions } from "@/domains/planning-execution/components/form-fields";
import { fetchLeg, legUpdateOf, useUpdateLeg, type LegDto, type LegEndSnapshotDto, type LegUpdateDto } from "@/domains/planning-execution/legs";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useIsFeatureEnabled, useIsGranted } from "@/lib/auth/permissions";
import { useFormat } from "@/lib/format";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The columns the Legs and Legs to schedule panes edit in the cell (design 74 §6). References, remarks, windows and planned
 * dates go through `PUT leg/{id}`; customer reference, transport unit, product and temperature through Orders, with the order's
 * stamp, which all rows of an order share: the panes patch every row of the order afterwards. A leg on a completed or cancelled
 * trip is read-only, per row; so is everything without its permission, and the Orders fields without the Planboard's Orders
 * feature. A 409 goes through saveWithStaleRetry. */

const TRIP_COMPLETED = 3;
const TRIP_CANCELLED = 4;
const REFERENCE_MAX = 128;
const REMARKS_MAX = 2500;
const CUSTOMER_REFERENCE_MAX = 128;

type LegSide = "origin" | "destination";
type LegRow = LegDto;

export interface LegInlineColumnsOptions<TRow extends LegRow> {
  /** Applies a patch to every row the pane holds that matches (the pane's own store: a query's data, or the grid's rows). */
  patchRows: (match: (row: TRow) => boolean, patch: (row: TRow) => TRow) => void;
}

const isLeg = (row: LegRow) => !!row.orderLineId;
const isOnFinalTrip = (row: LegRow) => row.tripStatus === TRIP_COMPLETED || row.tripStatus === TRIP_CANCELLED;

export function legWindowText(end: LegEndSnapshotDto | undefined, format: (value: string) => string): string {
  if (!end?.windowFrom && !end?.windowTill) return "";
  return `${end.windowFrom ? format(end.windowFrom) : ""} – ${end.windowTill ? format(end.windowTill) : ""}`;
}

export function useLegInlineColumns<TRow extends LegRow>({ patchRows }: LegInlineColumnsOptions<TRow>) {
  const { t } = useL();
  const f = useFormat();
  const canUpdateLegs = useIsGranted("PlanningExecution.Legs.Update");
  const canUpdateOrders = useIsGranted("Orders.TransportOrders.Update");
  const planboardOn = useIsFeatureEnabled(PLANBOARD_ORDER_EDIT_FEATURES[0]);
  const ordersOn = useIsFeatureEnabled(PLANBOARD_ORDER_EDIT_FEATURES[1]);
  const canEditOrders = canUpdateOrders && planboardOn && ordersOn;
  const { mutateAsync: updateLeg } = useUpdateLeg();
  const { mutateAsync: updateOrderField } = usePlanboardOrderField();
  const { mutateAsync: updateLineField } = usePlanboardOrderLineField();
  const latestPatch = useRef(patchRows);
  latestPatch.current = patchRows;

  return useMemo(() => {
    const patch: LegInlineColumnsOptions<TRow>["patchRows"] = (match, change) => latestPatch.current(match, change);
    const canEditLeg = (row: TRow) => canUpdateLegs && isLeg(row) && !isOnFinalTrip(row);
    const canEditOrder = (row: TRow) => canEditOrders && isLeg(row) && !isOnFinalTrip(row);
    const showing = (run: () => Promise<void>) => async () => {
      try {
        await run();
      } catch (error) {
        toast.error(getApiErrorMessage(error, t));
        throw error;
      }
    };

    /** A `PUT leg/{id}` of one change; `seen` reads the edited field, for the retry after a stale stamp. */
    const saveLeg = (row: TRow, seen: (leg: LegDto) => unknown, change: Partial<LegUpdateDto>) =>
      showing(async () => {
        const saved = await saveWithStaleRetry<LegDto, LegDto>({
          row,
          save: (leg) => updateLeg({ id: leg.id!, dto: { ...legUpdateOf(leg), ...change } }),
          refetch: (leg) => fetchLeg(leg.id!),
          unchanged: (fresh, old) => sameValue(seen(fresh), seen(old)),
        });
        patch((r) => r.id === saved.id, (r) => ({ ...r, ...saved }));
      })();

    const patchOrderStamp = (orderId: string | undefined, stamp: string | null | undefined, extra: (row: TRow) => Partial<LegDto>) =>
      patch((r) => r.orderId === orderId, (r) => ({ ...r, orderConcurrencyStamp: stamp, ...extra(r) }));

    const saveLine = (row: TRow, field: PlanboardOrderLineField, value: string | null, seen: (leg: LegDto) => unknown) =>
      showing(async () => {
        const result = await saveWithStaleRetry<LegDto, PlanboardOrderLineFieldResultDto>({
          row,
          save: (leg) => updateLineField({ orderId: leg.orderId!, lineId: leg.orderLineId!, field, value, concurrencyStamp: leg.orderConcurrencyStamp ?? "" }),
          refetch: (leg) => fetchLeg(leg.id!),
          unchanged: (fresh, old) => sameValue(seen(fresh), seen(old)),
        });
        patchOrderStamp(row.orderId, result.concurrencyStamp, (r) =>
          r.orderLineId === row.orderLineId
            ? { transportUnitId: result.transportUnitId, transportUnitName: result.transportUnitName, productId: result.productId, productName: result.productName, temperatureId: result.temperatureId, temperatureName: result.temperatureName }
            : {},
        );
      })();

    const reference = (side: LegSide, colId: string, headerName: string, extra: Partial<ColDef<TRow>> = {}): ColDef<TRow> => ({
      colId,
      headerName,
      minWidth: 130,
      sortable: false,
      valueGetter: (p) => p.data?.[side]?.reference,
      cellRenderer: (p: { value: string | null | undefined; data: TRow | undefined }) => (
        <InlineTextCell<TRow>
          value={p.value}
          data={p.data}
          maxLength={REFERENCE_MAX}
          canEdit={canEditLeg}
          onCommit={(row, value) => saveLeg(row, (leg) => leg[side]?.reference, side === "origin" ? { originReference: value } : { destinationReference: value })}
        />
      ),
      ...extra,
    });

    const plannedDate = (side: LegSide, colId: string, headerName: string): ColDef<TRow> => ({
      colId,
      headerName,
      minWidth: 120,
      sortable: false,
      valueGetter: (p) => p.data?.[side]?.plannedDate,
      cellRenderer: (p: { value: string | null | undefined; data: TRow | undefined }) => (
        <InlineDateCell<TRow>
          value={p.value}
          data={p.data}
          label={headerName}
          required
          canEdit={canEditLeg}
          onCommit={(row, value) => saveLeg(row, (leg) => leg[side]?.plannedDate, side === "origin" ? { originPlannedDate: value } : { destinationPlannedDate: value })}
        />
      ),
    });

    const window = (side: LegSide, colId: string, headerName: string, extra: Partial<ColDef<TRow>> = {}): ColDef<TRow> => ({
      colId,
      headerName,
      minWidth: 170,
      sortable: false,
      valueGetter: (p) => legWindowText(p.data?.[side], f.dateTime),
      cellRenderer: (p: { value: string; data: TRow | undefined }) => (
        <PopupEditCell<TRow>
          text={<span className="tabular-nums">{p.value}</span>}
          data={p.data}
          canEdit={canEditLeg}
          renderPopup={(row, close) => (
            <LegWindowEditor
              idPrefix={`${colId}-${row.id}`}
              end={row[side]}
              close={close}
              onSave={(from, till) => {
                const value = { from, till };
                return saveLeg(row, (leg) => `${leg[side]?.windowFrom ?? ""}|${leg[side]?.windowTill ?? ""}`, side === "origin" ? { originWindow: value } : { destinationWindow: value });
              }}
            />
          )}
        />
      ),
      ...extra,
    });

    const lineField = (colId: string, headerName: string, kind: LineLookupKind, name: (row: TRow) => string | null | undefined, id: (row: TRow) => string | null | undefined, field: PlanboardOrderLineField, extra: Partial<ColDef<TRow>> = {}): ColDef<TRow> => ({
      colId,
      headerName,
      minWidth: 130,
      sortable: false,
      valueGetter: (p) => (p.data ? name(p.data) : null),
      cellRenderer: (p: { value: string | null | undefined; data: TRow | undefined }) => (
        <PopupEditCell<TRow>
          text={p.value ?? ""}
          data={p.data}
          canEdit={canEditOrder}
          renderPopup={(row, close) => (
            <LineLookupEditor kind={kind} idPrefix={`${colId}-${row.id}`} label={headerName} value={id(row) ?? null} name={name(row) ?? null} close={close} onSave={(value) => saveLine(row, field, value, (leg) => id(leg as TRow))} />
          )}
        />
      ),
      ...extra,
    });

    return {
      originReference: (extra?: Partial<ColDef<TRow>>) => reference("origin", "originReference", t("Planboard:Lists.OriginReference"), extra),
      destinationReference: (extra?: Partial<ColDef<TRow>>) => reference("destination", "destinationReference", t("Planboard:Lists.DestinationReference"), extra),
      originDate: () => plannedDate("origin", "originDate", t("Planboard:Lists.OriginDate")),
      destinationDate: () => plannedDate("destination", "destinationDate", t("Planboard:Lists.DestinationDate")),
      originWindow: (extra?: Partial<ColDef<TRow>>) => window("origin", "originWindow", t("Planboard:Lists.OriginWindow"), extra),
      destinationWindow: (extra?: Partial<ColDef<TRow>>) => window("destination", "destinationWindow", t("Planboard:Lists.DestinationWindow"), extra),
      remarks: (extra: Partial<ColDef<TRow>> = {}): ColDef<TRow> => ({
        colId: "remarks",
        headerName: t("PlanningExecution:Remarks"),
        minWidth: 150,
        sortable: false,
        valueGetter: (p) => p.data?.remarks,
        cellRenderer: (p: { value: string | null | undefined; data: TRow | undefined }) => (
          <InlineTextCell<TRow> value={p.value} data={p.data} maxLength={REMARKS_MAX} canEdit={canEditLeg} onCommit={(row, value) => saveLeg(row, (leg) => leg.remarks, { remarks: value })} />
        ),
        ...extra,
      }),
      customerReference: (extra: Partial<ColDef<TRow>> = {}): ColDef<TRow> => ({
        colId: "customerReference",
        headerName: t("Orders:CustomerReference"),
        minWidth: 140,
        sortable: false,
        valueGetter: (p) => p.data?.customerReference,
        cellRenderer: (p: { value: string | null | undefined; data: TRow | undefined }) => (
          <InlineTextCell<TRow>
            value={p.value}
            data={p.data}
            maxLength={CUSTOMER_REFERENCE_MAX}
            canEdit={canEditOrder}
            onCommit={(row, value) =>
              showing(async () => {
                const result = await saveWithStaleRetry<LegDto, { concurrencyStamp?: string | null; customerReference?: string | null }>({
                  row,
                  save: (leg) => updateOrderField({ orderId: leg.orderId!, field: PLANBOARD_ORDER_FIELD.CustomerReference, value, concurrencyStamp: leg.orderConcurrencyStamp ?? "" }),
                  refetch: (leg) => fetchLeg(leg.id!),
                  unchanged: (fresh, old) => sameValue(fresh.customerReference, old.customerReference),
                });
                patchOrderStamp(row.orderId, result.concurrencyStamp, () => ({ customerReference: result.customerReference }));
              })()
            }
          />
        ),
        ...extra,
      }),
      transportUnit: (extra?: Partial<ColDef<TRow>>) => lineField("transportUnit", t("Orders:TransportUnit"), "unit", (r) => r.transportUnitName, (r) => r.transportUnitId, PLANBOARD_ORDER_LINE_FIELD.TransportUnit, extra),
      product: (extra?: Partial<ColDef<TRow>>) => lineField("product", t("Orders:Product"), "product", (r) => r.productName, (r) => r.productId, PLANBOARD_ORDER_LINE_FIELD.Product, extra),
      temperature: (extra?: Partial<ColDef<TRow>>) => lineField("temperature", t("Orders:Temperature"), "temperature", (r) => r.temperatureName, (r) => r.temperatureId, PLANBOARD_ORDER_LINE_FIELD.Temperature, extra),
    };
  }, [t, f, canUpdateLegs, canEditOrders, updateLeg, updateOrderField, updateLineField]);
}

type LineLookupKind = "unit" | "product" | "temperature";

/** A record picker in the cell's popup: choosing saves. "Add new …" as in every picker (UI-LOOK-004). */
function LineLookupEditor({ kind, idPrefix, label, value, name, close, onSave }: { kind: LineLookupKind; idPrefix: string; label: string; value: string | null; name: string | null; close: () => void; onSave: (value: string | null) => Promise<void> }) {
  const inPlace = useCreateInPlace();
  const units = transportUnits.useLookup(kind === "unit");
  const productList = products.useLookup(kind === "product");
  const temperatureList = temperatures.useLookup(kind === "temperature");
  const lookup = kind === "unit" ? units : kind === "product" ? productList : temperatureList;
  const options = useMemo(() => toOptions(lookup.data, { id: value, name }), [lookup.data, value, name]);
  const [busy, setBusy] = useState(false);
  const required = kind === "unit";
  const id = `${idPrefix}-lookup`;

  const choose = async (next: string | null) => {
    if (busy || next === value || (required && !next)) return;
    setBusy(true);
    try {
      await onSave(next);
      close();
    } catch {
      /* The failure was shown; the popup stays for another try. */
    } finally {
      setBusy(false);
    }
  };

  return (
    <>
      <FormField label={label} htmlFor={id} required={required} floating="always">
        <ReferenceCombobox
          id={id}
          value={value}
          onChange={(next) => void choose(next)}
          options={options}
          required={required}
          disabled={busy}
          {...inPlace.offer(kind === "unit" ? "TransportUnit" : kind === "product" ? "Product" : "Temperature", busy, (r) => void choose(r.id))}
        />
      </FormField>
      {inPlace.dialog}
    </>
  );
}

/** A leg end's window from and till: a date and a time each, like the other time fields of the board. */
function LegWindowEditor({ idPrefix, end, close, onSave }: { idPrefix: string; end: LegEndSnapshotDto | undefined; close: () => void; onSave: (from: string | null, till: string | null) => Promise<void> }) {
  const { t } = useL();
  const from = splitDateTime(end?.windowFrom);
  const till = splitDateTime(end?.windowTill);
  const [fromDate, setFromDate] = useState(from.date);
  const [fromTime, setFromTime] = useState(from.time);
  const [tillDate, setTillDate] = useState(till.date);
  const [tillTime, setTillTime] = useState(till.time);
  const [submitted, setSubmitted] = useState(false);
  const [busy, setBusy] = useState(false);
  const timeError = (time: string) => (submitted && time.trim() && !parseTimeOfDay(time) ? t("Orders:InvalidTime") : undefined);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setSubmitted(true);
    if ((fromTime.trim() && !parseTimeOfDay(fromTime)) || (tillTime.trim() && !parseTimeOfDay(tillTime))) return;
    setBusy(true);
    try {
      await onSave(joinDateTime(fromDate, fromTime), joinDateTime(tillDate, tillTime));
      close();
    } catch {
      /* The failure was shown; the popup stays for another try. */
    } finally {
      setBusy(false);
    }
  };

  return (
    <form onSubmit={(e) => void submit(e)} noValidate className="grid grid-cols-2 gap-x-3 gap-y-3">
      <FormField label={t("PlanningExecution:WindowFrom")} htmlFor={`${idPrefix}-from-date`} floating="always">
        <DatePicker id={`${idPrefix}-from-date`} value={fromDate} onChange={setFromDate} disabled={busy} />
      </FormField>
      <FormField label={t("Orders:Time")} htmlFor={`${idPrefix}-from-time`} error={timeError(fromTime)}>
        <Input id={`${idPrefix}-from-time`} value={fromTime} onChange={(e) => setFromTime(e.target.value)} placeholder="HH:mm" disabled={busy} />
      </FormField>
      <FormField label={t("PlanningExecution:WindowTill")} htmlFor={`${idPrefix}-till-date`} floating="always">
        <DatePicker id={`${idPrefix}-till-date`} value={tillDate} onChange={setTillDate} disabled={busy} />
      </FormField>
      <FormField label={t("Orders:Time")} htmlFor={`${idPrefix}-till-time`} error={timeError(tillTime)}>
        <Input id={`${idPrefix}-till-time`} value={tillTime} onChange={(e) => setTillTime(e.target.value)} placeholder="HH:mm" disabled={busy} />
      </FormField>
      <div className="col-span-2 flex justify-end gap-2">
        <Button type="button" variant="outline" size="sm" onClick={close} disabled={busy}>
          {t("Cancel")}
        </Button>
        <Button type="submit" size="sm" disabled={busy}>
          {busy ? t("Saving") : t("Save")}
        </Button>
      </div>
    </form>
  );
}
