import { STOP_STATES, type StopState } from "@/apps/planboard/panes/map/model";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { cn } from "@/lib/utils";

/* The map's legend (design 70 "Map"): the route kinds and the stop states, plus the all-trips and unplanned-nearby layers
 * when they are on. Colours are the same tokens the layers and markers use. */

const STATE_SWATCH: Record<StopState, string> = {
  done: "border-tone-success bg-tone-success-bg",
  "on-time": "border-tone-info bg-tone-info-bg",
  "at-risk": "border-tone-warning bg-tone-warning-bg",
  late: "border-tone-danger bg-tone-danger-bg",
};

export function MapLegend({ showAllTrips, showUnplannedNear }: { showAllTrips: boolean; showUnplannedNear: boolean }) {
  const { t } = useL();
  return (
    <section aria-label={t("Planboard:Map.Legend")} className="pointer-events-auto w-fit rounded-md border border-border bg-background p-2 text-label text-muted-foreground shadow-sm">
      <ul className="flex flex-col gap-1">
        <li className="flex items-center gap-2">
          <span className="h-1 w-5 rounded-full bg-primary" aria-hidden="true" />
          {t("Planboard:Map.LegendRoute")}
        </li>
        <li className="flex items-center gap-2">
          <span className="h-1 w-5 rounded-full bg-tone-warning" aria-hidden="true" />
          {t("Planboard:Map.LegendToll")}
        </li>
        <li className="flex items-center gap-2">
          <span className="w-5 border-t-2 border-dashed border-primary" aria-hidden="true" />
          {t("Planboard:Map.LegendStraight")}
        </li>
        {showAllTrips && (
          <li className="flex items-center gap-2">
            <span className="h-0.5 w-5 rounded-full bg-tone-neutral" aria-hidden="true" />
            {t("Planboard:Map.LegendAllTrips")}
          </li>
        )}
        {showUnplannedNear && (
          <li className="flex items-center gap-2">
            <span className="ml-1 size-2.5 rotate-45 border-2 border-tone-warning bg-tone-warning-bg" aria-hidden="true" />
            {t("Planboard:Map.LegendUnplanned")}
          </li>
        )}
        {STOP_STATES.map((state) => (
          <li key={state} className="flex items-center gap-2">
            <span className={cn("ml-0.5 size-3.5 rounded-full border-2", STATE_SWATCH[state])} aria-hidden="true" />
            {t(`Planboard:Map.StopState.${state}`)}
          </li>
        ))}
      </ul>
    </section>
  );
}
