/// <reference types="node" />
import { readFileSync } from "node:fs";
import { resolve } from "node:path";

import { describe, expect, it } from "vitest";

import { createAppFeatureChecker, featureChain } from "@/app/app-features";
import { APPS, type AppDefinition, type AppView } from "@/app/apps.config";

/* The app and navigation features (PO decision 2026-10-14, docs/designs/tenants-and-editions.md, "App features"):
 * a node is on only when it and every dot-prefix ancestor are on; groups without an enabled child and apps without an
 * enabled view drop out; Administration ignores its features on the host. Risk class: High (feature gating of every
 * screen) — this checker decides the launcher, the nav pane, the FeatureUnavailable state and the index redirects. */

const masterData = APPS.find((a) => a.id === "master-data")!;
const admin = APPS.find((a) => a.id === "admin")!;
const dashboard = APPS.find((a) => a.id === "dashboard")!;
const tenants = APPS.find((a) => a.id === "tenants")!;

const on = (...features: string[]) => Object.fromEntries(features.map((f) => [f, "true"]));

/** Every node of a tree, at any depth (Master data: domain group → group → list, PO 2026-10-16). */
const nodes = (views: readonly AppView[] | undefined): AppView[] => (views ?? []).flatMap((v) => [v, ...nodes(v.children)]);

/** Every feature of an app's tree, switched on. */
function allOn(app: AppDefinition): Record<string, string> {
  const values: Record<string, string> = {};
  if (app.feature) values[app.feature] = "true";
  for (const view of nodes(app.views)) {
    if (view.feature) values[view.feature] = "true";
  }
  return values;
}

const tenant = (values: Record<string, string>) => createAppFeatureChecker(values, false);
const host = (values: Record<string, string>) => createAppFeatureChecker(values, true);

/** The feature names the backend defines: every `const string` of AppFeatures.cs except the group name, resolved as
 *  C# resolves a simple or qualified name (innermost enclosing class first). Read from the source, so a node added on
 *  one side only fails here. */
function backendAppFeatures(): string[] {
  const repoRoot = resolve((import.meta as unknown as { dirname: string }).dirname, "../../../..");
  const src = readFileSync(resolve(repoRoot, "backend/modules/platform/OpenTms.Platform.Application.Contracts/Features/AppFeatures.cs"), "utf-8");
  const values = new Map<string, string>();
  const scope: string[] = [];
  const pending: (string | null)[] = [];
  const lookup = (name: string): string => {
    for (let depth = scope.length; depth >= 0; depth--) {
      const key = [...scope.slice(0, depth), name].join(".");
      if (values.has(key)) return values.get(key)!;
    }
    throw new Error(`AppFeatures.cs: cannot resolve ${name} in ${scope.join(".")}`);
  };
  for (const line of src.split("\n")) {
    const cls = /\bclass\s+(\w+)/.exec(line);
    if (cls) pending.push(cls[1]);
    const constant = /\bconst\s+string\s+(\w+)\s*=\s*(.+);/.exec(line);
    if (constant) {
      const value = constant[2]
        .split("+")
        .map((part) => part.trim())
        .map((part) => (part.startsWith('"') ? part.slice(1, -1) : lookup(part)))
        .join("");
      values.set([...scope, constant[1]].join("."), value);
    }
    for (const char of line) {
      if (char === "{") scope.push(pending.pop() ?? "");
      else if (char === "}") scope.pop();
    }
  }
  /* Blocks that are not classes (none today) would push "": drop them from the keys. */
  return [...values.entries()].filter(([key]) => key !== "AppFeatures.GroupName").map(([, value]) => value);
}

type PathNode = { path: string; children?: readonly PathNode[] };
const paths = (views: readonly PathNode[]): string[] =>
  views.map((v) => (v.children ? `${v.path}[${paths(v.children).join(",")}]` : v.path));

describe("featureChain", () => {
  it("lists the app, every group and the node itself, root to leaf", () => {
    expect(featureChain("Apps.MasterData.General.Reference.Countries")).toEqual([
      "Apps.MasterData",
      "Apps.MasterData.General",
      "Apps.MasterData.General.Reference",
      "Apps.MasterData.General.Reference.Countries",
    ]);
  });

  it("an app feature is its own chain; the bare group name is never part of it", () => {
    expect(featureChain("Apps.Dashboard")).toEqual(["Apps.Dashboard"]);
    expect(featureChain("Apps.Administration.AuditLogs.TechnicalLogbook")).not.toContain("Apps");
  });

  it("a name outside the Apps group chains from its first segment", () => {
    expect(featureChain("Integrations.AddressLookup")).toEqual(["Integrations", "Integrations.AddressLookup"]);
  });
});

