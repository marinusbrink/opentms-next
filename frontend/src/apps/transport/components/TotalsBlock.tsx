import type { ReactNode } from "react";

import { CalculatingIndicator, calculatingValueClass } from "@/components/ui/calculating-indicator";
import { useFormat } from "@/lib/format";
import { cn } from "@/lib/utils";

/* Document totals under a lines grid (UI-GRID-015): a right-aligned label / value list, labels in text-label
 * text-muted-foreground, values in f.amount with tabular figures, the grand total in font-semibold with a top border.
 * The currency code shows once (UI-FMT-007). While the values are being recalculated (flow 2) the block shows the
 * CalculatingIndicator and the values muted (design 49). */

export interface TotalsLine {
  label: string;
  value: number | null | undefined;
  /** The grand total: semibold with a top border. */
  grand?: boolean;
}

export interface TotalsBlockProps {
  lines: TotalsLine[];
  currencyCode?: string | null;
  state?: { status?: number | null; lastErrorCode?: string | null } | null;
  /** A line under the block (the preview notice). */
  footer?: ReactNode;
  className?: string;
}

export function TotalsBlock({ lines, currencyCode, state, footer, className }: TotalsBlockProps) {
  const f = useFormat();
  const muted = calculatingValueClass(state);
  return (
    <div className={cn("flex flex-col items-end gap-1", className)} data-slot="totals-block">
      <div className="flex items-center gap-2 text-label text-muted-foreground">
        <CalculatingIndicator state={state} />
        {currencyCode && <span>{currencyCode}</span>}
      </div>
      <dl className="grid grid-cols-[auto_minmax(7rem,auto)] gap-x-6 gap-y-1 text-sm">
        {lines.map((line) => (
          <div key={line.label} className={cn("contents", line.grand && "font-semibold")}>
            <dt className={cn("text-label text-muted-foreground", line.grand && "border-t pt-1 font-semibold text-foreground")}>{line.label}</dt>
            <dd className={cn("text-right tabular-nums", muted, line.grand && "border-t pt-1")}>{f.amount(line.value ?? 0)}</dd>
          </div>
        ))}
      </dl>
      {footer}
    </div>
  );
}
