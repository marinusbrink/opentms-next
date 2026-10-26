import { useMemo } from "react";

import { Checkbox } from "@/components/ui/checkbox";
import { FormField } from "@/components/ui/form-field";
import { FormSection } from "@/components/ui/form-section";
import { TreeSelect, buildTree } from "@/components/ui/tree-select";
import { AttributeFields } from "@/apps/transport/components/AttributeFields";
import { CheckboxField, ComboField, DateField, NumberField, SelectField, TextAreaField, TextField, toOptions } from "@/apps/transport/components/fields";
import { ContactPersonField, RelationField } from "@/apps/transport/components/RelationFields";
import { StopsEditor } from "@/apps/transport/components/StopsEditor";
import { REQUIRED_FLAG, hasFlag, type EditorMode, type OrderValues } from "@/apps/transport/orders/order-form";
import { useCreateInPlace } from "@/domains/master-data/components/create-in-place";
import { RECURRENCE_DAYS, TEMPLATE_RECURRENCES } from "@/domains/orders/templates";
import { timeSlots, transportOrderTypes, waypointTypes, type TransportOrderTypeDto } from "@/domains/orders/reference-data";
import { useOrderAttributeDefinitions } from "@/domains/orders/transport-orders";
import { useAllOrganizationUnits } from "@/domains/platform/administration-organization-units";
import type { FormContext } from "@/lib/forms/use-form-state";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The General tab of the order editor (design 61): Type (locked once numbered), Customer, Contact, Order date,
 * Customer reference, Organisation unit, Attributes (from the definitions), remarks, and the Stop pattern (the default
 * stops of new lines) as a LinesGrid. In template mode the header carries the template's name, description and
 * Active, and the Schedule section (recurrence, interval, weekdays / day of month, horizon, validity). */

/** Relations with the Customer role (design 43 flags). */
const CUSTOMER_ROLE = 1;

export interface GeneralTabProps {
  form: FormContext<OrderValues>;
  mode: EditorMode;
  /** The order has a number: its type is locked (design 58, `OrderDatePeriodLocked` and the numbering per type). */
  numbered: boolean;
  type: TransportOrderTypeDto | undefined;
}

export function GeneralTab({ form, mode, numbered, type }: GeneralTabProps) {
  const { t } = useL();
  const typeLookup = transportOrderTypes.useLookup();
  const typeOptions = useMemo(() => toOptions(typeLookup.data, { id: form.values.transportOrderTypeId, name: type?.name }), [typeLookup.data, form.values.transportOrderTypeId, type?.name]);
  const waypointLookup = waypointTypes.useLookup();
  const waypointOptions = useMemo(() => toOptions(waypointLookup.data), [waypointLookup.data]);
  const slotLookup = timeSlots.useLookup();
  const slotOptions = useMemo(() => toOptions(slotLookup.data), [slotLookup.data]);
  const units = useAllOrganizationUnits();
  const unitTree = useMemo(() => buildTree(units.data ?? [], (u) => u.id ?? "", (u) => u.parentId, (u) => u.displayName ?? ""), [units.data]);
  const definitions = useOrderAttributeDefinitions(1);
  const flags = type?.requiredFields as number | undefined;
  /* "Add new …" in the type and organisation-unit pickers (UI-LOOK-004); the relation pickers carry their own. */
  const inPlace = useCreateInPlace();

  return (
    <div className="flex flex-col gap-4">
      <div className="grid grid-cols-2 gap-4">
        <FormSection title={mode === "template" ? t("Orders:TransportOrderTemplate") : t("Orders:TransportOrder")}>
          <div className="grid grid-cols-2 gap-x-4 gap-y-3">
            {mode === "template" && (
              <>
                <TextField form={form} field="name" labelKey="Orders:Name" required maxLength={128} />
                <CheckboxField form={form} field="isActive" labelKey="Orders:IsActive" />
              </>
            )}
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
            <RelationField
              id={form.id("customer")}
              label={t("Orders:Customer")}
              value={form.values.customer}
              onChange={(next) => {
                form.set("customer", next);
                form.set("customerContactPersonId", null);
              }}
              roles={CUSTOMER_ROLE}
              required
              disabled={form.readOnly}
              error={form.error("customer")}
            />
            <ContactPersonField id={form.id("customerContactPersonId")} relationId={form.values.customer?.id} value={form.values.customerContactPersonId} onChange={(next) => form.set("customerContactPersonId", next)} disabled={form.readOnly} />
            <TextField form={form} field="customerReference" labelKey="Orders:CustomerReference" required={hasFlag(flags, REQUIRED_FLAG.CustomerReference)} maxLength={128} />
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
            {mode === "template" && <TextAreaField form={form} field="description" labelKey="MasterData:Remarks" maxLength={2500} className="col-span-2" />}
          </div>
        </FormSection>
        <div className="flex flex-col gap-4">
          <FormSection title={t("Orders:Remarks")}>
            <div className="grid grid-cols-1 gap-3">
              <TextAreaField form={form} field="internalRemarks" labelKey="Orders:InternalRemarks" maxLength={2500} />
              <TextAreaField form={form} field="invoicingRemarks" labelKey="Orders:InvoicingRemarks" maxLength={2500} />
            </div>
          </FormSection>
          <AttributeFields idPrefix={form.id("order")} title={t("Orders:Attributes")} definitions={definitions.data} value={form.values.attributes} onChange={(next) => form.set("attributes", next)} readOnly={form.readOnly} />
        </div>
      </div>
      {mode === "template" && <ScheduleSection form={form} />}
      <FormSection title={t("Orders:DefaultStops")}>
        <p className="mb-2 text-label text-muted-foreground">{t("Orders:DefaultStopsHint")}</p>
        {form.error("defaultStops") && <p className="mb-2 text-label text-destructive">{form.error("defaultStops")}</p>}
        <StopsEditor
          idPrefix={form.id("pattern")}
          stops={form.values.defaultStops}
          onChange={(next) => {
            form.set("defaultStops", next);
            form.blur("defaultStops");
          }}
          mode={mode}
          kind="pattern"
          readOnly={form.readOnly}
          orderDate={form.values.orderDate}
          waypointTypeOptions={waypointOptions}
          timeSlotOptions={slotOptions}
        />
      </FormSection>
      {inPlace.dialog}
    </div>
  );
}

