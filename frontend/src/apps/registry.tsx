import type { ReactElement } from "react";
import { APPS, type AppDefinition } from "@/app/apps.config";
import { DashboardApp } from "@/apps/dashboard";
import { PlanboardApp } from "@/apps/planboard";
import { TransportApp } from "@/apps/transport";
import { FinanceApp } from "@/apps/finance";
import { MasterDataApp } from "@/apps/master-data";
import { IntegrationsApp } from "@/apps/integrations";
import { ReportsApp } from "@/apps/reports";
import { AdministrationApp } from "@/apps/admin";

/* Maps every app id from apps.config.ts to its view component (folder under
 * src/apps/). apps.config.ts stays pure data; this is the only place components
 * are attached. */
const components: Record<string, () => ReactElement> = {
  dashboard: DashboardApp,
  planboard: PlanboardApp,
  transport: TransportApp,
  finance: FinanceApp,
  "master-data": MasterDataApp,
  integrations: IntegrationsApp,
  reports: ReportsApp,
  admin: AdministrationApp,
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
