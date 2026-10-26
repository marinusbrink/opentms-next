import { useMemo } from "react";
import type { ColDef } from "ag-grid-community";
import { Plus } from "lucide-react";

import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { FormSection } from "@/components/ui/form-section";
import { LazyLinesGrid } from "@/components/ui/lines-grid-lazy";
import { AttributeFields } from "@/apps/transport/components/AttributeFields";
import { DateField, NumberField, SelectField } from "@/apps/transport/components/fields";
import { MAX_REFERENCES, newReference, type OrderValues, type ReferenceValues } from "@/apps/transport/orders/order-form";
import { RECURRENCE_DAYS, TEMPLATE_RECURRENCES } from "@/domains/orders/templates";
import { useOrderAttributeDefinitions } from "@/domains/orders/transport-orders";
import type { FormContext } from "@/lib/forms/use-form-state";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* Order-level sections of the order page (design 61 "Order page, revised" 1): References and attributes, and the
 * template's Schedule (recurrence, interval, weekdays / day of month, horizon, validity). Collapsible FormSections,
 * opened on submit when they hold an error (UI-FORM-006). */

/** The order's references (design 58: type, value, description; at most 50), edited inline, and the order's
 * attributes (the dynamic fields from the definitions). */
export function ReferencesSection({ form, open, onOpenChange }: { form: FormContext<OrderValues>; open: boolean; onOpenChange: (open: boolean) => void }) {
  const { t } = useL();
  const definitions = useOrderAttributeDefinitions(1);
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
    <FormSection title={t("Orders:ReferencesAndAttributes")} collapsible open={open} onOpenChange={onOpenChange} hasError={form.error("references") != null}>
      <h4 className="mb-2 text-label font-semibold text-muted-foreground">{t("Orders:References")}</h4>
      {form.error("references") && <p className="mb-2 text-label text-destructive">{form.error("references")}</p>}
      <div className="h-40">
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
      <AttributeFields idPrefix={form.id("order")} definitions={definitions.data} value={form.values.attributes} onChange={(next) => form.set("attributes", next)} readOnly={form.readOnly} className="mt-4" />
    </FormSection>
  );
}

export function ScheduleSection({ form, open, onOpenChange }: { form: FormContext<OrderValues>; open: boolean; onOpenChange: (open: boolean) => void }) {
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
    <FormSection title={t("Orders:Schedule")} collapsible open={open} onOpenChange={onOpenChange} hasError={form.error("schedule") != null}>
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
