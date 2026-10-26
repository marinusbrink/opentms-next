import { useId, useState, type ReactNode } from "react";
import { ChevronRight, CircleAlert } from "lucide-react";

import { cn } from "@/lib/utils";

/* A topic of a form (UI-FORM-005): a card on the editor body with a brand-coloured caption
 * and the fields that belong to it. The caption names the topic ("Customer", "Financial");
 * when a form has a single section the caption is the entity's own name ("Role", "Invoice"),
 * so the box reads as the record itself (PO decision 2026-10-10, the old app's look).
 *
 * A long one-page editor (design 61 "Order page, revised": the order-level sections instead of tabs) makes its
 * sections `collapsible`: the caption is then a disclosure button (aria-expanded) and a closed section renders only
 * its caption. A section that holds an invalid field shows the caption in the destructive colour with the alert icon
 * (the section twin of UI-FORM-007); the editor opens it on submit (UI-FORM-006). */
export interface FormSectionProps {
  /** The caption; a section whose first row is its own caption (a switch, a tab strip) leaves it out and names itself with `aria-label`. */
  title?: string;
  "aria-label"?: string;
  children: ReactNode;
  className?: string;
  /** The caption opens and closes the section. */
  collapsible?: boolean;
  /** Uncontrolled initial state of a collapsible section (default closed). */
  defaultOpen?: boolean;
  /** Controlled state of a collapsible section. */
  open?: boolean;
  onOpenChange?: (open: boolean) => void;
  /** The section holds an invalid field: the caption is marked. */
  hasError?: boolean;
  /** Controls or a short summary at the caption's end, outside the disclosure button. */
  captionEnd?: ReactNode;
}

/** The caption's classes, for a section whose caption is a control rather than a heading. */
export const FORM_SECTION_CAPTION = "text-label font-semibold text-brand";

export function FormSection({
  title,
  "aria-label": ariaLabel,
  children,
  className,
  collapsible = false,
  defaultOpen = false,
  open: controlledOpen,
  onOpenChange,
  hasError = false,
  captionEnd,
}: FormSectionProps) {
  const contentId = useId();
  const [uncontrolledOpen, setUncontrolledOpen] = useState(defaultOpen);
  const open = !collapsible || (controlledOpen ?? uncontrolledOpen);
  const toggle = () => {
    const next = !open;
    if (controlledOpen === undefined) setUncontrolledOpen(next);
    onOpenChange?.(next);
  };

  return (
    <section
      data-slot="form-section"
      data-state={collapsible ? (open ? "open" : "closed") : undefined}
      aria-label={ariaLabel ?? title}
      className={cn(
        "rounded-lg border border-border-section bg-surface-section p-4",
        className,
      )}
    >
      {title && !collapsible && <h3 className={cn("mb-3", FORM_SECTION_CAPTION)}>{title}</h3>}
      {title && collapsible && (
        <div className={cn("flex items-center gap-2", open && "mb-3")}>
          <h3 className={cn(FORM_SECTION_CAPTION, hasError && "text-destructive")}>
            <button
              type="button"
              aria-expanded={open}
              aria-controls={contentId}
              onClick={toggle}
              className="inline-flex items-center gap-1 rounded-sm outline-none focus-visible:ring-2 focus-visible:ring-ring"
            >
              <ChevronRight aria-hidden="true" className={cn("size-4 transition-transform", open && "rotate-90")} />
              {hasError && <CircleAlert aria-hidden="true" className="size-4" />}
              {title}
            </button>
          </h3>
          {captionEnd && <div className="ml-auto flex items-center gap-2 text-label text-muted-foreground">{captionEnd}</div>}
        </div>
      )}
      {open && (collapsible ? <div id={contentId}>{children}</div> : children)}
    </section>
  );
}
