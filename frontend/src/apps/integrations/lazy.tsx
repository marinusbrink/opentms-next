import { lazy, Suspense, type ReactElement } from "react";

// Lazy-loaded: the Integrations app (design 72: the Received list and the trace page) stays out of the main chunk.
const _LazyIntegrations = lazy(() => import("@/apps/integrations").then((m) => ({ default: m.IntegrationsApp })));

export function LazyIntegrationsApp(): ReactElement {
  return (
    <Suspense fallback={null}>
      <_LazyIntegrations />
    </Suspense>
  );
}
