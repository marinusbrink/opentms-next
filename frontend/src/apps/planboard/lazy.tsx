import { lazy, Suspense, type ReactElement } from "react";

// Lazy-loaded: the Planboard (dockview, FullCalendar, MapLibre; design 70) stays out of the main chunk.
const _LazyPlanboard = lazy(() => import("@/apps/planboard").then((m) => ({ default: m.PlanboardApp })));

export function LazyPlanboardApp(): ReactElement {
  return (
    <Suspense fallback={null}>
      <_LazyPlanboard />
    </Suspense>
  );
}
