import type { ReactNode } from "react";

import { cn } from "@/lib/utils";

/* A topic of a form (UI-FORM-005): a card on the editor body with a brand-coloured caption
 * and the fields that belong to it. The caption names the topic ("Customer", "Financial");
 * when a form has a single section the caption is the entity's own name ("Role", "Invoice"),
 * so the box reads as the record itself (PO decision 2026-10-10, the old app's look). */
export interface FormSectionProps {
  /** The caption; a section whose first row is its own caption (a switch, a tab strip) leaves it out and names itself with `aria-label`. */
  title?: string;
  "aria-label"?: string;
  children: ReactNode;
  className?: string;
}

/** The caption's classes, for a section whose caption is a control rather than a heading. */
export const FORM_SECTION_CAPTION = "text-label font-semibold text-brand";

export function FormSection({ title, "aria-label": ariaLabel, children, className }: FormSectionProps) {
  return (
    <section
      data-slot="form-section"
      aria-label={ariaLabel ?? title}
      className={cn("rounded-lg border border-border-section bg-surface-section p-4", className)}
    >
      {title && <h3 className={cn("mb-3", FORM_SECTION_CAPTION)}>{title}</h3>}
      {children}
    </section>
  );
}
