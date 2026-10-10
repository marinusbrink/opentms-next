import { lazy, Suspense } from "react";

import type { OpenTmsGridProps } from "@/components/ui/opentms-grid";
import { Skeleton } from "@/components/ui/skeleton";

/* The grid as pages use it: lazy-loaded (ag-grid-enterprise must stay out of the main chunk,
 * see opentms-grid.tsx) and typed per row, with the Skeleton of UI-PAGE-005 while it loads. */
const _OpenTmsGrid = lazy(() =>
  import("@/components/ui/opentms-grid").then((m) => ({ default: m.OpenTmsGrid })),
);

export function LazyOpenTmsGrid<TRow>(props: OpenTmsGridProps<TRow>) {
  return (
    <Suspense fallback={<Skeleton className="h-full w-full" />}>
      {/* eslint-disable-next-line @typescript-eslint/no-explicit-any */}
      <_OpenTmsGrid {...(props as any)} />
    </Suspense>
  );
}
