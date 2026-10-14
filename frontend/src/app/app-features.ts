import { useMemo } from "react";
import type { AppDefinition, AppView } from "@/app/apps.config";
import { useApplicationConfiguration } from "@/lib/abp/queries";

/* App and navigation features (docs/designs/tenants-and-editions.md, "App features"; PO 2026-10-14): every app and
 * every node of its navigation tree is an ABP feature, named after its place in the tree. A node is on only when it
 * and every ancestor are on; the ancestors are the dot-prefixes of its name ("Apps.MasterData.Reference.Countries"
 * needs "Apps.MasterData" and "Apps.MasterData.Reference"). An app marked hostAlwaysEnabled ignores its features on
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
}

function matches(path: string, pathname: string): boolean {
  return pathname === path || pathname.startsWith(`${path}/`);
}

export function createAppFeatureChecker(
  values: Record<string, string | null | undefined>,
  isHost: boolean,
  isLoaded = true,
): AppFeatureChecker {
  const on = (feature: string | undefined, app: AppDefinition) =>
    feature == null || (isHost && app.hostAlwaysEnabled === true) || featureChain(feature).every((f) => values[f] === "true");

  const leafPaths = (app: AppDefinition, view: AppView): string[] => {
    if (!on(view.feature, app)) return [];
    if (!view.children?.length) return [view.path];
    return view.children.flatMap((child) => leafPaths(app, child));
  };

  const isViewEnabled = (app: AppDefinition, view: AppView) => on(app.feature, app) && leafPaths(app, view).length > 0;

  const visibleViews = (app: AppDefinition): AppView[] =>
    (app.views ?? [])
      .filter((view) => isViewEnabled(app, view))
      .map((view) => (view.children ? { ...view, children: view.children.filter((child) => isViewEnabled(app, child)) } : view));

  const isAppEnabled = (app: AppDefinition) =>
    on(app.feature, app) && (app.views == null || app.views.length === 0 || visibleViews(app).length > 0);

  /** Every node in tree order. A list's path need not sit under its group's path (Master data), so nodes are
   * found through the tree, never through the URL. */
  const allNodes = (views: readonly AppView[] | undefined): AppView[] =>
    (views ?? []).flatMap((view) => [view, ...allNodes(view.children)]);

  /** The node a pathname belongs to: the longest matching path; on a tie (the Relations list owns its group's
   * path) the deeper one, which comes later in tree order. */
  const nodeFor = (app: AppDefinition, pathname: string): AppView | undefined => {
    let best: AppView | undefined;
    for (const node of allNodes(app.views)) {
      if (matches(node.path, pathname) && (best == null || node.path.length >= best.path.length)) best = node;
    }
    return best;
  };

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
      /* The node's feature name carries its ancestors (dot-prefixes), so checking it checks its group too. A path
       * that is no node (the app root) is decided by the redirect it leads to. */
      const node = nodeFor(app, pathname);
      return node == null || on(node.feature, app);
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
