import { forwardRef, lazy, Suspense } from "react";

import type { MapRef, MapViewProps } from "@/components/ui/map-view";
import { Skeleton } from "@/components/ui/skeleton";

/* The map as feature code uses it when the rest of its module does not need MapLibre: lazy-loaded, so MapLibre GL and
 * its stylesheet load with the first map only. A module that renders sources and layers imports them from map-view.tsx
 * and is itself lazy (the Planboard's Map pane is its own chunk). */
const _MapView = lazy(() => import("@/components/ui/map-view").then((m) => ({ default: m.MapView })));

export const LazyMapView = forwardRef<MapRef | null, MapViewProps>(function LazyMapView(props, ref) {
  return (
    <Suspense fallback={<Skeleton className="h-full w-full" />}>
      <_MapView ref={ref} {...props} />
    </Suspense>
  );
});
