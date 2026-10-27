import { useEffect, useMemo, useState } from "react";
import type { ColDef, ValueFormatterParams } from "ag-grid-community";
import { Check, ExternalLink, Pencil, Plus } from "lucide-react";

import { Alert, AlertDescription } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { EntityDialog, type EntityDialogTab, type RibbonCommand } from "@/components/ui/entity-dialog";
import { EntityLookup } from "@/components/ui/entity-lookup";
import { FormField } from "@/components/ui/form-field";
import { FormSection } from "@/components/ui/form-section";
import { AmountCell, DateCell, NumberCell } from "@/components/ui/grid-cells";
import { Input } from "@/components/ui/input";
import { NestedTabs } from "@/components/ui/nested-tabs";
import { LazyOpenTmsGrid } from "@/components/ui/opentms-grid-lazy";
import { StatusBadge } from "@/components/ui/status-badge";
import { AttributeFields } from "@/apps/transport/components/AttributeFields";
import { ComboField, NumberField, SelectField, TextAreaField, TextField, toOptions } from "@/apps/transport/components/fields";
import { LegDialog } from "@/apps/transport/components/LegDialog";
import { RelationField } from "@/apps/transport/components/RelationFields";
import { StopStrip } from "@/apps/transport/components/StopStrip";
import { TotalsBlock } from "@/apps/transport/components/TotalsBlock";
import { REQUIRED_FLAG, hasFlag, lineProblemsOf, stopProblemText, stopProblemsOf, type EditorMode, type LineValues, type OrderValues } from "@/apps/transport/orders/order-form";
import { RateLineDialog, RateLinesGrid } from "@/apps/transport/orders/RateLines";
import type { PricePreviewState } from "@/apps/transport/orders/use-price-preview";
import { RATE_LINE_KINDS, type RateLineSetDto } from "@/domains/financial/rate-lines";
import { useCreateInPlace, withId } from "@/domains/master-data/components/create-in-place";
import {
  productRelationCodes,
  products,
  temperatures,
  toTransportCharacteristicOption,
  transportCharacteristicLookupPage,
  transportUnits,
  vehicleTypes,
  type TransportCharacteristicOption,
  type TransportCharacteristicsDto,
} from "@/domains/master-data/entities";
import { timeSlots, waypointTypes } from "@/domains/orders/reference-data";
import { PAYMENT_INSTRUCTIONS, useOrderAttributeDefinitions } from "@/domains/orders/transport-orders";
import { legEndLabel, type LegDto } from "@/domains/planning-execution/legs";
import { tripStatusName } from "@/domains/planning-execution/trips";
import { isSameValue, type FormContext } from "@/lib/forms/use-form-state";
import { useFormat } from "@/lib/format";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { useOpenRecord } from "@/lib/use-open-record";

/* Edit freight item (design 61 "Order page, legacy tab structure" 4): an EntityDialog size `full` with tabs of its
 * own, where the order → freight item → legs and pricing hierarchy lives:
 * - Freight details: cargo and quantities, then the nested tabs Container, Measurements, Texts and documents and
 *   Custom fields (only when there are item attributes);
 * - Stops and legs: the item's stops in the same row editor as the stop strip (add, remove, move) and under them the
 *   item's legs, read from Planning and filtered to the item; before Confirm the legs part says they are created on
 *   Confirm (template: Stops, as day offsets, no legs);
 * - Pricing (order only): the item's stored rate lines and its total, with the preview of an unsaved change.
 * The dialog edits the item in the order's form, live (the price preview follows): Apply keeps the changes, Close
 * (X, Esc) on a changed item asks "Discard changes?" and restores the item as it was when the dialog opened; the
 * order's Save stores them. A save while the dialog is open (Ctrl/⌘+S) makes the saved item the one Close restores.
 * Rate-line actions act on the stored item, at once, as before. Every record picker offers "Add new …" (UI-LOOK-004). */

export type ItemTab = "details" | "stops" | "pricing";

export interface FreightItemDialogProps {
  form: FormContext<OrderValues>;
  mode: EditorMode;
  /** The edited item's form key; null closes the dialog. */
  lineKey: string | null;
  flags: number | undefined;
  onClose: () => void;
  /** Order mode: the stored order (legs and rate lines exist only for it). */
  orderId: string | undefined;
  /** Legs exist from Confirm on (a Draft and a new order have none yet). */
  legsCreated: boolean;
  legs: LegDto[] | undefined;
  rateLines: RateLineSetDto | undefined;
  preview: PricePreviewState;
  canEditRateLines: boolean;
}

