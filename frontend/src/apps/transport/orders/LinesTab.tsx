import { useMemo, useState } from "react";
import type { ColDef, ValueFormatterParams } from "ag-grid-community";
import { Plus, X } from "lucide-react";

import { Button } from "@/components/ui/button";
import { EntityLookup } from "@/components/ui/entity-lookup";
import { FormField } from "@/components/ui/form-field";
import { FormSection } from "@/components/ui/form-section";
import { LazyLinesGrid } from "@/components/ui/lines-grid-lazy";
import { referenceCellEditorColDef } from "@/components/ui/reference-cell-editor";
import { AttributeFields } from "@/apps/transport/components/AttributeFields";
import { ComboField, NumberField, SelectField, TextAreaField, TextField, toOptions } from "@/apps/transport/components/fields";
import { RelationField } from "@/apps/transport/components/RelationFields";
import { StopsEditor } from "@/apps/transport/components/StopsEditor";
import { REQUIRED_FLAG, hasFlag, newLine, type EditorMode, type LineValues, type OrderValues } from "@/apps/transport/orders/order-form";
import { products, productRelationCodes, temperatures, transportCharacteristicLookupPage, transportUnits, vehicleTypes, type TransportCharacteristicOption } from "@/domains/master-data/entities";
import { timeSlots, waypointTypes, type TransportOrderTypeDto } from "@/domains/orders/reference-data";
import { PAYMENT_INSTRUCTIONS, useOrderAttributeDefinitions } from "@/domains/orders/transport-orders";
import type { RateLineSetDto } from "@/domains/financial/rate-lines";
import type { FormContext } from "@/lib/forms/use-form-state";
import { useFormat } from "@/lib/format";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The Lines tab of the order editor (design 61): a LinesGrid of the lines (unit, product, quantity, positions, colli,
 * weight, volume, loading metres, EPQ, distance, duration, CO₂ (stored), price (the stored total of the line's rate
 * lines)) with the quantities edited inline, and a side panel for the selected line with the sections Cargo,
 * Container, Documents/texts, Measurements, Attributes and the Stops grid (no nested modal, UX finding §6.3/6.4). The
 * type's required flags mark the fields (UI-FORM-002); a Draft saves without them. */

const MAX_LINES = 200;

export interface LinesTabProps {
  form: FormContext<OrderValues>;
  mode: EditorMode;
  type: TransportOrderTypeDto | undefined;
  /** The stored rate lines (order mode): the line's price column is the total of its lines. */
  rateLines?: RateLineSetDto;
}

type Row = LineValues & { position: number; price: number | null };

