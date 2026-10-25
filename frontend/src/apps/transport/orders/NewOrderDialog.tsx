import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Save } from "lucide-react";
import { toast } from "sonner";

import { Alert, AlertDescription } from "@/components/ui/alert";
import { EntityDialog } from "@/components/ui/entity-dialog";
import { ComboField, DateField, TextField } from "@/apps/transport/components/fields";
import { RelationField } from "@/apps/transport/components/RelationFields";
import { fetchRelationFinancialDefaults } from "@/domains/financial/rate-lines";
import type { RelationOption } from "@/domains/master-data/entities";
import { transportOrderTypes } from "@/domains/orders/reference-data";
import { useCreateTransportOrder, useTransportOrderTypeRows, type TransportOrderFinancialTermsInputDto } from "@/domains/orders/transport-orders";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useFormState, type FieldErrors } from "@/lib/forms/use-form-state";
import { toIsoDate } from "@/lib/format";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { useOpenRecord } from "@/lib/use-open-record";

/* New order (design 61, Assumption 1): a small editor dialog for the minimum fields (type, customer, order date,
 * customer reference) that creates the Draft and then opens its page. The type's default stops become the order's
 * stop pattern and the customer's financial defaults its terms (design 58, build note 7: the screen posts both). */

const CUSTOMER_ROLE = 1;

interface Values {
  transportOrderTypeId: string | null;
  customer: RelationOption | null;
  orderDate: string | null;
  customerReference: string;
}

const validate = (v: Values): FieldErrors<Values> => {
  const errors: FieldErrors<Values> = {};
  if (!v.transportOrderTypeId) errors.transportOrderTypeId = "MasterData:Required";
  if (!v.customer) errors.customer = "MasterData:Required";
  if (!v.orderDate) errors.orderDate = "MasterData:Required";
  return errors;
};

export interface NewOrderDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** The type chosen in the command bar's New dropdown; the default type otherwise. */
  typeId?: string;
}

/** The customer's financial defaults as the order's terms (revenue side). */
export function termsFromDefaults(defaults: Awaited<ReturnType<typeof fetchRelationFinancialDefaults>>): TransportOrderFinancialTermsInputDto | undefined {
  if (!defaults) return undefined;
  return {
    currencyCode: defaults.currencyCode ?? "EUR",
    vatPercentageId: defaults.vatPercentageId ?? null,
    paymentTermDays: defaults.paymentTermDays ?? null,
    priceListIds: (defaults.revenuePriceLists ?? []).map((p) => p.priceListId!).filter(Boolean),
    multipleMatches: defaults.revenueMultipleMatches,
    discountsOrSurcharges: (defaults.discountsOrSurcharges ?? []).filter((d) => d.discountOrSurchargeId).map((d) => ({ discountOrSurchargeId: d.discountOrSurchargeId!, source: 1 as const })),
    sendFreightDocumentsWithInvoice: defaults.sendFreightDocumentsWithInvoice ?? false,
  };
}

export function NewOrderDialog({ open, onOpenChange, typeId }: NewOrderDialogProps) {
  const { t } = useL();
  const openRecord = useOpenRecord();
  const create = useCreateTransportOrder();
  const types = useTransportOrderTypeRows(open);
  const typeOptions = useMemo(() => (types.data ?? []).map((row) => ({ value: row.id ?? "", label: row.name ?? row.code ?? "" })), [types.data]);
  const [error, setError] = useState<string | null>(null);
  const formRef = useRef<HTMLFormElement>(null);

  const initial = useMemo<Values>(() => ({ transportOrderTypeId: typeId ?? null, customer: null, orderDate: toIsoDate(new Date()), customerReference: "" }), [typeId]);
  const { form, isDirty, submit, reset } = useFormState<Values>(initial, validate, false);
  useEffect(() => {
    if (open) reset({ ...initial, transportOrderTypeId: typeId ?? (types.data ?? []).find((row) => row.isDefault)?.id ?? null });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, typeId, types.data]);

  const type = transportOrderTypes.useDetail(form.values.transportOrderTypeId ?? undefined);

  const save = useCallback(async () => {
    const values = submit(formRef.current);
    if (!values) return;
    setError(null);
    try {
      const defaults = await fetchRelationFinancialDefaults(values.customer!.id).catch(() => null);
      const order = await create.mutateAsync({
        transportOrderTypeId: values.transportOrderTypeId!,
        customerRelationId: values.customer!.id,
        orderDate: values.orderDate,
        customerReference: values.customerReference.trim() || null,
        defaultStops: (type.data?.defaultWaypoints ?? []).map((w) => ({ waypointTypeId: w.waypointTypeId ?? "", dayOffset: 0 })),
        financialTerms: termsFromDefaults(defaults),
        source: 1,
      });
      toast.success(t("Orders:TransportOrderSaved"));
      onOpenChange(false);
      if (order.id) openRecord("TransportOrder", order.id);
    } catch (err) {
      setError(getApiErrorMessage(err, t));
    }
  }, [submit, create, type.data, t, onOpenChange, openRecord]);

  return (
    <EntityDialog
      open={open}
      onOpenChange={onOpenChange}
      variant="editor"
      size="sm"
      title={t("Orders:NewTransportOrder")}
      isDirty={isDirty}
      isBusy={create.isPending}
      commands={[{ id: "save", labelKey: create.isPending ? "Saving" : "Save", icon: Save, isPrimary: true, onClick: () => void save() }]}
    >
      <form
        ref={formRef}
        noValidate
        onSubmit={(e) => {
          e.preventDefault();
          void save();
        }}
        className="grid grid-cols-1 gap-3"
      >
        {error && (
          <Alert variant="destructive">
            <AlertDescription>{error}</AlertDescription>
          </Alert>
        )}
        <ComboField form={form} field="transportOrderTypeId" labelKey="Orders:TransportOrderType" required options={typeOptions} />
        <RelationField id={form.id("customer")} label={t("Orders:Customer")} value={form.values.customer} onChange={(next) => form.set("customer", next)} roles={CUSTOMER_ROLE} required error={form.error("customer")} />
        <DateField form={form} field="orderDate" labelKey="Orders:OrderDate" required />
        <TextField form={form} field="customerReference" labelKey="Orders:CustomerReference" maxLength={128} />
        <button type="submit" className="hidden" aria-hidden="true" tabIndex={-1} />
      </form>
    </EntityDialog>
  );
}
