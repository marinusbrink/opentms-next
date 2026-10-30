import { useMemo } from "react";
import type { ColDef } from "ag-grid-community";
import { Plus } from "lucide-react";

import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { FormSection } from "@/components/ui/form-section";
import { LazyLinesGrid } from "@/components/ui/lines-grid-lazy";
import { AttributeFields, type AttributeDefinition } from "@/apps/transport/components/AttributeFields";
import { DateField, NumberField, SelectField, TextAreaField } from "@/apps/transport/components/fields";
import { MAX_REFERENCES, newReference, type OrderValues, type ReferenceValues } from "@/apps/transport/orders/order-form";
import { RECURRENCE_DAYS, TEMPLATE_RECURRENCES } from "@/domains/orders/templates";
import { ORDER_REMARK_KINDS, type TransportOrderRemarkDto } from "@/domains/orders/transport-orders";
import { useFormat } from "@/lib/format";
import type { FormContext } from "@/lib/forms/use-form-state";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* Parts of the order page's tabs (design 61 "Order page, legacy tab structure" 2): the nested tabs of General Other
 * references, Custom fields and Remarks for the planner, and the template's Schedule tab (recurrence, interval,
 * weekdays / day of month, horizon, validity). Each shows its one message above its fields (UI-FB-004). */

/** Other references (design 58: type, reference, description; at most 50), edited inline: add and remove rows. */
export function OtherReferences({ form }: { form: FormContext<OrderValues> }) {
  const { t } = useL();
  const columns = useMemo<ColDef<ReferenceValues>[]>(
    () => [
      { field: "type", headerName: `${t("Orders:ReferenceType")} *`, flex: 1, minWidth: 120, editable: !form.readOnly },
      { field: "value", headerName: `${t("Orders:ReferenceValue")} *`, flex: 1.5, minWidth: 160, editable: !form.readOnly },
      { field: "description", headerName: t("Orders:Description"), flex: 2, minWidth: 180, editable: !form.readOnly },
    ],
    [t, form.readOnly],
  );
  const setReferences = (references: ReferenceValues[]) => {
    form.set("references", references);
    form.blur("references");
  };
  return (
    <div className="flex flex-col gap-2" data-slot="order-references">
      {form.error("references") && <p className="text-label text-destructive">{form.error("references")}</p>}
      <div className="h-56">
        <LazyLinesGrid<ReferenceValues>
          rows={form.values.references}
          onRowsChange={setReferences}
          getRowId={(row) => row.key}
          columnDefs={columns}
          readOnly={form.readOnly}
          emptyText={t("Orders:NoReferences")}
          toolbar={
            form.values.references.length < MAX_REFERENCES ? (
              <Button type="button" size="sm" variant="outline" onClick={() => setReferences([...form.values.references, newReference()])}>
                <Plus /> {t("Orders:AddReference")}
              </Button>
            ) : undefined
          }
        />
      </div>
    </div>
  );
}

/** The order's attributes (Custom fields): the dynamic fields the order type's definitions give. */
export function CustomFields({ form, definitions }: { form: FormContext<OrderValues>; definitions: AttributeDefinition[] | undefined }) {
  return (
    <div data-slot="order-custom-fields">
      <AttributeFields idPrefix={form.id("order")} definitions={definitions} value={form.values.attributes} onChange={(next) => form.set("attributes", next)} readOnly={form.readOnly} />
    </div>
  );
}

/** Remarks for the planner (`internalRemarks`). */
export function PlannerRemarks({ form }: { form: FormContext<OrderValues> }) {
  return (
    <div className="grid grid-cols-1" data-slot="planner-remarks">
      <TextAreaField form={form} field="internalRemarks" labelKey="Orders:PlannerRemarks" maxLength={2500} rows={8} />
    </div>
  );
}

export function ScheduleSection({ form }: { form: FormContext<OrderValues> }) {
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

/** The Remarks icon tab (UI-FORM-008): the order's dated remarks (a revision's reason), newest first, read-only. */
export function OrderRemarks({ remarks }: { remarks: TransportOrderRemarkDto[] | null | undefined }) {
  const { t } = useL();
  const f = useFormat();
  const rows = [...(remarks ?? [])].sort((a, b) => (b.at ?? "").localeCompare(a.at ?? ""));
  return (
    <FormSection title={t("Remarks")}>
      {rows.length === 0 ? (
        <p className="text-sm text-muted-foreground">{t("Orders:NoRemarks")}</p>
      ) : (
        <ul className="flex flex-col gap-3" data-slot="order-remarks">
          {rows.map((remark, index) => (
            <li key={`${remark.at ?? ""}-${index}`} className="flex flex-col gap-1 rounded-md bg-background p-3" data-slot="order-remark">
              <span className="text-label text-muted-foreground tabular-nums">
                {[f.dateTime(remark.at), remark.kind != null ? t(ORDER_REMARK_KINDS[remark.kind] ?? "") : null].filter(Boolean).join(" · ")}
              </span>
              <p className="text-sm whitespace-pre-wrap">{remark.text}</p>
            </li>
          ))}
        </ul>
      )}
    </FormSection>
  );
}
