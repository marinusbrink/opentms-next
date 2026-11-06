import { TriangleAlert } from "lucide-react";

import type { BarData, BarField } from "@/apps/planboard/panes/scheduler/model";
import { CalculatingIndicator, type CalculatingIndicatorProps } from "@/components/ui/calculating-indicator";
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui/tooltip";
import { planboardWarningKey, type PlanboardResourceMode } from "@/domains/planning-execution/planboard-scheduler";
import { TRANSPORT_TRIP_STATUSES } from "@/domains/planning-execution/trips";
import { useFormat } from "@/lib/format";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The content of one Scheduler bar (design 70 "Scheduler"): the fields the pane's bar-fields menu chose, in whitelist
 * order, and the warning icon with the localised list of the trip's warnings. Names of vehicles, drivers and carriers
 * come from the read's batched `names`. */

export interface BarContentProps {
  data: BarData;
  fields: readonly BarField[];
  names: Readonly<Record<string, string>>;
  mode: PlanboardResourceMode;
  /** The trip's calculation state: the bar says so while its values are about to change (flow 2, design 74 §9). */
  calculation?: CalculatingIndicatorProps["state"];
}

const nameOf = (names: Readonly<Record<string, string>>, id: string | null | undefined) => {
  if (!id) return "";
  return names[id] ?? names[id.toLowerCase()] ?? Object.entries(names).find(([key]) => key.toLowerCase() === id.toLowerCase())?.[1] ?? "";
};

export function BarContent({ data, fields, names, mode, calculation }: BarContentProps) {
  const { t } = useL();
  const f = useFormat();
  const { bar } = data;
  const show = (field: BarField) => fields.includes(field);
  const parts: string[] = [];
  if (show("name") && bar.name) parts.push(bar.name);
  if (show("status")) {
    const status = TRANSPORT_TRIP_STATUSES.find((s) => s.value === bar.status);
    if (status) parts.push(t(status.key));
  }
  if (show("carrier") && bar.carrierRelationId) parts.push(nameOf(names, bar.carrierRelationId));
  if (show("drivers") && mode !== "driver") {
    const drivers = (bar.driverIds ?? []).map((id) => nameOf(names, id)).filter(Boolean);
    if (drivers.length > 0) parts.push(drivers.join(", "));
  }
  if (show("trailer") && mode !== "trailer") {
    const trailers = (bar.vehicleIds ?? []).slice(1).map((id) => nameOf(names, id)).filter(Boolean);
    if (trailers.length > 0) parts.push(trailers.join(", "));
  }
  if (show("cities") && (bar.firstStopCity || bar.lastStopCity)) parts.push(`${bar.firstStopCity ?? ""} → ${bar.lastStopCity ?? ""}`);
  if (show("loadFactor") && bar.loadFactor != null) parts.push(f.percent(bar.loadFactor * 100));
  const warnings = show("warnings") ? (bar.warningCodes ?? []) : [];

  return (
    <span className="flex min-w-0 items-center gap-1">
      <CalculatingIndicator state={calculation} showLabel={false} className="shrink-0" />
      {warnings.length > 0 && (
        <Tooltip>
          <TooltipTrigger
            render={
              <span role="img" aria-label={`${t("PlanningExecution:Warnings")} (${warnings.length})`} className="inline-flex shrink-0 items-center text-tone-warning">
                <TriangleAlert className="size-3.5" aria-hidden="true" />
              </span>
            }
          />
          <TooltipContent>
            <ul className="list-disc pl-4">
              {[...new Set(warnings)].map((code) => (
                <li key={code}>{t(planboardWarningKey(code))}</li>
              ))}
            </ul>
          </TooltipContent>
        </Tooltip>
      )}
      {show("number") && <span className="shrink-0 font-medium tabular-nums">{bar.number}</span>}
      {parts.length > 0 && <span className="truncate">{parts.join(" · ")}</span>}
    </span>
  );
}
