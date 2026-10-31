import { useEffect, useMemo, useState } from "react";
import { toast } from "sonner";

import { formToPattern, patternErrors, patternToForm, type PatternDay } from "@/apps/planboard/panes/resources/model";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { useSaveWorkingPatterns, type ResourceKind, type ResourceWorkingPatternDto } from "@/domains/planning-execution/resource-availability";
import { getApiErrorMessage } from "@/lib/api/errors";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The weekly working pattern of a driver or vehicle (design 68 §2): per weekday (Monday first) off, or working from a
 * start to an end time; no row for a weekday means no working-time constraint that day. Saving replaces the pattern and
 * the server recalculates the resource's trips (warning OutsideWorkingTime). Read-only without the Manage permission. */

export interface WorkingPatternEditorProps {
  kind: ResourceKind;
  resourceId: string;
  patterns: readonly ResourceWorkingPatternDto[];
  canManage: boolean;
}

export function WorkingPatternEditor({ kind, resourceId, patterns, canManage }: WorkingPatternEditorProps) {
  const { t } = useL();
  const initial = useMemo(() => patternToForm(patterns), [patterns]);
  const [days, setDays] = useState<PatternDay[]>(initial);
  const [submitted, setSubmitted] = useState(false);
  const save = useSaveWorkingPatterns();
  useEffect(() => {
    setDays(initial);
    setSubmitted(false);
  }, [initial]);
  const errors = submitted ? patternErrors(days) : new Set<number>();
  const dirty = JSON.stringify(days) !== JSON.stringify(initial);
  const setDay = (weekday: number, patch: Partial<PatternDay>) => setDays((all) => all.map((d) => (d.weekday === weekday ? { ...d, ...patch } : d)));

  const submit = async () => {
    setSubmitted(true);
    if (patternErrors(days).size > 0) return;
    try {
      await save.mutateAsync({ kind, resourceId, days: formToPattern(days) });
      toast.success(t("Planboard:Resources.PatternSaved"));
    } catch (error) {
      toast.error(getApiErrorMessage(error, t));
    }
  };

  return (
    <form
      className="flex flex-col gap-2"
      onSubmit={(e) => {
        e.preventDefault();
        void submit();
      }}
      noValidate
    >
      <table className="w-full text-sm">
        <thead className="text-label text-muted-foreground">
          <tr>
            <th className="text-left font-medium">{t("Planboard:Resources.Weekday")}</th>
            <th className="text-left font-medium">{t("Planboard:Resources.Start")}</th>
            <th className="text-left font-medium">{t("Planboard:Resources.End")}</th>
          </tr>
        </thead>
        <tbody>
          {days.map((day) => {
            const id = `pattern-${resourceId}-${day.weekday}`;
            const invalid = errors.has(day.weekday);
            return (
              <tr key={day.weekday}>
                <td className="py-0.5 pr-2">
                  <label htmlFor={`${id}-works`} className="flex items-center gap-1.5">
                    <Checkbox
                      id={`${id}-works`}
                      checked={day.works}
                      disabled={!canManage}
                      onCheckedChange={(checked) => setDay(day.weekday, checked === true ? { works: true, start: day.start || "08:00", end: day.end || "17:00" } : { works: false })}
                    />
                    {t(`Planboard:Resources.WeekdayShort.${day.weekday}`)}
                  </label>
                </td>
                <td className="py-0.5 pr-2">
                  <Input
                    id={`${id}-start`}
                    className="h-7 w-20 tabular-nums"
                    value={day.start}
                    disabled={!canManage || !day.works}
                    aria-invalid={invalid || undefined}
                    aria-label={`${t(`Planboard:Resources.WeekdayShort.${day.weekday}`)} ${t("Planboard:Resources.Start")}`}
                    placeholder="HH:mm"
                    onChange={(e) => setDay(day.weekday, { start: e.target.value })}
                  />
                </td>
                <td className="py-0.5">
                  <Input
                    id={`${id}-end`}
                    className="h-7 w-20 tabular-nums"
                    value={day.end}
                    disabled={!canManage || !day.works}
                    aria-invalid={invalid || undefined}
                    aria-label={`${t(`Planboard:Resources.WeekdayShort.${day.weekday}`)} ${t("Planboard:Resources.End")}`}
                    placeholder="HH:mm"
                    onChange={(e) => setDay(day.weekday, { end: e.target.value })}
                  />
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
      {errors.size > 0 && (
        <p role="alert" className="text-label text-destructive">
          {t("Planboard:Resources.PatternInvalid")}
        </p>
      )}
      {canManage && (
        <Button type="submit" size="sm" className="w-fit" disabled={!dirty || save.isPending}>
          {save.isPending ? t("Saving") : t("Planboard:Resources.SavePattern")}
        </Button>
      )}
    </form>
  );
}
