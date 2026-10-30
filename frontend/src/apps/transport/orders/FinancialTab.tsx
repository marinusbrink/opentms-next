import { useMemo, useState, type ReactNode } from "react";
import { Plus, Tag, Trash2 } from "lucide-react";
import { toast } from "sonner";

import { Alert, AlertDescription } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { FormField } from "@/components/ui/form-field";
import { FormSection } from "@/components/ui/form-section";
import { MultiSelect } from "@/components/ui/multi-select";
import { RadioGroup, RadioGroupItem } from "@/components/ui/radio-group";
import { CheckboxField, ComboField, NumberField, SelectField, TextAreaField, toOptions } from "@/apps/transport/components/fields";
import { CurrencyField } from "@/apps/transport/components/CurrencyField";
import type { EditorMode, OrderValues } from "@/apps/transport/orders/order-form";
import { OrderTotals } from "@/apps/transport/orders/OrderTotals";
import { RateLineDialog, RateLinesGrid } from "@/apps/transport/orders/RateLines";
import type { PricePreviewState } from "@/apps/transport/orders/use-price-preview";
import type { PriceListDto } from "@/domains/financial/price-lists";
import { discountsOrSurcharges, vatPercentages } from "@/domains/financial/reference-data";
import { MULTIPLE_MATCHES, RATE_LINE_KIND, RATE_SIDE, useRemoveFixedPrice, type RateLineSetDto } from "@/domains/financial/rate-lines";
import { PriceListsPerBasis } from "@/domains/financial/components/PriceListsPerBasis";
import { withCreatedList, withKnownList } from "@/domains/financial/price-lists";
import { useCreateInPlace } from "@/domains/master-data/components/create-in-place";
import type { TransportOrderDto } from "@/domains/orders/transport-orders";
import { getApiErrorMessage } from "@/lib/api/errors";
import type { FormContext } from "@/lib/forms/use-form-state";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The Financial tab of the order page (design 61 "Order page, legacy tab structure" 6), the legacy blocks:
 * - Price calculation: fixed price for the order (first, PO 2026-10-28) vs the freight items priced individually (the terms' fixed price
 *   amount), what happens on multiple matches, and the price lists per basis (design 61 "Order page review" 3: a row
 *   per charged basis in the legacy order plus "Add price basis", PriceListsPerBasis; the fixed price disables them);
 * - Revenue overview (read-only, OrderTotals): freight items, fixed price, additional services, discounts or
 *   surcharges and the total, with the CalculatingIndicator; a stored order only;
 * - Invoicing: currency, VAT, discounts or surcharges with the defaults marked, payment term, freight documents with
 *   the invoice;
 * - Remarks for invoicing (`invoicingRemarks`);
 * - the order-level lines (manual lines of the order, the fixed price, D&S) with Add manual line and Set or Remove
 *   fixed price, once the order is stored. The additional services are a tab of General; a freight item's own lines
 *   are on its Pricing tab.
 * Left column: Price calculation and Invoicing; right column: Revenue overview and Remarks; the order-level lines span
 * both (PO 2026-10-28: the revenue overview no longer stretches next to the price calculation).
 * The financial defaults are copied when the customer is chosen (OrderHeader). The terms' one message shows on top
 * (UI-FB-004). */

export interface FinancialTabProps {
  form: FormContext<OrderValues>;
  mode: EditorMode;
  order: TransportOrderDto | undefined;
  rateLines: RateLineSetDto | undefined;
  canEditRateLines: boolean;
  /** The ids of the stored freight items (Revenue overview). */
  lineIds: string[];
  preview: PricePreviewState;
}

export function FinancialTab({ form, mode, order, rateLines, canEditRateLines, lineIds, preview }: FinancialTabProps) {
  const { t } = useL();
  const [dialog, setDialog] = useState<"manual" | "fixed" | null>(null);
  const [error, setError] = useState<string | null>(null);
  const removeFixed = useRemoveFixedPrice();
  const currency = rateLines?.currencyCode ?? form.values.terms.currencyCode;
  const orderLevel = useMemo(() => (rateLines?.lines ?? []).filter((line) => !line.orderLineId && line.kind !== RATE_LINE_KIND.AdditionalService), [rateLines]);
  const hasFixedPrice = rateLines?.totals?.hasFixedPrice === true;
  const editable = canEditRateLines && !form.readOnly;
  const stored = mode === "order" && order?.id != null;
  return (
    <div className="flex flex-col gap-4" data-slot="financial-tab">
      {form.error("terms") && <p className="text-label text-destructive">{form.error("terms")}</p>}
      <TermsFields form={form} order={order} revenue={stored || preview.preview ? <OrderTotals rateLines={rateLines} lineIds={lineIds} currency={form.values.terms.currencyCode} preview={preview} /> : <p className="text-label text-muted-foreground" data-slot="revenue-after-save">{t(mode !== "order" ? "Orders:RevenueOnOrders" : preview.failed ? "Orders:PricePreviewUnavailable" : "Orders:RevenueAfterSave")}</p>} />
      {stored && (
        <FormSection title={t("Orders:OrderLevelLines")}>
          <div className="flex flex-col gap-2" data-slot="order-level-rate-lines">
            {error && (
              <Alert variant="destructive">
                <AlertDescription>{error}</AlertDescription>
              </Alert>
            )}
            <RateLinesGrid
              rows={orderLevel}
              currency={currency}
              editable={editable}
              onError={setError}
              toolbar={
                <>
                  <Button type="button" size="sm" variant="outline" onClick={() => setDialog("manual")}>
                    <Plus /> {t("Financial:AddManualLine")}
                  </Button>
                  {hasFixedPrice ? (
                    <Button
                      type="button"
                      size="sm"
                      variant="outline"
                      onClick={() => removeFixed.mutateAsync(order.id!).then(() => toast.success(t("Financial:RateLineDeleted"))).catch((err: unknown) => setError(getApiErrorMessage(err, t)))}
                    >
                      <Trash2 /> {t("Financial:RemoveFixedPrice")}
                    </Button>
                  ) : (
                    <Button type="button" size="sm" variant="outline" onClick={() => setDialog("fixed")}>
                      <Tag /> {t("Financial:SetFixedPrice")}
                    </Button>
                  )}
                </>
              }
            />
          </div>
        </FormSection>
      )}
      {order?.id && dialog && <RateLineDialog kind={dialog} orderId={order.id} orderLineId={null} currency={currency} onClose={() => setDialog(null)} />}
    </div>
  );
}

