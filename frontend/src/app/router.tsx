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

const routeTree = rootRoute.addChildren([
  landingRoute,
  authCallbackRoute,
  shellRoute.addChildren(appRoutes),
]);

/* No `Register` module augmentation on purpose: app routes are derived from
 * apps.config.ts at runtime, so route paths are strings, not literal types. */
export const router = createRouter({ routeTree });
