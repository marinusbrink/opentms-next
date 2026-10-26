import { useMemo, useState } from "react";
import type { ColDef, ValueFormatterParams } from "ag-grid-community";
import { ExternalLink, Pencil, Plus, X } from "lucide-react";

import { Alert, AlertDescription } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { EntityLookup } from "@/components/ui/entity-lookup";
import { FormField } from "@/components/ui/form-field";
import { FormSection } from "@/components/ui/form-section";
import { AmountCell, DateCell, NumberCell } from "@/components/ui/grid-cells";
import { Input } from "@/components/ui/input";
import { LazyOpenTmsGrid } from "@/components/ui/opentms-grid-lazy";
import { StatusBadge } from "@/components/ui/status-badge";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { AttributeFields } from "@/apps/transport/components/AttributeFields";
import { ComboField, NumberField, SelectField, TextAreaField, TextField, toOptions } from "@/apps/transport/components/fields";
import { LegDialog } from "@/apps/transport/components/LegDialog";
import { RelationField } from "@/apps/transport/components/RelationFields";
import { StopsEditor } from "@/apps/transport/components/StopsEditor";
import { TotalsBlock } from "@/apps/transport/components/TotalsBlock";
import { REQUIRED_FLAG, hasFlag, type EditorMode, type LineValues, type OrderValues } from "@/apps/transport/orders/order-form";
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
import type { FormContext } from "@/lib/forms/use-form-state";
import { useFormat } from "@/lib/format";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { useOpenRecord } from "@/lib/use-open-record";

/* The selected freight item's detail, inline under the list (design 61 "Order page, revised" 2), with three tabs of
 * its own (nested tabs: start-aligned, text only, one level, UI-FORM-008):
 * - Freight details: cargo and quantities first, then the collapsible Container, Measurements, Texts and documents and
 *   Attributes sections;
 * - Stops and legs: the item's stops (StopsEditor: add, remove, move; the relation's default address preselected) and
 *   under them the item's legs, read from Planning and filtered to the item; before Confirm the legs part says they
 *   are created on Confirm;
 * - Pricing: the item's stored rate lines and its total, with the preview of an unsaved change for the item.
 * Every record picker offers "Add new …" (UI-LOOK-004): the editor opens on top and the record lands in this item. */

export type ItemTab = "details" | "stops" | "pricing";

