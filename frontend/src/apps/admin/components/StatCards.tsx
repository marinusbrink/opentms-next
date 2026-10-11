import type { ReactNode } from "react";

import { cn } from "@/lib/utils";

/* A row of small figures above a grid or report (the Maintenance tabs): a label and a value per
 * card, tabular figures so the numbers line up (UI-TOK-009). A card with `onClick` is a button. */
export interface StatCard {
  id: string;
  label: string;
  value: ReactNode;
  /** Highlights the card, e.g. the state the grid currently shows. */
  active?: boolean;
  /** Warning tone for figures that need attention (failed jobs, unreachable databases). */
  tone?: "default" | "warning" | "destructive";
  onClick?: () => void;
}

export function StatCards({ cards, className }: { cards: StatCard[]; className?: string }) {
  return (
    <div className={cn("flex flex-wrap gap-3", className)} data-slot="stat-cards">
      {cards.map((card) => {
        const content = (
          <>
            <span className="text-label font-medium text-muted-foreground">{card.label}</span>
            <span
              className={cn(
                "text-base font-semibold tabular-nums",
                card.tone === "warning" && "text-tone-warning",
                card.tone === "destructive" && "text-destructive",
              )}
            >
              {card.value}
            </span>
          </>
        );
        const baseClassName = cn(
          "flex min-w-32 flex-col gap-1 rounded-lg border border-border-section bg-surface-section px-4 py-3 text-left",
          card.active && "border-brand",
        );
        return card.onClick ? (
          <button
            key={card.id}
            type="button"
            onClick={card.onClick}
            aria-pressed={card.active}
            className={cn(baseClassName, "hover:bg-accent transition-colors")}
          >
            {content}
          </button>
        ) : (
          <div key={card.id} className={baseClassName}>
            {content}
          </div>
        );
      })}
    </div>
  );
}
