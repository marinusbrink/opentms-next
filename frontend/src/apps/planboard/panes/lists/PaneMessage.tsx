import type { ReactNode } from "react";

/* The empty and error states of a list pane (design 70; the four screen states): a short centred message. */
export function PaneMessage({ children, tone = "muted" }: { children: ReactNode; tone?: "muted" | "error" }) {
  return (
    <div role={tone === "error" ? "alert" : "status"} className={tone === "error" ? "flex h-full items-center justify-center p-6 text-sm text-destructive" : "flex h-full items-center justify-center p-6 text-sm text-muted-foreground"}>
      {children}
    </div>
  );
}
