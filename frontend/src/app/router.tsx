import { lazy, Suspense } from "react";
import {
  createRootRoute,
  createRoute,
  createRouter,
  Outlet,
} from "@tanstack/react-router";
import { APPS } from "@/app/apps.config";
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
const adminSubRoutes = ["/admin/users", "/admin/roles"].map((path) =>
  createRoute({
    getParentRoute: () => shellRoute,
    path,
    component: AdminRoute,
  }),
);

const routeTree = rootRoute.addChildren([
  landingRoute,
  authCallbackRoute,
  shellRoute.addChildren([...appRoutes, ...adminSubRoutes]),
]);

/* No `Register` module augmentation on purpose: app routes are derived from
 * apps.config.ts at runtime, so route paths are strings, not literal types. */
export const router = createRouter({ routeTree });
