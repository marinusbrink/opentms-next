import { useEffect, useMemo, useState, type FormEvent } from "react";
import type { ColDef, ValueFormatterParams } from "ag-grid-community";
import { Plus, Tag, Trash2 } from "lucide-react";
import { toast } from "sonner";

import { Alert, AlertDescription } from "@/components/ui/alert";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { EntityDialog } from "@/components/ui/entity-dialog";
import { FormField } from "@/components/ui/form-field";
import { FormSection } from "@/components/ui/form-section";
import { Input } from "@/components/ui/input";
import { LazyLinesGrid } from "@/components/ui/lines-grid-lazy";
import { MultiSelect } from "@/components/ui/multi-select";
import { ReferenceCombobox } from "@/components/ui/reference-combobox";
import { Spinner } from "@/components/ui/spinner";
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui/tooltip";
import { CheckboxField, ComboField, NumberField, SelectField, toOptions } from "@/apps/transport/components/fields";
import { CurrencyField } from "@/apps/transport/components/CurrencyField";
import { TotalsBlock } from "@/apps/transport/components/TotalsBlock";
import { pricePreviewRequestOf, type EditorMode, type OrderValues } from "@/apps/transport/orders/order-form";
import type { PriceListDto } from "@/domains/financial/price-lists";
import { additionalServices, discountsOrSurcharges, vatPercentages } from "@/domains/financial/reference-data";
import {
  MULTIPLE_MATCHES,
  QUANTITY_SOURCES,
  RATE_LINE_KIND,
  RATE_LINE_KINDS,
  RATE_SIDE,
  usePriceListLookup,
  useAddManualRateLine,
  useRemoveFixedPrice,
  useRemoveRateLine,
  useSetFixedPrice,
  useUpdateRateLine,
  type RateLineDto,
  type RateLineSetDto,
} from "@/domains/financial/rate-lines";
import { useCreateInPlace, withId } from "@/domains/master-data/components/create-in-place";
import { previewPrices, type PricePreviewDto, type TransportOrderDto } from "@/domains/orders/transport-orders";
import { getApiErrorMessage } from "@/lib/api/errors";
import { parseNumber, type FormContext } from "@/lib/forms/use-form-state";
import { useFormat } from "@/lib/format";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The Pricing tab of the order editor (design 61): the stored rate lines grouped by line then order-level (kind badge,
 * quantity, unit price, amount, provenance tooltip), the actions Add manual line, Add additional service, Adjust unit
 * price (inline on a calculated line), Remove (manual), Set fixed price; the Financial terms section; the totals
 * footer with the CalculatingIndicator (flow 2); and the Preview of an unsaved change through `preview-prices`
 * (debounced, pure), replaced by the stored lines after save. Reads never calculate (AD-07). */

const PREVIEW_DEBOUNCE_MS = 600;

export interface PricingTabProps {
  form: FormContext<OrderValues>;
  mode: EditorMode;
  order: TransportOrderDto | undefined;
  rateLines: RateLineSetDto | undefined;
  /** Whether the lines or terms differ from the stored order: the preview runs then. */
  previewDirty: boolean;
  canEditRateLines: boolean;
}

type Row = RateLineDto & { key: string; group: string };

