import type { ReactNode } from "react";

/* Read-only label / value rows for the logbook dialogs: the same `<dl>` grid the record
 * identification tab uses, so every read-only detail in the app looks alike. */
export interface DetailRow {
  label: string;
  value: ReactNode;
  /** Figures and timestamps align with tabular digits (UI-TOK-009). */
  tabular?: boolean;
}

export function LogbookDetailRows({ rows }: { rows: DetailRow[] }) {
  return (
    <dl className="grid grid-cols-[auto_1fr] gap-x-6 gap-y-3 text-sm">
      {rows.map((row) => (
        <div key={row.label} className="contents">
          <dt className="text-label font-medium text-muted-foreground">{row.label}</dt>
          <dd className={row.tabular ? "tabular-nums break-all" : "break-all"}>{row.value}</dd>
        </div>
      ))}
    </dl>
  );
}