export function FreightItemDialog({ form, mode, lineKey, flags, onClose, orderId, legsCreated, legs, rateLines, preview, canEditRateLines }: FreightItemDialogProps) {
  const { t } = useL();
  const index = form.values.lines.findIndex((line) => line.key === lineKey);
  const line = index >= 0 ? form.values.lines[index] : null;
  const [tab, setTab] = useState<ItemTab>("details");
  /* The item as it was when the dialog opened (or was last saved): what Close restores. */
  const [snapshot, setSnapshot] = useState<LineValues | null>(line);
  useEffect(() => {
    setSnapshot(line);
    if (lineKey == null) setTab("details");
    // eslint-disable-next-line react-hooks/exhaustive-deps -- a new key: another item, or the same one after a save
  }, [lineKey]);

  if (!line || !snapshot) return null;
  const isDirty = !isSameValue(line, snapshot);
  const update = (change: Partial<LineValues>) => {
    form.update((values) => ({ ...values, lines: values.lines.map((l) => (l.key === line.key ? { ...l, ...change } : l)) }));
    form.blur("lines");
  };
  const restore = () => form.update((values) => ({ ...values, lines: values.lines.map((l) => (l.key === snapshot.key ? snapshot : l)) }));

  /* The tab marks (UI-FORM-007) follow the items' messages: shown once the items were touched or submitted. */
  const shown = form.error("lines") != null;
  const problem = lineProblemsOf([line], mode).get(line.key);
  const stopsHaveError = shown && problem?.stop != null;
  const detailsHaveError = shown && problem != null && problem.stop == null && problem.key !== "Orders:LineNeedsTwoStopsHint";

  const commands: RibbonCommand[] = form.readOnly ? [] : [{ id: "apply", labelKey: "Orders:Apply", icon: Check, isPrimary: true, onClick: onClose }];
  const tabs: EntityDialogTab[] = [
    { value: "details", label: t("Orders:FreightDetails"), hasError: detailsHaveError, content: <FreightDetails form={form} line={line} flags={flags} onChange={update} /> },
    {
      value: "stops",
      label: mode === "order" ? t("Orders:StopsAndLegs") : t("Orders:Stops"),
      hasError: stopsHaveError || (shown && problem?.key === "Orders:LineNeedsTwoStopsHint"),
      content: <StopsAndLegs form={form} mode={mode} line={line} flags={flags} onChange={update} showErrors={shown} orderId={orderId} legsCreated={legsCreated} legs={legs} />,
    },
    ...(mode === "order" ? [{ value: "pricing", label: t("Orders:Pricing"), content: <ItemPricing form={form} line={line} orderId={orderId} rateLines={rateLines} preview={preview} canEditRateLines={canEditRateLines} /> }] : []),
  ];

  return (
    <EntityDialog
      open
      onOpenChange={(next) => {
        if (next) return;
        /* Close after the dirty guard (or on a clean item): the item as it was. */
        restore();
        onClose();
      }}
      variant="editor"
      size="full"
      title={`${t("Orders:Line")} ${index + 1}`}
      commands={commands}
      tabs={tabs}
      activeTab={tab}
      onActiveTabChange={(value) => setTab(value as ItemTab)}
      isDirty={isDirty}
    />
  );
}

/** A form over one item's values with the page's blur/error mechanics (the items tab shows the items' one message). */
function lineFormOf(form: FormContext<OrderValues>, line: LineValues, onChange: (change: Partial<LineValues>) => void): FormContext<LineValues> {
  return {
    values: line,
    set: (field: string, value: unknown) => onChange({ [field]: value }),
    update: () => undefined,
    error: () => undefined,
    blur: () => form.blur("lines"),
    readOnly: form.readOnly,
    id: (field: string) => form.id(`line-${line.key}-${field}`),
  } as unknown as FormContext<LineValues>;
}

