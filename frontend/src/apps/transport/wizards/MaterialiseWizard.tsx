import { useEffect, useMemo, useState } from "react";
import { toast } from "sonner";

import { Calendar } from "@/components/ui/calendar";
import { Checkbox } from "@/components/ui/checkbox";
import { DatePicker } from "@/components/ui/date-picker";
import { FormField } from "@/components/ui/form-field";
import { FormSection } from "@/components/ui/form-section";
import { Input } from "@/components/ui/input";
import { ReferenceCombobox } from "@/components/ui/reference-combobox";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Wizard, type WizardTask } from "@/components/ui/wizard";
import { useAppFeatures } from "@/app/app-features";
import { NewTemplateDialog } from "@/apps/transport/components/NewTemplateDialog";
import { addDays } from "@/apps/transport/orders/order-form";
import { MAX_COPIES, RECURRENCE_DAYS, copyOrder, materialiseTemplate, useInvalidateOrdersAfterMaterialisation, useTemplateLookup } from "@/domains/orders/templates";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useIsGranted } from "@/lib/auth/permissions";
import { parseInteger } from "@/lib/forms/use-form-state";
import { toDate, toIsoDate, useFormat } from "@/lib/format";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The wizard New from template / Copy order (design 61; UI-WIZ-001 to -005): Source (a template, or the order being
 * copied) → Dates (per weekday over a range, or a calendar with copies per day) → Details (the copies per date; the
 * order's reference, quantity and weight are the template's: the API creates whole orders) → Confirm with per-date
 * progress through `materialise` / `copy-order` (design 58). */

export interface MaterialiseSource {
  kind: "template" | "order";
  id: string;
  label: string;
}

export interface MaterialiseWizardProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** Preselected source; without one the Source step offers the templates. */
  source?: MaterialiseSource;
}

type DatesMode = "weekdays" | "calendar";