function ScheduleSection({ form }: { form: FormContext<OrderValues> }) {
  const { t } = useL();
  const schedule = form.values.schedule;
  const set = (change: Partial<OrderValues["schedule"]>) => {
    form.set("schedule", { ...schedule, ...change });
    form.blur("schedule");
  };
  const scheduleForm = {
    ...form,
    values: schedule,
    set: (field: string, value: unknown) => set({ [field]: value }),
/* The section shows its one message above the fields (UI-FB-004: exactly once), not under each field. */
    error: () => undefined,
    blur: () => form.blur("schedule"),
    id: (field: string) => form.id(`schedule-${field}`),
  } as unknown as FormContext<OrderValues["schedule"]>;

  return (
    <FormSection title={t("Orders:Schedule")}>
      {form.error("schedule") && <p className="mb-2 text-label text-destructive">{form.error("schedule")}</p>}
      <div className="grid grid-cols-4 gap-x-4 gap-y-3">
        <SelectField form={scheduleForm} field="recurrence" labelKey="Orders:Recurrence" items={TEMPLATE_RECURRENCES.map((r) => ({ value: r.value, label: t(r.key) }))} />
        <NumberField form={scheduleForm} field="interval" labelKey="Orders:Interval" required disabled={schedule.recurrence === 0} />
        <NumberField form={scheduleForm} field="horizonDays" labelKey="Orders:HorizonDays" required />
        {schedule.recurrence === 2 ? <NumberField form={scheduleForm} field="dayOfMonth" labelKey="Orders:DayOfMonth" required /> : <span />}
        <DateField form={scheduleForm} field="validFrom" labelKey="Orders:ValidFrom" required />
        <DateField form={scheduleForm} field="validTill" labelKey="Orders:ValidTill" />
        {schedule.recurrence === 1 && (
          <fieldset className="col-span-2 flex flex-wrap items-center gap-x-4 gap-y-1" aria-label={t("Orders:DaysOfWeek")}>
            <legend className="text-label font-medium text-muted-foreground">{t("Orders:DaysOfWeek")}</legend>
            {RECURRENCE_DAYS.map((day) => (
              <label key={day.flag} className="flex h-8 items-center gap-2 text-sm" htmlFor={form.id(`day-${day.flag}`)}>
                <Checkbox
                  id={form.id(`day-${day.flag}`)}
                  checked={(schedule.daysOfWeek & day.flag) !== 0}
                  disabled={form.readOnly}
                  onCheckedChange={(next) => set({ daysOfWeek: next === true ? schedule.daysOfWeek | day.flag : schedule.daysOfWeek & ~day.flag })}
                />
                {t(day.key)}
              </label>
            ))}
          </fieldset>
        )}
      </div>
    </FormSection>
  );
}
