import { useState, type DragEvent } from "react";
import { ChevronDown, ChevronUp } from "lucide-react";

import { planboardDragKind, readDragPayload } from "@/apps/planboard/dnd";
import { orderedStops, tollSharePercent } from "@/apps/planboard/panes/map/model";
import { Button } from "@/components/ui/button";
import { StatusBadge } from "@/components/ui/status-badge";
import type { PlanboardTripMapDto } from "@/domains/planning-execution/planboard-map";
import { tripStatusName } from "@/domains/planning-execution/trips";
import { useFormat } from "@/lib/format";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { cn } from "@/lib/utils";

/* The focused trip's summary card on the map (design 70 "Map", like legacy): distance, duration, toll amount and toll-km
 * share, CO₂, and the per-segment toll list on expand. It is also the drop target for unplanned legs (a leg marker or a
 * row of Legs to schedule dragged onto it assigns the legs to the trip), and for an order (design 74 §8). */

export interface TripSummaryCardProps {
  map: PlanboardTripMapDto;
  /** Legs dropped on the card; absent when the user may not assign or the trip takes no legs. */
  onDropLegs?: (legIds: string[]) => void;
  /** An order dropped on the card; absent when the user may not assign or the trip takes no legs. */
  onDropOrder?: (orderId: string) => void;
  /** True while an assignment runs. */
  busy?: boolean;
}

export function TripSummaryCard({ map, onDropLegs, onDropOrder, busy }: TripSummaryCardProps) {
  const { t } = useL();
  const f = useFormat();
  const [expanded, setExpanded] = useState(false);
  const [dragOver, setDragOver] = useState(false);
  const stops = orderedStops(map);
  const stopName = (id: string | null | undefined) => {
    const stop = stops.find((s) => s.id && id && s.id.toLowerCase() === id.toLowerCase());
    return stop ? `${stop.position}. ${stop.name ?? stop.city ?? ""}` : "";
  };
  const tollSegments = (map.segments ?? []).filter((s) => (s.tollDistanceM ?? 0) > 0 || (s.tollAmount ?? 0) > 0);
  const tollShare = tollSharePercent(map);
  const currency = map.tollCurrencyCode ?? "";

  const accepts = (e: DragEvent) => {
    const kind = planboardDragKind(e.dataTransfer.types);
    return (kind === "legs" && !!onDropLegs) || (kind === "order" && !!onDropOrder);
  };

  return (
    <section
      aria-label={t("Planboard:Map.TripSummary")}
      className={cn(
        "w-72 rounded-lg border border-border bg-background p-3 text-sm shadow-sm",
        dragOver && "ring-2 ring-brand",
      )}
      onDragOver={(e) => {
        if (!accepts(e)) return;
        e.preventDefault();
        e.dataTransfer.dropEffect = "move";
        setDragOver(true);
      }}
      onDragLeave={() => setDragOver(false)}
      onDrop={(e) => {
        setDragOver(false);
        if (!accepts(e)) return;
        e.preventDefault();
        const payload = readDragPayload(e.dataTransfer, ["legs", "order"]);
        if (payload?.kind === "legs") onDropLegs?.(payload.legIds);
        else if (payload?.kind === "order") onDropOrder?.(payload.orderId);
      }}
      aria-busy={busy || undefined}
    >
      <header className="mb-2 flex items-center justify-between gap-2">
        <h3 className="truncate font-semibold text-brand tabular-nums">{map.number}</h3>
        <StatusBadge entity="TransportTrip" state={tripStatusName(map.status)} />
      </header>
      <dl className="grid grid-cols-2 gap-x-3 gap-y-1">
        <dt className="text-label text-muted-foreground">{t("Planboard:Map.DistanceKm")}</dt>
        <dd className="text-right tabular-nums">{map.plannedDistanceM != null ? f.number(map.plannedDistanceM / 1000, { maxDecimals: 1 }) : "–"}</dd>
        <dt className="text-label text-muted-foreground">{t("Planboard:Map.Duration")}</dt>
        <dd className="text-right tabular-nums">{map.plannedDurationS != null ? f.duration(map.plannedDurationS / 60) : "–"}</dd>
        <dt className="text-label text-muted-foreground">{`${t("Planboard:Map.Toll")}${currency ? ` (${currency})` : ""}`}</dt>
        <dd className="text-right tabular-nums">{map.tollAmount != null ? f.amount(map.tollAmount) : "–"}</dd>
        <dt className="text-label text-muted-foreground">{t("Planboard:Map.TollSharePercent")}</dt>
        <dd className="text-right tabular-nums">{tollShare != null ? f.number(tollShare, { maxDecimals: 1 }) : "–"}</dd>
        <dt className="text-label text-muted-foreground">{t("Planboard:Map.Co2Kg")}</dt>
        <dd className="text-right tabular-nums">{map.co2Kg != null ? f.number(map.co2Kg, { maxDecimals: 1 }) : "–"}</dd>
      </dl>
      {tollSegments.length > 0 && (
        <div className="mt-2">
          <Button type="button" variant="ghost" size="sm" aria-expanded={expanded} onClick={() => setExpanded((x) => !x)}>
            {expanded ? <ChevronUp /> : <ChevronDown />}
            {t("Planboard:Map.TollSegments")}
          </Button>
          {expanded && (
            <table className="mt-1 w-full text-label">
              <thead className="text-muted-foreground">
                <tr>
                  <th className="text-left font-medium">{t("Planboard:Map.Segment")}</th>
                  <th className="text-right font-medium">{t("Planboard:Map.TollDistanceKm")}</th>
                  <th className="text-right font-medium">{`${t("Planboard:Map.Toll")}${currency ? ` (${currency})` : ""}`}</th>
                </tr>
              </thead>
              <tbody>
                {tollSegments.map((segment, index) => (
                  <tr key={`${segment.fromStopId ?? ""}-${segment.toStopId ?? ""}-${index}`}>
                    <td className="truncate pr-2">{`${stopName(segment.fromStopId)} → ${stopName(segment.toStopId)}`}</td>
                    <td className="text-right tabular-nums">{f.number((segment.tollDistanceM ?? 0) / 1000, { maxDecimals: 1 })}</td>
                    <td className="text-right tabular-nums">{segment.tollAmount != null ? f.amount(segment.tollAmount) : "–"}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>
      )}
      {map.routedAt == null && stops.length > 1 && <p className="mt-2 text-label text-muted-foreground">{t("Planboard:Map.NoRouteYet")}</p>}
      {onDropLegs && <p className="mt-2 text-label text-muted-foreground">{t("Planboard:Map.DropLegsHint")}</p>}
    </section>
  );
}