describe("apps.config – every app and node has its tree feature", () => {
  it("every node's feature extends its parent's (the ancestors are the dot-prefixes)", () => {
    for (const app of APPS.filter((a) => a.feature)) {
      for (const view of app.views ?? []) {
        expect(view.feature?.startsWith(`${app.feature}.`), view.path).toBe(true);
      }
      for (const view of nodes(app.views)) {
        view.children?.forEach((child) => expect(child.feature?.startsWith(`${view.feature}.`), child.path).toBe(true));
      }
    }
  });

  it("the host-only Tenants app has no feature; Administration is hostAlwaysEnabled", () => {
    expect(tenants.feature).toBeUndefined();
    expect(admin.hostAlwaysEnabled).toBe(true);
    expect(masterData.hostAlwaysEnabled).not.toBe(true);
  });

  it("names the same 40 features as the backend's AppFeatures", () => {
    const names = APPS.flatMap((app) => Object.keys(allOn(app)));
    expect(names).toHaveLength(40);
    expect(new Set(names).size).toBe(40);
    const backend = backendAppFeatures();
    expect(backend).toHaveLength(40);
    expect([...names].sort()).toEqual([...backend].sort());
  });

  it("carries design 49's Platform domain group and its four lists directly under it", () => {
    const names = Object.keys(allOn(masterData));
    expect(names).toEqual(
      expect.arrayContaining([
        "Apps.MasterData.Platform",
        "Apps.MasterData.Platform.EmailTemplates",
        "Apps.MasterData.Platform.ReportDefinitions",
        "Apps.MasterData.Platform.DocumentTypes",
        "Apps.MasterData.Platform.NumberSequences",
      ]),
    );
    expect(names.filter((n) => n.startsWith("Apps.MasterData.Platform"))).toHaveLength(5);
  });

  it("carries design 48's three nodes under their groups", () => {
    const names = Object.keys(allOn(masterData));
    expect(names).toContain("Apps.MasterData.General.Fleet.Vehicles");
    expect(names).toContain("Apps.MasterData.General.Fleet.VehicleCombinations");
    expect(names).toContain("Apps.MasterData.General.Transport.ProductRelationCodes");
  });
});

