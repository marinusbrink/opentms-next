import { useMemo, useState } from "react";
import { Save, Trash2 } from "lucide-react";
import { toast } from "sonner";

import { joinLocal, parseTime, splitLocal } from "@/apps/planboard/panes/resources/model";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { Checkbox } from "@/components/ui/checkbox";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import { DatePicker } from "@/components/ui/date-picker";
import { EntityDialog, type RibbonCommand } from "@/components/ui/entity-dialog";
import { FormField } from "@/components/ui/form-field";
import { Input } from "@/components/ui/input";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Textarea } from "@/components/ui/textarea";
import {
  DRIVER_REASONS,
  flagsFromWeekdays,
  MAX_UNAVAILABILITY_REMARKS,
  RESOURCE_KIND,
  UNAVAILABILITY_REASONS,
  useCreateUnavailability,
  useDeleteUnavailability,
  useUpdateUnavailability,
  VEHICLE_REASONS,
  WEEKDAYS,
  weekdaysFromFlags,
  type ResourceKind,
  type ResourceUnavailabilityDto,
  type UnavailabilityReason,
} from "@/domains/planning-execution/resource-availability";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* One unavailability of a driver or vehicle (design 68 §2, design 70 "Resources"): reason, period (from / till, date and
 * time), an optional weekly repetition of the period's time of day on chosen weekdays until a date, and remarks. Create
 * and edit use this one editor (UI-DLG-009); Delete asks first. Without the Manage permission it is not opened. */

export interface UnavailabilityDialogProps {
  kind: ResourceKind;
  resourceId: string;
  resourceName: string;
  /** The unavailability to edit; absent for a new one. */
  unavailability?: ResourceUnavailabilityDto;
  /** The day a new unavailability starts on (`YYYY-MM-DD`). */
  defaultDate: string;
  canDelete: boolean;
  onClose: () => void;
}

interface Values {
  reason: UnavailabilityReason;
  fromDate: string | null;
  fromTime: string;
  tillDate: string | null;
  tillTime: string;
  repeat: boolean;
  weekdays: number[];
  until: string | null;
  remarks: string;
}

function initialValues(kind: ResourceKind, dto: ResourceUnavailabilityDto | undefined, defaultDate: string): Values {
  if (!dto) {
    return {
      reason: kind === RESOURCE_KIND.Driver ? 1 : 4,
      fromDate: defaultDate,
      fromTime: "00:00",
      tillDate: defaultDate,
      tillTime: "23:59",
      repeat: false,
      weekdays: [],
      until: null,
      remarks: "",
    };
  }
  const from = splitLocal(dto.from);
  const till = splitLocal(dto.till);
  return {
    reason: dto.reason ?? 99,
    fromDate: from.date,
    fromTime: from.time,
    tillDate: till.date,
    tillTime: till.time,
    repeat: dto.recurrenceWeekdays != null && dto.recurrenceWeekdays > 0,
    weekdays: weekdaysFromFlags(dto.recurrenceWeekdays),
    until: dto.recurrenceUntil ?? null,
    remarks: dto.remarks ?? "",
  };
}

type Errors = Partial<Record<keyof Values, string>>;

function validate(v: Values): Errors {
  const errors: Errors = {};
  if (!v.fromDate) errors.fromDate = "Planboard:Resources.Required";
  if (!parseTime(v.fromTime)) errors.fromTime = "Planboard:Resources.InvalidTime";
  if (!v.tillDate) errors.tillDate = "Planboard:Resources.Required";
  if (!parseTime(v.tillTime)) errors.tillTime = "Planboard:Resources.InvalidTime";
  const from = joinLocal(v.fromDate, v.fromTime);
  const till = joinLocal(v.tillDate, v.tillTime);
  if (from && till && till <= from) errors.tillDate = "Planboard:Resources.TillAfterFrom";
  if (v.repeat) {
    if (v.weekdays.length === 0) errors.weekdays = "Planboard:Resources.ChooseWeekdays";
    if (!v.until) errors.until = "Planboard:Resources.Required";
    else if (v.fromDate && v.until < v.fromDate) errors.until = "Planboard:Resources.UntilAfterFrom";
  }
  return errors;
}

