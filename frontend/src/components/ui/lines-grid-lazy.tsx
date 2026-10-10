import { lazy, Suspense } from "react";

import type { LinesGridProps } from "@/components/ui/lines-grid";
import { Skeleton } from "@/components/ui/skeleton";

/* The lines grid as editors use it: lazy-loaded, typed per row (see lines-grid.tsx). */
const _LinesGrid = lazy(() => import("@/components/ui/lines-grid").then((m) => ({ default: m.LinesGrid })));

export function LazyLinesGrid<TRow>(props: LinesGridProps<TRow>) {
  return (
    <Suspense fallback={<Skeleton className="h-full w-full" />}>
      {/* eslint-disable-next-line @typescript-eslint/no-explicit-any */}
      <_LinesGrid {...(props as any)} />
    </Suspense>
  );
}