function FreightDetails({ form, line, flags, onChange }: { form: FormContext<OrderValues>; line: LineValues; flags: number | undefined; onChange: (change: Partial<LineValues>) => void }) {
  const { t } = useL();
  const lineForm = lineFormOf(form, line, onChange);
  const [characteristics, setCharacteristics] = useState<TransportCharacteristicOption[]>([]);
  const unitLookup = transportUnits.useLookup();
  const unitOptions = useMemo(() => toOptions(unitLookup.data, { id: line.transportUnitId, name: line.transportUnitName }), [unitLookup.data, line.transportUnitId, line.transportUnitName]);
  const productLookup = products.useLookup();
  const productOptions = useMemo(() => toOptions(productLookup.data, { id: line.productId, name: line.productName }), [productLookup.data, line.productId, line.productName]);
  const codeLookup = productRelationCodes.useLookup();
  const codeOptions = useMemo(() => toOptions(codeLookup.data), [codeLookup.data]);
  const temperatureLookup = temperatures.useLookup();
  const temperatureOptions = useMemo(() => toOptions(temperatureLookup.data), [temperatureLookup.data]);
  const vehicleTypeLookup = vehicleTypes.useLookup();
  const vehicleTypeOptions = useMemo(() => toOptions(vehicleTypeLookup.data), [vehicleTypeLookup.data]);
  const definitions = useOrderAttributeDefinitions(2);
  const inPlace = useCreateInPlace();
  /* A record created in place lands in this item of the latest values (its editor saves on top of the dialog). */
  const applyToLine = (change: (l: LineValues) => Partial<LineValues>) =>
    form.update((values) => ({ ...values, lines: values.lines.map((l) => (l.key === line.key ? { ...l, ...change(l) } : l)) }));
  const nameOf = (options: { value: string; label: string }[], id: string | null) => options.find((o) => o.value === id)?.label ?? "";
  const unitForm = { ...lineForm, set: (_field: string, value: unknown) => onChange({ transportUnitId: value as string | null, transportUnitName: nameOf(unitOptions, value as string | null) }) } as FormContext<LineValues>;
  const productForm = { ...lineForm, set: (_field: string, value: unknown) => onChange({ productId: value as string | null, productName: nameOf(productOptions, value as string | null) }) } as FormContext<LineValues>;
  const hasCustomFields = (definitions.data ?? []).length > 0 || Object.keys(line.attributes).length > 0;

  return (
    <div className="flex flex-col gap-4" data-slot="freight-details">
      <FormSection title={t("Orders:Cargo")}>
        <div className="grid grid-cols-4 gap-x-4 gap-y-3">
          <ComboField
            form={unitForm}
            field="transportUnitId"
            labelKey="Orders:TransportUnit"
            required
            options={unitOptions}
            {...inPlace.offer("TransportUnit", form.readOnly, (r) => applyToLine(() => ({ transportUnitId: r.id, transportUnitName: r.name })))}
          />
          <NumberField form={lineForm} field="quantity" labelKey="Orders:Quantity" required={hasFlag(flags, REQUIRED_FLAG.QuantityPerLine)} />
          <NumberField form={lineForm} field="positions" labelKey="Orders:Positions" />
          <NumberField form={lineForm} field="colli" labelKey="Orders:Colli" />
          <NumberField form={lineForm} field="weightKg" labelKey="Orders:WeightKg" required={hasFlag(flags, REQUIRED_FLAG.WeightPerLine)} />
          <NumberField form={lineForm} field="volumeM3" labelKey="Orders:VolumeM3" required={hasFlag(flags, REQUIRED_FLAG.VolumePerLine)} />
          <NumberField form={lineForm} field="loadingMeters" labelKey="Orders:LoadingMeters" required={hasFlag(flags, REQUIRED_FLAG.LoadingMetersPerLine)} />
          <NumberField form={lineForm} field="epq" labelKey="Orders:Epq" />
          <NumberField form={lineForm} field="loadFactor" labelKey="Orders:LoadFactor" />
          <ComboField
            form={productForm}
            field="productId"
            labelKey="Orders:Product"
            required={hasFlag(flags, REQUIRED_FLAG.ProductPerLine)}
            options={productOptions}
            {...inPlace.offer("Product", form.readOnly, (r) => applyToLine(() => ({ productId: r.id, productName: r.name })))}
          />
          <ComboField
            form={lineForm}
            field="productRelationCodeId"
            labelKey="Orders:ProductRelationCode"
            options={codeOptions}
            {...inPlace.offer("ProductRelationCode", form.readOnly, (r) => applyToLine(() => ({ productRelationCodeId: r.id })))}
          />
          <ComboField
            form={lineForm}
            field="vehicleTypeId"
            labelKey="Orders:VehicleType"
            options={vehicleTypeOptions}
            {...inPlace.offer("VehicleType", form.readOnly, (r) => applyToLine(() => ({ vehicleTypeId: r.id })))}
          />
          <ComboField
            form={lineForm}
            field="temperatureId"
            labelKey="Orders:Temperature"
            required={hasFlag(flags, REQUIRED_FLAG.Temperature)}
            options={temperatureOptions}
            {...inPlace.offer("Temperature", form.readOnly, (r) => applyToLine(() => ({ temperatureId: r.id })))}
          />
          <FormField label={t("Orders:RequiredTransportCharacteristics")} htmlFor={lineForm.id("characteristics")} floating="always" className="col-span-2">
            <EntityLookup<TransportCharacteristicOption>
              id={lineForm.id("characteristics")}
              multiple
              value={line.requiredTransportCharacteristicIds.map((id) => characteristics.find((c) => c.id === id) ?? { id, code: "", name: id, description: null })}
              onChange={(next) => {
                setCharacteristics(next);
                onChange({ requiredTransportCharacteristicIds: next.map((c) => c.id) });
              }}
              fetchPage={transportCharacteristicLookupPage}
              getLabel={(c) => c.name}
              disabled={form.readOnly}
              {...inPlace.offer<TransportCharacteristicsDto>("TransportCharacteristics", form.readOnly, (r) => {
                const created = toTransportCharacteristicOption(r.dto);
                setCharacteristics((known) => [...known.filter((c) => c.id !== created.id), created]);
                applyToLine((l) => ({ requiredTransportCharacteristicIds: withId(l.requiredTransportCharacteristicIds, created.id) }));
              })}
            />
          </FormField>
          <SelectField form={lineForm} field="paymentInstruction" labelKey="Orders:PaymentInstruction" items={PAYMENT_INSTRUCTIONS.map((p) => ({ value: p.value, label: t(p.key) }))} />
          <TextField form={lineForm} field="barcode" labelKey="Orders:Barcode" maxLength={64} />
        </div>
      </FormSection>
      <NestedTabs
        tabs={[
          {
            value: "container",
            label: t("Orders:Container"),
            content: (
              <div className="grid grid-cols-4 gap-x-4 gap-y-3" data-slot="item-container">
                <TextField form={lineForm} field="containerNumber" labelKey="Orders:ContainerNumber" maxLength={64} />
                <TextField form={lineForm} field="sizeOrIso" labelKey="Orders:SizeOrIso" maxLength={64} />
                <TextField form={lineForm} field="pinCode" labelKey="Orders:PinCode" maxLength={64} />
                <TextField form={lineForm} field="shippingCompany" labelKey="Orders:ShippingCompany" maxLength={64} />
                <RelationField id={lineForm.id("shipper")} label={t("Orders:Shipper")} value={line.shipperRelation} onChange={(next) => onChange({ shipperRelation: next })} disabled={form.readOnly} className="col-span-2" />
              </div>
            ),
          },
          {
            value: "measurements",
            label: t("Orders:Measurements"),
            content: (
              <div className="grid grid-cols-3 gap-x-4 gap-y-3" data-slot="item-measurements">
                <NumberField form={lineForm} field="lengthCm" labelKey="Orders:LengthCm" />
                <NumberField form={lineForm} field="widthCm" labelKey="Orders:WidthCm" />
                <NumberField form={lineForm} field="heightCm" labelKey="Orders:HeightCm" />
                {/* The individual weighings and volumes (design 58: up to three of each), saved with the items. */}
                {(["weightMeasurements", "volumeMeasurements"] as const).map((field) =>
                  line[field].map((text, index) => {
                    const id = lineForm.id(`${field}-${index + 1}`);
                    const labelKey = field === "weightMeasurements" ? "Orders:WeightMeasurement" : "Orders:VolumeMeasurement";
                    return (
                      <FormField key={id} label={t(labelKey).replace("{0}", String(index + 1))} htmlFor={id}>
                        <Input
                          id={id}
                          inputMode="decimal"
                          className="tabular-nums"
                          value={text}
                          disabled={form.readOnly}
                          onChange={(e) => onChange({ [field]: line[field].map((v, i) => (i === index ? e.target.value : v)) })}
                          onBlur={() => form.blur("lines")}
                        />
                      </FormField>
                    );
                  }),
                )}
              </div>
            ),
          },
          {
            value: "texts",
            label: t("Orders:Texts"),
            content: (
              <div className="grid grid-cols-2 gap-x-4 gap-y-3" data-slot="item-texts">
                <TextField form={lineForm} field="invoiceReference" labelKey="Orders:InvoiceReference" maxLength={128} />
                <TextField form={lineForm} field="attachedDocuments" labelKey="Orders:AttachedDocuments" maxLength={512} />
                <TextAreaField form={lineForm} field="remarks" labelKey="Orders:Remarks" maxLength={2500} rows={2} />
                <TextAreaField form={lineForm} field="internalRemarks" labelKey="Orders:InternalRemarks" maxLength={2500} rows={2} />
                <TextAreaField form={lineForm} field="shipperInstructions" labelKey="Orders:ShipperInstructions" maxLength={2500} rows={2} />
                <TextAreaField form={lineForm} field="specialAgreements" labelKey="Orders:SpecialAgreements" maxLength={2500} rows={2} />
              </div>
            ),
          },
          ...(hasCustomFields
            ? [
                {
                  value: "custom",
                  label: t("Orders:CustomFields"),
                  content: <AttributeFields idPrefix={lineForm.id("attributes")} definitions={definitions.data} value={line.attributes} onChange={(next) => onChange({ attributes: next })} readOnly={form.readOnly} />,
                },
              ]
            : []),
        ]}
      />
      {inPlace.dialog}
    </div>
  );
}

