import { lazy, Suspense, type ReactElement } from "react";

// Lazy-loaded: the Transport app (design 61, Cost & SLO: lazy chunks per app) stays out of the main chunk.
const _LazyTransport = lazy(() => import("@/apps/transport").then((m) => ({ default: m.TransportApp })));

export function LazyTransportApp(): ReactElement {
  return (
    <Suspense fallback={null}>
      <_LazyTransport />
    </Suspense>
  );
}
