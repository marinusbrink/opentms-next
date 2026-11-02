import { useEffect, useState, type ReactNode } from "react";
import { ChevronLeft, ChevronRight, ListChecks, Search } from "lucide-react";

import type { PaneFilters } from "@/apps/planboard/filters";
import { addDays, rangeDays } from "@/apps/planboard/range";
import { BAR_FIELDS, presetRange, RANGE_PRESETS, type BarField, type SchedulerPaneSettings } from "@/apps/planboard/panes/scheduler/model";
import { PaneFilterControls } from "@/apps/planboard/panes/scheduler/PaneFilterControls";
import { Button } from "@/components/ui/button";
import { DatePicker } from "@/components/ui/date-picker";
import { DropdownMenu, DropdownMenuCheckboxItem, DropdownMenuContent, DropdownMenuTrigger } from "@/components/ui/dropdown-menu";
import { Input } from "@/components/ui/input";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui/tooltip";
import { MAX_SCHEDULER_DAYS, PLANBOARD_RESOURCE_MODES, resourceModeKey, type PlanboardResourceMode, type PlanboardSchedulerKpisDto } from "@/domains/planning-execution/planboard-scheduler";
import { useFormat } from "@/lib/format";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { cn } from "@/lib/utils";

/* The Scheduler pane's header (design 70 "Scheduler"): mode switch (Tractor / Trailer / Driver), range (≤ 14 days, presets
 * day / 3 days / week / 2 weeks, previous / next period) and zoom (hour / day), search, the board-filter toggle with the
 * pane's own units, the bar-fields menu, and the KPI strip (rows, trips, in progress, empty rows, utilisation). */

const PRESET_KEYS: Record<(typeof RANGE_PRESETS)[number], string> = {
  1: "Planboard:Scheduler.PresetDay",
  3: "Planboard:Scheduler.Preset3Days",
  7: "Planboard:Scheduler.PresetWeek",
  14: "Planboard:Scheduler.Preset2Weeks",
};

const FIELD_KEYS: Record<BarField, string> = {
  number: "Planboard:Scheduler.Field.Number",
  name: "Planboard:Scheduler.Field.Name",
  status: "Planboard:Scheduler.Field.Status",
  carrier: "Planboard:Scheduler.Field.Carrier",
  drivers: "Planboard:Scheduler.Field.Drivers",
  trailer: "Planboard:Scheduler.Field.Trailer",
  cities: "Planboard:Scheduler.Field.Cities",
  loadFactor: "Planboard:Scheduler.Field.LoadFactor",
  warnings: "Planboard:Scheduler.Field.Warnings",
};

export interface SchedulerHeaderProps {
  idPrefix: string;
  settings: SchedulerPaneSettings;
  update: (patch: Partial<SchedulerPaneSettings>) => void;
  paneFilters: PaneFilters;
  kpis: PlanboardSchedulerKpisDto | undefined;
}

function IconButton({ label, onClick, children }: { label: string; onClick: () => void; children: ReactNode }) {
  return (
    <Tooltip>
      <TooltipTrigger
        render={
          <Button type="button" variant="ghost" size="icon-sm" aria-label={label} onClick={onClick}>
            {children}
          </Button>
        }
      />
      <TooltipContent>{label}</TooltipContent>
    </Tooltip>
  );
}