export function UnavailabilityDialog({ kind, resourceId, resourceName, unavailability, defaultDate, canDelete, onClose }: UnavailabilityDialogProps) {
  const { t } = useL();
  const isNew = !unavailability?.id;
  const initial = useMemo(() => initialValues(kind, unavailability, defaultDate), [kind, unavailability, defaultDate]);
  const [values, setValues] = useState<Values>(initial);
  const [submitted, setSubmitted] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [confirmDelete, setConfirmDelete] = useState(false);
  const create = useCreateUnavailability();
  const update = useUpdateUnavailability();
  const remove = useDeleteUnavailability();
  const busy = create.isPending || update.isPending || remove.isPending;
  const isDirty = JSON.stringify(values) !== JSON.stringify(initial);
  const errors = submitted ? validate(values) : {};
  const err = (field: keyof Values) => (errors[field] ? t(errors[field]!) : undefined);
  const set = <K extends keyof Values>(field: K, value: Values[K]) => setValues((v) => ({ ...v, [field]: value }));

  const allowed = kind === RESOURCE_KIND.Driver ? DRIVER_REASONS : VEHICLE_REASONS;
  const reasonItems = UNAVAILABILITY_REASONS.map((r) => ({ value: String(r.value), label: t(r.key), offered: allowed.includes(r.value) || r.value === values.reason })).filter((r) => r.offered);

  const save = async () => {
    setSubmitted(true);
    if (Object.keys(validate(values)).length > 0) return;
    setError(null);
    const body = {
      from: joinLocal(values.fromDate, values.fromTime)!,
      till: joinLocal(values.tillDate, values.tillTime)!,
      reason: values.reason,
      remarks: values.remarks.trim() || null,
      recurrenceWeekdays: values.repeat ? flagsFromWeekdays(values.weekdays) : null,
      recurrenceUntil: values.repeat ? values.until : null,
    };
    try {
      if (isNew) await create.mutateAsync({ ...body, resourceKind: kind, resourceId });
      else await update.mutateAsync({ id: unavailability!.id!, dto: { ...body, concurrencyStamp: unavailability!.concurrencyStamp ?? "" } });
      toast.success(t("Planboard:Resources.UnavailabilitySaved"));
      onClose();
    } catch (e) {
      setError(getApiErrorMessage(e, t));
    }
  };

  const commands: RibbonCommand[] = [
    { id: "save", labelKey: "Save", icon: Save, isPrimary: true, onClick: () => void save() },
    ...(!isNew && canDelete ? [{ id: "delete", labelKey: "Delete", icon: Trash2, variant: "destructive" as const, groupKey: "Planboard:Resources.Record", onClick: () => setConfirmDelete(true) }] : []),
  ];

  const id = (field: string) => `unavailability-${field}`;

  return (
    <>
      <EntityDialog
        open
        onOpenChange={(next) => {
          if (!next) onClose();
        }}
        variant="editor"
        size="md"
        title={isNew ? t("Planboard:Resources.NewUnavailability") : `${t("Planboard:Resources.Unavailability")} ${resourceName}`}
        commands={commands}
        isDirty={isDirty}
        isBusy={busy}
      >
        <div className="flex flex-col gap-3">
          {error && (
            <Alert variant="destructive">
              <AlertDescription>{error}</AlertDescription>
            </Alert>
          )}
          <p className="text-sm text-muted-foreground">{resourceName}</p>
          <div className="grid grid-cols-2 gap-x-4 gap-y-3">
            <FormField label={t("Planboard:Resources.Reason")} htmlFor={id("reason")} required floating="always" className="col-span-2">
              <Select items={reasonItems} value={String(values.reason)} onValueChange={(next) => next && set("reason", Number(next) as UnavailabilityReason)}>
                <SelectTrigger id={id("reason")} className="h-10 w-full bg-background">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {reasonItems.map((item) => (
                    <SelectItem key={item.value} value={item.value}>
                      {item.label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </FormField>
            <FormField label={t("Planboard:Resources.FromDate")} htmlFor={id("from-date")} required floating="always" error={err("fromDate")}>
              <DatePicker id={id("from-date")} value={values.fromDate} onChange={(v) => set("fromDate", v)} required />
            </FormField>
            <FormField label={t("Planboard:Resources.FromTime")} htmlFor={id("from-time")} required error={err("fromTime")}>
              <Input id={id("from-time")} value={values.fromTime} onChange={(e) => set("fromTime", e.target.value)} placeholder="HH:mm" className="tabular-nums" />
            </FormField>
            <FormField label={t("Planboard:Resources.TillDate")} htmlFor={id("till-date")} required floating="always" error={err("tillDate")}>
              <DatePicker id={id("till-date")} value={values.tillDate} onChange={(v) => set("tillDate", v)} required />
            </FormField>
            <FormField label={t("Planboard:Resources.TillTime")} htmlFor={id("till-time")} required error={err("tillTime")}>
              <Input id={id("till-time")} value={values.tillTime} onChange={(e) => set("tillTime", e.target.value)} placeholder="HH:mm" className="tabular-nums" />
            </FormField>
            <label htmlFor={id("repeat")} className="col-span-2 flex items-center gap-2 text-sm">
              <Checkbox id={id("repeat")} checked={values.repeat} onCheckedChange={(checked) => set("repeat", checked === true)} />
              {t("Planboard:Resources.RepeatWeekly")}
            </label>
            {values.repeat && (
              <>
                <fieldset className="col-span-2 flex flex-col gap-1" aria-invalid={err("weekdays") ? true : undefined} aria-describedby={err("weekdays") ? id("weekdays-error") : undefined}>
                  <legend className="text-label font-medium text-muted-foreground">{t("Planboard:Resources.Weekdays")}</legend>
                  <div className="flex flex-wrap gap-3">
                    {WEEKDAYS.map((day) => (
                      <label key={day} htmlFor={id(`weekday-${day}`)} className="flex items-center gap-1.5 text-sm">
                        <Checkbox
                          id={id(`weekday-${day}`)}
                          checked={values.weekdays.includes(day)}
                          onCheckedChange={(checked) => set("weekdays", checked === true ? [...new Set([...values.weekdays, day])].sort() : values.weekdays.filter((d) => d !== day))}
                        />
                        {t(`Planboard:Resources.WeekdayShort.${day}`)}
                      </label>
                    ))}
                  </div>
                  {err("weekdays") && (
                    <p id={id("weekdays-error")} className="text-label text-destructive">
                      {err("weekdays")}
                    </p>
                  )}
                </fieldset>
                <FormField label={t("Planboard:Resources.RepeatUntil")} htmlFor={id("until")} required floating="always" error={err("until")}>
                  <DatePicker id={id("until")} value={values.until} onChange={(v) => set("until", v)} required />
                </FormField>
              </>
            )}
            <FormField label={t("Planboard:Resources.Remarks")} htmlFor={id("remarks")} className="col-span-2">
              <Textarea id={id("remarks")} value={values.remarks} maxLength={MAX_UNAVAILABILITY_REMARKS} onChange={(e) => set("remarks", e.target.value)} rows={3} />
            </FormField>
          </div>
        </div>
      </EntityDialog>
      {confirmDelete && unavailability?.id && (
        <ConfirmDialog
          open
          onOpenChange={(open) => {
            if (!open) setConfirmDelete(false);
          }}
          title={t("Planboard:Resources.DeleteUnavailability")}
          description={t("Planboard:Resources.DeleteUnavailabilityQuestion").replace("{0}", resourceName)}
          confirmLabel={t("Delete")}
          busyLabel={t("Deleting")}
          destructive
          onConfirm={async () => {
            await remove.mutateAsync(unavailability.id!);
            toast.success(t("Planboard:Resources.UnavailabilityDeleted"));
            setConfirmDelete(false);
            onClose();
          }}
        />
      )}
    </>
  );
}
