import { useMemo } from "react";

import type { PaneFilters } from "@/apps/planboard/filters";
import { Checkbox } from "@/components/ui/checkbox";
import { DatePicker } from "@/components/ui/date-picker";
import { MultiSelect } from "@/components/ui/multi-select";
import { useAllOrganizationUnits } from "@/domains/platform/administration-organization-units";
import { useFormat } from "@/lib/format";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The "Use board filter" toggle of a pane (design 70, "Workspace") with the pane's own range and organisation units
 * while it is off. Used by the Scheduler (which shows its range itself: `showRange={false}`), the Map and Resources. */

export interface PaneFilterControlsProps {
  /** Prefix of the control ids, unique per pane instance. */
  idPrefix: string;
  paneFilters: PaneFilters;
  /** Shows the range pickers (default true); the Scheduler has its own range controls. */
  showRange?: boolean;
  /** Shows the organisation-unit filter (default true). */
  showUnits?: boolean;
}

export function PaneFilterControls({ idPrefix, paneFilters, showRange = true, showUnits = true }: PaneFilterControlsProps) {
  const { t } = useL();
  const f = useFormat();
  const units = useAllOrganizationUnits();
  const unitOptions = useMemo(() => (units.data ?? []).filter((u) => u.id).map((u) => ({ id: u.id!, label: u.displayName ?? "" })), [units.data]);
  const { filters, useBoardFilter, setUseBoardFilter, setRange, setOrganizationUnitIds } = paneFilters;

  return (
    <div className="flex flex-wrap items-center gap-2">
      <label htmlFor={`${idPrefix}-board-filter`} className="flex items-center gap-1.5 text-label text-muted-foreground">
        <Checkbox id={`${idPrefix}-board-filter`} checked={useBoardFilter} onCheckedChange={(checked) => setUseBoardFilter(checked === true)} />
        {t("Planboard:Scheduler.UseBoardFilter")}
      </label>
      {showRange &&
        (useBoardFilter ? (
          <span className="text-label text-muted-foreground tabular-nums">
            {f.date(filters.range.from)} – {f.date(filters.range.till)}
          </span>
        ) : (
          <>
            <label htmlFor={`${idPrefix}-from`} className="sr-only">
              {t("Planboard:RangeFrom")}
            </label>
            <DatePicker
              id={`${idPrefix}-from`}
              className="w-36"
              required
              value={filters.range.from}
              onChange={(from) => from && setRange({ ...filters.range, from }, "from")}
            />
            <label htmlFor={`${idPrefix}-till`} className="sr-only">
              {t("Planboard:RangeTill")}
            </label>
            <DatePicker
              id={`${idPrefix}-till`}
              className="w-36"
              required
              value={filters.range.till}
              onChange={(till) => till && setRange({ ...filters.range, till }, "till")}
            />
          </>
        ))}
      {showUnits && !useBoardFilter && (
        <>
          <label htmlFor={`${idPrefix}-units`} className="sr-only">
            {t("Planboard:OrganizationUnits")}
          </label>
          {/* DEVIATION(UI-LOOK-004): a pane's organisation-unit filter, not a form picker (the rule excludes filters), approved in docs/designs/70-planboard-app.md ("Workspace": the pane's own units). */}
          <MultiSelect
            id={`${idPrefix}-units`}
            className="w-48"
            options={unitOptions}
            value={[...filters.organizationUnitIds]}
            onChange={setOrganizationUnitIds}
            disabled={units.isError}
            aria-describedby={units.isError ? `${idPrefix}-units-error` : undefined}
          />
          {units.isError && (
            <span id={`${idPrefix}-units-error`} role="alert" className="text-label text-destructive">
              {t("Planboard:OrganizationUnitsLoadFailed")}
            </span>
          )}
        </>
      )}
    </div>
  );
}
