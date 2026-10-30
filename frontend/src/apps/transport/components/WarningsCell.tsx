import { TriangleAlert } from "lucide-react";

import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui/tooltip";
import { tripWarningKey, type TripWarningDto } from "@/domains/planning-execution/trips";
import { useL } from "@/lib/i18n/LocalizationProvider";

/* The warnings of a trip or a stop (design 59, `Warnings` jsonb of typed codes; design 61: "Warnings (icon with
 * tooltip)"): one warning icon with the localized codes in its tooltip (`PlanningExecution:TripWarningCode.<Code>`),
 * nothing for an empty list. Tone tokens only (UI-TOK-005). */

export function WarningsCell({ value }: { value: TripWarningDto[] | null | undefined }) {
  const { t } = useL();
  if (!value || value.length === 0) return null;
  const label = t("PlanningExecution:Warnings");
  return (
    <Tooltip>
      <TooltipTrigger
        render={
          <span role="img" aria-label={`${label} (${value.length})`} className="inline-flex items-center gap-1 text-tone-warning">
            <TriangleAlert className="size-4" aria-hidden="true" />
            <span className="text-label tabular-nums">{value.length}</span>
          </span>
        }
      />
      <TooltipContent>
        <ul className="list-disc pl-4">
          {value.map((warning, index) => (
            <li key={`${warning.code}-${warning.legId ?? ""}-${warning.stopId ?? ""}-${index}`}>{t(tripWarningKey(warning.code))}</li>
          ))}
        </ul>
      </TooltipContent>
    </Tooltip>
  );
}

/** The warnings that concern one stop (by stop id) or one leg (by leg id). */
export function warningsOf(all: TripWarningDto[] | null | undefined, match: { stopId?: string; legId?: string }): TripWarningDto[] {
  return (all ?? []).filter((w) => (match.stopId != null && w.stopId === match.stopId) || (match.legId != null && w.legId === match.legId));
}