describe("createAppFeatureChecker – tenant side", () => {
  it("an app is on only with its feature 'true'; anything else is off", () => {
    expect(tenant(on("Apps.Dashboard")).isAppEnabled(dashboard)).toBe(true);
    expect(tenant({ "Apps.Dashboard": "false" }).isAppEnabled(dashboard)).toBe(false);
    expect(tenant({ "Apps.Dashboard": "True" }).isAppEnabled(dashboard)).toBe(false);
    expect(tenant({}).isAppEnabled(dashboard)).toBe(false);
  });

  it("an ancestor that is off hides the node even when the node itself is on", () => {
    const values = { ...allOn(masterData), "Apps.MasterData.General.Reference": "false" };
    const checker = tenant(values);
    const visible = paths(checker.visibleViews(masterData));
    expect(visible.join()).not.toContain("/master-data/countries");
    /* Two domain groups: General (without Reference) and Platform. */
    expect(visible).toHaveLength(2);
    expect(checker.visibleViews(masterData)[0].children).toHaveLength(3);
    expect(checker.visibleViews(masterData)[1].path).toBe("/master-data/platform");
  });

  it("the Platform group off hides its four lists, even with the lists on; General stays", () => {
    const checker = tenant({ ...allOn(masterData), "Apps.MasterData.Platform": "false" });
    expect(checker.visibleViews(masterData).map((v) => v.path)).toEqual(["/master-data/general"]);
    for (const path of ["/master-data/email-templates", "/master-data/report-definitions", "/master-data/document-types", "/master-data/number-sequences"]) {
      expect(checker.isPathEnabled(masterData, path), path).toBe(false);
    }
    expect(checker.isPathEnabled(masterData, "/master-data/countries")).toBe(true);
  });

  it("a group with all its children off is hidden, even though the group feature is on", () => {
    const values = {
      ...allOn(masterData),
      "Apps.MasterData.General.Fleet.Vehicles": "false",
      "Apps.MasterData.General.Fleet.VehicleCombinations": "false",
      "Apps.MasterData.General.Fleet.Drivers": "false",
      "Apps.MasterData.General.Fleet.VehicleTypes": "false",
      "Apps.MasterData.General.Fleet.TransportCharacteristics": "false",
    };
    const checker = tenant(values);
    const fleet = nodes(masterData.views).find((v) => v.path === "/master-data/fleet")!;
    expect(checker.isViewEnabled(masterData, fleet)).toBe(false);
    expect(nodes(checker.visibleViews(masterData)).map((v) => v.path)).not.toContain("/master-data/fleet");
  });

  it("a group keeps only its enabled children", () => {
    const checker = tenant(on("Apps.MasterData", "Apps.MasterData.General", "Apps.MasterData.General.Reference", "Apps.MasterData.General.Reference.Currencies"));
    expect(paths(checker.visibleViews(masterData))).toEqual(["/master-data/general[/master-data/reference[/master-data/currencies]]"]);
  });

  it("an app that is on but has all its views off is hidden", () => {
    const checker = tenant(on("Apps.MasterData", "Apps.MasterData.General", "Apps.MasterData.General.Relations", "Apps.MasterData.General.Transport", "Apps.MasterData.General.Fleet", "Apps.MasterData.General.Reference"));
    expect(checker.visibleViews(masterData)).toEqual([]);
    expect(checker.isAppEnabled(masterData)).toBe(false);
  });

  it("an app that is off hides every view, whatever their own values", () => {
    const checker = tenant({ ...allOn(masterData), "Apps.MasterData": "false" });
    expect(checker.isAppEnabled(masterData)).toBe(false);
    expect(checker.visibleViews(masterData)).toEqual([]);
    expect(checker.firstEnabledPath(masterData)).toBeUndefined();
  });

  it("Administration on a tenant respects its values", () => {
    const checker = tenant({ ...allOn(admin), "Apps.Administration.Users": "false", "Apps.Administration.Maintenance": "false" });
    expect(checker.visibleViews(admin).map((v) => v.path)).toEqual(["/admin/roles", "/admin/organization-units", "/admin/logbook"]);
    expect(tenant({}).isAppEnabled(admin)).toBe(false);
  });

  it("an app without a feature (Tenants) is always on as far as features go", () => {
    expect(tenant({}).isAppEnabled(tenants)).toBe(true);
  });

  it("is loaded unless told otherwise", () => {
    expect(tenant({}).isLoaded).toBe(true);
    expect(createAppFeatureChecker({}, false, false).isLoaded).toBe(false);
  });
});

describe("createAppFeatureChecker – host side", () => {
  it("a hostAlwaysEnabled app ignores its feature values on the host", () => {
    const checker = host({ "Apps.Administration": "false", "Apps.Administration.Users": "false" });
    expect(checker.isAppEnabled(admin)).toBe(true);
    expect(checker.visibleViews(admin)).toHaveLength(5);
    expect(checker.isPathEnabled(admin, "/admin/users")).toBe(true);
    expect(checker.isPathEnabled(admin, "/admin/maintenance")).toBe(true);
    expect(checker.firstEnabledPath(admin)).toBe("/admin/users");
  });

  it("other apps still follow their values on the host", () => {
    expect(host({}).isAppEnabled(dashboard)).toBe(false);
    expect(host({}).isAppEnabled(masterData)).toBe(false);
    expect(host(on("Apps.Dashboard")).isAppEnabled(dashboard)).toBe(true);
  });

  it("the tenant side never gets the host exemption", () => {
    expect(tenant({ "Apps.Administration": "false" }).isAppEnabled(admin)).toBe(false);
  });
});

