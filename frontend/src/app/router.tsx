import { lazy, Suspense } from "react";
import {
  createRootRoute,
  createRoute,
  createRouter,
  Outlet,
} from "@tanstack/react-router";
import { APPS, type AppView } from "@/app/apps.config";
import { componentForApp } from "@/apps/registry";
import { AppShell } from "@/app/shell/AppShell";
import { Landing } from "@/app/Landing";
import { AuthCallback } from "@/auth/AuthCallback";

const _AdministrationApp = lazy(() =>
  import("@/apps/admin").then((m) => ({ default: m.AdministrationApp }))
);

function AdminRoute() {
  return (
    <Suspense fallback={null}>
      <_AdministrationApp />
    </Suspense>
  );
}

/* Code-based route tree. App routes are derived from apps.config.ts — adding an
 * app there (a PO decision) plus a component in src/apps/ is all it takes. */

const rootRoute = createRootRoute({
  component: () => <Outlet />,
});

const landingRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/",
  component: Landing,
});

const authCallbackRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/auth/callback",
  component: AuthCallback,
});

const shellRoute = createRoute({
  getParentRoute: () => rootRoute,
  id: "shell",
  component: AppShell,
});

const appRoutes = APPS.map((app) =>
  createRoute({
    getParentRoute: () => shellRoute,
    path: app.path,
    component: componentForApp(app),
  }),
);

// Sub-routes for the Administration app — both resolve to the same component;
// the component reads the pathname to determine the active tab.
// Lazy-loaded per design §Cost & SLO: admin bundle must not land in the main chunk.
const adminSubRoutes = [
  "/admin/users",
  "/admin/roles",
  "/admin/organization-units",
  "/admin/logbook",
  "/admin/logbook/functional",
  "/admin/logbook/technical",
  "/admin/maintenance",
].map((path) =>
  createRoute({
    getParentRoute: () => shellRoute,
    path,
    component: AdminRoute,
  }),
);

/** Every path in a nav tree, at any depth (Master data has three levels since D15). */
export function treePaths(views: readonly AppView[]): string[] {
  return views.flatMap((view) => [view.path, ...treePaths(view.children ?? [])]);
}

/* Sub-routes of the Master data app (design 41): every nav group and list path in apps.config.ts, at any depth,
 * resolves to the app component, which reads the pathname. */
const masterDataApp = APPS.find((app) => app.id === "master-data")!;
const masterDataSubRoutes = Array.from(new Set(treePaths(masterDataApp.views ?? []))).map((path) =>
    createRoute({
      getParentRoute: () => shellRoute,
      path,
      component: componentForApp(masterDataApp),
    }),
  );

/* Sub-routes of the Transport app (design 61): every nav path (the saved filters share their list's path, so the set
 * dedupes them) plus the record pages (/transport/orders/{id}, …; design 61 Assumption 1, reached through
 * useOpenRecord). All resolve to the app component, which reads the pathname. */
const transportApp = APPS.find((app) => app.id === "transport")!;
export const TRANSPORT_RECORD_ROUTES = ["/transport/orders/$id", "/transport/templates/$id", "/transport/trips/$id"] as const;
const transportSubRoutes = [...new Set(treePaths(transportApp.views ?? [])), ...TRANSPORT_RECORD_ROUTES].map((path) =>
  createRoute({
    getParentRoute: () => shellRoute,
    path,
    component: componentForApp(transportApp),
  }),
);

/* Sub-routes of the Tenants app (host only): its two views resolve to the app component, which reads the pathname. */
const tenantsApp = APPS.find((app) => app.id === "tenants")!;
const tenantsSubRoutes = (tenantsApp.views ?? []).map((view) =>
  createRoute({
    getParentRoute: () => shellRoute,
    path: view.path,
    component: componentForApp(tenantsApp),
  }),
);

const routeTree = rootRoute.addChildren([
  landingRoute,
  authCallbackRoute,
  shellRoute.addChildren([...appRoutes, ...adminSubRoutes, ...masterDataSubRoutes, ...transportSubRoutes, ...tenantsSubRoutes]),
]);

/* No `Register` module augmentation on purpose: app routes are derived from
 * apps.config.ts at runtime, so route paths are strings, not literal types. */
export const router = createRouter({ routeTree });