function TermsFields({ form, order, revenue }: { form: FormContext<OrderValues>; order: TransportOrderDto | undefined; revenue: ReactNode }) {
  const { t } = useL();
  const vatLookup = vatPercentages.useLookup();
  const vatOptions = useMemo(() => toOptions(vatLookup.data, { id: form.values.terms.vatPercentageId, name: order?.financialTerms?.vatPercentageName }), [vatLookup.data, form.values.terms.vatPercentageId, order?.financialTerms?.vatPercentageName]);
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

  /* Fixed price vs priced per freight item (the legacy radio): the choice follows the amount; choosing "per item"
   * clears it. The choice of "fixed" with no amount typed yet is kept locally until the amount is there. */
  const [fixedChosen, setFixedChosen] = useState(false);
  const fixed = fixedChosen || terms.fixedPriceAmount.trim() !== "";

  return (
    /* Two columns that stack on their own (no shared row height): what is set on the left, what it yields and the
     * remarks on the right, so the short revenue overview does not stretch to the height of the price calculation. */
    <div className="grid grid-cols-2 items-start gap-4" data-slot="financial-terms">
      <div className="flex flex-col gap-4">
        <FormSection title={t("Orders:PriceCalculation")}>
          <div className="flex flex-col gap-3">
            <RadioGroup
              value={fixed ? "fixed" : "items"}
              onValueChange={(next) => {
                setFixedChosen(next === "fixed");
                if (next !== "fixed") setTerms({ fixedPriceAmount: "" });
              }}
              disabled={form.readOnly}
              aria-label={t("Orders:PriceCalculation")}
              className="flex flex-wrap gap-x-6"
              data-slot="price-calculation-choice"
            >
              <label className="flex items-center gap-2 text-sm">
                <RadioGroupItem value="fixed" />
                {t("Orders:FixedPriceForOrder")}
              </label>
              <label className="flex items-center gap-2 text-sm">
                <RadioGroupItem value="items" />
                {t("Orders:PricedPerFreightItem")}
              </label>
            </RadioGroup>
            <div className="grid grid-cols-2 gap-x-4 gap-y-3">
              <NumberField form={termsForm} field="fixedPriceAmount" labelKey="Orders:FixedPriceAmount" disabled={!fixed} />
              <SelectField form={termsForm} field="multipleMatches" labelKey="Orders:MultipleMatches" items={MULTIPLE_MATCHES.map((m) => ({ value: m.value, label: t(m.key) }))} />
            </div>
            <PriceListsPerBasis
              idPrefix={form.id("terms-bases")}
              value={{ priceListIds: terms.priceListIds, chargedBases: terms.chargedBases }}
              onChange={(next) => setTerms(next)}
              known={terms.knownPriceLists}
              disabled={form.readOnly || fixed}
              createInPlace={inPlace.offer<PriceListDto>("PriceList", form.readOnly || fixed, (r) => {
                /* The order's terms hold revenue lists only (design 57); a cost list made here is not one of them. A new
                 * revenue list joins the row of its basis. */
                if (r.dto.side === RATE_SIDE.Revenue)
                  setLatest((current) => ({
                    ...withCreatedList(current, r.id, r.dto.basis),
                    knownPriceLists: r.dto.basis == null ? current.knownPriceLists : withKnownList(current.knownPriceLists, { id: r.id, basis: r.dto.basis, label: [r.code, r.name].filter(Boolean).join(" ") }),
                  }));
              })}
            />
          </div>
        </FormSection>
        <FormSection title={t("Orders:Invoicing")}>
          <div className="grid grid-cols-2 gap-x-4 gap-y-3">
            <CurrencyField id={form.id("terms-currencyCode")} label={t("Orders:CurrencyCode")} value={terms.currencyCode} onChange={(next) => setTerms({ currencyCode: next ?? "" })} required disabled={form.readOnly} />
            <ComboField
              form={termsForm}
              field="vatPercentageId"
              labelKey="Orders:VatPercentage"
              options={vatOptions}
              {...inPlace.offer("VatPercentage", form.readOnly, (r) => setLatest(() => ({ vatPercentageId: r.id })))}
            />
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
            <NumberField form={termsForm} field="paymentTermDays" labelKey="Orders:PaymentTermDays" />
            <CheckboxField form={termsForm} field="sendFreightDocumentsWithInvoice" labelKey="Orders:SendFreightDocumentsWithInvoice" />
          </div>
        </FormSection>
      </div>
      <div className="flex flex-col gap-4">
        <FormSection title={t("Orders:RevenueOverview")}>{revenue}</FormSection>
        <FormSection title={t("Orders:InvoicingRemarksBlock")}>
          <div className="grid grid-cols-1">
            <TextAreaField form={form} field="invoicingRemarks" labelKey="Orders:InvoicingRemarks" maxLength={2500} rows={5} />
          </div>
        </FormSection>
      </div>
      {inPlace.dialog}
    </div>
  );
}