export function PricingTab({ form, mode, order, rateLines, previewDirty, canEditRateLines }: PricingTabProps) {
  const { t } = useL();
  const f = useFormat();
  const [dialog, setDialog] = useState<"manual" | "service" | "fixed" | null>(null);
  const update = useUpdateRateLine();
  const remove = useRemoveRateLine();
  const removeFixed = useRemoveFixedPrice();
  const [error, setError] = useState<string | null>(null);
  const currency = rateLines?.currencyCode ?? form.values.terms.currencyCode;

  /* The preview of unsaved lines/terms (pricing §4.3): debounced, never stored; cleared once the form is saved. */
  const [preview, setPreview] = useState<PricePreviewDto | null>(null);
  const [previewing, setPreviewing] = useState(false);
  const [previewFailed, setPreviewFailed] = useState(false);
  /* Only a priceable input is previewed (finding 1): every line has a transport unit and two stops with a relation and
   * an address. Below that, or when the server cannot price it, a quiet note replaces the preview: never a toast. */
  const previewInput = useMemo(
    () => (mode === "order" && order?.id && previewDirty ? pricePreviewRequestOf({ customer: form.values.customer, orderDate: form.values.orderDate, terms: form.values.terms, lines: form.values.lines }) : undefined),
    [mode, order?.id, previewDirty, form.values.customer, form.values.orderDate, form.values.terms, form.values.lines],
  );
  const previewRequest = previewInput ? JSON.stringify(previewInput) : null;
  const previewIncomplete = previewInput === null;
  useEffect(() => {
    setPreviewFailed(false);
    if (!previewRequest || !order?.id) {
      setPreview(null);
      return;
    }
    let cancelled = false;
    const handle = setTimeout(() => {
      setPreviewing(true);
      previewPrices(order.id!, JSON.parse(previewRequest))
        .then((result) => {
          if (!cancelled) setPreview(result);
        })
        .catch(() => {
          if (!cancelled) {
            setPreview(null);
            setPreviewFailed(true);
          }
        })
        .finally(() => {
          if (!cancelled) setPreviewing(false);
        });
    }, PREVIEW_DEBOUNCE_MS);
    return () => {
      cancelled = true;
      clearTimeout(handle);
    };
  }, [previewRequest, order?.id]);

  const lineLabel = useMemo(() => {
    const byId = new Map<string, number>();
    form.values.lines.forEach((line, index) => {
      if (line.id) byId.set(line.id.toLowerCase(), index + 1);
    });
    return (orderLineId: string | null | undefined) => (orderLineId && byId.has(orderLineId.toLowerCase()) ? `${t("Orders:Line")} ${byId.get(orderLineId.toLowerCase())}` : t("Orders:OrderLevel"));
  }, [form.values.lines, t]);

  const rows = useMemo<Row[]>(() => {
    const lines = [...(rateLines?.lines ?? [])];
    lines.sort((a, b) => (a.orderLineId ? 0 : 1) - (b.orderLineId ? 0 : 1) || (a.orderLineId ?? "").localeCompare(b.orderLineId ?? "") || (a.position ?? 0) - (b.position ?? 0));
    return lines.map((line) => ({ ...line, key: line.id ?? "", group: lineLabel(line.orderLineId) }));
  }, [rateLines, lineLabel]);

  const kindLabel = (kind: number | undefined) => t(RATE_LINE_KINDS.find((k) => k.value === kind)?.key ?? "");
  const descriptionOf = (row: RateLineDto) => row.description || row.priceListCode || row.discountOrSurchargeName || row.additionalServiceName || (row.descriptionKey ? t(row.descriptionKey) : "");
  const provenance = (row: RateLineDto) =>
    [
      row.priceListCode ? `${t("Financial:PriceList")}: ${row.priceListCode}` : null,
      row.priceListRuleId ? `${t("Financial:Rule")}: ${row.priceListRuleId.slice(0, 8)}` : null,
      row.columnPosition != null ? `${t("Financial:Columns")}: ${row.columnPosition}` : null,
      row.quantitySource != null ? `${t("Financial:Quantity")}: ${t(QUANTITY_SOURCES.find((q) => q.value === row.quantitySource)?.key ?? "")}` : null,
      row.isAdjusted ? t("Financial:Adjusted") : null,
    ]
      .filter(Boolean)
      .join(" · ");

  const editable = canEditRateLines && !form.readOnly;
  const columns = useMemo<ColDef<Row>[]>(
    () => [
      { field: "group", headerName: t("Orders:Line"), minWidth: 110, width: 120 },
      {
        field: "kind",
        headerName: t("Financial:ValueKind"),
        minWidth: 130,
        width: 150,
        cellRenderer: (p: { value: number | undefined; data: Row | undefined }) => (
          <Tooltip>
            <TooltipTrigger render={<Badge variant="secondary" />}>{kindLabel(p.value)}</TooltipTrigger>
            <TooltipContent>{p.data ? provenance(p.data) || kindLabel(p.value) : ""}</TooltipContent>
          </Tooltip>
        ),
      },
      { colId: "description", headerName: t("Orders:Description"), flex: 1, minWidth: 180, valueGetter: (p) => (p.data ? descriptionOf(p.data) : "") },
      { field: "quantity", headerName: t("Financial:Quantity"), width: 110, minWidth: 90, type: "rightAligned", cellClass: "tabular-nums", editable: (p) => editable && p.data?.isManual === true, valueFormatter: (p: ValueFormatterParams<Row, number>) => f.number(p.value, { maxDecimals: 4 }) },
      { field: "unitPrice", headerName: t("Financial:UnitPrice"), width: 120, minWidth: 100, type: "rightAligned", cellClass: "tabular-nums", editable: (p) => editable && p.data?.kind !== RATE_LINE_KIND.FixedPrice, valueFormatter: (p: ValueFormatterParams<Row, number>) => f.unitPrice(p.value) },
      { field: "amount", headerName: `${t("Financial:Amount")} (${currency})`, width: 130, minWidth: 110, type: "rightAligned", cellClass: "tabular-nums", valueFormatter: (p: ValueFormatterParams<Row, number>) => f.amount(p.value) },
    ],
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [t, f, editable, currency],
  );

  /* The grid hands back the rows after an inline edit (adjust) or a removal (the trash icon of a manual line); each
   * is its own command on the server (design 57), run at once: these are stored lines, not form state. */
  const onRowsChange = (next: Row[]) => {
    setError(null);
    const removed = rows.filter((row) => !next.some((r) => r.key === row.key));
    for (const row of removed) {
      remove
        .mutateAsync(row.id!)
        .then(() => toast.success(t("Financial:RateLineDeleted")))
        .catch((err: unknown) => setError(getApiErrorMessage(err, t)));
    }
    const changed = next.find((row) => {
      const before = rows.find((r) => r.key === row.key);
      return before && (before.quantity !== row.quantity || before.unitPrice !== row.unitPrice);
    });
    if (!changed) return;
    update
      .mutateAsync({ id: changed.id!, dto: { concurrencyStamp: changed.concurrencyStamp ?? "", unitPrice: Number(changed.unitPrice), quantity: changed.isManual ? Number(changed.quantity) : null, description: changed.description } })
      .then(() => toast.success(t("Financial:RateLineSaved")))
      .catch((err: unknown) => setError(getApiErrorMessage(err, t)));
  };

  const totals = rateLines?.totals;
  const hasFixedPrice = totals?.hasFixedPrice === true;
  const shownTotals = preview
    ? [
        { label: t("Financial:LinesAmount"), value: preview.linesAmount },
        { label: t("Financial:DiscountsAndSurcharges"), value: preview.discountsAndSurchargesAmount },
        { label: t("Financial:Total"), value: preview.totalAmount, grand: true },
      ]
    : [
        { label: t("Financial:LinesAmount"), value: totals?.linesAmount },
        { label: t("Financial:DiscountsAndSurcharges"), value: totals?.discountsAndSurchargesAmount },
        { label: t("Financial:AdditionalServices"), value: totals?.additionalServicesAmount },
        { label: t("Financial:Total"), value: totals?.total, grand: true },
      ];

  return (
    <div className="flex flex-col gap-4">
      {error && (
        <Alert variant="destructive">
          <AlertDescription>{error}</AlertDescription>
        </Alert>
      )}
      {mode === "order" && (
        <FormSection title={t("Financial:RateLines")}>
          {preview && (
            <Alert variant="warning" className="mb-3" data-slot="price-preview">
              <AlertDescription>
                {t("Orders:PricePreviewNotice")}
                {preview.warnings && preview.warnings.length > 0 && <span> {preview.warnings.map((w) => t(w)).join(" ")}</span>}
              </AlertDescription>
            </Alert>
          )}
          {!preview && (previewIncomplete || previewFailed) && (
            <p className="mb-3 text-label text-muted-foreground" data-slot="price-preview-unavailable">
              {t(previewIncomplete ? "Orders:PricePreviewIncomplete" : "Orders:PricePreviewUnavailable")}
            </p>
          )}
          <div className={preview ? "relative opacity-50" : "relative"}>
            {previewing && (
              <div className="absolute inset-0 z-10 flex items-center justify-center">
                <Spinner />
              </div>
            )}
            <div className="h-64">
              <LazyLinesGrid<Row>
                rows={rows}
                onRowsChange={onRowsChange}
                getRowId={(row) => row.key}
                columnDefs={columns}
                readOnly={!editable}
                canRemoveRow={(row) => row.isManual === true}
                emptyText={t("Financial:NoRateLines")}
                toolbar={
                  <>
                    <Button type="button" size="sm" variant="outline" onClick={() => setDialog("manual")} disabled={!order?.id}>
                      <Plus /> {t("Financial:AddManualLine")}
                    </Button>
                    <Button type="button" size="sm" variant="outline" onClick={() => setDialog("service")} disabled={!order?.id}>
                      <Plus /> {t("Financial:AddAdditionalService")}
                    </Button>
                    {hasFixedPrice ? (
                      <Button
                        type="button"
                        size="sm"
                        variant="outline"
                        onClick={() => removeFixed.mutateAsync(order!.id!).then(() => toast.success(t("Financial:RateLineDeleted"))).catch((err: unknown) => setError(getApiErrorMessage(err, t)))}
                      >
                        <Trash2 /> {t("Financial:RemoveFixedPrice")}
                      </Button>
                    ) : (
                      <Button type="button" size="sm" variant="outline" onClick={() => setDialog("fixed")} disabled={!order?.id}>
                        <Tag /> {t("Financial:SetFixedPrice")}
                      </Button>
                    )}
                  </>
                }
              />
            </div>
          </div>
          <TotalsBlock className="mt-3" lines={shownTotals} currencyCode={preview?.currencyCode ?? currency} state={preview ? undefined : rateLines?.state} footer={preview ? <span className="text-label text-muted-foreground">{t("Orders:PreviewPrices")}</span> : undefined} />
        </FormSection>
      )}
      <TermsSection form={form} order={order} />
      {order?.id && dialog && <RateLineDialog kind={dialog} orderId={order.id} currency={currency} lines={form.values.lines.map((line, index) => ({ id: line.id, label: `${t("Orders:Line")} ${index + 1}` }))} onClose={() => setDialog(null)} />}
    </div>
  );
}

