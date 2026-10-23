import { TriangleAlert } from "lucide-react";

import { Spinner } from "@/components/ui/spinner";
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui/tooltip";
import { DerivedValueStatus } from "@/lib/realtime/record-updates";
import { useL } from "@/lib/i18n/LocalizationProvider";
import { cn } from "@/lib/utils";

/* The visible "calculating" state of flow 2 (design 49, UI design): while a record's derived values are being
 * recalculated the values stay visible but muted and this indicator says so; a failed attempt shows a warning with its
 * error code. Nothing renders for an up-to-date (or never calculated) record. Tokens only (UI-TOK-001). */

export interface CalculatingIndicatorProps {
  state: { status?: number | null; lastErrorCode?: string | null } | null | undefined;
  /** Shows the label next to the icon from md up; the label is always the accessible name. */
  showLabel?: boolean;
  className?: string;
}

export function isCalculating(state: CalculatingIndicatorProps["state"]): boolean {
  return state?.status === DerivedValueStatus.Pending;
}

/** The class a value under a pending state takes, so a planner sees it is about to change. */
export function calculatingValueClass(state: CalculatingIndicatorProps["state"]): string | undefined {
  return isCalculating(state) ? "text-muted-foreground" : undefined;
}

export function CalculatingIndicator({ state, showLabel = true, className }: CalculatingIndicatorProps) {
  const { t } = useL();

  if (state?.status === DerivedValueStatus.Pending) {
    const label = t("Platform:DerivedValues.Calculating");
    return (
      <span role="status" aria-label={label} className={cn("inline-flex items-center gap-1 text-xs text-muted-foreground", className)}>
        <Spinner aria-hidden className="size-3.5" />
        {showLabel && <span className="hidden md:inline">{label}</span>}
      </span>
    );
  }

  if (state?.status === DerivedValueStatus.Failed) {
    const label = t("Platform:DerivedValues.Failed");
    return (
      <Tooltip>
        <TooltipTrigger
          render={
            <span role="status" aria-label={label} className={cn("inline-flex items-center gap-1 text-xs text-destructive", className)}>
              <TriangleAlert aria-hidden className="size-3.5" />
              {showLabel && <span className="hidden md:inline">{label}</span>}
            </span>
          }
        />
        <TooltipContent>{t("Platform:DerivedValues.FailedDetail").replace("{0}", state.lastErrorCode ?? "")}</TooltipContent>
      </Tooltip>
    );
  }

  return null;
}