describe("createAppFeatureChecker – firstEnabledPath", () => {
  it("is the first enabled leaf of the app in tree order", () => {
    expect(tenant(allOn(masterData)).firstEnabledPath(masterData)).toBe("/master-data/relations");
    expect(tenant(allOn(admin)).firstEnabledPath(admin)).toBe("/admin/users");
    expect(tenant({ ...allOn(admin), "Apps.Administration.Users": "false" }).firstEnabledPath(admin)).toBe("/admin/roles");
  });

  it("skips disabled leaves and disabled groups", () => {
    const values = { ...allOn(masterData), "Apps.MasterData.General.Relations": "false", "Apps.MasterData.General.Transport.Products": "false" };
    expect(tenant(values).firstEnabledPath(masterData)).toBe("/master-data/transport-units");
  });

  it("under a path whose leaves share its prefix (the audit logs) returns the first enabled one", () => {
    expect(tenant(allOn(admin)).firstEnabledPath(admin, "/admin/logbook")).toBe("/admin/logbook/functional");
    expect(tenant({ ...allOn(admin), "Apps.Administration.AuditLogs.FunctionalLogbook": "false" }).firstEnabledPath(admin, "/admin/logbook")).toBe(
      "/admin/logbook/technical",
    );
    expect(tenant({ ...allOn(admin), "Apps.Administration.AuditLogs": "false" }).firstEnabledPath(admin, "/admin/logbook")).toBeUndefined();
  });

  it("under a Master data group path returns the first enabled list of that group (the group redirect)", () => {
    /* The Master data index redirects /master-data/transport, /fleet and /reference to firstEnabledPath(app, groupPath)
     * (apps/master-data/index.tsx; it replaced fixed redirects to /products, /vehicle-types and /countries). The lists
     * of those groups do not live under the group's path, so the lookup has to follow the tree, not the URL. */
    const checker = tenant(allOn(masterData));
    expect(checker.firstEnabledPath(masterData, "/master-data/transport")).toBe("/master-data/products");
    /* Design 48: Vehicles is the first list of Fleet. */
    expect(checker.firstEnabledPath(masterData, "/master-data/fleet")).toBe("/master-data/vehicles");
    expect(tenant({ ...allOn(masterData), "Apps.MasterData.General.Fleet.Vehicles": "false" }).firstEnabledPath(masterData, "/master-data/fleet")).toBe(
      "/master-data/vehicle-combinations",
    );
    expect(checker.firstEnabledPath(masterData, "/master-data/reference")).toBe("/master-data/countries");
  });

  it("under the Platform group path returns its first enabled list; with General off the app's first list is a Platform one", () => {
    const checker = tenant(allOn(masterData));
    expect(checker.firstEnabledPath(masterData, "/master-data/platform")).toBe("/master-data/email-templates");
    expect(
      tenant({ ...allOn(masterData), "Apps.MasterData.Platform.EmailTemplates": "false" }).firstEnabledPath(masterData, "/master-data/platform"),
    ).toBe("/master-data/report-definitions");
    expect(tenant({ ...allOn(masterData), "Apps.MasterData.General": "false" }).firstEnabledPath(masterData)).toBe("/master-data/email-templates");
    expect(
      tenant({ ...allOn(masterData), "Apps.MasterData.General": "false", "Apps.MasterData.Platform": "false" }).firstEnabledPath(masterData),
    ).toBeUndefined();
  });

  it("is undefined when nothing is on", () => {
    expect(tenant({}).firstEnabledPath(admin)).toBeUndefined();
    expect(tenant(on("Apps.MasterData")).firstEnabledPath(masterData)).toBeUndefined();
  });
});