interface StopsAndLegsProps {
  form: FormContext<OrderValues>;
  mode: EditorMode;
  line: LineValues;
  flags: number | undefined;
  onChange: (change: Partial<LineValues>) => void;
  /** The items' messages are shown (touched or submitted): the stops' fields carry theirs. */
  showErrors: boolean;
  orderId: string | undefined;
  legsCreated: boolean;
  legs: LegDto[] | undefined;
}

function StopsAndLegs({ form, mode, line, flags, onChange, showErrors, orderId, legsCreated, legs }: StopsAndLegsProps) {
  const { t } = useL();
  const waypointLookup = waypointTypes.useLookup();
  const waypointOptions = useMemo(() => toOptions(waypointLookup.data), [waypointLookup.data]);
  const slotLookup = timeSlots.useLookup();
  const slotOptions = useMemo(() => toOptions(slotLookup.data), [slotLookup.data]);
  const errors = showErrors ? Object.fromEntries(stopProblemsOf(line.stops, mode).map((p) => [`${p.index}:${p.field}`, stopProblemText(t, line.stops, p)])) : {};
  return (
    <div className="flex flex-col gap-4">
      <FormSection title={t("Orders:Stops")}>
        <StopStrip
          idPrefix={form.id(`line-${line.key}`)}
          stops={line.stops}
          onChange={(next) => onChange({ stops: next })}
          mode={mode}
          readOnly={form.readOnly}
          orderDate={form.values.orderDate}
          waypointTypeOptions={waypointOptions}
          timeSlotOptions={slotOptions}
          errors={errors}
          movable
          requireReference={hasFlag(flags, REQUIRED_FLAG.LoadingReference) || hasFlag(flags, REQUIRED_FLAG.UnloadingReference)}
          requireWindow={hasFlag(flags, REQUIRED_FLAG.LoadingTimeWindow) || hasFlag(flags, REQUIRED_FLAG.UnloadingTimeWindow)}
        />
      </FormSection>
      {mode === "order" && (
        <FormSection title={t("PlanningExecution:Legs")}>
          {orderId && legsCreated && line.id ? <ItemLegs lineId={line.id} legs={legs} /> : <p className="text-label text-muted-foreground" data-slot="legs-after-confirm">{t("Orders:LegsAfterConfirm")}</p>}
        </FormSection>
      )}
    </div>
  );
}