export function LinesTab({ form, mode, type, rateLines }: LinesTabProps) {
  const { t } = useL();
  const f = useFormat();
  const [openKey, setOpenKey] = useState<string | null>(null);
  const flags = type?.requiredFields as number | undefined;

  const unitLookup = transportUnits.useLookup();
  const unitOptions = useMemo(() => toOptions(unitLookup.data), [unitLookup.data]);
  const productLookup = products.useLookup();
  const productOptions = useMemo(() => toOptions(productLookup.data), [productLookup.data]);
  const codeLookup = productRelationCodes.useLookup();
  const codeOptions = useMemo(() => toOptions(codeLookup.data), [codeLookup.data]);
  const temperatureLookup = temperatures.useLookup();
  const temperatureOptions = useMemo(() => toOptions(temperatureLookup.data), [temperatureLookup.data]);
  const vehicleTypeLookup = vehicleTypes.useLookup();
  const vehicleTypeOptions = useMemo(() => toOptions(vehicleTypeLookup.data), [vehicleTypeLookup.data]);
  const waypointLookup = waypointTypes.useLookup();
  const waypointOptions = useMemo(() => toOptions(waypointLookup.data), [waypointLookup.data]);
  const slotLookup = timeSlots.useLookup();
  const slotOptions = useMemo(() => toOptions(slotLookup.data), [slotLookup.data]);
  const definitions = useOrderAttributeDefinitions(2);

  const priceOf = useMemo(() => {
    const totals = new Map<string, number>();
    for (const line of rateLines?.lines ?? []) {
      if (!line.orderLineId) continue;
      totals.set(line.orderLineId.toLowerCase(), (totals.get(line.orderLineId.toLowerCase()) ?? 0) + (line.amount ?? 0));
    }
    return (id: string | null) => (id ? (totals.get(id.toLowerCase()) ?? null) : null);
  }, [rateLines]);

  const rows = useMemo<Row[]>(() => form.values.lines.map((line, index) => ({ ...line, position: index + 1, price: priceOf(line.id) })), [form.values.lines, priceOf]);
  const open = rows.find((row) => row.key === openKey) ?? null;
  const labelOf = (options: { value: string; label: string }[], value: string | null) => options.find((o) => o.value === value)?.label ?? "";

  const mark = (label: string, flag: number) => (hasFlag(flags, flag) ? `${label} *` : label);
  const numeric = (field: keyof LineValues & string, label: string, flag?: number): ColDef<Row> => ({
    field,
    headerName: flag ? mark(label, flag) : label,
    width: 110,
    minWidth: 90,
    editable: !form.readOnly,
    type: "rightAligned",
    cellClass: "tabular-nums",
  });

  const columns = useMemo<ColDef<Row>[]>(
    () => [
      { field: "position", headerName: t("Orders:Position"), width: 70, minWidth: 60, type: "rightAligned" },
      {
        field: "transportUnitId",
        headerName: mark(t("Orders:TransportUnit"), REQUIRED_FLAG.TransportUnitPerLine),
        flex: 1,
        minWidth: 140,
        editable: !form.readOnly,
        /* DEVIATION(UI-LOOK-004): no "Add new…": the entity's editor lives in the Master data app and an app never imports another app (CLAUDE.md); approved in design 61 build notes, open question for the PO. */
        ...referenceCellEditorColDef<Row>({ options: unitOptions }),
        valueFormatter: (p: ValueFormatterParams<Row, string | null>) => labelOf(unitOptions, p.value ?? null),
      },
      {
        field: "productId",
        headerName: mark(t("Orders:Product"), REQUIRED_FLAG.ProductPerLine),
        flex: 1,
        minWidth: 140,
        editable: !form.readOnly,
        /* DEVIATION(UI-LOOK-004): no "Add new…": the entity's editor lives in the Master data app and an app never imports another app (CLAUDE.md); approved in design 61 build notes, open question for the PO. */
        ...referenceCellEditorColDef<Row>({ options: productOptions }),
        valueFormatter: (p: ValueFormatterParams<Row, string | null>) => labelOf(productOptions, p.value ?? null),
      },
      numeric("quantity", t("Orders:Quantity"), REQUIRED_FLAG.QuantityPerLine),
      numeric("positions", t("Orders:Positions")),
      numeric("colli", t("Orders:Colli")),
      numeric("weightKg", t("Orders:WeightKg"), REQUIRED_FLAG.WeightPerLine),
      numeric("volumeM3", t("Orders:VolumeM3"), REQUIRED_FLAG.VolumePerLine),
      numeric("loadingMeters", t("Orders:LoadingMeters"), REQUIRED_FLAG.LoadingMetersPerLine),
      numeric("epq", t("Orders:Epq")),
      ...(mode === "order"
        ? [
            { field: "distanceM", headerName: `${t("Orders:DistanceM")} (km)`, width: 110, minWidth: 90, type: "rightAligned", cellClass: "tabular-nums", valueFormatter: (p: ValueFormatterParams<Row, number | null>) => (p.value == null ? "" : f.number(p.value / 1000, { maxDecimals: 1 })) } as ColDef<Row>,
            { field: "durationS", headerName: t("Orders:DurationS"), width: 110, minWidth: 90, type: "rightAligned", cellClass: "tabular-nums", valueFormatter: (p: ValueFormatterParams<Row, number | null>) => (p.value == null ? "" : f.duration(Math.round(p.value / 60))) } as ColDef<Row>,
            { field: "co2Kg", headerName: t("Orders:Co2Kg"), width: 110, minWidth: 90, type: "rightAligned", cellClass: "tabular-nums", valueFormatter: (p: ValueFormatterParams<Row, number | null>) => f.number(p.value, { maxDecimals: 3 }) } as ColDef<Row>,
            { field: "price", headerName: `${t("Financial:Amount")} (${form.values.terms.currencyCode})`, width: 120, minWidth: 100, type: "rightAligned", cellClass: "tabular-nums", valueFormatter: (p: ValueFormatterParams<Row, number | null>) => (p.value == null ? "" : f.amount(p.value)) } as ColDef<Row>,
          ]
        : []),
    ],
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [t, form.readOnly, unitOptions, productOptions, mode, f, flags, form.values.terms.currencyCode],
  );

  const setLines = (lines: LineValues[]) => {
    form.set("lines", lines);
    form.blur("lines");
  };
  const updateLine = (key: string, change: Partial<LineValues>) => setLines(form.values.lines.map((line) => (line.key === key ? { ...line, ...change } : line)));

  const addLine = () => {
    if (form.values.lines.length >= MAX_LINES) return;
    const line = newLine(form.values.defaultStops, form.values.orderDate, mode);
    setLines([...form.values.lines, line]);
    setOpenKey(line.key);
  };

  return (
    <div className="flex h-full min-h-0 flex-col gap-4">
      {form.error("lines") && <p className="text-label text-destructive">{form.error("lines")}</p>}
      <div className={open ? "h-64 shrink-0" : "min-h-64 flex-1"}>
        <LazyLinesGrid<Row>
          rows={rows}
          onRowsChange={(next) =>
            setLines(
              next.map(({ position: _position, price: _price, ...line }) => ({
                ...line,
                transportUnitName: labelOf(unitOptions, line.transportUnitId),
                productName: labelOf(productOptions, line.productId),
              })),
            )
          }
          getRowId={(row) => row.key}
          columnDefs={columns}
          readOnly={form.readOnly}
          onOpen={(row) => setOpenKey(row.key)}
          emptyText={t("Orders:NoLines")}
          toolbar={
            form.values.lines.length < MAX_LINES ? (
              <Button type="button" size="sm" variant="outline" onClick={addLine}>
                <Plus /> {t("Orders:AddLine")}
              </Button>
            ) : undefined
          }
        />
      </div>
      {open && (
        <LineDetail
          key={open.key}
          line={open}
          form={form}
          mode={mode}
          flags={flags}
          onChange={(change) => updateLine(open.key, change)}
          onClose={() => setOpenKey(null)}
          options={{ codeOptions, temperatureOptions, vehicleTypeOptions, waypointOptions, slotOptions }}
          definitions={definitions.data}
        />
      )}
    </div>
  );
}

