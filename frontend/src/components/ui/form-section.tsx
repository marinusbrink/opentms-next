import type { ReactNode } from "react";

import { cn } from "@/lib/utils";

/* A topic of a form (UI-FORM-005): a card on the editor body with a brand-coloured caption
 * and the fields that belong to it. The caption names the topic ("Customer", "Financial");
 * when a form has a single section the caption is the entity's own name ("Role", "Invoice"),
 * so the box reads as the record itself (PO decision 2026-10-10, the old app's look). */
export interface FormSectionProps {
  title: string;
  children: ReactNode;
  className?: string;
}

export function FormSection({ title, children, className }: FormSectionProps) {
  return (
    <section
      data-slot="form-section"
      aria-label={title}
      className={cn("rounded-lg border border-border-section bg-surface-section p-4", className)}
    >
      <h3 className="mb-3 text-label font-semibold text-brand">{title}</h3>
      {children}
    </section>
  );
}