/** The item's legs (design 59: generated from its stops), filtered from the order's legs; the leg fields are edited in
 * the shared LegDialog, a planned leg opens its trip. */
function ItemLegs({ lineId, legs }: { lineId: string; legs: LegDto[] | undefined }) {
  const { t } = useL();
  const f = useFormat();
  const openRecord = useOpenRecord();
  const [editing, setEditing] = useState<LegDto | null>(null);
  const rows = useMemo(() => (legs ?? []).filter((leg) => leg.orderLineId?.toLowerCase() === lineId.toLowerCase()), [legs, lineId]);
  /* A new fetcher when the order's legs were read again (a leg edit, a push): the grid shows the item's rows. */
  const fetchRows = useMemo(() => async () => ({ rows, totalCount: rows.length, filteredCount: rows.length }), [rows]);

  const columns = useMemo<ColDef<LegDto>[]>(
    () => [
      { field: "position", headerName: t("PlanningExecution:Position"), minWidth: 70, cellRenderer: NumberCell, type: "rightAligned", sortable: false },
      { colId: "origin", headerName: t("PlanningExecution:Origin"), flex: 1, minWidth: 160, valueGetter: (p) => legEndLabel(p.data?.origin), sortable: false },
      { colId: "destination", headerName: t("PlanningExecution:Destination"), flex: 1, minWidth: 160, valueGetter: (p) => legEndLabel(p.data?.destination), sortable: false },
      { colId: "originDate", headerName: t("PlanningExecution:PlannedDate"), minWidth: 110, valueGetter: (p) => p.data?.origin?.plannedDate, cellRenderer: DateCell, sortable: false },
      { colId: "destinationDate", headerName: `${t("PlanningExecution:PlannedDate")} (${t("PlanningExecution:Destination")})`, minWidth: 110, valueGetter: (p) => p.data?.destination?.plannedDate, cellRenderer: DateCell, sortable: false },
      { colId: "distance", headerName: `${t("PlanningExecution:Distance")} (km)`, minWidth: 100, type: "rightAligned", valueGetter: (p) => (p.data?.distanceM == null ? null : p.data.distanceM / 1000), valueFormatter: (p: ValueFormatterParams<LegDto, number | null>) => f.number(p.value, { maxDecimals: 1 }), cellClass: "tabular-nums", sortable: false },
      { field: "estimatedCo2Kg", headerName: t("PlanningExecution:Co2Kg"), minWidth: 100, type: "rightAligned", valueFormatter: (p: ValueFormatterParams<LegDto, number | null>) => f.number(p.value, { maxDecimals: 3 }), cellClass: "tabular-nums", sortable: false },
      { field: "tripNumber", headerName: t("PlanningExecution:Trip"), minWidth: 120, cellRenderer: (p: { value: string | null; data: LegDto | undefined }) => (p.value ? <span className="inline-flex items-center gap-1 text-brand">{p.value}<StatusBadge entity="TransportTrip" state={tripStatusName(p.data?.tripStatus)} /></span> : null), sortable: false },
      { field: "allocatedCostAmount", headerName: t("PlanningExecution:AllocatedCost"), minWidth: 110, cellRenderer: AmountCell, type: "rightAligned", sortable: false },
      { field: "allocatedRevenueAmount", headerName: t("PlanningExecution:AllocatedRevenue"), minWidth: 110, cellRenderer: AmountCell, type: "rightAligned", sortable: false },
      { field: "durationS", headerName: t("Orders:DurationS"), minWidth: 100, type: "rightAligned", hide: true, valueFormatter: (p: ValueFormatterParams<LegDto, number | null>) => (p.value == null ? "" : f.duration(Math.round(p.value / 60))), cellClass: "tabular-nums", sortable: false },
      { colId: "originReference", headerName: `${t("PlanningExecution:Reference")} (${t("PlanningExecution:Origin")})`, minWidth: 120, valueGetter: (p) => p.data?.origin?.reference, hide: true, sortable: false },
      { colId: "destinationReference", headerName: `${t("PlanningExecution:Reference")} (${t("PlanningExecution:Destination")})`, minWidth: 120, valueGetter: (p) => p.data?.destination?.reference, hide: true, sortable: false },
      { field: "remarks", headerName: t("PlanningExecution:Remarks"), minWidth: 120, hide: true, sortable: false },
    ],
    [t, f],
  );

  return (
    <div className="h-56" data-slot="item-legs">
      <LazyOpenTmsGrid<LegDto>
        gridId="planning.transport.orderItemLegs"
        columnDefs={columns}
        fetchRows={fetchRows}
        onOpen={(row) => {
          if (row.tripId) openRecord("TransportTrip", row.tripId);
          else setEditing(row);
        }}
        rowActions={(selected) => [
          { id: "edit", labelKey: "Edit", icon: Pencil, onClick: () => setEditing(selected[0]) },
          ...(selected[0]?.tripId ? [{ id: "trip", labelKey: "PlanningExecution:OpenTrip", icon: ExternalLink, onClick: () => openRecord("TransportTrip", selected[0].tripId!) }] : []),
        ]}
      />
      {editing && <LegDialog leg={editing} title={`${t("PlanningExecution:Leg")} ${editing.position ?? ""}`} idPrefix="leg" onClose={() => setEditing(null)} />}
    </div>
  );
}

