import { useEffect, useMemo, useState } from "react";
import { toast } from "sonner";

import { Button } from "@/components/ui/button";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import { FormField } from "@/components/ui/form-field";
import { FormSection } from "@/components/ui/form-section";
import { TreeSelect, buildTree } from "@/components/ui/tree-select";
import { CheckboxField, ComboField, DateField, TextAreaField, TextField, toOptions } from "@/apps/transport/components/fields";
import { ContactPersonField, RelationField } from "@/apps/transport/components/RelationFields";
import { termsFromDefaults } from "@/apps/transport/orders/financial-defaults";
import { REQUIRED_FLAG, hasFlag, termsValuesOfInput, type EditorMode, type OrderValues } from "@/apps/transport/orders/order-form";
import { fetchRelationFinancialDefaults } from "@/domains/financial/rate-lines";
import { useCreateInPlace } from "@/domains/master-data/components/create-in-place";
import type { RelationOption } from "@/domains/master-data/entities";
import { transportOrderTypes, type TransportOrderTypeDto } from "@/domains/orders/reference-data";
import { useAllOrganizationUnits } from "@/domains/platform/administration-organization-units";
import { getApiErrorMessage } from "@/lib/api/errors";
import type { FormContext } from "@/lib/forms/use-form-state";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The order page's header block (design 61 "Order page, revised" 1): Customer, Order type (locked once numbered),
 * Order date, Customer reference, Contact, Organisation unit, and the remarks behind a disclosure. In template mode it
 * carries the template's name, Active and description instead of the order date. The customer comes first: a new
 * order opens with the cursor in it (5). Its financial defaults are copied when the customer of a new order is chosen;
 * on a stored order a customer change asks first (design 58, build note 7: Take over reads them into the terms,
 * Cancel keeps the current terms; the save sends the terms either way). */

/** Relations with the Customer role (design 43 flags). */
const CUSTOMER_ROLE = 1;

export interface OrderHeaderProps {
  form: FormContext<OrderValues>;
  mode: EditorMode;
  /** The order has a number: its type is locked (design 58, the numbering per type). */
  numbered: boolean;
  type: TransportOrderTypeDto | undefined;
  /** A stored order: a customer change asks about the new customer's financial defaults; a new one copies them. */
  stored: boolean;
  /** Puts the cursor in the customer field once (a new order, design 61 "Order page, revised" 5). */
  autoFocusCustomer?: boolean;
}