export interface FreightItemDetailProps {
  form: FormContext<OrderValues>;
  mode: EditorMode;
  line: LineValues;
  position: number;
  flags: number | undefined;
  tab: ItemTab;
  onTabChange: (tab: ItemTab) => void;
  onChange: (change: Partial<LineValues>) => void;
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

export function FreightItemDetail({ form, mode, line, position, flags, tab, onTabChange, onChange, onClose, orderId, legsCreated, legs, rateLines, preview, canEditRateLines }: FreightItemDetailProps) {
  const { t } = useL();
  const caption = `${t("Orders:Line")} ${position}`;
  return (
    <div className="rounded-lg border border-border-section bg-surface-page p-3" data-slot="line-detail" data-key={line.key} role="region" aria-label={caption}>
      <Tabs value={tab} onValueChange={(value) => onTabChange(value as ItemTab)} className="gap-3">
        <div className="flex items-center gap-3">
          <span className="text-label font-semibold text-brand">{caption}</span>
          <TabsList variant="line" className="justify-start">
            <TabsTrigger value="details">{t("Orders:FreightDetails")}</TabsTrigger>
            <TabsTrigger value="stops">{mode === "order" ? t("Orders:StopsAndLegs") : t("Orders:Stops")}</TabsTrigger>
            {mode === "order" && <TabsTrigger value="pricing">{t("Orders:Pricing")}</TabsTrigger>}
          </TabsList>
          <Button type="button" variant="ghost" size="icon-sm" className="ml-auto" aria-label={t("Close")} title={t("Orders:CloseItemDetail")} onClick={onClose}>
            <X />
          </Button>
        </div>
        <TabsContent value="details">
          <FreightDetails form={form} line={line} flags={flags} onChange={onChange} />
        </TabsContent>
        <TabsContent value="stops">
          <StopsAndLegs form={form} mode={mode} line={line} flags={flags} onChange={onChange} orderId={orderId} legsCreated={legsCreated} legs={legs} />
        </TabsContent>
        {mode === "order" && (
          <TabsContent value="pricing">
            <ItemPricing form={form} line={line} orderId={orderId} rateLines={rateLines} preview={preview} canEditRateLines={canEditRateLines} />
          </TabsContent>
        )}
      </Tabs>
    </div>
  );
}

/** A form over one item's values with the page's blur/error mechanics (the page shows the items' one message). */
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
  /* A record created in place lands in this item of the latest values (its editor saves on top of the page). */
  const applyToLine = (change: (l: LineValues) => Partial<LineValues>) =>
    form.update((values) => ({ ...values, lines: values.lines.map((l) => (l.key === line.key ? { ...l, ...change(l) } : l)) }));
  const nameOf = (options: { value: string; label: string }[], id: string | null) => options.find((o) => o.value === id)?.label ?? "";
  const unitForm = { ...lineForm, set: (_field: string, value: unknown) => onChange({ transportUnitId: value as string | null, transportUnitName: nameOf(unitOptions, value as string | null) }) } as FormContext<LineValues>;
  const productForm = { ...lineForm, set: (_field: string, value: unknown) => onChange({ productId: value as string | null, productName: nameOf(productOptions, value as string | null) }) } as FormContext<LineValues>;

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
      <FormSection title={t("Orders:Container")} collapsible defaultOpen={!!(line.containerNumber || line.sizeOrIso || line.pinCode || line.shippingCompany || line.shipperRelation)}>
        <div className="grid grid-cols-4 gap-x-4 gap-y-3">
          <TextField form={lineForm} field="containerNumber" labelKey="Orders:ContainerNumber" maxLength={64} />
          <TextField form={lineForm} field="sizeOrIso" labelKey="Orders:SizeOrIso" maxLength={64} />
          <TextField form={lineForm} field="pinCode" labelKey="Orders:PinCode" maxLength={64} />
          <TextField form={lineForm} field="shippingCompany" labelKey="Orders:ShippingCompany" maxLength={64} />
          <RelationField id={lineForm.id("shipper")} label={t("Orders:Shipper")} value={line.shipperRelation} onChange={(next) => onChange({ shipperRelation: next })} disabled={form.readOnly} className="col-span-2" />
        </div>
      </FormSection>
      <FormSection title={t("Orders:Measurements")} collapsible defaultOpen={[line.lengthCm, line.widthCm, line.heightCm, ...line.weightMeasurements, ...line.volumeMeasurements].some((v) => v.trim() !== "")}>
        <div className="grid grid-cols-3 gap-x-4 gap-y-3">
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
      </FormSection>
      <FormSection
        title={t("Orders:Texts")}
        collapsible
        defaultOpen={[line.invoiceReference, line.attachedDocuments, line.remarks, line.internalRemarks, line.shipperInstructions, line.specialAgreements].some((v) => v.trim() !== "")}
      >
        <div className="grid grid-cols-2 gap-x-4 gap-y-3">
          <TextField form={lineForm} field="invoiceReference" labelKey="Orders:InvoiceReference" maxLength={128} />
          <TextField form={lineForm} field="attachedDocuments" labelKey="Orders:AttachedDocuments" maxLength={512} />
          <TextAreaField form={lineForm} field="remarks" labelKey="Orders:Remarks" maxLength={2500} rows={2} />
          <TextAreaField form={lineForm} field="internalRemarks" labelKey="Orders:InternalRemarks" maxLength={2500} rows={2} />
          <TextAreaField form={lineForm} field="shipperInstructions" labelKey="Orders:ShipperInstructions" maxLength={2500} rows={2} />
          <TextAreaField form={lineForm} field="specialAgreements" labelKey="Orders:SpecialAgreements" maxLength={2500} rows={2} />
        </div>
      </FormSection>
      {(definitions.data ?? []).length > 0 || Object.keys(line.attributes).length > 0 ? (
        <FormSection title={t("Orders:Attributes")} collapsible defaultOpen={Object.keys(line.attributes).length > 0}>
          <AttributeFields idPrefix={lineForm.id("attributes")} definitions={definitions.data} value={line.attributes} onChange={(next) => onChange({ attributes: next })} readOnly={form.readOnly} />
        </FormSection>
      ) : null}
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
  orderId: string | undefined;
  legsCreated: boolean;
  legs: LegDto[] | undefined;
}

function StopsAndLegs({ form, mode, line, flags, onChange, orderId, legsCreated, legs }: StopsAndLegsProps) {
  const { t } = useL();
  const waypointLookup = waypointTypes.useLookup();
  const waypointOptions = useMemo(() => toOptions(waypointLookup.data), [waypointLookup.data]);
  const slotLookup = timeSlots.useLookup();
  const slotOptions = useMemo(() => toOptions(slotLookup.data), [slotLookup.data]);
  const requireStopReference = hasFlag(flags, REQUIRED_FLAG.LoadingReference) || hasFlag(flags, REQUIRED_FLAG.UnloadingReference);
  const requireStopWindow = hasFlag(flags, REQUIRED_FLAG.LoadingTimeWindow) || hasFlag(flags, REQUIRED_FLAG.UnloadingTimeWindow);
  return (
    <div className="flex flex-col gap-4">
      <FormSection title={t("Orders:Stops")}>
        <StopsEditor
          idPrefix={form.id(`line-${line.key}-stops`)}
          stops={line.stops}
          onChange={(next) => onChange({ stops: next })}
          mode={mode}
          kind="line"
          readOnly={form.readOnly}
          orderDate={form.values.orderDate}
          waypointTypeOptions={waypointOptions}
          timeSlotOptions={slotOptions}
          requireReference={requireStopReference}
          requireWindow={requireStopWindow}
          heightClass="h-48"
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
          <dl className="grid grid-cols-[1fr_auto] gap-x-6 gap-y-1 text-sm" data-slot="item-preview-lines">
            {previewLines.map((r, index) => (
              <div key={index} className="contents">
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