interface ItemPricingProps {
  form: FormContext<OrderValues>;
  line: LineValues;
  orderId: string | undefined;
  rateLines: RateLineSetDto | undefined;
  preview: PricePreviewState;
  canEditRateLines: boolean;
}

/** The item's rate lines and total; while an unsaved change is previewed, the preview's lines for the item instead. */
function ItemPricing({ form, line, orderId, rateLines, preview, canEditRateLines }: ItemPricingProps) {
  const { t } = useL();
  const f = useFormat();
  const [dialog, setDialog] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const currency = rateLines?.currencyCode ?? form.values.terms.currencyCode;
  const id = line.id?.toLowerCase();
  const stored = useMemo(() => (rateLines?.lines ?? []).filter((r) => id && r.orderLineId?.toLowerCase() === id), [rateLines, id]);
  const previewLines = useMemo(() => (preview.preview?.lines ?? []).filter((r) => id && r.orderLineId?.toLowerCase() === id), [preview.preview, id]);
  const storedTotal = id ? (rateLines?.totals?.byAnchor?.find((a) => a.anchorId?.toLowerCase() === id)?.amount ?? stored.reduce((sum, r) => sum + (r.amount ?? 0), 0)) : null;

  if (!orderId || !line.id) return <p className="text-label text-muted-foreground" data-slot="item-priced-after-save">{t("Orders:ItemPricedAfterSave")}</p>;

  return (
    <div className="flex flex-col gap-3" data-slot="item-pricing">
      {error && (
        <Alert variant="destructive">
          <AlertDescription>{error}</AlertDescription>
        </Alert>
      )}
      {preview.preview ? (
        <>
          <Alert variant="warning" data-slot="price-preview">
            <AlertDescription>{t("Orders:PricePreviewNotice")}</AlertDescription>
          </Alert>
          <dl className="flex flex-col gap-1 text-sm" data-slot="item-preview-lines">
            {previewLines.map((r, index) => (
              <div key={index} className="flex justify-between gap-6">
                <dt className="text-muted-foreground">{r.descriptionKey ? t(r.descriptionKey) : t(RATE_LINE_KINDS.find((k) => k.value === r.kind)?.key ?? "")}</dt>
                <dd className="text-right tabular-nums">{f.amount(r.amount)}</dd>
              </div>
            ))}
          </dl>
          <TotalsBlock lines={[{ label: t("Orders:ItemTotal"), value: previewLines.reduce((sum, r) => sum + (r.amount ?? 0), 0), grand: true }]} currencyCode={preview.preview.currencyCode ?? currency} footer={<span className="text-label text-muted-foreground">{t("Orders:PreviewPrices")}</span>} />
        </>
      ) : (
        <>
          {(preview.incomplete || preview.failed) && (
            <p className="text-label text-muted-foreground" data-slot="price-preview-unavailable">
              {t(preview.incomplete ? "Orders:PricePreviewIncomplete" : "Orders:PricePreviewUnavailable")}
            </p>
          )}
          <RateLinesGrid
            rows={stored}
            currency={currency}
            editable={canEditRateLines && !form.readOnly}
            onError={setError}
            toolbar={
              <Button type="button" size="sm" variant="outline" onClick={() => setDialog(true)}>
                <Plus /> {t("Financial:AddManualLine")}
              </Button>
            }
          />
          <TotalsBlock lines={[{ label: t("Orders:ItemTotal"), value: storedTotal, grand: true }]} currencyCode={currency} state={rateLines?.state} />
        </>
      )}
      {dialog && <RateLineDialog kind="manual" orderId={orderId} orderLineId={line.id} currency={currency} onClose={() => setDialog(false)} />}
    </div>
  );
}
