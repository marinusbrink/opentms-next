import { useMemo, useState } from "react";
import { Plus, Tag, Trash2 } from "lucide-react";
import { toast } from "sonner";

import { Alert, AlertDescription } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { FormField } from "@/components/ui/form-field";
import { FormSection } from "@/components/ui/form-section";
import { MultiSelect } from "@/components/ui/multi-select";
import { CheckboxField, ComboField, NumberField, SelectField, toOptions } from "@/apps/transport/components/fields";
import { CurrencyField } from "@/apps/transport/components/CurrencyField";
import type { EditorMode, OrderValues } from "@/apps/transport/orders/order-form";
import { RateLineDialog, RateLinesGrid } from "@/apps/transport/orders/RateLines";
import type { PriceListDto } from "@/domains/financial/price-lists";
import { discountsOrSurcharges, vatPercentages } from "@/domains/financial/reference-data";
import { MULTIPLE_MATCHES, RATE_SIDE, usePriceListLookup, useRemoveFixedPrice, type RateLineSetDto } from "@/domains/financial/rate-lines";
import { useCreateInPlace, withId } from "@/domains/master-data/components/create-in-place";
import type { TransportOrderDto } from "@/domains/orders/transport-orders";
import { getApiErrorMessage } from "@/lib/api/errors";
import type { FormContext } from "@/lib/forms/use-form-state";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The order-level Financial terms section of the order page (design 61 "Order page, revised" 1 and 6): the terms
 * (currency, VAT, payment term, multiple matches, price lists, discounts or surcharges with the defaults marked, fixed
 * price amount, freight documents with the invoice) and the order-level rate lines (manual lines of the order,
 * additional services, the fixed price, D&S) with Add manual line, Add additional service and Set or Remove fixed
 * price. A freight item's own lines are on its Pricing tab. A collapsible FormSection, opened on submit when the terms
 * hold an error (UI-FORM-006). */

export interface FinancialTermsSectionProps {
  form: FormContext<OrderValues>;
  mode: EditorMode;
  order: TransportOrderDto | undefined;
  rateLines: RateLineSetDto | undefined;
  canEditRateLines: boolean;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

export function FinancialTermsSection({ form, mode, order, rateLines, canEditRateLines, open, onOpenChange }: FinancialTermsSectionProps) {
  const { t } = useL();
  const [dialog, setDialog] = useState<"manual" | "service" | "fixed" | null>(null);
  const [error, setError] = useState<string | null>(null);
  const removeFixed = useRemoveFixedPrice();
  const currency = rateLines?.currencyCode ?? form.values.terms.currencyCode;
  const orderLevel = useMemo(() => (rateLines?.lines ?? []).filter((line) => !line.orderLineId), [rateLines]);
  const hasFixedPrice = rateLines?.totals?.hasFixedPrice === true;
  const editable = canEditRateLines && !form.readOnly;
  return (
    <FormSection title={t("Orders:FinancialTerms")} collapsible open={open} onOpenChange={onOpenChange} hasError={form.error("terms") != null}>
      <div className="flex flex-col gap-4">
        <TermsFields form={form} order={order} />
        {mode === "order" && order?.id && (
          <div className="flex flex-col gap-2" data-slot="order-level-rate-lines">
            <h4 className="text-label font-semibold text-muted-foreground">{t("Orders:OrderLevelLines")}</h4>
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
                  <Button type="button" size="sm" variant="outline" onClick={() => setDialog("service")}>
                    <Plus /> {t("Financial:AddAdditionalService")}
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
        )}
      </div>
      {order?.id && dialog && <RateLineDialog kind={dialog} orderId={order.id} orderLineId={null} currency={currency} onClose={() => setDialog(null)} />}
    </FormSection>
  );
}

function TermsFields({ form, order }: { form: FormContext<OrderValues>; order: TransportOrderDto | undefined }) {
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
    <div data-slot="financial-terms">
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
    </div>
  );
}