interface LineDetailProps {
  line: Row;
  form: FormContext<OrderValues>;
  mode: EditorMode;
  flags: number | undefined;
  onChange: (change: Partial<LineValues>) => void;
  onClose: () => void;
  options: Record<"codeOptions" | "temperatureOptions" | "vehicleTypeOptions" | "waypointOptions" | "slotOptions", { value: string; label: string }[]>;
  definitions: { key?: string | null; name?: string | null }[] | undefined;
}

/** The side panel of the selected line: a form over the line's values with the editor's blur/error mechanics. */
function LineDetail({ line, form, mode, flags, onChange, onClose, options, definitions }: LineDetailProps) {
  const { t } = useL();
  const [characteristics, setCharacteristics] = useState<TransportCharacteristicOption[]>([]);
  const lineForm = {
    values: line,
    set: (field: string, value: unknown) => onChange({ [field]: value }),
    update: () => undefined,
    error: () => undefined,
    blur: () => form.blur("lines"),
    readOnly: form.readOnly,
    id: (field: string) => form.id(`line-${line.key}-${field}`),
  } as unknown as FormContext<LineValues>;

  const requireStopReference = hasFlag(flags, REQUIRED_FLAG.LoadingReference) || hasFlag(flags, REQUIRED_FLAG.UnloadingReference);
  const requireStopWindow = hasFlag(flags, REQUIRED_FLAG.LoadingTimeWindow) || hasFlag(flags, REQUIRED_FLAG.UnloadingTimeWindow);

  return (
    <div className="min-h-0 flex-1 overflow-y-auto rounded-lg border border-border-section bg-surface-page p-3" data-slot="line-detail" data-key={line.key}>
      <div className="mb-3 flex items-center gap-2">
        <span className="text-label font-semibold text-brand">
          {t("Orders:Line")} {line.position}
        </span>
        <Button type="button" variant="ghost" size="icon-sm" className="ml-auto" aria-label={t("Close")} title={t("Close")} onClick={onClose}>
          <X />
        </Button>
      </div>
      <div className="grid grid-cols-2 gap-4">
        <FormSection title={t("Orders:Cargo")}>
          <div className="grid grid-cols-2 gap-x-4 gap-y-3">
            <ComboField form={lineForm} field="productRelationCodeId" labelKey="Orders:ProductRelationCode" options={options.codeOptions} />
            <ComboField form={lineForm} field="temperatureId" labelKey="Orders:Temperature" required={hasFlag(flags, REQUIRED_FLAG.Temperature)} options={options.temperatureOptions} />
            <ComboField form={lineForm} field="vehicleTypeId" labelKey="Orders:VehicleType" options={options.vehicleTypeOptions} />
            <SelectField form={lineForm} field="paymentInstruction" labelKey="Orders:PaymentInstruction" items={PAYMENT_INSTRUCTIONS.map((p) => ({ value: p.value, label: t(p.key) }))} />
            <TextField form={lineForm} field="barcode" labelKey="Orders:Barcode" maxLength={64} />
            <NumberField form={lineForm} field="loadFactor" labelKey="Orders:LoadFactor" />
            <FormField label={t("Orders:RequiredTransportCharacteristics")} htmlFor={lineForm.id("characteristics")} floating="always" className="col-span-2">
              {/* DEVIATION(UI-LOOK-004): no "Add new…": the entity's editor lives in the Master data app and an app never imports another app (CLAUDE.md); approved in design 61 build notes, open question for the PO. */}
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
              />
            </FormField>
          </div>
        </FormSection>
        <FormSection title={t("Orders:Container")}>
          <div className="grid grid-cols-2 gap-x-4 gap-y-3">
            <TextField form={lineForm} field="containerNumber" labelKey="Orders:ContainerNumber" maxLength={64} />
            <TextField form={lineForm} field="sizeOrIso" labelKey="Orders:SizeOrIso" maxLength={64} />
            <TextField form={lineForm} field="pinCode" labelKey="Orders:PinCode" maxLength={64} />
            <TextField form={lineForm} field="shippingCompany" labelKey="Orders:ShippingCompany" maxLength={64} />
            <RelationField id={lineForm.id("shipper")} label={t("Orders:Shipper")} value={line.shipperRelation} onChange={(next) => onChange({ shipperRelation: next })} disabled={form.readOnly} className="col-span-2" />
          </div>
        </FormSection>
        <FormSection title={t("Orders:Texts")}>
          <div className="grid grid-cols-2 gap-x-4 gap-y-3">
            <TextField form={lineForm} field="invoiceReference" labelKey="Orders:InvoiceReference" maxLength={128} />
            <TextField form={lineForm} field="attachedDocuments" labelKey="Orders:AttachedDocuments" maxLength={512} />
            <TextAreaField form={lineForm} field="remarks" labelKey="Orders:Remarks" maxLength={2500} rows={2} />
            <TextAreaField form={lineForm} field="internalRemarks" labelKey="Orders:InternalRemarks" maxLength={2500} rows={2} />
            <TextAreaField form={lineForm} field="shipperInstructions" labelKey="Orders:ShipperInstructions" maxLength={2500} rows={2} />
            <TextAreaField form={lineForm} field="specialAgreements" labelKey="Orders:SpecialAgreements" maxLength={2500} rows={2} />
          </div>
        </FormSection>
        <div className="flex flex-col gap-4">
          <FormSection title={t("Orders:Measurements")}>
            <div className="grid grid-cols-3 gap-x-4 gap-y-3">
              <NumberField form={lineForm} field="lengthCm" labelKey="Orders:LengthCm" />
              <NumberField form={lineForm} field="widthCm" labelKey="Orders:WidthCm" />
              <NumberField form={lineForm} field="heightCm" labelKey="Orders:HeightCm" />
            </div>
          </FormSection>
          <AttributeFields idPrefix={lineForm.id("attributes")} title={t("Orders:Attributes")} definitions={definitions} value={line.attributes} onChange={(next) => onChange({ attributes: next })} readOnly={form.readOnly} />
        </div>
      </div>
      <FormSection title={t("Orders:Stops")} className="mt-4">
        <StopsEditor
          idPrefix={lineForm.id("stops")}
          stops={line.stops}
          onChange={(next) => onChange({ stops: next })}
          mode={mode}
          kind="line"
          readOnly={form.readOnly}
          orderDate={form.values.orderDate}
          waypointTypeOptions={options.waypointOptions}
          timeSlotOptions={options.slotOptions}
          requireReference={requireStopReference}
          requireWindow={requireStopWindow}
          heightClass="h-48"
        />
      </FormSection>
    </div>
  );
}