describe("createAppFeatureChecker – isPathEnabled", () => {
  it("a leaf of the audit-log group follows its own chain", () => {
    const values = { ...allOn(admin), "Apps.Administration.AuditLogs.TechnicalLogbook": "false" };
    expect(tenant(values).isPathEnabled(admin, "/admin/logbook/functional")).toBe(true);
    expect(tenant(values).isPathEnabled(admin, "/admin/logbook/technical")).toBe(false);
    expect(tenant({ ...allOn(admin), "Apps.Administration.AuditLogs": "false" }).isPathEnabled(admin, "/admin/logbook/functional")).toBe(false);
  });

  it("a top-level leaf and its sub-paths follow the node", () => {
    const values = { ...allOn(admin), "Apps.Administration.Maintenance": "false" };
    expect(tenant(values).isPathEnabled(admin, "/admin/maintenance")).toBe(false);
    expect(tenant(values).isPathEnabled(admin, "/admin/maintenance/anything")).toBe(false);
    expect(tenant(values).isPathEnabled(admin, "/admin/users")).toBe(true);
  });

  it("the app root is on when the app is (its redirect decides) and off when the app is off", () => {
    expect(tenant(allOn(admin)).isPathEnabled(admin, "/admin")).toBe(true);
    expect(tenant({ ...allOn(admin), "Apps.Administration": "false" }).isPathEnabled(admin, "/admin")).toBe(false);
    expect(tenant({ ...allOn(admin), "Apps.Administration": "false" }).isPathEnabled(admin, "/admin/users")).toBe(false);
    expect(tenant(on("Apps.Dashboard")).isPathEnabled(dashboard, "/dashboard")).toBe(true);
    expect(tenant({}).isPathEnabled(dashboard, "/dashboard")).toBe(false);
  });

  it("a group path is on while the group is (its redirect decides)", () => {
    expect(tenant(allOn(admin)).isPathEnabled(admin, "/admin/logbook")).toBe(true);
    expect(tenant({ ...allOn(admin), "Apps.Administration.AuditLogs": "false" }).isPathEnabled(admin, "/admin/logbook")).toBe(false);
  });

  it("the Relations list, which owns its group's path, follows both the group and the node", () => {
    expect(tenant(allOn(masterData)).isPathEnabled(masterData, "/master-data/relations")).toBe(true);
    expect(tenant({ ...allOn(masterData), "Apps.MasterData.General.Relations.Relations": "false" }).isPathEnabled(masterData, "/master-data/relations")).toBe(false);
    expect(tenant({ ...allOn(masterData), "Apps.MasterData.General.Relations": "false" }).isPathEnabled(masterData, "/master-data/relations")).toBe(false);
  });

  it.each([
    ["/master-data/address-types", "Apps.MasterData.General.Relations.AddressTypes"],
    ["/master-data/products", "Apps.MasterData.General.Transport.Products"],
    ["/master-data/drivers", "Apps.MasterData.General.Fleet.Drivers"],
    ["/master-data/vehicles", "Apps.MasterData.General.Fleet.Vehicles"],
    ["/master-data/vehicle-combinations", "Apps.MasterData.General.Fleet.VehicleCombinations"],
    ["/master-data/product-relation-codes", "Apps.MasterData.General.Transport.ProductRelationCodes"],
    ["/master-data/countries", "Apps.MasterData.General.Reference.Countries"],
    ["/master-data/geographical-regions", "Apps.MasterData.General.Reference.GeographicalRegions"],
    ["/master-data/email-templates", "Apps.MasterData.Platform.EmailTemplates"],
    ["/master-data/report-definitions", "Apps.MasterData.Platform.ReportDefinitions"],
    ["/master-data/document-types", "Apps.MasterData.Platform.DocumentTypes"],
    ["/master-data/number-sequences", "Apps.MasterData.Platform.NumberSequences"],
  ])("a Master data list outside its group's path (%s) is off when its node is off", (path, node) => {
    /* The lists of a group do not live under the group's path (/master-data/countries is in the group
     * /master-data/reference), so a direct link must still be refused when the node is off. */
    expect(tenant(allOn(masterData)).isPathEnabled(masterData, path)).toBe(true);
    expect(tenant({ ...allOn(masterData), [node]: "false" }).isPathEnabled(masterData, path)).toBe(false);
  });

  it("the three design-48 lists are independent of their neighbours with similar paths", () => {
    /* /master-data/vehicles vs /master-data/vehicle-combinations and /vehicle-types, /master-data/products vs
     * /master-data/product-relation-codes: switching one off must not switch off the other. */
    const vehiclesOff = tenant({ ...allOn(masterData), "Apps.MasterData.General.Fleet.Vehicles": "false" });
    expect(vehiclesOff.isPathEnabled(masterData, "/master-data/vehicles")).toBe(false);
    expect(vehiclesOff.isPathEnabled(masterData, "/master-data/vehicle-combinations")).toBe(true);
    expect(vehiclesOff.isPathEnabled(masterData, "/master-data/vehicle-types")).toBe(true);
    const productsOff = tenant({ ...allOn(masterData), "Apps.MasterData.General.Transport.Products": "false" });
    expect(productsOff.isPathEnabled(masterData, "/master-data/products")).toBe(false);
    expect(productsOff.isPathEnabled(masterData, "/master-data/product-relation-codes")).toBe(true);
    const codesOff = tenant({ ...allOn(masterData), "Apps.MasterData.General.Transport.ProductRelationCodes": "false" });
    expect(codesOff.isPathEnabled(masterData, "/master-data/products")).toBe(true);
  });

  it("a Master data list is off when its group is off, even with the node on", () => {
    expect(tenant({ ...allOn(masterData), "Apps.MasterData.General.Reference": "false" }).isPathEnabled(masterData, "/master-data/countries")).toBe(false);
  });

  it("on the host, Administration paths are on whatever the values", () => {
    expect(host({}).isPathEnabled(admin, "/admin/logbook/technical")).toBe(true);
  });
});
