import { stripPosition, stripSegment } from "@/apps/planboard/panes/resources/model";
import type { PlanboardBlockDto } from "@/domains/planning-execution/planboard-scheduler";
import { unavailabilityReasonKey } from "@/domains/planning-execution/resource-availability";
import { useFormat } from "@/lib/format";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { cn } from "@/lib/utils";

/* The compact availability strip of a resource row (design 70 "Resources"): the board range as a bar, unavailability in
 * the danger tone, outside working time neutral, and the resource's trips as dots at their departure. Positions are
 * percentages of the window, computed from the data (a bespoke surface, UI-BSP-001). */

export interface StripTrip {
  id: string;
  number: string;
  departure: string;
}

export interface AvailabilityStripProps {
  windowStart: number;
  windowEnd: number;
  blocks: readonly PlanboardBlockDto[];
  trips: readonly StripTrip[];
  className?: string;
}

export function AvailabilityStrip({ windowStart, windowEnd, blocks, trips, className }: AvailabilityStripProps) {
  const { t } = useL();
  const f = useFormat();
  const unavailable = blocks.filter((b) => b.unavailabilityId).length;
  const label = t("Planboard:Resources.StripLabel").replace("{0}", f.number(unavailable)).replace("{1}", f.number(trips.length));
  return (
    <div role="img" aria-label={label} className={cn("relative h-3 w-full overflow-hidden rounded-sm bg-surface-section", className)}>
      {blocks.map((block, index) => {
        const segment = block.from && block.till ? stripSegment(windowStart, windowEnd, block.from, block.till) : null;
        if (!segment) return null;
        const title = block.unavailabilityId ? t(unavailabilityReasonKey(block.reason)) : t("Planboard:Scheduler.OutsideWorkingTime");
        return (
          <span
            key={`${block.unavailabilityId ?? "owt"}-${block.from}-${index}`}
            title={`${title}: ${f.dateTime(block.from)} – ${f.dateTime(block.till)}`}
            className={cn("absolute inset-y-0", block.unavailabilityId ? "bg-tone-danger-bg" : "bg-tone-neutral-bg")}
            style={{ left: `${segment.left}%`, width: `${segment.width}%` }}
          />
        );
      })}
      {trips.map((trip) => {
        const left = stripPosition(windowStart, windowEnd, trip.departure);
        if (left == null) return null;
        return (
          <span
            key={trip.id}
            title={`${trip.number} ${f.dateTime(trip.departure)}`}
            className="absolute top-1/2 size-2 -translate-x-1/2 -translate-y-1/2 rounded-full bg-brand"
            style={{ left: `${left}%` }}
          />
        );
      })}
    </div>
  );
}