export function OrderHeader({ form, mode, numbered, type, stored, autoFocusCustomer = false }: OrderHeaderProps) {
  const { t } = useL();
  const typeLookup = transportOrderTypes.useLookup();
  const typeOptions = useMemo(() => toOptions(typeLookup.data, { id: form.values.transportOrderTypeId, name: type?.name }), [typeLookup.data, form.values.transportOrderTypeId, type?.name]);
  const units = useAllOrganizationUnits();
  const unitTree = useMemo(() => buildTree(units.data ?? [], (u) => u.id ?? "", (u) => u.parentId, (u) => u.displayName ?? ""), [units.data]);
  const flags = type?.requiredFields as number | undefined;
  /* "Add new …" in the type and organisation-unit pickers (UI-LOOK-004); the relation pickers carry their own. */
  const inPlace = useCreateInPlace();
  const [defaultsOf, setDefaultsOf] = useState<RelationOption | null>(null);
  const [remarksOpen, setRemarksOpen] = useState(() => !!(form.values.internalRemarks.trim() || form.values.invoicingRemarks.trim()));
  const customerId = form.id("customer");

  useEffect(() => {
    if (autoFocusCustomer) document.getElementById(customerId)?.focus();
  }, [autoFocusCustomer, customerId]);

  const takeOverDefaults = async (customer: RelationOption) => {
    try {
      const defaults = await fetchRelationFinancialDefaults(customer.id);
      form.update((values) => (values.customer?.id === customer.id ? { ...values, terms: termsValuesOfInput(termsFromDefaults(defaults, values.orderDate)) } : values));
      form.blur("terms");
    } catch (err) {
      toast.error(getApiErrorMessage(err, t));
    }
  };
  const changeCustomer = (next: RelationOption | null) => {
    const changed = next != null && next.id !== form.values.customer?.id;
    form.set("customer", next);
    form.set("customerContactPersonId", null);
    if (!changed || mode !== "order") return;
    if (stored) setDefaultsOf(next);
    else void takeOverDefaults(next);
  };

  return (
    <FormSection title={mode === "template" ? t("Orders:TransportOrderTemplate") : t("Orders:OrderDetails")} className="shrink-0">
      <div className="grid grid-cols-4 gap-x-4 gap-y-3" data-slot="order-header">
        {mode === "template" && (
          <>
            <TextField form={form} field="name" labelKey="Orders:Name" required maxLength={128} className="col-span-2" />
            <CheckboxField form={form} field="isActive" labelKey="Orders:IsActive" />
            <span />
          </>
        )}
        <RelationField
          id={customerId}
          label={t("Orders:Customer")}
          value={form.values.customer}
          onChange={changeCustomer}
          roles={CUSTOMER_ROLE}
          required
          disabled={form.readOnly}
          error={form.error("customer")}
        />
        <ComboField
          form={form}
          field="transportOrderTypeId"
          labelKey="Orders:TransportOrderType"
          required
          options={typeOptions}
          disabled={numbered}
          {...inPlace.offer("TransportOrderType", form.readOnly || numbered, (r) => form.set("transportOrderTypeId", r.id))}
        />
        {mode === "order" ? <DateField form={form} field="orderDate" labelKey="Orders:OrderDate" required /> : <span />}
        <TextField form={form} field="customerReference" labelKey="Orders:CustomerReference" required={hasFlag(flags, REQUIRED_FLAG.CustomerReference)} maxLength={128} />
        <ContactPersonField id={form.id("customerContactPersonId")} relationId={form.values.customer?.id} value={form.values.customerContactPersonId} onChange={(next) => form.set("customerContactPersonId", next)} disabled={form.readOnly} />
        <FormField label={t("Orders:OrganizationUnit")} htmlFor={form.id("organizationUnitId")} floating="always">
          <TreeSelect
            id={form.id("organizationUnitId")}
            nodes={unitTree}
            value={form.values.organizationUnitId}
            onChange={(next) => form.set("organizationUnitId", next)}
            disabled={form.readOnly}
            {...inPlace.offer("OrganizationUnit", form.readOnly, (r) => form.set("organizationUnitId", r.id))}
          />
        </FormField>
        <div className="col-span-2 flex items-center">
          <Button type="button" variant="link" size="sm" aria-expanded={remarksOpen} onClick={() => setRemarksOpen((open) => !open)}>
            {t("Orders:Remarks")}
          </Button>
        </div>
        {remarksOpen && (
          <>
            <TextAreaField form={form} field="internalRemarks" labelKey="Orders:InternalRemarks" maxLength={2500} rows={2} className="col-span-2" />
            <TextAreaField form={form} field="invoicingRemarks" labelKey="Orders:InvoicingRemarks" maxLength={2500} rows={2} className="col-span-2" />
          </>
        )}
        {mode === "template" && <TextAreaField form={form} field="description" labelKey="Orders:Description" maxLength={2500} rows={2} className="col-span-4" />}
      </div>
      {inPlace.dialog}
      <ConfirmDialog
        open={defaultsOf != null}
        onOpenChange={(next) => {
          if (!next) setDefaultsOf(null);
        }}
        title={t("Orders:FinancialDefaults")}
        description={`${t("Orders:TakeOverFinancialDefaultsQuestion").replace("{0}", defaultsOf?.name ?? "")} ${t("Orders:TakeOverFinancialDefaultsHint")}`}
        confirmLabel={t("Orders:TakeOver")}
        onConfirm={async () => {
          const customer = defaultsOf;
          setDefaultsOf(null);
          if (customer) await takeOverDefaults(customer);
        }}
      />
    </FormSection>
  );
}
