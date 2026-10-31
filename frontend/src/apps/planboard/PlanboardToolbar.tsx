import { useMemo } from "react";
import { Plus, RotateCcw } from "lucide-react";

import { usePlanboardFilters } from "@/apps/planboard/filters";
import { PLANBOARD_PANE_TYPES, PLANBOARD_PANES, type PlanboardPaneType } from "@/apps/planboard/panes/registry";
import { AppCommandBar, type AppCommandBarCommand } from "@/components/ui/app-command-bar";
import { DatePicker } from "@/components/ui/date-picker";
import { MultiSelect } from "@/components/ui/multi-select";
import { useAllOrganizationUnits } from "@/domains/platform/administration-organization-units";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The workspace toolbar (design 70, "Workspace"): Add pane (the panes this tenant has; the Scheduler many times, the
 * others once: choosing one already open focuses it), Reset layout, and the board-wide date range (at most 92 days) and
 * organisation units every pane follows unless its own "Use board filter" toggle is off. */

export interface PlanboardToolbarProps {
  /** The pane types this tenant has (their features are on). */
  enabledTypes: readonly PlanboardPaneType[];
  onAddPane: (type: PlanboardPaneType) => void;
  onResetLayout: () => void;
  /** False until the workspace is ready (layout loaded). */
  ready: boolean;
}

export function PlanboardToolbar({ enabledTypes, onAddPane, onResetLayout, ready }: PlanboardToolbarProps) {
  const { t } = useL();
  const { board, setBoardRange, setBoardOrganizationUnitIds } = usePlanboardFilters();
  const units = useAllOrganizationUnits();
  const unitOptions = useMemo(() => (units.data ?? []).filter((u) => u.id).map((u) => ({ id: u.id!, label: u.displayName ?? "" })), [units.data]);

  const commands: AppCommandBarCommand[] = [
    {
      id: "add-pane",
      labelKey: "Planboard:AddPane",
      icon: Plus,
      isPrimary: true,
      disabled: !ready || enabledTypes.length === 0,
      items: PLANBOARD_PANE_TYPES.filter((type) => enabledTypes.includes(type)).map((type) => ({
        id: type,
        labelKey: PLANBOARD_PANES[type].titleKey,
        icon: PLANBOARD_PANES[type].icon,
        onClick: () => onAddPane(type),
      })),
      onClick: () => {},
    },
    { id: "reset-layout", labelKey: "Planboard:ResetLayout", icon: RotateCcw, disabled: !ready, onClick: onResetLayout },
  ];

  const trailing = (
    <>
      <label htmlFor="planboard-range-from" className="text-label text-muted-foreground">
        {t("Planboard:RangeFrom")}
      </label>
      <DatePicker
        id="planboard-range-from"
        className="w-36"
        required
        value={board.range.from}
        onChange={(from) => from && setBoardRange({ ...board.range, from }, "from")}
      />
      <label htmlFor="planboard-range-till" className="text-label text-muted-foreground">
        {t("Planboard:RangeTill")}
      </label>
      <DatePicker
        id="planboard-range-till"
        className="w-36"
        required
        value={board.range.till}
        onChange={(till) => till && setBoardRange({ ...board.range, till }, "till")}
      />
      <label htmlFor="planboard-units-filter" className="text-label text-muted-foreground">
        {t("Planboard:OrganizationUnits")}
      </label>
      {/* DEVIATION(UI-LOOK-004): the board's organisation-unit filter, not a form picker (the rule excludes filters), approved in docs/designs/70-planboard-app.md ("Workspace": the board-wide organisation units filter). */}
      <MultiSelect
        id="planboard-units-filter"
        className="w-56"
        options={unitOptions}
        value={[...board.organizationUnitIds]}
        onChange={setBoardOrganizationUnitIds}
        disabled={units.isError}
        aria-describedby={units.isError ? "planboard-units-filter-error" : undefined}
      />
      {units.isError && (
        <span id="planboard-units-filter-error" role="alert" className="text-label text-destructive">
          {t("Planboard:OrganizationUnitsLoadFailed")}
        </span>
      )}
    </>
  );

  return <AppCommandBar commands={commands} trailing={trailing} />;
}
