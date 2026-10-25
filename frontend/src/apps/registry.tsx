import { lazy, Suspense, type ReactElement } from "react";
import { APPS, type AppDefinition } from "@/app/apps.config";
import { DashboardApp } from "@/apps/dashboard";
import { PlanboardApp } from "@/apps/planboard";
import { FinanceApp } from "@/apps/finance";
// The Transport app is lazy (design 61); its wrapper lives next to it.
import { LazyTransportApp } from "@/apps/transport/lazy";
import { IntegrationsApp } from "@/apps/integrations";
import { ReportsApp } from "@/apps/reports";

// Lazy-loaded: the Master data app (ten views, AG Grid editors) stays out of the main chunk (design 41).
const _LazyMasterData = lazy(() =>
  import("@/apps/master-data").then((m) => ({ default: m.MasterDataApp }))
);
function LazyMasterDataApp(): ReactElement {
  return (
    <Suspense fallback={null}>
      <_LazyMasterData />
    </Suspense>
  );
}

// Lazy-loaded per design §Cost & SLO: admin bundle must not land in the main chunk.
const _LazyAdmin = lazy(() =>
  import("@/apps/admin").then((m) => ({ default: m.AdministrationApp }))
);
function LazyAdministrationApp(): ReactElement {
  return (
    <Suspense fallback={null}>
      <_LazyAdmin />
    </Suspense>
  );
}

// Lazy-loaded: the host-only Tenants app (docs/designs/tenants-and-editions.md) stays out of the main chunk.
const _LazyTenants = lazy(() => import("@/apps/tenants").then((m) => ({ default: m.TenantsApp })));
function LazyTenantsApp(): ReactElement {
  return (
    <Suspense fallback={null}>
      <_LazyTenants />
    </Suspense>
  );
}

/* Maps every app id from apps.config.ts to its view component (folder under
 * src/apps/). apps.config.ts stays pure data; this is the only place components
 * are attached. */
const components: Record<string, () => ReactElement> = {
  dashboard: DashboardApp,
  planboard: PlanboardApp,
  transport: LazyTransportApp,
  finance: FinanceApp,
  "master-data": LazyMasterDataApp,
  integrations: IntegrationsApp,
  reports: ReportsApp,
  admin: LazyAdministrationApp,
  tenants: LazyTenantsApp,
};

export function componentForApp(app: AppDefinition): () => ReactElement {
  const component = components[app.id];
  if (!component) {
    throw new Error(`No component registered for app "${app.id}" — add it to src/apps/registry.tsx`);
  }
  return component;
}

// Fail fast in development when the config and the registry drift apart.
for (const app of APPS) {
  componentForApp(app);
}
