import { useMemo } from "react";

import type { PaneFilters } from "@/apps/planboard/filters";
import { Checkbox } from "@/components/ui/checkbox";
import { DatePicker } from "@/components/ui/date-picker";
import { MultiSelect } from "@/components/ui/multi-select";
import { useAllOrganizationUnits } from "@/domains/platform/administration-organization-units";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The filter part of a list pane's grid toolbar (design 70 "Workspace"): the "Use board filter" toggle; while it is off
 * the pane's own date range and organisation units (kept in the pane's settings). UI-GRID-005: the controls sit in the
 * grid's `filters` slot. */
export function PaneFilterBar({ instanceId, paneFilters }: { instanceId: string; paneFilters: PaneFilters }) {
  const { t } = useL();
  const units = useAllOrganizationUnits();
  const unitOptions = useMemo(() => (units.data ?? []).filter((u) => u.id).map((u) => ({ id: u.id!, label: u.displayName ?? "" })), [units.data]);
  const { filters, useBoardFilter, setUseBoardFilter, setRange, setOrganizationUnitIds } = paneFilters;
  const id = (part: string) => `planboard-${instanceId}-${part}`;

  return (
    <>
      <label className="flex items-center gap-2 text-sm" htmlFor={id("use-board")}>
        <Checkbox id={id("use-board")} checked={useBoardFilter} onCheckedChange={(next) => setUseBoardFilter(next === true)} />
        {t("Planboard:Lists.UseBoardFilter")}
      </label>
      {!useBoardFilter && (
        <>
          <DatePicker
            id={id("from")}
            className="w-36"
            required
            placeholder={t("Planboard:RangeFrom")}
            value={filters.range.from}
            onChange={(from) => from && setRange({ ...filters.range, from }, "from")}
          />
          <DatePicker
            id={id("till")}
            className="w-36"
            required
            placeholder={t("Planboard:RangeTill")}
            value={filters.range.till}
            onChange={(till) => till && setRange({ ...filters.range, till }, "till")}
          />
          {/* DEVIATION(UI-LOOK-004): a pane's organisation-unit filter, not a form picker (the rule excludes filters), approved in docs/designs/70-planboard-app.md ("Workspace": the pane's own units). */}
          <MultiSelect
            id={id("units-filter")}
            className="w-48"
            options={unitOptions}
            value={[...filters.organizationUnitIds]}
            onChange={setOrganizationUnitIds}
            disabled={units.isError}
            aria-describedby={units.isError ? id("units-filter-error") : undefined}
          />
          {units.isError && (
            <span id={id("units-filter-error")} role="alert" className="text-label text-destructive">
              {t("Planboard:OrganizationUnitsLoadFailed")}
            </span>
          )}
        </>
      )}
    </>
  );
}
