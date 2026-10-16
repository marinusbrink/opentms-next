/**
 * Every path in an app's nav tree is a route of the real router, at any depth. Since D15 (PO 2026-10-16) the Master
 * data lists sit on the third level (Master data → General → group → list); the router used to collect two levels
 * only, so every list except Relations (whose group shares its path) rendered "Not Found". The other tests render
 * the app component directly and never went through the router.
 */
import { describe, expect, it } from "vitest";
import { APPS } from "@/app/apps.config";
import { router, treePaths } from "@/app/router";

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
});
