import type { ReactNode } from "react";
import { Link } from "@tanstack/react-router";

import { cn } from "@/lib/utils";

export interface RecordLinkProps {
  /** The route of the record or page (an app's own path; navigating to another app's page is a link, never an import). */
  to: string;
  search?: Record<string, string>;
  className?: string;
  children: ReactNode;
}

/* A link to a record or page inside the SPA (design 72: the trace's links to the intake and the order, an import
 * profile's link to the document it was learned from). Links carry the brand colour (UI-TOK-002) and go through the
 * router, so the browser's Back returns to where the user came from. */
export function RecordLink({ to, search, className, children }: RecordLinkProps) {
  return (
    <Link to={to} search={search} className={cn("text-brand hover:underline", className)}>
      {children}
    </Link>
  );
}
