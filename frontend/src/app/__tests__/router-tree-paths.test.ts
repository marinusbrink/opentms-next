/**
 * Every path in an app's nav tree is a route of the real router, at any depth. Since D15 (PO 2026-10-16) the Master
 * data lists sit on the third level (Master data → General → group → list); the router used to collect two levels
 * only, so every list except Relations (whose group shares its path) rendered "Not Found". The other tests render
 * the app component directly and never went through the router.
 */
import { describe, expect, it } from "vitest";
import { APPS } from "@/app/apps.config";
import { router, TRANSPORT_RECORD_ROUTES, treePaths } from "@/app/router";

const routePaths = new Set(Object.keys(router.routesByPath));

describe("router: nav tree paths", () => {
  it("treePaths walks every depth", () => {
    expect(
      treePaths([
        { nameKey: "a", path: "/a", icon: () => null, children: [{ nameKey: "b", path: "/b", icon: () => null, children: [{ nameKey: "c", path: "/c", icon: () => null }] }] },
      ] as never),
    ).toEqual(["/a", "/b", "/c"]);
  });

  it.each(APPS.filter((app) => app.id === "master-data").flatMap((app) => treePaths(app.views ?? [])))(
    "Master data path %s is a route",
    (path) => {
      expect(routePaths.has(path), `${path} has no route; it would render Not Found`).toBe(true);
    },
  );

  it("covers the third level (a list under General)", () => {
    expect(routePaths.has("/master-data/vehicles")).toBe(true);
    expect(routePaths.has("/master-data/product-relation-codes")).toBe(true);
    expect(routePaths.has("/master-data/countries")).toBe(true);
  });

  it("covers design 61's Master data lists", () => {
    for (const path of ["/master-data/consignment-note-profiles", "/master-data/via-depot-rules", "/master-data/price-lists", "/master-data/relation-financial-settings"]) {
      expect(routePaths.has(path), path).toBe(true);
    }
  });

  it.each(APPS.filter((app) => app.id === "transport").flatMap((app) => [...new Set(treePaths(app.views ?? []))]))("Transport path %s is a route", (path) => {
    /* The saved filters share their list's path (UI-PAGE-004): one route per distinct path. */
    expect(routePaths.has(path), `${path} has no route; it would render Not Found`).toBe(true);
  });

  it("the Transport record pages are routes with an $id param (design 61, Assumption 1)", () => {
    expect([...TRANSPORT_RECORD_ROUTES]).toEqual(["/transport/orders/$id", "/transport/templates/$id", "/transport/trips/$id"]);
    for (const path of TRANSPORT_RECORD_ROUTES) {
      expect(routePaths.has(path), path).toBe(true);
    }
    /* The shell is a pathless layout, so the record route's id is prefixed by it; match by the route object. */
    const recordRouteId = (router.routesByPath as Record<string, { id: string }>)["/transport/orders/$id"].id;
    const matches = router.matchRoutes("/transport/orders/3fa85f64-5717-4562-b3fc-2c963f66afa6", {});
    const match = matches.find((m) => m.routeId === recordRouteId);
    expect(match, matches.map((m) => m.routeId).join()).toBeDefined();
    expect(match!.params).toEqual({ id: "3fa85f64-5717-4562-b3fc-2c963f66afa6" });
  });
});