export function MaterialiseWizard({ open, onOpenChange, source }: MaterialiseWizardProps) {
  const { t } = useL();
  const f = useFormat();
  const refresh = useInvalidateOrdersAfterMaterialisation();
  const templates = useTemplateLookup(open && !source);
  const [templateId, setTemplateId] = useState<string | null>(source?.kind === "template" ? source.id : null);
  const [datesMode, setDatesMode] = useState<DatesMode>("weekdays");
  const [from, setFrom] = useState<string | null>(toIsoDate(new Date()));
  const [till, setTill] = useState<string | null>(addDays(toIsoDate(new Date()), 6));
  const [weekdays, setWeekdays] = useState<number>(1 | 2 | 4 | 8 | 16);
  const [picked, setPicked] = useState<string[]>([]);
  const [copies, setCopies] = useState("1");
  const [tasks, setTasks] = useState<WizardTask[] | undefined>(undefined);
  const [created, setCreated] = useState(0);
  /* "Add new template…" (UI-LOOK-004): the New template dialog on top (the Templates node's Create permission and
   * feature chain, UI-PERM-006); the new template is selected and named until the lookup refetches. */
  const features = useAppFeatures();
  const canCreateTemplate = useIsGranted("Orders.TransportOrderTemplates.Create") && features.isFeatureEnabled("Apps.Transport.Orders.Templates");
  const [newTemplate, setNewTemplate] = useState<{ term: string } | null>(null);
  const [createdTemplate, setCreatedTemplate] = useState<{ value: string; label: string } | null>(null);

  useEffect(() => {
    if (open) {
      setTasks(undefined);
      setCreated(0);
      setTemplateId(source?.kind === "template" ? source.id : null);
    }
  }, [open, source]);

  const templateOptions = useMemo(() => {
    const options = (templates.data ?? []).map((item) => ({ value: item.id ?? "", label: item.name ?? "" }));
    return createdTemplate && !options.some((o) => o.value === createdTemplate.value) ? [...options, createdTemplate] : options;
  }, [templates.data, createdTemplate]);
  const sourceLabel = source ? source.label : (templateOptions.find((o) => o.value === templateId)?.label ?? "");

  const dates = useMemo(() => {
    if (datesMode === "calendar") return [...picked].sort();
    if (!from || !till || from > till) return [];
    const result: string[] = [];
    let current: string | null = from;
    let guard = 0;
    while (current && current <= till && guard++ < 366) {
      const day = toDate(current)?.getDay() ?? -1;
      const flag = RECURRENCE_DAYS.find((d) => d.day === day)?.flag ?? 0;
      if ((weekdays & flag) !== 0) result.push(current);
      current = addDays(current, 1);
    }
    return result;
  }, [datesMode, picked, from, till, weekdays]);

  const copiesValue = parseInteger(copies);
  const copiesValid = copiesValue != null && !Number.isNaN(copiesValue) && copiesValue >= 1 && copiesValue <= MAX_COPIES;
  const sourceId = source?.kind === "order" ? source.id : templateId;

  const run = async () => {
    if (!sourceId) return;
    setTasks(dates.map((date) => ({ id: date, label: f.date(date), status: "waiting" })));
    let total = 0;
    for (const date of dates) {
      setTasks((prev) => prev?.map((task) => (task.id === date ? { ...task, status: "running" } : task)));
      try {
        const result = source?.kind === "order" ? await copyOrder(source.id, [date], copiesValue ?? 1) : await materialiseTemplate(sourceId, [date], copiesValue ?? 1);
        total += result.createdOrderIds?.length ?? 0;
        setCreated(total);
        setTasks((prev) => prev?.map((task) => (task.id === date ? { ...task, status: "done" } : task)));
      } catch (err) {
        setTasks((prev) => prev?.map((task) => (task.id === date ? { ...task, status: "failed", reason: getApiErrorMessage(err, t) } : task)));
      }
    }
    refresh();
    toast.success(t("Orders:OrdersCreated").replace("{0}", String(total)));
  };

  const modeItems = [
    { value: "weekdays", label: t("Orders:DatesPerWeekday") },
    { value: "calendar", label: t("Orders:DatesFromCalendar") },
  ];

  return (
    <Wizard
      open={open}
      onOpenChange={onOpenChange}
      title={source?.kind === "order" ? t("Orders:CopyOrder") : t("Orders:NewFromTemplate")}
      finishLabel={t("Orders:Materialise")}
      busyLabel={t("Orders:Creating")}
      onFinish={run}
      tasks={tasks}
      runContent={tasks ? <p className="shrink-0 text-sm text-muted-foreground">{t("Orders:OrdersCreated").replace("{0}", String(created))}</p> : undefined}
      steps={[
        {
          id: "source",
          title: t("Orders:Source"),
          isValid: sourceId != null,
          content: (
            <div className="flex flex-col gap-4">
              <p className="text-sm text-muted-foreground">{source?.kind === "order" ? t("Orders:CopyOrderIntro").replace("{0}", source.label) : t("Orders:NewFromTemplateIntro")}</p>
              {source ? (
                <FormSection title={t("Orders:Source")}>
                  <p className="text-sm">{source.label}</p>
                </FormSection>
              ) : (
                <FormSection title={t("Orders:TransportOrderTemplate")}>
                  <FormField label={t("Orders:TransportOrderTemplate")} htmlFor="materialise-template" required floating="always" className="max-w-md">
                    <ReferenceCombobox
                      id="materialise-template"
                      value={templateId}
                      onChange={setTemplateId}
                      options={templateOptions}
                      required
                      onCreateNew={canCreateTemplate && tasks == null ? (term) => setNewTemplate({ term }) : undefined}
                      createLabel={t("Orders:AddNewTransportOrderTemplate")}
                    />
                  </FormField>
                  {newTemplate && (
                    <NewTemplateDialog
                      open
                      initialName={newTemplate.term}
                      onOpenChange={(next) => {
                        if (!next) setNewTemplate(null);
                      }}
                      onCreated={(template) => {
                        setCreatedTemplate({ value: template.id, label: template.name });
                        setTemplateId(template.id);
                      }}
                    />
                  )}
                </FormSection>
              )}
            </div>
          ),
        },
        {
          id: "dates",
          title: t("Orders:Dates"),
          isValid: dates.length > 0,
          content: (
            <div className="flex flex-col gap-4">
              <FormSection title={t("Orders:Dates")}>
                <div className="grid grid-cols-4 gap-x-4 gap-y-3">
                  <FormField label={t("Orders:DatesMode")} htmlFor="materialise-mode" floating="always">
                    <Select items={modeItems} value={datesMode} onValueChange={(next) => setDatesMode((next as DatesMode) ?? "weekdays")}>
                      <SelectTrigger id="materialise-mode" className="h-10 w-full bg-background">
                        <SelectValue />
                      </SelectTrigger>
                      <SelectContent>
                        {modeItems.map((item) => (
                          <SelectItem key={item.value} value={item.value}>
                            {item.label}
                          </SelectItem>
                        ))}
                      </SelectContent>
                    </Select>
                  </FormField>
                  {datesMode === "weekdays" ? (
                    <>
                      <FormField label={t("Financial:From")} htmlFor="materialise-from" required floating="always">
                        <DatePicker id="materialise-from" value={from} onChange={setFrom} required />
                      </FormField>
                      <FormField label={t("Financial:To")} htmlFor="materialise-till" required floating="always">
                        <DatePicker id="materialise-till" value={till} onChange={setTill} required />
                      </FormField>
                      <fieldset className="col-span-4 flex flex-wrap items-center gap-x-4 gap-y-1" aria-label={t("Orders:DaysOfWeek")}>
                        <legend className="text-label font-medium text-muted-foreground">{t("Orders:DaysOfWeek")}</legend>
                        {RECURRENCE_DAYS.map((day) => (
                          <label key={day.flag} className="flex h-8 items-center gap-2 text-sm" htmlFor={`materialise-day-${day.flag}`}>
                            <Checkbox id={`materialise-day-${day.flag}`} checked={(weekdays & day.flag) !== 0} onCheckedChange={(next) => setWeekdays((v) => (next === true ? v | day.flag : v & ~day.flag))} />
                            {t(day.key)}
                          </label>
                        ))}
                      </fieldset>
                    </>
                  ) : (
                    <div className="col-span-3">
                      <Calendar
                        mode="multiple"
                        selected={picked.map((d) => toDate(d)!).filter(Boolean)}
                        onSelect={(next: Date[] | undefined) => setPicked((next ?? []).map(toIsoDate))}
                      />
                    </div>
                  )}
                </div>
              </FormSection>
              <p className="text-label text-muted-foreground" data-slot="materialise-dates">
                {t("Orders:DatesSelected").replace("{0}", String(dates.length))}
                {dates.length > 0 && `: ${dates.slice(0, 10).map((d) => f.date(d)).join(", ")}${dates.length > 10 ? "…" : ""}`}
              </p>
            </div>
          ),
        },
        {
          id: "details",
          title: t("Orders:Details"),
          isValid: copiesValid,
          content: (
            <FormSection title={t("Orders:Details")}>
              <div className="grid grid-cols-4 gap-x-4 gap-y-3">
                <FormField label={t("Orders:Copies")} htmlFor="materialise-copies" required error={copiesValid ? undefined : t("OpenTms.Orders:InvalidCopies").replace("{min}", "1").replace("{max}", String(MAX_COPIES))}>
                  <Input id="materialise-copies" inputMode="numeric" className="tabular-nums" value={copies} onChange={(e) => setCopies(e.target.value)} />
                </FormField>
              </div>
              <p className="mt-3 text-label text-muted-foreground">{t("Orders:MaterialiseDetailsHint")}</p>
            </FormSection>
          ),
        },
        {
          id: "confirm",
          title: t("Orders:Confirm"),
          content: (
            <FormSection title={t("Orders:Summary")}>
              <dl className="grid grid-cols-[auto_1fr] gap-x-6 gap-y-2 text-sm">
                <dt className="text-label text-muted-foreground">{t("Orders:Source")}</dt>
                <dd>{sourceLabel}</dd>
                <dt className="text-label text-muted-foreground">{t("Orders:Dates")}</dt>
                <dd className="tabular-nums">{dates.length}</dd>
                <dt className="text-label text-muted-foreground">{t("Orders:Copies")}</dt>
                <dd className="tabular-nums">{copies}</dd>
              </dl>
            </FormSection>
          ),
        },
      ]}
    />
  );
}