export function SchedulerHeader({ idPrefix, settings, update, paneFilters, kpis }: SchedulerHeaderProps) {
  const { t } = useL();
  const f = useFormat();
  const { filters, useBoardFilter, setUseBoardFilter, setRange } = paneFilters;
  const range = filters.range;
  const days = rangeDays(range);

  /* The search is applied 300 ms after the last keystroke. */
  const [search, setSearch] = useState(settings.search);
  useEffect(() => setSearch(settings.search), [settings.search]);
  useEffect(() => {
    if (search === settings.search) return;
    const timer = setTimeout(() => update({ search }), 300);
    return () => clearTimeout(timer);
  }, [search, settings.search, update]);

  /** A range change of this pane: it leaves the board filter (the pane keeps its own range from then on). */
  const applyRange = (next: { from: string; till: string }, changed: "from" | "till" = "from") => {
    if (useBoardFilter) setUseBoardFilter(false);
    setRange(next, changed);
  };

  const modeItems = PLANBOARD_RESOURCE_MODES.map((mode) => ({ value: mode, label: t(resourceModeKey(mode)) }));

  return (
    <div className="flex flex-col gap-2 border-b border-border px-2 py-2">
      <div className="flex flex-wrap items-center gap-2">
        <label htmlFor={`${idPrefix}-mode`} className="sr-only">
          {t("Planboard:Scheduler.Mode")}
        </label>
        <Select items={modeItems} value={settings.mode} onValueChange={(next) => next && update({ mode: next as PlanboardResourceMode })}>
          <SelectTrigger id={`${idPrefix}-mode`} className="h-8 w-32 bg-background">
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

        <div className="flex items-center gap-0.5" role="group" aria-label={t("Planboard:Scheduler.Range")}>
          <IconButton label={t("Planboard:Scheduler.PreviousPeriod")} onClick={() => applyRange({ from: addDays(range.from, -days), till: addDays(range.till, -days) })}>
            <ChevronLeft />
          </IconButton>
          <label htmlFor={`${idPrefix}-from`} className="sr-only">
            {t("Planboard:RangeFrom")}
          </label>
          <DatePicker id={`${idPrefix}-from`} className="w-36" required value={range.from} onChange={(from) => from && applyRange(presetRange(from, days))} />
          <IconButton label={t("Planboard:Scheduler.NextPeriod")} onClick={() => applyRange({ from: addDays(range.from, days), till: addDays(range.till, days) })}>
            <ChevronRight />
          </IconButton>
        </div>

        <div className="flex items-center gap-0.5" role="group" aria-label={t("Planboard:Scheduler.Presets")}>
          {RANGE_PRESETS.filter((n) => n <= MAX_SCHEDULER_DAYS).map((n) => (
            <Button key={n} type="button" size="sm" variant={days === n ? "secondary" : "ghost"} aria-pressed={days === n} onClick={() => applyRange(presetRange(range.from, n))}>
              {t(PRESET_KEYS[n])}
            </Button>
          ))}
        </div>

        <div className="flex items-center gap-0.5" role="group" aria-label={t("Planboard:Scheduler.Zoom")}>
          {(["hour", "day"] as const).map((zoom) => (
            <Button key={zoom} type="button" size="sm" variant={settings.zoom === zoom ? "secondary" : "ghost"} aria-pressed={settings.zoom === zoom} onClick={() => update({ zoom })}>
              {t(zoom === "hour" ? "Planboard:Scheduler.ZoomHour" : "Planboard:Scheduler.ZoomDay")}
            </Button>
          ))}
        </div>

        <div className="relative">
          <Search className="pointer-events-none absolute top-1/2 left-2 size-3.5 -translate-y-1/2 text-muted-foreground" aria-hidden="true" />
          <Input
            id={`${idPrefix}-search`}
            className="h-8 w-44 pl-7"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder={t("Planboard:Scheduler.Search")}
            aria-label={t("Planboard:Scheduler.Search")}
          />
        </div>

        <DropdownMenu>
          <DropdownMenuTrigger
            render={
              <Button type="button" variant="outline" size="sm">
                <ListChecks />
                {t("Planboard:Scheduler.BarFields")}
              </Button>
            }
          />
          <DropdownMenuContent align="end">
            {BAR_FIELDS.map((field) => (
              <DropdownMenuCheckboxItem
                key={field}
                checked={settings.barFields.includes(field)}
                onCheckedChange={(checked) =>
                  update({ barFields: BAR_FIELDS.filter((x) => (x === field ? Boolean(checked) : settings.barFields.includes(x))) })
                }
              >
                {t(FIELD_KEYS[field])}
              </DropdownMenuCheckboxItem>
            ))}
          </DropdownMenuContent>
        </DropdownMenu>

        <PaneFilterControls idPrefix={idPrefix} paneFilters={paneFilters} showRange={false} />
      </div>

      <dl className={cn("flex flex-wrap items-center gap-x-4 gap-y-1 text-label text-muted-foreground", !kpis && "invisible")} aria-label={t("Planboard:Scheduler.Kpis")}>
        <Kpi label={t("Planboard:Scheduler.KpiRows")} value={f.number(kpis?.rows ?? 0, { decimals: 0 })} />
        <Kpi label={t("Planboard:Scheduler.KpiTrips")} value={f.number(kpis?.trips ?? 0, { decimals: 0 })} />
        <Kpi label={t("Planboard:Scheduler.KpiInProgress")} value={f.number(kpis?.inProgress ?? 0, { decimals: 0 })} />
        <Kpi label={t("Planboard:Scheduler.KpiRowsWithoutTrip")} value={f.number(kpis?.rowsWithoutTrip ?? 0, { decimals: 0 })} />
        <Kpi label={t("Planboard:Scheduler.KpiUtilisation")} value={f.percent(kpis?.utilisationPercent ?? 0)} />
      </dl>
    </div>
  );
}

function Kpi({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex items-center gap-1">
      <dt>{label}</dt>
      <dd className="font-medium text-foreground tabular-nums">{value}</dd>
    </div>
  );
}