function TermsSection({ form, order }: { form: FormContext<OrderValues>; order: TransportOrderDto | undefined }) {
  const { t } = useL();
  const vatLookup = vatPercentages.useLookup();
  const vatOptions = useMemo(() => toOptions(vatLookup.data, { id: form.values.terms.vatPercentageId, name: order?.financialTerms?.vatPercentageName }), [vatLookup.data, form.values.terms.vatPercentageId, order?.financialTerms?.vatPercentageName]);
  const priceLists = usePriceListLookup(RATE_SIDE.Revenue);
  const priceListOptions = useMemo(() => (priceLists.data ?? []).map((p) => ({ id: p.id ?? "", label: p.name ?? p.code ?? "" })), [priceLists.data]);
  const dsLookup = discountsOrSurcharges.useLookup();
  const defaults = new Set(form.values.terms.discountsOrSurcharges.filter((d) => d.source === 1).map((d) => d.discountOrSurchargeId));
  const dsOptions = useMemo(
    () => (dsLookup.data ?? []).map((d) => ({ id: d.id ?? "", label: defaults.has(d.id ?? "") ? `${d.name ?? ""} (${t("Orders:DiscountOrSurchargeSource.Default")})` : (d.name ?? "") })),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [dsLookup.data, form.values.terms.discountsOrSurcharges, t],
  );
  const terms = form.values.terms;
  const setTerms = (change: Partial<OrderValues["terms"]>) => {
    form.set("terms", { ...terms, ...change });
    form.blur("terms");
  };
  const termsForm = {
    values: terms,
    set: (field: string, value: unknown) => setTerms({ [field]: value }),
    update: () => undefined,
/* The section shows its one message above the fields (UI-FB-004: exactly once), not under each field. */
    error: () => undefined,
    blur: () => form.blur("terms"),
    readOnly: form.readOnly,
    id: (field: string) => form.id(`terms-${field}`),
  } as unknown as FormContext<OrderValues["terms"]>;
  /* "Add new …" in every picker (UI-LOOK-004): the record its editor saved lands in the latest terms. */
  const inPlace = useCreateInPlace();
  const setLatest = (change: (current: OrderValues["terms"]) => Partial<OrderValues["terms"]>) =>
    form.update((values) => ({ ...values, terms: { ...values.terms, ...change(values.terms) } }));

  return (
    <FormSection title={t("Orders:FinancialTerms")}>
      {form.error("terms") && <p className="mb-2 text-label text-destructive">{form.error("terms")}</p>}
      <div className="grid grid-cols-4 gap-x-4 gap-y-3">
        <CurrencyField id={form.id("terms-currencyCode")} label={t("Orders:CurrencyCode")} value={terms.currencyCode} onChange={(next) => setTerms({ currencyCode: next ?? "" })} required disabled={form.readOnly} />
        <ComboField
          form={termsForm}
          field="vatPercentageId"
          labelKey="Orders:VatPercentage"
          options={vatOptions}
          {...inPlace.offer("VatPercentage", form.readOnly, (r) => setLatest(() => ({ vatPercentageId: r.id })))}
        />
        <NumberField form={termsForm} field="paymentTermDays" labelKey="Orders:PaymentTermDays" />
        <SelectField form={termsForm} field="multipleMatches" labelKey="Orders:MultipleMatches" items={MULTIPLE_MATCHES.map((m) => ({ value: m.value, label: t(m.key) }))} />
        <FormField label={t("Orders:PriceLists")} htmlFor={form.id("terms-priceListIds")} floating="always" className="col-span-2">
          <MultiSelect
            id={form.id("terms-priceListIds")}
            value={terms.priceListIds}
            onChange={(next) => setTerms({ priceListIds: next })}
            options={priceListOptions}
            disabled={form.readOnly}
            {...inPlace.offer<PriceListDto>("PriceList", form.readOnly, (r) => {
              /* The order's terms hold revenue lists only (design 57); a cost list made here is not one of them. */
              if (r.dto.side === RATE_SIDE.Revenue) setLatest((current) => ({ priceListIds: withId(current.priceListIds, r.id) }));
            })}
          />
        </FormField>
        <FormField label={t("Orders:DiscountsOrSurcharges")} htmlFor={form.id("terms-ds")} floating="always" className="col-span-2">
          <MultiSelect
            id={form.id("terms-ds")}
            value={terms.discountsOrSurcharges.map((d) => d.discountOrSurchargeId)}
            onChange={(next) => setTerms({ discountsOrSurcharges: next.map((id) => terms.discountsOrSurcharges.find((d) => d.discountOrSurchargeId === id) ?? { discountOrSurchargeId: id, source: 2 }) })}
            options={dsOptions}
            disabled={form.readOnly}
            {...inPlace.offer("DiscountOrSurcharge", form.readOnly, (r) =>
              setLatest((current) => ({
                discountsOrSurcharges: current.discountsOrSurcharges.some((d) => d.discountOrSurchargeId === r.id) ? current.discountsOrSurcharges : [...current.discountsOrSurcharges, { discountOrSurchargeId: r.id, source: 2 }],
              })),
            )}
          />
        </FormField>
        <NumberField form={termsForm} field="fixedPriceAmount" labelKey="Orders:FixedPriceAmount" />
        <CheckboxField form={termsForm} field="sendFreightDocumentsWithInvoice" labelKey="Orders:SendFreightDocumentsWithInvoice" className="col-span-2" />
      </div>
      {inPlace.dialog}
    </FormSection>
  );
}

