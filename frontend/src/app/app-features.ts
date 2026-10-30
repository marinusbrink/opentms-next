import { useMemo } from "react";
import { APPS, type AppDefinition, type AppView } from "@/app/apps.config";
import { useApplicationConfiguration } from "@/lib/abp/queries";

/* App and navigation features (docs/designs/tenants-and-editions.md, "App features"; PO 2026-10-14): every app and
 * every node of its navigation tree is an ABP feature, named after its place in the tree. A node is on only when it
 * and every ancestor are on; the ancestors are the dot-prefixes of its name
 * ("Apps.MasterData.General.Reference.Countries" needs "Apps.MasterData", "Apps.MasterData.General" and
 * "Apps.MasterData.General.Reference"). The tree may be any depth (Master data: app → domain group → group → list,
 * PO 2026-10-16). An app marked hostAlwaysEnabled ignores its features on
 * the host side, which has no tenant or edition to carry them. Permission checks stay where they were. */

const ROOT = "Apps";

/** "Apps.A.B.C" → ["Apps.A", "Apps.A.B", "Apps.A.B.C"]. */
export function featureChain(feature: string): string[] {
  const parts = feature.split(".");
  const start = parts[0] === ROOT ? 2 : 1;
  const chain: string[] = [];
  for (let i = start; i <= parts.length; i++) chain.push(parts.slice(0, i).join("."));
  return chain;
}

export interface AppFeatureChecker {
  /** False until the application configuration (with the feature values) has loaded. */
  isLoaded: boolean;
  isAppEnabled: (app: AppDefinition) => boolean;
  isViewEnabled: (app: AppDefinition, view: AppView) => boolean;
  /** The app's views and children that are on; groups without an enabled child drop out. */
  visibleViews: (app: AppDefinition) => AppView[];
  /** The first enabled leaf path at or under the given path, or undefined. */
  firstEnabledPath: (app: AppDefinition, underPath?: string) => string | undefined;
  /** Whether the route at this pathname is on (the deepest matching node decides). */
  isPathEnabled: (app: AppDefinition, pathname: string) => boolean;
  /**
   * Whether a node feature ("Apps.MasterData.General.Reference.Countries") and every ancestor are on, as the server's
   * feature chain on that node's endpoints requires (UI-PERM-006). On the host side the owning app's
   * hostAlwaysEnabled applies, as in the shell. False until the configuration has loaded.
   */
  isFeatureEnabled: (feature: string) => boolean;
}

function matches(path: string, pathname: string): boolean {
  return pathname === path || pathname.startsWith(`${path}/`);
}

export function createAppFeatureChecker(
  values: Record<string, string | null | undefined>,
  isHost: boolean,
  isLoaded = true,
  apps: readonly AppDefinition[] = APPS,
): AppFeatureChecker {
  const on = (feature: string | undefined, app: AppDefinition | undefined) =>
    !(isHost && app?.tenantOnly === true) &&
    (feature == null || (isHost && app?.hostAlwaysEnabled === true) || featureChain(feature).every((f) => values[f] === "true"));

  const leafPaths = (app: AppDefinition, view: AppView): string[] => {
    if (!on(view.feature, app)) return [];
    if (!view.children?.length) return [view.path];
    return view.children.flatMap((child) => leafPaths(app, child));
  };

  const isViewEnabled = (app: AppDefinition, view: AppView) => on(app.feature, app) && leafPaths(app, view).length > 0;

  /** The enabled nodes at every depth (the Master data tree has three levels: domain group, group, list). */
  const enabledNodes = (app: AppDefinition, views: readonly AppView[]): AppView[] =>
    views
      .filter((view) => isViewEnabled(app, view))
      .map((view) => (view.children ? { ...view, children: enabledNodes(app, view.children) } : view));

  const visibleViews = (app: AppDefinition): AppView[] => enabledNodes(app, app.views ?? []);

  const isAppEnabled = (app: AppDefinition) =>
    on(app.feature, app) && (app.views == null || app.views.length === 0 || visibleViews(app).length > 0);

  /** Every node in tree order, each with its ancestors (root first). A list's path need not sit under its group's
   * path (Master data), so nodes are found through the tree, never through the URL. */
  const allLineages = (views: readonly AppView[] | undefined, ancestors: AppView[] = []): AppView[][] =>
    (views ?? []).flatMap((view) => [[...ancestors, view], ...allLineages(view.children, [...ancestors, view])]);

  /** The node a pathname belongs to, with its ancestors: the longest matching path; on a tie (the Relations list
   * owns its group's path; a saved filter shares its list's path) the deeper one, which comes later in tree order. */
  const lineageFor = (app: AppDefinition, pathname: string): AppView[] | undefined => {
    let best: AppView[] | undefined;
    for (const lineage of allLineages(app.views)) {
      const node = lineage[lineage.length - 1];
      if (matches(node.path, pathname) && (best == null || node.path.length >= best[best.length - 1].path.length)) best = lineage;
    }
    return best;
  };
  const nodeFor = (app: AppDefinition, pathname: string): AppView | undefined => lineageFor(app, pathname)?.at(-1);

  return {
    isLoaded,
    isAppEnabled,
    isViewEnabled,
    visibleViews,
    firstEnabledPath: (app, underPath) => {
      if (underPath == null) return (app.views ?? []).flatMap((view) => leafPaths(app, view))[0];
      const node = nodeFor(app, underPath);
      return node ? leafPaths(app, node)[0] : undefined;
    },
    isPathEnabled: (app, pathname) => {
      if (!on(app.feature, app)) return false;
      /* A node's feature name carries its ancestors (dot-prefixes), so checking it checks its group too; a node
       * without a feature (a saved filter of a list, UI-PAGE-004) follows its ancestors. A path that is no node (the
       * app root) is decided by the redirect it leads to. */
      const lineage = lineageFor(app, pathname);
      return lineage == null || lineage.every((node) => on(node.feature, app));
    },
    isFeatureEnabled: (feature) => {
      if (!isLoaded) return false;
      /* The app is the one whose feature is the chain's first link. */
      const root = featureChain(feature)[0];
      return on(feature, apps.find((app) => app.feature === root));
    },
  };
}

export function useAppFeatures(): AppFeatureChecker {
  const { data } = useApplicationConfiguration();
  const values = data?.features?.values as Record<string, string | null> | undefined;
  const isHost = data != null && data.currentTenant?.id == null;
  const isLoaded = data != null;
  return useMemo(() => createAppFeatureChecker(values ?? {}, isHost, isLoaded), [values, isHost, isLoaded]);
}