interface RateLineDialogProps {
  kind: "manual" | "service" | "fixed";
  orderId: string;
  currency: string;
  lines: { id: string | null; label: string }[];
  onClose: () => void;
}

/** Add manual line / Add additional service / Set fixed price: one action dialog (UI-DLG-002, UI-ACT-003, UI-KEY-001). */
function RateLineDialog({ kind, orderId, currency, lines, onClose }: RateLineDialogProps) {
  const { t } = useL();
  const add = useAddManualRateLine();
  const setFixed = useSetFixedPrice();
  const services = additionalServices.useLookup(kind === "service");
  const serviceOptions = useMemo(() => toOptions(services.data), [services.data]);
  const [description, setDescription] = useState("");
  const [quantity, setQuantity] = useState("1");
  const [unitPrice, setUnitPrice] = useState("");
  const [lineId, setLineId] = useState<string | null>(null);
  const [serviceId, setServiceId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [submitted, setSubmitted] = useState(false);
  const busy = add.isPending || setFixed.isPending;
  const inPlace = useCreateInPlace();

  const title = kind === "manual" ? t("Financial:AddManualLine") : kind === "service" ? t("Financial:AddAdditionalService") : t("Financial:SetFixedPrice");
  const verb = kind === "fixed" ? t("Financial:SetFixedPrice") : t("Financial:AddLine");
  const price = parseNumber(unitPrice);
  const qty = parseNumber(quantity);
  const priceError = submitted && (price == null || Number.isNaN(price)) ? t("MasterData:Required") : undefined;
  const serviceError = submitted && kind === "service" && !serviceId ? t("MasterData:Required") : undefined;

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setSubmitted(true);
    setError(null);
    if (price == null || Number.isNaN(price) || (kind === "service" && !serviceId)) return;
    try {
      if (kind === "fixed") {
        await setFixed.mutateAsync({ orderId, amount: price, currencyCode: currency });
      } else {
        await add.mutateAsync({
          orderId,
          orderLineId: lineId,
          side: RATE_SIDE.Revenue,
          kind: kind === "service" ? RATE_LINE_KIND.AdditionalService : RATE_LINE_KIND.Manual,
          additionalServiceId: kind === "service" ? serviceId : null,
          description: description.trim() || null,
          quantity: qty == null || Number.isNaN(qty) ? 1 : qty,
          quantitySource: 0,
          unitPrice: price,
          currencyCode: currency,
        });
      }
      toast.success(t("Financial:RateLineSaved"));
      onClose();
    } catch (err) {
      setError(getApiErrorMessage(err, t));
    }
  };

  return (
    <EntityDialog
      open
      onOpenChange={(next) => {
        if (!next) onClose();
      }}
      variant="action"
      size="sm"
      title={title}
      isBusy={busy}
      footer={
        <>
          <Button type="button" variant="outline" onClick={onClose} disabled={busy}>
            {t("Cancel")}
          </Button>
          <Button type="submit" form="rate-line-form" disabled={busy}>
            {busy ? t("Saving") : verb}
          </Button>
        </>
      }
    >
      <form id="rate-line-form" onSubmit={(e) => void submit(e)} noValidate className="grid grid-cols-1 gap-3">
        {error && (
          <Alert variant="destructive">
            <AlertDescription>{error}</AlertDescription>
          </Alert>
        )}
        {kind !== "fixed" && (
          <FormField label={t("Orders:Line")} htmlFor="rate-line-line" floating="always">
            {/* DEVIATION(UI-LOOK-004): the order's own lines (or the order itself): nothing to create in place, a line is added on the Lines tab, approved in docs/designs/61-transport-app.md (Library components, Deviations). */}
            <ReferenceCombobox id="rate-line-line" value={lineId} onChange={setLineId} options={[{ value: "", label: t("Orders:OrderLevel") }, ...lines.filter((l) => l.id).map((l) => ({ value: l.id!, label: l.label }))]} />
          </FormField>
        )}
        {kind === "service" && (
          <FormField label={t("Financial:AdditionalService")} htmlFor="rate-line-service" required error={serviceError} floating="always">
            <ReferenceCombobox
              id="rate-line-service"
              value={serviceId}
              onChange={setServiceId}
              options={serviceOptions}
              required
              aria-invalid={serviceError != null}
              {...inPlace.offer("AdditionalService", busy, (r) => setServiceId(r.id))}
            />
          </FormField>
        )}
        {kind === "manual" && (
          <FormField label={t("Orders:Description")} htmlFor="rate-line-description">
            <Input id="rate-line-description" value={description} maxLength={256} onChange={(e) => setDescription(e.target.value)} />
          </FormField>
        )}
        {kind !== "fixed" && (
          <FormField label={t("Financial:Quantity")} htmlFor="rate-line-quantity">
            <Input id="rate-line-quantity" inputMode="decimal" className="tabular-nums" value={quantity} onChange={(e) => setQuantity(e.target.value)} />
          </FormField>
        )}
        <FormField label={`${kind === "fixed" ? t("Financial:FixedPrice") : t("Financial:UnitPrice")} (${currency})`} htmlFor="rate-line-price" required error={priceError}>
          <Input id="rate-line-price" inputMode="decimal" className="tabular-nums" value={unitPrice} onChange={(e) => setUnitPrice(e.target.value)} autoFocus />
        </FormField>
      </form>
      {inPlace.dialog}
    </EntityDialog>
  );
}
